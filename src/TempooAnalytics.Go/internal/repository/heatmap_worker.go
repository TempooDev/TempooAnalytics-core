package repository

import (
	"context"
	"log"
	"sync"
	"time"

	"github.com/ClickHouse/clickhouse-go/v2"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/model"
)

// HeatmapWorker batches heatmap_clicks rows the same way ClickHouseWorker batches events.
// A single goroutine is enough: at most one small batch per page view.
type HeatmapWorker struct {
	conn      clickhouse.Conn
	clickChan chan []*model.HeatmapClick
	wg        sync.WaitGroup
}

func NewHeatmapWorker(conn clickhouse.Conn) *HeatmapWorker {
	return &HeatmapWorker{
		conn:      conn,
		clickChan: make(chan []*model.HeatmapClick, BufferCapacity/50),
	}
}

func (w *HeatmapWorker) Start() {
	w.wg.Add(1)
	go w.run()
}

func (w *HeatmapWorker) Stop() {
	close(w.clickChan)
	w.wg.Wait()
}

func (w *HeatmapWorker) Enqueue(clicks []*model.HeatmapClick) {
	select {
	case w.clickChan <- clicks:
	default:
		// Heatmaps are best effort: drop instead of blocking the HTTP request.
		log.Println("WARNING: Buffer de mapas de calor lleno, descartando lote")
	}
}

func (w *HeatmapWorker) run() {
	defer w.wg.Done()

	batch := make([]*model.HeatmapClick, 0, BatchSize)
	ticker := time.NewTicker(FlushInterval)
	defer ticker.Stop()

	for {
		select {
		case clicks, ok := <-w.clickChan:
			if !ok {
				if len(batch) > 0 {
					w.flush(batch)
				}
				return
			}
			batch = append(batch, clicks...)
			if len(batch) >= BatchSize {
				w.flush(batch)
				batch = batch[:0]
			}
		case <-ticker.C:
			if len(batch) > 0 {
				w.flush(batch)
				batch = batch[:0]
			}
		}
	}
}

func (w *HeatmapWorker) flush(batch []*model.HeatmapClick) {
	ctx := context.Background()

	b, err := w.conn.PrepareBatch(ctx, `INSERT INTO tempoo_analytics.heatmap_clicks (
		site_id, date, path, device_type, kind, x, y, viewport_width, selector
	)`)
	if err != nil {
		log.Printf("Error preparando batch de mapas de calor en ClickHouse: %v", err)
		return
	}

	for _, c := range batch {
		if err := b.Append(c.SiteID, c.Date, c.Path, c.DeviceType, c.Kind, c.X, c.Y, c.ViewportWidth, c.Selector); err != nil {
			log.Printf("Error haciendo append al batch de mapas de calor: %v", err)
		}
	}

	if err := b.Send(); err != nil {
		log.Printf("CRITICAL: Error enviando batch de mapas de calor a ClickHouse: %v", err)
	}
}
