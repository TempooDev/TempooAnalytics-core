package service

import (
	"context"
	"errors"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/repository"
	"github.com/alicebob/miniredis/v2"
	"github.com/google/uuid"
	"github.com/redis/go-redis/v9"
)

func dedupSetup(t *testing.T, repo *mockEventRepo) (*IngestionService, *miniredis.Miniredis) {
	t.Helper()
	mr := miniredis.RunT(t)
	rdb := redis.NewClient(&redis.Options{Addr: mr.Addr()})
	t.Cleanup(func() { _ = rdb.Close() })
	site := quotaSite()
	site.config.MaxMonthlyEvents = 1000
	return NewIngestionService(repo, site, nil, nil, rdb), mr
}

func send(svc *IngestionService, eventID string) error {
	req := httptest.NewRequest("POST", "/analytics/events", nil)
	req.Header.Set("X-API-Key", "wk")
	dto := eventRequest()
	dto.EventId = eventID
	return svc.ProcessEvent(context.Background(), dto, req)
}

func TestDedup_RetryWithSameEventIDIsAcceptedOnce(t *testing.T) {
	repo := &mockEventRepo{}
	svc, mr := dedupSetup(t, repo)
	id := uuid.Must(uuid.NewV7()).String()

	for i := 0; i < 3; i++ {
		if err := send(svc, id); err != nil {
			t.Fatalf("attempt %d: %v", i, err)
		}
	}
	if len(repo.events) != 1 {
		t.Fatalf("stored %d events, want 1", len(repo.events))
	}
	if repo.events[0].EventID.String() != id {
		t.Fatalf("event_id = %s, want the client's %s", repo.events[0].EventID, id)
	}
	if ttl := mr.TTL("evt:" + id); ttl != DefaultDedupTTL {
		t.Fatalf("dedup TTL = %v, want %v", ttl, DefaultDedupTTL)
	}
	key := quotaCounterKey(quotaSite().config.ID, monthStart(time.Now()))
	if v, _ := mr.Get(key); v != "1" {
		t.Fatalf("quota counter = %s, want 1 (retries are not counted)", v)
	}
}

func TestDedup_DifferentEventIDsAreStored(t *testing.T) {
	repo := &mockEventRepo{}
	svc, _ := dedupSetup(t, repo)
	for i := 0; i < 3; i++ {
		if err := send(svc, uuid.NewString()); err != nil {
			t.Fatal(err)
		}
	}
	if len(repo.events) != 3 {
		t.Fatalf("stored %d events, want 3", len(repo.events))
	}
}

func TestDedup_WithoutOrWithInvalidEventIDNoDedup(t *testing.T) {
	repo := &mockEventRepo{}
	svc, mr := dedupSetup(t, repo)
	for _, id := range []string{"", "", "not-a-uuid", "not-a-uuid", uuid.Nil.String()} {
		if err := send(svc, id); err != nil {
			t.Fatal(err)
		}
	}
	if len(repo.events) != 5 {
		t.Fatalf("stored %d events, want 5", len(repo.events))
	}
	if keys := mr.Keys(); len(keys) > 0 {
		for _, k := range keys {
			if len(k) > 4 && k[:4] == "evt:" {
				t.Fatalf("dedup key %s created without a valid client event_id", k)
			}
		}
	}
}

func TestDedup_RejectedEventCanBeRetried(t *testing.T) {
	repo := &mockEventRepo{err: repository.ErrBufferFull}
	svc, mr := dedupSetup(t, repo)
	id := uuid.NewString()

	if err := send(svc, id); !errors.Is(err, ErrOverloaded) {
		t.Fatalf("got %v, want ErrOverloaded", err)
	}
	if mr.Exists("evt:" + id) {
		t.Fatal("dedup key kept for an event that was not accepted")
	}
	repo.err = nil
	if err := send(svc, id); err != nil {
		t.Fatalf("retry: %v", err)
	}
	if len(repo.events) != 1 {
		t.Fatalf("stored %d events after the retry, want 1", len(repo.events))
	}
}

func TestDedup_RedisFailingAcceptsEvent(t *testing.T) {
	repo := &mockEventRepo{}
	svc, mr := dedupSetup(t, repo)
	mr.SetError("READONLY")
	if err := send(svc, uuid.NewString()); err != nil {
		t.Fatal(err)
	}
	if len(repo.events) != 1 {
		t.Fatalf("stored %d events, want 1 (fail-open)", len(repo.events))
	}
}

func TestDedup_ClientThatGaveUpDoesNotBreakDeduplication(t *testing.T) {
	repo := &mockEventRepo{}
	svc, _ := dedupSetup(t, repo)
	id := uuid.NewString()

	// The client times out and disconnects: its request context is already cancelled.
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	req := httptest.NewRequest("POST", "/analytics/events", nil).WithContext(ctx)
	req.Header.Set("X-API-Key", "wk")
	dto := eventRequest()
	dto.EventId = id
	if err := svc.ProcessEvent(ctx, dto, req); err != nil {
		t.Fatal(err)
	}
	// Its retry must be recognised as the same event.
	if err := send(svc, id); err != nil {
		t.Fatal(err)
	}
	if len(repo.events) != 1 {
		t.Fatalf("stored %d events, want 1", len(repo.events))
	}
}
