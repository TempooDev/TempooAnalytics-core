package repository

import (
	"context"
	"errors"
	"math"
	"net"
	"strings"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/alicebob/miniredis/v2"
	"github.com/jackc/pgx/v5"
	"github.com/redis/go-redis/v9"
)

// fakeRow answers the site query with a fixed config, an error, or waits for the context.
type fakeQuerier struct {
	calls atomic.Int64
	err   error
	delay time.Duration
}

type fakeRow struct {
	ctx   context.Context
	err   error
	delay time.Duration
}

func (f *fakeQuerier) QueryRow(ctx context.Context, _ string, _ ...any) pgx.Row {
	f.calls.Add(1)
	return fakeRow{ctx: ctx, err: f.err, delay: f.delay}
}

func (r fakeRow) Scan(dest ...any) error {
	if r.delay > 0 {
		select {
		case <-time.After(r.delay):
		case <-r.ctx.Done():
			return r.ctx.Err()
		}
	}
	if r.err != nil {
		return r.err
	}
	*dest[0].(*string) = "site-uuid"
	*dest[1].(*string) = "wk"
	*dest[2].(*[]string) = []string{"example.com"}
	*dest[3].(*string) = "owner"
	*dest[4].(*int64) = 1000
	*dest[5].(*time.Time) = time.Unix(0, 0)
	plan := "pro"
	*dest[6].(**string) = &plan
	return nil
}

func TestSiteRepository_MemoryCacheAvoidsRedisAndPostgres(t *testing.T) {
	db := &fakeQuerier{}
	r := newSiteRepository(db, nil)
	for i := 0; i < 5; i++ {
		c, err := r.GetSiteConfig(context.Background(), "key", "")
		if err != nil || c.ID != "site-uuid" {
			t.Fatalf("got %v, %v", c, err)
		}
	}
	if db.calls.Load() != 1 {
		t.Fatalf("Postgres asked %d times, want 1", db.calls.Load())
	}
}

func TestSiteRepository_UnlimitedDropsThePlanLimits(t *testing.T) {
	r := newSiteRepository(&fakeQuerier{}, nil)
	r.Unlimited = true
	c, err := r.GetSiteConfig(context.Background(), "key", "")
	if err != nil {
		t.Fatal(err)
	}
	if c.Plan != CommunityPlan || c.MaxMonthlyEvents != math.MaxInt64 || !c.HasAdvancedAnalytics || c.HeatmapMonthlyClicks != math.MaxInt64 {
		t.Fatalf("unlimited config = %+v", c)
	}
}

func TestSiteRepository_PlanLimitsApplyByDefault(t *testing.T) {
	r := newSiteRepository(&fakeQuerier{}, nil)
	c, err := r.GetSiteConfig(context.Background(), "key", "")
	if err != nil {
		t.Fatal(err)
	}
	if c.Plan != "pro" || c.MaxMonthlyEvents != 1000 {
		t.Fatalf("default config = %+v, want the subscription's plan and limit", c)
	}
}

func TestSiteRepository_NotFound(t *testing.T) {
	r := newSiteRepository(&fakeQuerier{err: pgx.ErrNoRows}, nil)
	if _, err := r.GetSiteConfig(context.Background(), "nope", ""); !errors.Is(err, ErrSiteNotFound) {
		t.Fatalf("got %v, want ErrSiteNotFound", err)
	}
}

func TestSiteRepository_PostgresSlowFailsWithinDeadline(t *testing.T) {
	r := newSiteRepository(&fakeQuerier{delay: 5 * time.Second}, nil)
	ctx, cancel := context.WithTimeout(context.Background(), 100*time.Millisecond)
	defer cancel()
	start := time.Now()
	_, err := r.GetSiteConfig(ctx, "key", "")
	if err == nil || errors.Is(err, ErrSiteNotFound) {
		t.Fatalf("got %v, want an availability error", err)
	}
	if d := time.Since(start); d > 300*time.Millisecond {
		t.Fatalf("took %v, want about the 100ms deadline", d)
	}
}

func TestSiteRepository_ServesStaleWhenLookupsFail(t *testing.T) {
	db := &fakeQuerier{}
	r := newSiteRepository(db, nil)
	now := time.Now()
	r.now = func() time.Time { return now }
	if _, err := r.GetSiteConfig(context.Background(), "key", ""); err != nil {
		t.Fatal(err)
	}

	db.err = errors.New("postgres down")
	now = now.Add(time.Minute) // past MemoryTTL, within StaleTTL
	c, err := r.GetSiteConfig(context.Background(), "key", "")
	if err != nil || c == nil {
		t.Fatalf("got %v, %v; want the stale config", c, err)
	}
	waitFor(t, "the background refresh", func() bool { return db.calls.Load() == 2 })

	// The failed refresh left the stale copy in place: still served.
	if c, err := r.GetSiteConfig(context.Background(), "key", ""); err != nil || c == nil {
		t.Fatalf("got %v, %v; want the stale config after a failed refresh", c, err)
	}

	now = now.Add(DefaultSiteStaleTTL)
	if _, err := r.GetSiteConfig(context.Background(), "key", ""); err == nil {
		t.Fatal("served a config older than StaleTTL")
	}
}

func TestSiteRepository_StaleIsServedAtOnceWhileRefreshingInBackground(t *testing.T) {
	db := &fakeQuerier{}
	r := newSiteRepository(db, nil)
	now := time.Now()
	r.now = func() time.Time { return now }
	if _, err := r.GetSiteConfig(context.Background(), "key", ""); err != nil {
		t.Fatal(err)
	}
	db.delay = 300 * time.Millisecond // the refresh is slow
	now = now.Add(time.Minute)        // past MemoryTTL

	start := time.Now()
	c, err := r.GetSiteConfig(context.Background(), "key", "")
	if err != nil || c == nil {
		t.Fatalf("got %v, %v", c, err)
	}
	if d := time.Since(start); d > 100*time.Millisecond {
		t.Fatalf("took %v, a stale copy must not wait for the refresh", d)
	}
	waitFor(t, "the background refresh to start", func() bool { return db.calls.Load() == 2 })
	waitFor(t, "the refresh to store the new copy", func() bool { // the copy is fresh again
		r.mu.RLock()
		defer r.mu.RUnlock()
		return r.memory["key"].storedAt.Equal(now)
	})
}

func TestSiteRepository_RefreshDropsDeletedSite(t *testing.T) {
	db := &fakeQuerier{}
	r := newSiteRepository(db, nil)
	now := time.Now()
	r.now = func() time.Time { return now }
	if _, err := r.GetSiteConfig(context.Background(), "key", ""); err != nil {
		t.Fatal(err)
	}
	db.err = pgx.ErrNoRows
	now = now.Add(time.Minute)
	if c, err := r.GetSiteConfig(context.Background(), "key", ""); err != nil || c == nil {
		t.Fatalf("got %v, %v; want the stale copy once", c, err)
	}
	waitFor(t, "the deleted site to be dropped", func() bool {
		_, err := r.GetSiteConfig(context.Background(), "key", "")
		return errors.Is(err, ErrSiteNotFound)
	})
}

func TestSiteRepository_PreloadFillsMemoryWithoutTouchingTheRequestPath(t *testing.T) {
	db := &fakeQuerier{err: errors.New("postgres down for single lookups")}
	r := newSiteRepository(db, nil)
	r.listSites = func(context.Context, int) ([]preloadedSite, error) {
		return []preloadedSite{
			{siteKey: "key-a", config: &SiteConfig{ID: "id-a", Plan: "pro"}},
			{siteKey: "key-b", config: &SiteConfig{ID: "id-b", Plan: "free"}},
		}, nil
	}
	n, err := r.Preload(context.Background())
	if err != nil || n != 2 {
		t.Fatalf("Preload = %d, %v", n, err)
	}
	for _, key := range []string{"key-a", "id-a", "key-b", "id-b"} {
		if c, err := r.GetSiteConfig(context.Background(), key, ""); err != nil || c == nil {
			t.Fatalf("%s: got %v, %v; want it from memory", key, c, err)
		}
	}
	if db.calls.Load() != 0 {
		t.Fatalf("Postgres asked %d times, want 0", db.calls.Load())
	}
}

func TestSiteRepository_PreloadErrorKeepsMemoryAndDropsRemovedSites(t *testing.T) {
	r := newSiteRepository(&fakeQuerier{}, nil)
	sites := []preloadedSite{
		{siteKey: "key-a", config: &SiteConfig{ID: "id-a"}},
		{siteKey: "key-b", config: &SiteConfig{ID: "id-b"}},
	}
	var failing bool
	r.listSites = func(context.Context, int) ([]preloadedSite, error) {
		if failing {
			return nil, errors.New("down")
		}
		return sites, nil
	}
	if _, err := r.Preload(context.Background()); err != nil {
		t.Fatal(err)
	}
	failing = true
	if _, err := r.Preload(context.Background()); err == nil {
		t.Fatal("want the error")
	}
	if c := r.fromMemory("key-a", time.Hour); c == nil {
		t.Fatal("a failed preload emptied the memory")
	}
	failing = false
	sites = sites[:1] // site b was deleted
	if _, err := r.Preload(context.Background()); err != nil {
		t.Fatal(err)
	}
	if r.fromMemory("key-b", time.Hour) != nil || r.fromMemory("id-b", time.Hour) != nil {
		t.Fatal("a deleted site stayed in memory")
	}
	if r.fromMemory("key-a", time.Hour) == nil {
		t.Fatal("the remaining site was dropped")
	}
}

func TestSiteRepository_PreloadAppliesUnlimitedAndRespectsLimit(t *testing.T) {
	r := newSiteRepository(&fakeQuerier{}, nil)
	r.Unlimited = true
	r.PreloadLimit = 1
	var gotLimit int
	r.listSites = func(_ context.Context, limit int) ([]preloadedSite, error) {
		gotLimit = limit
		return []preloadedSite{{siteKey: "k", config: &SiteConfig{ID: "i", Plan: "free", MaxMonthlyEvents: 5}}}, nil
	}
	if _, err := r.Preload(context.Background()); err != nil {
		t.Fatal(err)
	}
	if gotLimit != 1 {
		t.Fatalf("limit = %d", gotLimit)
	}
	if c := r.fromMemory("k", time.Hour); c == nil || c.Plan != CommunityPlan || c.MaxMonthlyEvents != math.MaxInt64 {
		t.Fatalf("preloaded config = %+v, want unlimited plan", c)
	}
}

func TestSiteRepository_StuckRedisLeavesTimeForPostgres(t *testing.T) {
	// A Redis that accepts connections and never answers, like a paused container.
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer ln.Close()
	go func() {
		for {
			c, err := ln.Accept()
			if err != nil {
				return
			}
			defer c.Close()
		}
	}()
	rdb := redis.NewClient(&redis.Options{Addr: ln.Addr().String(), MaxRetries: -1, ContextTimeoutEnabled: true})
	defer rdb.Close()

	r := newSiteRepository(&fakeQuerier{}, rdb)
	ctx, cancel := context.WithTimeout(context.Background(), 200*time.Millisecond)
	defer cancel()
	start := time.Now()
	c, err := r.GetSiteConfig(ctx, "key", "")
	if err != nil || c == nil {
		t.Fatalf("got %v, %v; want the config from Postgres", c, err)
	}
	if d := time.Since(start); d > 150*time.Millisecond {
		t.Fatalf("took %v, want Redis cut at half the deadline", d)
	}
}

func TestSiteRepository_CachesInRedis(t *testing.T) {
	mr := miniredis.RunT(t)
	rdb := redis.NewClient(&redis.Options{Addr: mr.Addr()})
	defer rdb.Close()
	db := &fakeQuerier{}
	r := newSiteRepository(db, rdb)
	if _, err := r.GetSiteConfig(context.Background(), "key", ""); err != nil {
		t.Fatal(err)
	}
	deadline := time.Now().Add(time.Second)
	for !mr.Exists("site:key:config:v3") {
		if time.Now().After(deadline) {
			t.Fatal("config not cached in Redis")
		}
		time.Sleep(5 * time.Millisecond)
	}
	// Another instance (empty memory) reads it from Redis without asking Postgres.
	other := newSiteRepository(db, rdb)
	if _, err := other.GetSiteConfig(context.Background(), "key", ""); err != nil {
		t.Fatal(err)
	}
	if db.calls.Load() != 1 {
		t.Fatalf("Postgres asked %d times, want 1", db.calls.Load())
	}
}

func TestSiteRepository_ColdLookupsShareOneQueryAndFinishAfterTimeout(t *testing.T) {
	db := &fakeQuerier{delay: 150 * time.Millisecond}
	r := newSiteRepository(db, nil)

	// 100 requests at once with a 50ms budget: they all give up, but only one query runs...
	var wg sync.WaitGroup
	for i := 0; i < 100; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			ctx, cancel := context.WithTimeout(context.Background(), 50*time.Millisecond)
			defer cancel()
			_, _ = r.GetSiteConfig(ctx, "key", "")
		}()
	}
	wg.Wait()
	if db.calls.Load() != 1 {
		t.Fatalf("Postgres asked %d times, want 1", db.calls.Load())
	}
	// ...and it completes in the background and fills the cache for the next requests.
	time.Sleep(200 * time.Millisecond)
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Millisecond)
	defer cancel()
	if c, err := r.GetSiteConfig(ctx, "key", ""); err != nil || c == nil {
		t.Fatalf("got %v, %v; want the config from memory", c, err)
	}
}

// The community edition has no billing schema: with Unlimited the site queries must not touch it (otherwise every lookup
// and Preload fails with "relation billing.analytics_subscriptions does not exist" and ingestion answers 503).
func TestSelectSites_UnlimitedDoesNotReadBilling(t *testing.T) {
	r := newSiteRepository(nil, nil)
	if !strings.Contains(r.selectSites(), "billing.") {
		t.Fatal("the cloud query should join the billing tables")
	}
	r.Unlimited = true
	if q := r.selectSites(); strings.Contains(q, "billing.") {
		t.Fatalf("the community query reads the billing schema: %s", q)
	}
}
