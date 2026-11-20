package repository

import (
	"context"
	"errors"
	"fmt"
	"time"

	"github.com/ClickHouse/clickhouse-go/v2"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/metrics"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/model"
	"github.com/redis/go-redis/v9"
)

type Repository interface {
	InsertEvent(ctx context.Context, event *model.AnalyticsEvent) error
	InsertHeatmapClicks(ctx context.Context, clicks []*model.HeatmapClick) error
	CountMonthlyEvents(ctx context.Context, siteIDs []string, monthStart time.Time) (map[string]int64, error)
	Close() error
}

type ClickHouseRepository struct {
	conn          clickhouse.Conn
	worker        *ClickHouseWorker
	stream        *StreamQueue // nil unless INGEST_QUEUE=redis
	heatmapWorker *HeatmapWorker
}

func NewClickHouseRepository(conn clickhouse.Conn, cfg WorkerConfig) *ClickHouseRepository {
	worker := NewClickHouseWorker(conn, cfg)
	worker.Start()
	heatmapWorker := NewHeatmapWorker(conn)
	heatmapWorker.Start()
	return &ClickHouseRepository{
		conn:          conn,
		worker:        worker,
		heatmapWorker: heatmapWorker,
	}
}

// EnableStream makes InsertEvent append to the Redis stream and starts its consumers. The in-memory buffer
// stays as the fallback for when Redis does not answer.
func (r *ClickHouseRepository) EnableStream(ctx context.Context, rdb *redis.Client, cfg StreamConfig) error {
	q, err := NewStreamQueue(ctx, rdb, cfg, r.worker)
	if err != nil {
		return err
	}
	q.Start()
	r.stream = q
	return nil
}

// InsertEvent queues the event: in the Redis stream when enabled, otherwise (or if Redis fails) in memory.
func (r *ClickHouseRepository) InsertEvent(ctx context.Context, event *model.AnalyticsEvent) error {
	if r.stream != nil {
		err := r.stream.Enqueue(ctx, event)
		if err == nil || errors.Is(err, ErrBufferFull) {
			return err
		}
		r.stream.warn("añadiendo al stream (se guarda en memoria)", err)
		metrics.StreamFallbackEvents.Inc()
	}
	return r.worker.Enqueue(event)
}

// QueueDepth returns the events accepted and not yet in ClickHouse (stream plus memory buffer).
func (r *ClickHouseRepository) QueueDepth() int {
	n := r.worker.QueueDepth()
	if r.stream != nil {
		n += r.stream.QueueDepth()
	}
	return n
}

// QueueCapacity returns the size of the queue (stream plus memory buffer).
func (r *ClickHouseRepository) QueueCapacity() int {
	n := r.worker.QueueCapacity()
	if r.stream != nil {
		n += r.stream.QueueCapacity()
	}
	return n
}

func (r *ClickHouseRepository) InsertHeatmapClicks(ctx context.Context, clicks []*model.HeatmapClick) error {
	r.heatmapWorker.Enqueue(clicks)
	return nil
}

// CountMonthlyEvents counts the events of each site with a date in the month that starts at monthStart
// (UTC), in one query. The quota uses it to initialise and reconcile its Redis counters; it is never called
// once per event.
func (r *ClickHouseRepository) CountMonthlyEvents(ctx context.Context, siteIDs []string, monthStart time.Time) (map[string]int64, error) {
	counts := make(map[string]int64, len(siteIDs))
	if len(siteIDs) == 0 {
		return counts, nil
	}
	from := time.Date(monthStart.Year(), monthStart.Month(), 1, 0, 0, 0, 0, time.UTC)
	to := from.AddDate(0, 1, 0)
	query := `
		SELECT toString(site_id) AS site, count() AS events
		FROM tempoo_analytics.events
		WHERE site_id IN (?)
		  AND date >= toDate(?) AND date < toDate(?)
		GROUP BY site_id
	`
	rows, err := r.conn.Query(ctx, query, siteIDs, from.Format("2006-01-02"), to.Format("2006-01-02"))
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	for rows.Next() {
		var site string
		var events uint64
		if err := rows.Scan(&site, &events); err != nil {
			return nil, fmt.Errorf("reading monthly counts: %w", err)
		}
		counts[site] = int64(events)
	}
	return counts, rows.Err()
}

func (r *ClickHouseRepository) Close() error {
	if r.stream != nil {
		r.stream.Stop()
	}
	r.worker.Stop()
	r.heatmapWorker.Stop()
	return nil
}
