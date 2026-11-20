package repository

import (
	"context"
	"os"
	"strings"
	"testing"
	"time"

	"github.com/ClickHouse/clickhouse-go/v2"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/model"
	"github.com/google/uuid"
)

// Runs only with CLICKHOUSE_TEST_ADDR set (host:port of a ClickHouse with the tempoo_analytics.events table
// from scripts/clickhouse/01_base_schema.sql), for example:
//
//	CLICKHOUSE_TEST_ADDR=localhost:9000 go test ./internal/repository -run Integration
func TestIntegration_CountMonthlyEvents(t *testing.T) {
	addr := os.Getenv("CLICKHOUSE_TEST_ADDR")
	if addr == "" {
		t.Skip("CLICKHOUSE_TEST_ADDR not set")
	}
	ctx := context.Background()
	conn, err := clickhouse.Open(&clickhouse.Options{Addr: []string{addr}, Auth: clickhouse.Auth{Database: "tempoo_analytics"}})
	if err != nil {
		t.Fatal(err)
	}
	defer conn.Close()

	siteA, siteB, siteEmpty := uuid.New(), uuid.New(), uuid.New()
	insert := func(site uuid.UUID, date time.Time, n int) {
		b, err := conn.PrepareBatch(ctx, "INSERT INTO tempoo_analytics.events (event_id, site_id, date, timestamp)")
		if err != nil {
			t.Fatal(err)
		}
		for i := 0; i < n; i++ {
			if err := b.Append(uuid.New(), site, date, date); err != nil {
				t.Fatal(err)
			}
		}
		if err := b.Send(); err != nil {
			t.Fatal(err)
		}
	}
	nov := time.Date(2026, 11, 1, 0, 0, 0, 0, time.UTC)
	insert(siteA, nov, 3)
	insert(siteA, time.Date(2026, 11, 30, 0, 0, 0, 0, time.UTC), 2)
	insert(siteA, time.Date(2026, 10, 31, 0, 0, 0, 0, time.UTC), 7) // previous month
	insert(siteA, time.Date(2026, 12, 1, 0, 0, 0, 0, time.UTC), 11) // next month
	insert(siteB, time.Date(2026, 11, 15, 0, 0, 0, 0, time.UTC), 4)

	repo := &ClickHouseRepository{conn: conn}
	counts, err := repo.CountMonthlyEvents(ctx, []string{siteA.String(), siteB.String(), siteEmpty.String()}, nov.Add(13*24*time.Hour))
	if err != nil {
		t.Fatal(err)
	}
	if counts[siteA.String()] != 5 || counts[siteB.String()] != 4 {
		t.Fatalf("counts = %v, want %s=5 %s=4", counts, siteA, siteB)
	}
	if _, ok := counts[siteEmpty.String()]; ok {
		t.Fatalf("site without events in the map: %v", counts)
	}
}

// Needs migration 005_events_insert_dedup.sql applied (non_replicated_deduplication_window on events).
func TestIntegration_RetriedBatchWithSameTokenIsStoredOnce(t *testing.T) {
	addr := os.Getenv("CLICKHOUSE_TEST_ADDR")
	if addr == "" {
		t.Skip("CLICKHOUSE_TEST_ADDR not set")
	}
	ctx := context.Background()
	conn, err := clickhouse.Open(&clickhouse.Options{Addr: []string{addr}, Auth: clickhouse.Auth{Database: "tempoo_analytics"}})
	if err != nil {
		t.Fatal(err)
	}
	defer conn.Close()

	site := uuid.New()
	batch := make([]*model.AnalyticsEvent, 50)
	now := time.Now().UTC().Truncate(time.Second)
	for i := range batch {
		batch[i] = &model.AnalyticsEvent{EventID: uuid.New(), SiteID: site, SessionID: uuid.New(), UserHash: strings.Repeat("a", 64),
			Timestamp: now, Date: now.Truncate(24 * time.Hour), EventName: "pageview", Path: "/", Data: "{}", TrafficType: 1}
	}
	w := NewClickHouseWorker(conn, WorkerConfig{})
	token := uuid.NewString()
	for i := 0; i < 3; i++ { // the original attempt and two retries whose answer was "lost"
		if err := w.insertClickHouse(ctx, batch, token); err != nil {
			t.Fatal(err)
		}
	}
	if err := w.insertClickHouse(ctx, batch, uuid.NewString()); err != nil { // a different batch token
		t.Fatal(err)
	}

	var rows, mvEvents uint64
	if err := conn.QueryRow(ctx, "SELECT count() FROM tempoo_analytics.events WHERE site_id = ?", site).Scan(&rows); err != nil {
		t.Fatal(err)
	}
	if err := conn.QueryRow(ctx, "SELECT sum(total_events) FROM tempoo_analytics.hourly_stats WHERE site_id = ?", site).Scan(&mvEvents); err != nil {
		t.Fatal(err)
	}
	if rows != 100 || mvEvents != 100 {
		t.Fatalf("rows = %d, hourly_stats = %d; want 100 each (same token stored once, new token stored again)", rows, mvEvents)
	}
}
