package service

import (
	"context"
	"errors"
	"net/http/httptest"
	"strings"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/repository"
	"github.com/alicebob/miniredis/v2"
	"github.com/redis/go-redis/v9"
)

// fakeCounter stands in for ClickHouse's monthly count.
type fakeCounter struct {
	mu     sync.Mutex
	counts map[string]int64
	err    error
	calls  atomic.Int64
	// during runs inside CountMonthlyEvents, to simulate events counted while ClickHouse is asked.
	during func()
	// block, if set, makes CountMonthlyEvents wait until it is closed.
	block     chan struct{}
	lastMonth time.Time
	lastSites []string
}

func (f *fakeCounter) CountMonthlyEvents(_ context.Context, siteIDs []string, month time.Time) (map[string]int64, error) {
	f.calls.Add(1)
	if f.block != nil {
		<-f.block
	}
	if f.during != nil {
		f.during()
	}
	f.mu.Lock()
	defer f.mu.Unlock()
	f.lastMonth = month
	f.lastSites = append([]string(nil), siteIDs...)
	if f.err != nil {
		return nil, f.err
	}
	out := map[string]int64{}
	for _, id := range siteIDs {
		if c, ok := f.counts[id]; ok {
			out[id] = c
		}
	}
	return out, nil
}

var quotaNow = time.Date(2026, 11, 10, 14, 0, 0, 0, time.UTC)

func newTestQuota(t *testing.T, counter *fakeCounter) (*QuotaCounter, *miniredis.Miniredis, *redis.Client) {
	t.Helper()
	mr := miniredis.RunT(t)
	rdb := redis.NewClient(&redis.Options{Addr: mr.Addr()})
	t.Cleanup(func() { _ = rdb.Close() })
	q := NewQuotaCounter(rdb, counter)
	q.now = func() time.Time { return quotaNow }
	return q, mr, rdb
}

func TestQuota_NoRedisIsUnchecked(t *testing.T) {
	q := NewQuotaCounter(nil, &fakeCounter{})
	d, err := q.Check(context.Background(), "site", 10, 1)
	if d != QuotaUnchecked || err != nil {
		t.Fatalf("got %v, %v; want unchecked without error", d, err)
	}
}

func TestQuota_InitialisesOnceFromClickHouse(t *testing.T) {
	counter := &fakeCounter{counts: map[string]int64{"site-a": 10}}
	q, mr, _ := newTestQuota(t, counter)
	ctx := context.Background()

	for i := 0; i < 50; i++ {
		d, err := q.Check(ctx, "site-a", 1000, 1)
		if d != QuotaAllowed || err != nil {
			t.Fatalf("check %d: got %v, %v", i, d, err)
		}
	}
	if got := counter.calls.Load(); got != 1 {
		t.Fatalf("ClickHouse asked %d times, want 1", got)
	}
	if !counter.lastMonth.Equal(time.Date(2026, 11, 1, 0, 0, 0, 0, time.UTC)) {
		t.Fatalf("asked for month %v", counter.lastMonth)
	}
	key := "quota:site-a:202611"
	if v, _ := mr.Get(key); v != "60" {
		t.Fatalf("counter = %s, want 60 (10 from ClickHouse + 50)", v)
	}
	if ttl := mr.TTL(key); ttl < 39*24*time.Hour || ttl > 40*24*time.Hour {
		t.Fatalf("counter TTL = %v, want about 40 days", ttl)
	}
}

func TestQuota_IncrBy(t *testing.T) {
	q, mr, _ := newTestQuota(t, &fakeCounter{})
	d, err := q.Check(context.Background(), "site-a", 1000, 25)
	if d != QuotaAllowed || err != nil {
		t.Fatalf("got %v, %v", d, err)
	}
	if v, _ := mr.Get("quota:site-a:202611"); v != "25" {
		t.Fatalf("counter = %s, want 25", v)
	}
}

func TestQuota_DoesNotOverwriteExistingCounter(t *testing.T) {
	counter := &fakeCounter{counts: map[string]int64{"site-a": 5}}
	q, mr, _ := newTestQuota(t, counter)
	// Another instance already initialised the counter.
	_ = mr.Set("quota:site-a:202611", "500")

	if d, _ := q.Check(context.Background(), "site-a", 1000, 1); d != QuotaAllowed {
		t.Fatalf("got %v", d)
	}
	if counter.calls.Load() != 0 {
		t.Fatal("ClickHouse asked although the counter existed")
	}
	if v, _ := mr.Get("quota:site-a:202611"); v != "501" {
		t.Fatalf("counter = %s, want 501", v)
	}
}

func TestQuota_BlocksAtLimitAndKeepsBlockFiveMinutes(t *testing.T) {
	counter := &fakeCounter{counts: map[string]int64{"site-a": 99}}
	q, mr, _ := newTestQuota(t, counter)
	ctx := context.Background()

	if d, _ := q.Check(ctx, "site-a", 100, 1); d != QuotaAllowed {
		t.Fatalf("100th event: got %v, want allowed", d)
	}
	if d, _ := q.Check(ctx, "site-a", 100, 1); d != QuotaBlocked {
		t.Fatalf("101st event: got %v, want blocked", d)
	}
	if v, _ := mr.Get("quota:site-a:202611"); v != "100" {
		t.Fatalf("counter = %s, want 100 (rejected events are not counted)", v)
	}
	if ttl := mr.TTL("quota:site-a:limit_reached"); ttl != 5*time.Minute {
		t.Fatalf("block TTL = %v, want 5m", ttl)
	}

	// The block holds even if the counter goes down (for example after reconciliation)...
	_ = mr.Set("quota:site-a:202611", "10")
	if d, _ := q.Check(ctx, "site-a", 100, 1); d != QuotaBlocked {
		t.Fatalf("during block: got %v, want blocked", d)
	}
	// ...until it expires.
	mr.FastForward(5*time.Minute + time.Second)
	if d, _ := q.Check(ctx, "site-a", 100, 1); d != QuotaAllowed {
		t.Fatalf("after block: got %v, want allowed", d)
	}
}

func TestQuota_BatchThatDoesNotFitIsBlocked(t *testing.T) {
	q, mr, _ := newTestQuota(t, &fakeCounter{counts: map[string]int64{"site-a": 95}})
	if d, _ := q.Check(context.Background(), "site-a", 100, 10); d != QuotaBlocked {
		t.Fatalf("got %v, want blocked", d)
	}
	if v, _ := mr.Get("quota:site-a:202611"); v != "95" {
		t.Fatalf("counter = %s, want 95", v)
	}
}

func TestQuota_ConcurrentFirstEventsShareOneClickHouseQuery(t *testing.T) {
	counter := &fakeCounter{counts: map[string]int64{"site-a": 7}, block: make(chan struct{})}
	q, mr, _ := newTestQuota(t, counter)

	const n = 20
	var wg sync.WaitGroup
	errs := make(chan error, n)
	for i := 0; i < n; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			if d, err := q.Check(context.Background(), "site-a", 1000, 1); d != QuotaAllowed {
				errs <- errors.Join(errors.New("not allowed: "+d.String()), err)
			}
		}()
	}
	time.Sleep(50 * time.Millisecond)
	close(counter.block)
	wg.Wait()
	close(errs)
	for err := range errs {
		t.Fatal(err)
	}
	if got := counter.calls.Load(); got != 1 {
		t.Fatalf("ClickHouse asked %d times, want 1", got)
	}
	if v, _ := mr.Get("quota:site-a:202611"); v != "27" {
		t.Fatalf("counter = %s, want 27", v)
	}
}

func TestQuota_ClickHouseFailureFailsOpenWithBackoff(t *testing.T) {
	counter := &fakeCounter{err: errors.New("clickhouse down")}
	q, mr, _ := newTestQuota(t, counter)
	ctx := context.Background()

	for i := 0; i < 10; i++ {
		d, err := q.Check(ctx, "site-a", 100, 1)
		if d != QuotaUnchecked || err == nil {
			t.Fatalf("check %d: got %v, %v; want unchecked with error", i, d, err)
		}
	}
	if got := counter.calls.Load(); got != 1 {
		t.Fatalf("ClickHouse asked %d times during the backoff, want 1", got)
	}
	if mr.Exists("quota:site-a:202611") {
		t.Fatal("counter created without a ClickHouse count")
	}

	// After the backoff it tries again, and once ClickHouse answers, it counts normally.
	counter.mu.Lock()
	counter.err = nil
	counter.counts = map[string]int64{"site-a": 3}
	counter.mu.Unlock()
	quotaNow = quotaNow.Add(31 * time.Second)
	defer func() { quotaNow = quotaNow.Add(-31 * time.Second) }()
	if d, err := q.Check(ctx, "site-a", 100, 1); d != QuotaAllowed || err != nil {
		t.Fatalf("after backoff: got %v, %v", d, err)
	}
	if v, _ := mr.Get("quota:site-a:202611"); v != "4" {
		t.Fatalf("counter = %s, want 4", v)
	}
}

func TestQuota_RedisDownFailsOpen(t *testing.T) {
	q, mr, _ := newTestQuota(t, &fakeCounter{})
	mr.Close()
	d, err := q.Check(context.Background(), "site-a", 100, 1)
	if d != QuotaUnchecked || err == nil {
		t.Fatalf("got %v, %v; want unchecked with error", d, err)
	}
}

func TestQuota_Refund(t *testing.T) {
	q, mr, _ := newTestQuota(t, &fakeCounter{counts: map[string]int64{"site-a": 10}})
	ctx := context.Background()
	_, _ = q.Check(ctx, "site-a", 100, 1)
	q.Refund(ctx, "site-a", 1)
	if v, _ := mr.Get("quota:site-a:202611"); v != "10" {
		t.Fatalf("counter = %s, want 10", v)
	}
}

func TestQuota_ReconcileSetsCountersFromClickHouse(t *testing.T) {
	counter := &fakeCounter{counts: map[string]int64{"site-a": 120, "site-b": 40}}
	q, mr, _ := newTestQuota(t, counter)
	_ = mr.Set("quota:site-a:202611", "100") // drifted low
	_ = mr.Set("quota:site-b:202611", "55")  // drifted high
	_ = mr.Set("quota:site-c:202611", "9")   // no events in ClickHouse yet
	mr.SetTTL("quota:site-a:202611", 30*24*time.Hour)
	_ = mr.Set("quota:site-a:202610", "999") // last month: left alone
	_ = mr.Set("quota:site-a:limit_reached", "1")

	n, err := q.Reconcile(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	if n != 3 {
		t.Fatalf("reconciled %d counters, want 3", n)
	}
	if counter.calls.Load() != 1 {
		t.Fatalf("ClickHouse asked %d times, want 1 for all sites", counter.calls.Load())
	}
	for key, want := range map[string]string{
		"quota:site-a:202611": "120",
		"quota:site-b:202611": "40",
		"quota:site-c:202611": "0",
		"quota:site-a:202610": "999",
	} {
		if v, _ := mr.Get(key); v != want {
			t.Errorf("%s = %s, want %s", key, v, want)
		}
	}
	if ttl := mr.TTL("quota:site-a:202611"); ttl != 30*24*time.Hour {
		t.Errorf("reconcile changed the TTL to %v", ttl)
	}
	if strings.Join(counter.lastSites, ",") == "" {
		t.Error("no sites sent to ClickHouse")
	}
}

func TestQuota_ReconcileKeepsEventsCountedMeanwhile(t *testing.T) {
	counter := &fakeCounter{counts: map[string]int64{"site-a": 200}}
	q, mr, rdb := newTestQuota(t, counter)
	_ = mr.Set("quota:site-a:202611", "190")
	// Five events accepted while ClickHouse is counting.
	counter.during = func() { rdb.IncrBy(context.Background(), "quota:site-a:202611", 5) }

	if _, err := q.Reconcile(context.Background()); err != nil {
		t.Fatal(err)
	}
	if v, _ := mr.Get("quota:site-a:202611"); v != "205" {
		t.Fatalf("counter = %s, want 205 (200 from ClickHouse + 5 counted meanwhile)", v)
	}
}

func TestQuota_ReconcileOncePerIntervalAcrossInstances(t *testing.T) {
	counter := &fakeCounter{counts: map[string]int64{"site-a": 1}}
	q, mr, rdb := newTestQuota(t, counter)
	other := NewQuotaCounter(rdb, counter)
	_ = mr.Set("quota:site-a:202611", "5")

	if n, err := q.Reconcile(context.Background()); err != nil || n != 1 {
		t.Fatalf("first: %d, %v", n, err)
	}
	if n, err := other.Reconcile(context.Background()); err != nil || n != 0 {
		t.Fatalf("second instance in the same interval: %d, %v; want 0", n, err)
	}
	mr.FastForward(5 * time.Minute)
	if n, err := other.Reconcile(context.Background()); err != nil || n != 1 {
		t.Fatalf("next interval: %d, %v", n, err)
	}
}

// ── ProcessEvent with the quota ──────────────────────────────────────

func quotaSite() *mockSiteRepo {
	return &mockSiteRepo{config: &repository.SiteConfig{
		ID:               "550e8400-e29b-41d4-a716-446655440000",
		WriteKey:         "wk",
		MaxMonthlyEvents: 2,
		Plan:             "pro",
	}}
}

func TestProcessEvent_QuotaBlocksWithoutEnqueueing(t *testing.T) {
	mr := miniredis.RunT(t)
	rdb := redis.NewClient(&redis.Options{Addr: mr.Addr()})
	defer rdb.Close()
	repo := &mockEventRepo{}
	svc := NewIngestionService(repo, quotaSite(), nil, nil, rdb)

	for i := 0; i < 3; i++ {
		req := httptest.NewRequest("POST", "/analytics/events", nil)
		req.Header.Set("X-API-Key", "wk")
		err := svc.ProcessEvent(context.Background(), &AnalyticsEventDto{SiteId: "s", Type: "pageview", Url: "https://x.test/"}, req)
		if i < 2 && err != nil {
			t.Fatalf("event %d: %v", i, err)
		}
		if i == 2 && !errors.Is(err, ErrQuotaExceeded) {
			t.Fatalf("event 3: got %v, want quota exceeded", err)
		}
	}
	if len(repo.events) != 2 {
		t.Fatalf("enqueued %d events, want 2", len(repo.events))
	}
}

func TestProcessEvent_QuotaRefundedWhenEnqueueFails(t *testing.T) {
	mr := miniredis.RunT(t)
	rdb := redis.NewClient(&redis.Options{Addr: mr.Addr()})
	defer rdb.Close()
	site := quotaSite()
	svc := NewIngestionService(&mockEventRepo{err: errors.New("buffer full")}, site, nil, nil, rdb)

	req := httptest.NewRequest("POST", "/analytics/events", nil)
	req.Header.Set("X-API-Key", "wk")
	if err := svc.ProcessEvent(context.Background(), &AnalyticsEventDto{SiteId: "s", Type: "pageview", Url: "https://x.test/"}, req); err == nil {
		t.Fatal("want the enqueue error")
	}
	key := quotaCounterKey(site.config.ID, monthStart(time.Now()))
	if v, _ := mr.Get(key); v != "0" {
		t.Fatalf("counter = %s, want 0 after the refund", v)
	}
}
