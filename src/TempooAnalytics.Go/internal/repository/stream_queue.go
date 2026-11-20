package repository

import (
	"context"
	"crypto/sha1"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"os"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/metrics"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/model"
	"github.com/google/uuid"
	"github.com/redis/go-redis/v9"
)

// Defaults of StreamConfig (INGEST_STREAM_KEY, INGEST_STREAM_MAXLEN, INGEST_STREAM_CLAIM_IDLE).
const (
	StreamKey       = "tempoo:ingest:events"
	StreamGroup     = "ingest"
	StreamMaxLen    = 1_000_000
	StreamClaimIdle = 60 * time.Second

	streamField       = "e"
	streamPublishWait = 150 * time.Millisecond
	streamDepthEvery  = 500 * time.Millisecond
	streamReadBlock   = 200 * time.Millisecond

	streamPublishers = 4   // goroutines that send XADD pipelines
	streamPublishMax = 256 // XADDs per pipeline
)

// StreamConfig sets up the Redis stream queue (INGEST_QUEUE=redis).
type StreamConfig struct {
	Key       string
	Group     string
	MaxLen    int64         // new events get 503 (ErrBufferFull) when the stream holds this many
	ClaimIdle time.Duration // entries a consumer read but did not acknowledge for this long are taken over
	Consumer  string        // this instance's name in the group; default hostname-pid
}

func (c StreamConfig) withDefaults() StreamConfig {
	if c.Key == "" {
		c.Key = StreamKey
	}
	if c.Group == "" {
		c.Group = StreamGroup
	}
	if c.MaxLen <= 0 {
		c.MaxLen = StreamMaxLen
	}
	if c.ClaimIdle <= 0 {
		c.ClaimIdle = StreamClaimIdle
	}
	if c.Consumer == "" {
		host, _ := os.Hostname()
		c.Consumer = fmt.Sprintf("%s-%d", host, os.Getpid())
	}
	return c
}

// StreamQueue keeps the accepted events in a Redis stream until ClickHouse has them, so a crash or kill of
// ingestion does not lose what was in memory, and several ingestion instances can share the work.
//
// Producer: Enqueue does XADD before the request is answered 202. Redis is the durability: run it with AOF
// (appendfsync everysec) and the loss window is about one second of Redis writes, not the flush interval.
// Consumers: each worker reads batches with XREADGROUP, inserts them into ClickHouse (retries and spill
// directory as with the in-memory buffer) and only then acknowledges and deletes the entries. Entries left
// pending by a dead instance are taken over after ClaimIdle.
//
// At-least-once: a crash between the INSERT and the acknowledgement makes another consumer insert the entries
// again. The deduplication token is derived from the entry ids, so ClickHouse ignores the repeat when the
// batch has the same entries; when it is split differently the rows can be duplicated (rare: it needs a
// crash during an INSERT).
type StreamQueue struct {
	rdb    *redis.Client
	cfg    StreamConfig
	worker *ClickHouseWorker // inserts the batches (retries, spill)

	pub chan pubReq // events waiting for the publishers, which send them in pipelines

	// existing returns which of the events are already in ClickHouse. It is used on entries delivered again
	// (pending of a dead consumer, ack lost with Redis down) so that they are not inserted twice: the batch
	// token only protects a repeat of the very same batch. nil disables the check.
	existing func(ctx context.Context, events []*model.AnalyticsEvent) (map[uuid.UUID]bool, error)

	depth    atomic.Int64
	stopCtx  context.Context
	stopFn   context.CancelFunc
	wg       sync.WaitGroup
	numReads int

	warnMu   sync.Mutex
	lastWarn time.Time
}

// NewStreamQueue creates the consumer group if needed. It fails when Redis cannot be reached, so the caller
// can fall back to the in-memory buffer at startup.
func NewStreamQueue(ctx context.Context, rdb *redis.Client, cfg StreamConfig, worker *ClickHouseWorker) (*StreamQueue, error) {
	cfg = cfg.withDefaults()
	err := rdb.XGroupCreateMkStream(ctx, cfg.Key, cfg.Group, "0").Err()
	if err != nil && !strings.Contains(err.Error(), "BUSYGROUP") {
		return nil, fmt.Errorf("creating the consumer group %s on %s: %w", cfg.Group, cfg.Key, err)
	}
	q := &StreamQueue{rdb: rdb, cfg: cfg, worker: worker, numReads: worker.cfg.NumWorkers,
		pub: make(chan pubReq, streamPublishers*streamPublishMax)}
	if worker.conn != nil {
		q.existing = q.existingInClickHouse
	}
	q.stopCtx, q.stopFn = context.WithCancel(context.Background())
	for i := 0; i < streamPublishers; i++ { // events can be enqueued before Start
		q.wg.Add(1)
		go q.publish()
	}
	return q, nil
}

// Start runs the consumers (one per worker) and the depth sampler.
func (q *StreamQueue) Start() {
	q.wg.Add(1)
	go q.sampleDepth()
	for i := 0; i < q.numReads; i++ {
		q.wg.Add(1)
		go q.consume(fmt.Sprintf("%s-%d", q.cfg.Consumer, i))
	}
	log.Printf("Cola persistente en Redis: stream %s, grupo %s, %d consumidores, máximo %d eventos, takeover tras %s",
		q.cfg.Key, q.cfg.Group, q.numReads, q.cfg.MaxLen, q.cfg.ClaimIdle)
	q.warnIfNoAOF()
}

// Stop stops the consumers; entries not acknowledged yet stay in the stream for the next start.
func (q *StreamQueue) Stop() {
	q.worker.stopping.Store(true) // a failing INSERT spills at once instead of retrying with backoff
	q.stopFn()
	q.wg.Wait()
}

// QueueDepth is the stream length sampled twice a second: events accepted and not yet in ClickHouse.
func (q *StreamQueue) QueueDepth() int { return int(q.depth.Load()) }

// QueueCapacity is StreamConfig.MaxLen.
func (q *StreamQueue) QueueCapacity() int { return int(q.cfg.MaxLen) }

// Enqueue appends the event to the stream. It returns ErrBufferFull when the stream is at MaxLen, and any
// other error when Redis did not take the event in time; the caller then keeps it in memory.
// The XADDs of concurrent requests travel in one pipeline, so Redis sees one round trip per group of events
// instead of one per request; every request still waits for its own XADD to be answered.
func (q *StreamQueue) Enqueue(ctx context.Context, event *model.AnalyticsEvent) error {
	if q.depth.Load() >= q.cfg.MaxLen {
		return ErrBufferFull
	}
	payload, err := json.Marshal(event)
	if err != nil {
		return fmt.Errorf("encoding the event: %w", err)
	}
	ctx, cancel := context.WithTimeout(ctx, streamPublishWait)
	defer cancel()
	req := pubReq{payload: payload, res: make(chan error, 1)}
	select {
	case q.pub <- req:
	case <-ctx.Done():
		return ctx.Err()
	case <-q.stopCtx.Done():
		return errors.New("stream queue stopped")
	}
	select {
	case err := <-req.res:
		return err
	case <-ctx.Done():
		return ctx.Err()
	}
}

type pubReq struct {
	payload []byte
	res     chan error // buffered: the publisher never blocks on an abandoned request
}

// publish takes the waiting events (up to streamPublishMax) and sends their XADDs in one pipeline.
func (q *StreamQueue) publish() {
	defer q.wg.Done()
	batch := make([]pubReq, 0, streamPublishMax)
	for {
		select {
		case <-q.stopCtx.Done():
			return
		case r := <-q.pub:
			batch = append(batch[:0], r)
		}
	fill:
		for len(batch) < streamPublishMax {
			select {
			case r := <-q.pub:
				batch = append(batch, r)
			default:
				break fill
			}
		}
		ctx, cancel := context.WithTimeout(context.Background(), streamPublishWait)
		pipe := q.rdb.Pipeline()
		cmds := make([]*redis.StringCmd, len(batch))
		for i, r := range batch {
			cmds[i] = pipe.XAdd(ctx, &redis.XAddArgs{Stream: q.cfg.Key, Values: map[string]any{streamField: r.payload}})
		}
		_, execErr := pipe.Exec(ctx)
		cancel()
		for i, r := range batch {
			err := cmds[i].Err()
			if err == nil && cmds[i].Val() == "" {
				// go-redis leaves Err()=nil on every command when the connection fails: only an entry id
				// means Redis took the event, otherwise the caller must keep it in memory.
				if err = execErr; err == nil {
					err = errors.New("redis did not confirm the XADD")
				}
			}
			r.res <- err
		}
	}
}

func (q *StreamQueue) sampleDepth() {
	defer q.wg.Done()
	t := time.NewTicker(streamDepthEvery)
	defer t.Stop()
	for {
		ctx, cancel := context.WithTimeout(q.stopCtx, time.Second)
		if n, err := q.rdb.XLen(ctx, q.cfg.Key).Result(); err == nil {
			q.depth.Store(n)
		}
		cancel()
		select {
		case <-q.stopCtx.Done():
			return
		case <-t.C:
		}
	}
}

// consume is one consumer: first its own pending entries and those of dead consumers, then new ones.
func (q *StreamQueue) consume(name string) {
	defer q.wg.Done()
	batchSize := q.worker.cfg.BatchSize
	flushEvery := q.worker.cfg.FlushInterval

	// Our own pending entries (read before a restart with the same consumer name).
	q.drainPending(name, batchSize)

	var (
		msgs      []redis.XMessage
		batchAt   = time.Now()
		lastClaim time.Time
	)
	for q.stopCtx.Err() == nil {
		// Take over what a dead or stuck consumer left, every ClaimIdle/2.
		if time.Since(lastClaim) >= q.cfg.ClaimIdle/2 {
			lastClaim = time.Now()
			q.claimIdle(name, batchSize)
		}
		want := int64(batchSize - len(msgs))
		res, err := q.rdb.XReadGroup(q.stopCtx, &redis.XReadGroupArgs{
			Group: q.cfg.Group, Consumer: name, Streams: []string{q.cfg.Key, ">"}, Count: want, Block: streamReadBlock,
		}).Result()
		switch {
		case err == nil:
			for _, s := range res {
				msgs = append(msgs, s.Messages...)
			}
		case errors.Is(err, redis.Nil) || errors.Is(err, context.Canceled):
		default:
			q.warn("leyendo el stream", err)
			q.sleep(time.Second)
			continue
		}
		if len(msgs) >= batchSize || (len(msgs) > 0 && time.Since(batchAt) >= flushEvery) {
			q.process(msgs, false)
			msgs = nil
		}
		if len(msgs) == 0 {
			batchAt = time.Now()
		}
	}
	if len(msgs) > 0 {
		q.process(msgs, false) // stopping: spills at once if ClickHouse is down; entries stay pending otherwise
	}
}

// drainPending processes the entries this consumer name already read but never acknowledged.
func (q *StreamQueue) drainPending(name string, batchSize int) {
	for q.stopCtx.Err() == nil {
		res, err := q.rdb.XReadGroup(q.stopCtx, &redis.XReadGroupArgs{
			Group: q.cfg.Group, Consumer: name, Streams: []string{q.cfg.Key, "0"}, Count: int64(batchSize),
		}).Result()
		if err != nil || len(res) == 0 || len(res[0].Messages) == 0 {
			return
		}
		metrics.StreamRecoveredEvents.Add(float64(len(res[0].Messages)))
		q.process(res[0].Messages, true)
	}
}

// claimIdle takes over entries that another consumer read and did not acknowledge for ClaimIdle.
func (q *StreamQueue) claimIdle(name string, batchSize int) {
	start := "0-0"
	for q.stopCtx.Err() == nil {
		msgs, next, err := q.rdb.XAutoClaim(q.stopCtx, &redis.XAutoClaimArgs{
			Stream: q.cfg.Key, Group: q.cfg.Group, Consumer: name, MinIdle: q.cfg.ClaimIdle, Start: start, Count: int64(batchSize),
		}).Result()
		if err != nil {
			if q.stopCtx.Err() == nil {
				q.warn("recuperando entradas pendientes", err)
			}
			return
		}
		if len(msgs) > 0 {
			metrics.StreamRecoveredEvents.Add(float64(len(msgs)))
			q.process(msgs, true)
		}
		if next == "0-0" || len(msgs) == 0 {
			return
		}
		start = next
	}
}

// process inserts the entries and acknowledges them. Entries that cannot be decoded are dropped (they cannot
// be inserted ever). When the batch is not safe (neither inserted nor spilled), the entries stay pending and
// are taken over later.
func (q *StreamQueue) process(msgs []redis.XMessage, redelivered bool) {
	events := make([]*model.AnalyticsEvent, 0, len(msgs))
	ids := make([]string, 0, len(msgs))
	for _, m := range msgs {
		ids = append(ids, m.ID)
		raw, _ := m.Values[streamField].(string)
		var e model.AnalyticsEvent
		if err := json.Unmarshal([]byte(raw), &e); err != nil {
			log.Printf("CRITICAL: entrada %s del stream ilegible, se descarta: %v", m.ID, err)
			metrics.EventsLost.Inc()
			continue
		}
		events = append(events, &e)
	}
	if redelivered && len(events) > 0 && q.existing != nil {
		events = q.withoutStored(events)
	}
	if len(events) > 0 && !q.worker.flushWithToken(events, streamToken(ids)) {
		return
	}
	// The ack must not depend on the request or shutdown context: the batch is already safe.
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	pipe := q.rdb.Pipeline()
	pipe.XAck(ctx, q.cfg.Key, q.cfg.Group, ids...)
	pipe.XDel(ctx, q.cfg.Key, ids...)
	if _, err := pipe.Exec(ctx); err != nil {
		// The batch is in ClickHouse; the entries will be read again and the token makes the repeat harmless.
		q.warn("confirmando el lote", err)
	}
}

// withoutStored drops the events that ClickHouse already has. When the lookup fails the batch goes on as it
// is: a possible duplicate is better than holding the entries.
func (q *StreamQueue) withoutStored(events []*model.AnalyticsEvent) []*model.AnalyticsEvent {
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	have, err := q.existing(ctx, events)
	if err != nil {
		q.warn("comprobando eventos ya insertados", err)
		return events
	}
	if len(have) == 0 {
		return events
	}
	kept := events[:0]
	for _, e := range events {
		if !have[e.EventID] {
			kept = append(kept, e)
		}
	}
	metrics.StreamRedeliveredSkipped.Add(float64(len(events) - len(kept)))
	return kept
}

// existingInClickHouse looks the event ids up in ClickHouse, limited to the dates of the batch.
func (q *StreamQueue) existingInClickHouse(ctx context.Context, events []*model.AnalyticsEvent) (map[uuid.UUID]bool, error) {
	ids := make([]uuid.UUID, len(events))
	minD, maxD := events[0].Date, events[0].Date
	for i, e := range events {
		ids[i] = e.EventID
		if e.Date.Before(minD) {
			minD = e.Date
		}
		if e.Date.After(maxD) {
			maxD = e.Date
		}
	}
	rows, err := q.worker.conn.Query(ctx,
		"SELECT event_id FROM tempoo_analytics.events WHERE date BETWEEN ? AND ? AND event_id IN (?)", minD, maxD, ids)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	have := make(map[uuid.UUID]bool)
	for rows.Next() {
		var id uuid.UUID
		if err := rows.Scan(&id); err != nil {
			return nil, err
		}
		have[id] = true
	}
	return have, rows.Err()
}

// streamToken is the deduplication token of a batch: the same entries give the same token.
func streamToken(ids []string) string {
	h := sha1.Sum([]byte(strings.Join(ids, ",")))
	return "s-" + hex.EncodeToString(h[:12])
}

func (q *StreamQueue) sleep(d time.Duration) {
	select {
	case <-q.stopCtx.Done():
	case <-time.After(d):
	}
}

func (q *StreamQueue) warn(what string, err error) {
	q.warnMu.Lock()
	defer q.warnMu.Unlock()
	if time.Since(q.lastWarn) >= 10*time.Second {
		q.lastWarn = time.Now()
		log.Printf("WARNING: cola de Redis, error %s: %v", what, err)
	}
}

// warnIfNoAOF logs when Redis would lose the queue on a restart: without AOF the stream lives only in memory.
func (q *StreamQueue) warnIfNoAOF() {
	ctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
	defer cancel()
	res, err := q.rdb.ConfigGet(ctx, "appendonly").Result()
	if err != nil {
		return // CONFIG may be disabled; nothing to say
	}
	if v := res["appendonly"]; v != "yes" {
		log.Printf("WARNING: Redis sin AOF (appendonly=%q): la cola en Redis se pierde si Redis se reinicia. Actívalo con appendonly yes y appendfsync everysec.", v)
	}
}
