package repository

import (
	"context"
	"errors"
	"fmt"
	"log"
	"sync"
	"sync/atomic"
	"time"

	"github.com/ClickHouse/clickhouse-go/v2"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/metrics"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/model"
	"github.com/google/uuid"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/codes"
	"go.opentelemetry.io/otel/trace"
)

// tracer: one span per batch INSERT (its own trace: a batch holds events from many requests).
var tracer = otel.Tracer("github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/repository")

// Defaults of WorkerConfig (INGEST_BATCH_SIZE, INGEST_FLUSH_INTERVAL, INGEST_BUFFER_CAPACITY, INGEST_WORKERS,
// INGEST_INSERT_RETRIES, INGEST_INSERT_TIMEOUT, INGEST_SPILL_DIR, INGEST_SPILL_REPLAY_INTERVAL).
const (
	BatchSize           = 5000
	FlushInterval       = 2 * time.Second
	BufferCapacity      = 100000
	NumWorkers          = 3
	InsertRetries       = 5
	InsertTimeout       = 30 * time.Second
	SpillDir            = "spill"
	SpillReplayInterval = 30 * time.Second

	insertBackoffBase = 500 * time.Millisecond
	insertBackoffMax  = 10 * time.Second
)

// ErrBufferFull: the event buffer is full; the event was not accepted. The handler answers 503.
var ErrBufferFull = errors.New("event buffer full")

// WorkerConfig sizes the event buffer and the workers that insert it into ClickHouse.
type WorkerConfig struct {
	BatchSize      int           // events per INSERT, at most
	FlushInterval  time.Duration // a partial batch is inserted after this long
	BufferCapacity int           // events waiting for a worker; when full, new events get 503
	NumWorkers     int

	InsertRetries       int           // retries of a failed INSERT, with exponential backoff, before spilling
	InsertTimeout       time.Duration // limit of each INSERT attempt
	SpillDir            string        // where batches that exhausted their retries are saved ("" disables)
	SpillReplayInterval time.Duration // how often saved batches are retried
}

// DefaultWorkerConfig returns the defaults above.
func DefaultWorkerConfig() WorkerConfig {
	return WorkerConfig{
		BatchSize:           BatchSize,
		FlushInterval:       FlushInterval,
		BufferCapacity:      BufferCapacity,
		NumWorkers:          NumWorkers,
		InsertRetries:       InsertRetries,
		InsertTimeout:       InsertTimeout,
		SpillDir:            SpillDir,
		SpillReplayInterval: SpillReplayInterval,
	}
}

func (c WorkerConfig) withDefaults() WorkerConfig {
	d := DefaultWorkerConfig()
	if c.BatchSize <= 0 {
		c.BatchSize = d.BatchSize
	}
	if c.FlushInterval <= 0 {
		c.FlushInterval = d.FlushInterval
	}
	if c.BufferCapacity <= 0 {
		c.BufferCapacity = d.BufferCapacity
	}
	if c.NumWorkers <= 0 {
		c.NumWorkers = d.NumWorkers
	}
	if c.InsertRetries < 0 {
		c.InsertRetries = 0
	}
	if c.InsertTimeout <= 0 {
		c.InsertTimeout = d.InsertTimeout
	}
	if c.SpillReplayInterval <= 0 {
		c.SpillReplayInterval = d.SpillReplayInterval
	}
	return c
}

// batchInserter inserts a batch with an idempotency token: retrying the same token must not duplicate rows.
type batchInserter func(ctx context.Context, events []*model.AnalyticsEvent, token string) error

type ClickHouseWorker struct {
	conn      clickhouse.Conn
	cfg       WorkerConfig
	eventChan chan *model.AnalyticsEvent
	wg        sync.WaitGroup

	insert batchInserter
	spill  *FileSpill // nil when SpillDir is empty
	sleep  func(ctx context.Context, d time.Duration) bool

	stopping   atomic.Bool
	stopCtx    context.Context
	stopCancel context.CancelFunc
	replayDone chan struct{}

	lastFullWarning time.Time
	warnMu          sync.Mutex
}

func NewClickHouseWorker(conn clickhouse.Conn, cfg WorkerConfig) *ClickHouseWorker {
	cfg = cfg.withDefaults()
	w := &ClickHouseWorker{
		conn:      conn,
		cfg:       cfg,
		eventChan: make(chan *model.AnalyticsEvent, cfg.BufferCapacity),
		sleep:     sleepCtx,
	}
	w.insert = w.insertClickHouse
	w.stopCtx, w.stopCancel = context.WithCancel(context.Background())
	if cfg.SpillDir != "" {
		spill, err := NewFileSpill(cfg.SpillDir)
		if err != nil {
			log.Printf("CRITICAL: no se puede usar el directorio de lotes pendientes %s, los lotes que fallen se perderán: %v", cfg.SpillDir, err)
		} else {
			w.spill = spill
		}
	}
	return w
}

func (w *ClickHouseWorker) Start() {
	for i := 0; i < w.cfg.NumWorkers; i++ {
		w.wg.Add(1)
		go w.run(i)
	}
	if w.spill != nil {
		w.replayDone = make(chan struct{})
		go w.replayLoop()
	}
	log.Printf("Iniciados %d workers de ClickHouse (lote %d, flush %s, buffer %d, %d reintentos, lotes pendientes en %q)",
		w.cfg.NumWorkers, w.cfg.BatchSize, w.cfg.FlushInterval, w.cfg.BufferCapacity, w.cfg.InsertRetries, w.cfg.SpillDir)
}

// Stop drains the buffer and inserts what is left. While stopping, a failing INSERT is not retried with
// backoff: the batch is spilled at once so shutdown does not wait for ClickHouse.
func (w *ClickHouseWorker) Stop() {
	w.stopping.Store(true)
	w.stopCancel()
	close(w.eventChan)
	w.wg.Wait()
	if w.replayDone != nil {
		<-w.replayDone
	}
	log.Println("Todos los workers de ClickHouse se han detenido de forma segura.")
}

// QueueDepth returns the events waiting in the buffer.
func (w *ClickHouseWorker) QueueDepth() int {
	return len(w.eventChan)
}

// QueueCapacity is the size of the event buffer.
func (w *ClickHouseWorker) QueueCapacity() int {
	return cap(w.eventChan)
}

// Enqueue adds the event to the buffer without blocking. When the buffer is full it returns ErrBufferFull
// at once, so the client gets a fast 503 instead of a request that hangs until a worker frees a slot.
func (w *ClickHouseWorker) Enqueue(event *model.AnalyticsEvent) error {
	select {
	case w.eventChan <- event:
		return nil
	default:
		w.warnMu.Lock()
		if time.Since(w.lastFullWarning) >= 10*time.Second {
			w.lastFullWarning = time.Now()
			log.Printf("WARNING: Buffer de eventos lleno (%d), respondiendo 503", cap(w.eventChan))
		}
		w.warnMu.Unlock()
		return ErrBufferFull
	}
}

func (w *ClickHouseWorker) run(workerID int) {
	defer w.wg.Done()

	batch := make([]*model.AnalyticsEvent, 0, w.cfg.BatchSize)
	ticker := time.NewTicker(w.cfg.FlushInterval)
	defer ticker.Stop()

	for {
		select {
		case event, ok := <-w.eventChan:
			if !ok {
				if len(batch) > 0 {
					w.flush(batch)
				}
				return
			}
			batch = append(batch, event)

			if len(batch) >= w.cfg.BatchSize {
				w.flush(batch)
				batch = make([]*model.AnalyticsEvent, 0, w.cfg.BatchSize)
			}
		case <-ticker.C:
			if len(batch) > 0 {
				w.flush(batch)
				batch = make([]*model.AnalyticsEvent, 0, w.cfg.BatchSize)
			}
		}
	}
}

// flush inserts the batch, retrying with exponential backoff. Every attempt uses the same deduplication
// token, so an attempt that ClickHouse stored but whose answer was lost does not duplicate rows. If every
// attempt fails, the batch is saved to the spill directory and replayed later; it is only lost if that
// fails too.
func (w *ClickHouseWorker) flush(batch []*model.AnalyticsEvent) {
	w.flushWithToken(batch, uuid.NewString())
}

// flushWithToken is flush with the caller's deduplication token. It returns true when the batch is safe:
// inserted into ClickHouse or saved in the spill directory. False means it was lost; a caller that still has
// the events elsewhere (the Redis stream) keeps them and tries again later.
func (w *ClickHouseWorker) flushWithToken(batch []*model.AnalyticsEvent, token string) bool {
	start := time.Now()
	spanCtx, span := tracer.Start(context.Background(), "clickhouse.insert_batch", trace.WithAttributes(
		attribute.Int("tempoo.batch.size", len(batch)),
		attribute.String("tempoo.batch.token", token),
		attribute.Int("tempoo.queue.depth", w.QueueDepth()),
	))
	outcome := "lost"
	defer func() {
		span.SetAttributes(attribute.String("tempoo.batch.outcome", outcome))
		span.End()
	}()
	var err error
	for attempt := 0; ; attempt++ {
		ctx, cancel := context.WithTimeout(spanCtx, w.cfg.InsertTimeout)
		err = w.insert(ctx, batch, token)
		cancel()
		if err == nil {
			metrics.BatchFlushDuration.Observe(time.Since(start).Seconds())
			metrics.BatchSize.Observe(float64(len(batch)))
			outcome = "inserted"
			span.SetAttributes(attribute.Int("tempoo.batch.retries", attempt))
			if attempt > 0 {
				log.Printf("Lote de %d eventos insertado en ClickHouse tras %d reintentos", len(batch), attempt)
			}
			return true
		}
		metrics.InsertErrors.Inc()
		span.AddEvent("insert failed", trace.WithAttributes(attribute.Int("attempt", attempt+1), attribute.String("error", err.Error())))
		if attempt >= w.cfg.InsertRetries || w.stopping.Load() {
			break
		}
		backoff := insertBackoffBase << attempt
		if backoff > insertBackoffMax || backoff <= 0 {
			backoff = insertBackoffMax
		}
		log.Printf("Error insertando lote de %d eventos en ClickHouse (intento %d/%d), reintento en %s: %v",
			len(batch), attempt+1, w.cfg.InsertRetries+1, backoff, err)
		if !w.sleep(w.stopCtx, backoff) {
			break // stopping: spill now
		}
	}

	if w.spill != nil {
		if serr := w.spill.Save(token, batch); serr == nil {
			log.Printf("ClickHouse no acepta el lote de %d eventos (%v): guardado en %s para reenviarlo", len(batch), err, w.cfg.SpillDir)
			outcome = "spilled"
			span.SetStatus(codes.Error, err.Error())
			metrics.SpillPendingBatches.Set(float64(w.spill.Pending()))
			return true
		} else {
			err = fmt.Errorf("%w; además no se pudo guardar el lote: %v", err, serr)
		}
	}
	metrics.EventsLost.Add(float64(len(batch)))
	span.SetStatus(codes.Error, err.Error())
	log.Printf("CRITICAL: %d eventos perdidos, no se pudieron insertar en ClickHouse: %v", len(batch), err)
	return false
}

// replayLoop retries the spilled batches every SpillReplayInterval, oldest first. It stops at the first
// failure (ClickHouse is probably still down) and tries again next time.
func (w *ClickHouseWorker) replayLoop() {
	defer close(w.replayDone)
	w.replaySpilled() // batches left by a previous run
	ticker := time.NewTicker(w.cfg.SpillReplayInterval)
	defer ticker.Stop()
	for {
		select {
		case <-w.stopCtx.Done():
			return
		case <-ticker.C:
			w.replaySpilled()
		}
	}
}

func (w *ClickHouseWorker) replaySpilled() {
	names, err := w.spill.List()
	if err != nil {
		log.Printf("Error listando lotes pendientes en %s: %v", w.cfg.SpillDir, err)
		return
	}
	metrics.SpillPendingBatches.Set(float64(len(names)))
	for _, name := range names {
		if w.stopCtx.Err() != nil {
			return
		}
		token, events, err := w.spill.Load(name)
		if err != nil {
			log.Printf("CRITICAL: lote pendiente ilegible %s, se aparta: %v", name, err)
			w.spill.Quarantine(name)
			continue
		}
		spanCtx, span := tracer.Start(w.stopCtx, "clickhouse.replay_batch", trace.WithAttributes(
			attribute.Int("tempoo.batch.size", len(events)),
			attribute.String("tempoo.batch.token", token),
		))
		ctx, cancel := context.WithTimeout(spanCtx, w.cfg.InsertTimeout)
		err = w.insert(ctx, events, token)
		cancel()
		if err != nil {
			span.SetStatus(codes.Error, err.Error())
		}
		span.End()
		if err != nil {
			metrics.InsertErrors.Inc()
			log.Printf("Lote pendiente %s aún no se puede insertar: %v", name, err)
			return
		}
		if err := w.spill.Remove(name); err != nil {
			log.Printf("Error borrando el lote pendiente %s (se reintentará; el token evita duplicados): %v", name, err)
		}
		metrics.SpillReplayedEvents.Add(float64(len(events)))
		log.Printf("Lote pendiente %s reenviado: %d eventos", name, len(events))
	}
	metrics.SpillPendingBatches.Set(float64(w.spill.Pending()))
}

// insertClickHouse sends the batch with insert_deduplication_token: with non_replicated_deduplication_window
// set on events (migration 005), ClickHouse ignores a repeated block with the same token.
func (w *ClickHouseWorker) insertClickHouse(ctx context.Context, batch []*model.AnalyticsEvent, token string) error {
	ctx = clickhouse.Context(ctx, clickhouse.WithSettings(clickhouse.Settings{
		"insert_deduplication_token": token,
	}))

	query := `INSERT INTO tempoo_analytics.events (
		event_id, site_id, user_hash, visitor_id, session_id, timestamp, date, country_code, city_name,
		device_type, os, browser, screen_width, screen_height, referrer, referrer_domain, utm_source,
		utm_medium, utm_campaign, event_name, path, page_title, traffic_type, bot_name, company_name, company_domain, asn, user_agent, data
	)`

	b, err := w.conn.PrepareBatch(ctx, query)
	if err != nil {
		return fmt.Errorf("preparando el lote: %w", err)
	}

	for _, event := range batch {
		err := b.Append(
			event.EventID,
			event.SiteID,
			event.UserHash,
			event.VisitorID,
			event.SessionID,
			event.Timestamp,
			event.Date,
			event.CountryCode,
			event.CityName,
			event.DeviceType,
			event.OS,
			event.Browser,
			event.ScreenWidth,
			event.ScreenHeight,
			event.Referrer,
			event.ReferrerDomain,
			event.UtmSource,
			event.UtmMedium,
			event.UtmCampaign,
			event.EventName,
			event.Path,
			event.PageTitle,
			event.TrafficType,
			event.BotName,
			event.CompanyName,
			event.CompanyDomain,
			event.ASN,
			event.UserAgent,
			event.Data,
		)
		if err != nil {
			metrics.EventsLost.Inc()
			log.Printf("Error haciendo append al batch (evento %s descartado): %v", event.EventID, err)
		}
	}

	// Sending is synchronous. It blocks only this particular worker, not the others.
	if err := b.Send(); err != nil {
		return fmt.Errorf("enviando el lote: %w", err)
	}
	return nil
}

// sleepCtx waits d or until ctx is done; it returns false if ctx ended first.
func sleepCtx(ctx context.Context, d time.Duration) bool {
	t := time.NewTimer(d)
	defer t.Stop()
	select {
	case <-t.C:
		return true
	case <-ctx.Done():
		return false
	}
}
