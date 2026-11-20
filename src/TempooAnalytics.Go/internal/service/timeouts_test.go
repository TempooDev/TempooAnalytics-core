package service

import (
	"context"
	"errors"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/repository"
	"github.com/alicebob/miniredis/v2"
	"github.com/redis/go-redis/v9"
)

// slowSiteRepo waits for the context, like Postgres or Redis that stopped answering.
type slowSiteRepo struct{}

func (slowSiteRepo) GetSiteConfig(ctx context.Context, _ string, _ string) (*repository.SiteConfig, error) {
	<-ctx.Done()
	return nil, ctx.Err()
}

// delayedSiteRepo answers after delay (a cold cache: Redis miss plus a Postgres query on a new connection),
// or gives up with the context's error when that comes first.
type delayedSiteRepo struct {
	delay  time.Duration
	config *repository.SiteConfig
}

func (d delayedSiteRepo) GetSiteConfig(ctx context.Context, _ string, _ string) (*repository.SiteConfig, error) {
	select {
	case <-time.After(d.delay):
		return d.config, nil
	case <-ctx.Done():
		return nil, ctx.Err()
	}
}

func authedRequest() *http.Request {
	req := httptest.NewRequest("POST", "/analytics/events", nil)
	req.Header.Set("X-API-Key", "wk")
	return req
}

func TestProcessEvent_ColdSiteLookupWithinDefaultTimeoutIsAccepted(t *testing.T) {
	if DefaultDependencyTimeout < 500*time.Millisecond {
		t.Fatalf("DefaultDependencyTimeout = %v, want at least 500ms", DefaultDependencyTimeout)
	}
	repo := &mockEventRepo{}
	svc := NewIngestionService(repo, delayedSiteRepo{delay: 200 * time.Millisecond, config: quotaSite().config}, nil, nil, nil)

	if err := svc.ProcessEvent(context.Background(), eventRequest(), authedRequest()); err != nil {
		t.Fatalf("a 200ms cold lookup must be accepted with the default timeout: %v", err)
	}
	if len(repo.events) != 1 {
		t.Fatalf("enqueued %d events, want 1", len(repo.events))
	}
}

func TestProcessEvent_SiteLookupPastConfiguredTimeoutIsUnavailableAndNamesTheDependency(t *testing.T) {
	svc := NewIngestionService(&mockEventRepo{}, delayedSiteRepo{delay: 200 * time.Millisecond, config: quotaSite().config}, nil, nil, nil)
	svc.DependencyTimeout = 100 * time.Millisecond

	err := svc.ProcessEvent(context.Background(), eventRequest(), authedRequest())
	if !errors.Is(err, ErrDependencyUnavailable) {
		t.Fatalf("got %v, want ErrDependencyUnavailable", err)
	}
	for _, want := range []string{"dependency=site_config", "elapsed=", "timeout=100ms"} {
		if !strings.Contains(err.Error(), want) {
			t.Fatalf("error %q lacks %q", err, want)
		}
	}
}

func eventRequest() *AnalyticsEventDto {
	return &AnalyticsEventDto{SiteId: "s", Type: "pageview", Url: "https://x.test/"}
}

func TestProcessEvent_SiteLookupTimeoutIsUnavailableAndFast(t *testing.T) {
	svc := NewIngestionService(&mockEventRepo{}, slowSiteRepo{}, nil, nil, nil)
	svc.DependencyTimeout = 50 * time.Millisecond

	start := time.Now()
	err := svc.ProcessEvent(context.Background(), eventRequest(), httptest.NewRequest("POST", "/analytics/events", nil))
	if !errors.Is(err, ErrDependencyUnavailable) {
		t.Fatalf("got %v, want ErrDependencyUnavailable", err)
	}
	if d := time.Since(start); d > 200*time.Millisecond {
		t.Fatalf("took %v, want about the 50ms dependency timeout", d)
	}
}

func TestProcessEvent_SiteNotFoundIsNotUnavailable(t *testing.T) {
	svc := NewIngestionService(&mockEventRepo{}, &mockSiteRepo{err: repository.ErrSiteNotFound}, nil, nil, nil)
	err := svc.ProcessEvent(context.Background(), eventRequest(), httptest.NewRequest("POST", "/analytics/events", nil))
	if err == nil || errors.Is(err, ErrDependencyUnavailable) {
		t.Fatalf("got %v, want a not-found error", err)
	}
}

func TestProcessEvent_SlowQuotaInitIsFailOpenAndFinishesInBackground(t *testing.T) {
	mr := miniredis.RunT(t)
	rdb := redis.NewClient(&redis.Options{Addr: mr.Addr(), ContextTimeoutEnabled: true})
	defer rdb.Close()
	repo := &slowCountRepo{release: make(chan struct{}), counts: map[string]int64{"550e8400-e29b-41d4-a716-446655440000": 1}}
	svc := NewIngestionService(repo, quotaSite(), nil, nil, rdb)
	svc.DependencyTimeout = 50 * time.Millisecond
	site := quotaSite().config
	site.MaxMonthlyEvents = 1000
	svc.siteRepo = &mockSiteRepo{config: site}

	req := httptest.NewRequest("POST", "/analytics/events", nil)
	req.Header.Set("X-API-Key", "wk")
	start := time.Now()
	if err := svc.ProcessEvent(context.Background(), eventRequest(), req); err != nil {
		t.Fatalf("slow ClickHouse must not reject the event: %v", err)
	}
	if d := time.Since(start); d > 200*time.Millisecond {
		t.Fatalf("took %v, want about the 50ms dependency timeout", d)
	}
	if len(repo.events) != 1 {
		t.Fatalf("enqueued %d events, want 1 (fail-open)", len(repo.events))
	}

	// ClickHouse answers later: the counter is initialised anyway and the next event is counted.
	close(repo.release)
	key := quotaCounterKey(site.ID, monthStart(time.Now()))
	deadline := time.Now().Add(2 * time.Second)
	for !mr.Exists(key) {
		if time.Now().After(deadline) {
			t.Fatal("counter not initialised after ClickHouse answered")
		}
		time.Sleep(5 * time.Millisecond)
	}
	if d, err := svc.quota.Check(context.Background(), site.ID, 1000, 1); d != QuotaAllowed || err != nil {
		t.Fatalf("next check: %v, %v", d, err)
	}
	if v, _ := mr.Get(key); v != "2" {
		t.Fatalf("counter = %s, want 2 (1 from ClickHouse + 1)", v)
	}
}

func TestProcessEvent_StuckRedisQuotaIsFailOpen(t *testing.T) {
	mr := miniredis.RunT(t)
	rdb := redis.NewClient(&redis.Options{Addr: mr.Addr(), ContextTimeoutEnabled: true, MaxRetries: -1})
	defer rdb.Close()
	repo := &mockEventRepo{}
	svc := NewIngestionService(repo, quotaSite(), nil, nil, rdb)
	svc.DependencyTimeout = 50 * time.Millisecond
	mr.SetError("LOADING Redis is loading the dataset in memory")

	req := httptest.NewRequest("POST", "/analytics/events", nil)
	req.Header.Set("X-API-Key", "wk")
	if err := svc.ProcessEvent(context.Background(), eventRequest(), req); err != nil {
		t.Fatalf("failing Redis must not reject the event: %v", err)
	}
	if len(repo.events) != 1 {
		t.Fatalf("enqueued %d events, want 1", len(repo.events))
	}
}

type slowCountRepo struct {
	mockEventRepo
	release chan struct{}
	counts  map[string]int64
}

func (s *slowCountRepo) CountMonthlyEvents(ctx context.Context, _ []string, _ time.Time) (map[string]int64, error) {
	select {
	case <-s.release:
		return s.counts, nil
	case <-ctx.Done():
		return nil, ctx.Err()
	}
}

func TestProcessEvent_BufferFullIsOverloadedAndRefundsQuota(t *testing.T) {
	mr := miniredis.RunT(t)
	rdb := redis.NewClient(&redis.Options{Addr: mr.Addr()})
	defer rdb.Close()
	site := quotaSite()
	svc := NewIngestionService(&mockEventRepo{err: repository.ErrBufferFull}, site, nil, nil, rdb)

	req := httptest.NewRequest("POST", "/analytics/events", nil)
	req.Header.Set("X-API-Key", "wk")
	err := svc.ProcessEvent(context.Background(), eventRequest(), req)
	if !errors.Is(err, ErrOverloaded) {
		t.Fatalf("got %v, want ErrOverloaded", err)
	}
	if v, _ := mr.Get(quotaCounterKey(site.config.ID, monthStart(time.Now()))); v != "0" {
		t.Fatalf("counter = %s, want 0 after the refund", v)
	}
}
