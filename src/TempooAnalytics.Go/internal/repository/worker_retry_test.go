package repository

import (
	"context"
	"errors"
	"os"
	"path/filepath"
	"sync"
	"testing"
	"time"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/model"
	"github.com/google/uuid"
)

// fakeInserter records every INSERT attempt and fails the first `fail` ones (or all, with fail < 0).
type fakeInserter struct {
	mu       sync.Mutex
	fail     int
	attempts []string // tokens
	stored   map[string][]*model.AnalyticsEvent
}

func (f *fakeInserter) insert(_ context.Context, events []*model.AnalyticsEvent, token string) error {
	f.mu.Lock()
	defer f.mu.Unlock()
	f.attempts = append(f.attempts, token)
	if f.fail < 0 || len(f.attempts) <= f.fail {
		return errors.New("clickhouse down")
	}
	if f.stored == nil {
		f.stored = map[string][]*model.AnalyticsEvent{}
	}
	f.stored[token] = append([]*model.AnalyticsEvent(nil), events...)
	return nil
}

func testWorker(t *testing.T, ins *fakeInserter, spillDir string) *ClickHouseWorker {
	t.Helper()
	w := NewClickHouseWorker(nil, WorkerConfig{InsertRetries: 3, SpillDir: spillDir})
	w.insert = ins.insert
	w.sleep = func(ctx context.Context, _ time.Duration) bool { return ctx.Err() == nil }
	return w
}

func events(n int) []*model.AnalyticsEvent {
	out := make([]*model.AnalyticsEvent, n)
	for i := range out {
		out[i] = &model.AnalyticsEvent{EventID: uuid.New(), SiteID: uuid.New(), Path: "/p", Timestamp: time.Unix(1700000000, 0).UTC()}
	}
	return out
}

func TestFlush_RetriesWithTheSameToken(t *testing.T) {
	ins := &fakeInserter{fail: 2}
	w := testWorker(t, ins, "")
	w.flush(events(3))

	if len(ins.attempts) != 3 {
		t.Fatalf("attempts = %d, want 3 (2 failures + success)", len(ins.attempts))
	}
	if ins.attempts[0] != ins.attempts[1] || ins.attempts[1] != ins.attempts[2] {
		t.Fatalf("tokens changed between retries: %v", ins.attempts)
	}
	if len(ins.stored[ins.attempts[0]]) != 3 {
		t.Fatal("batch not stored")
	}
}

func TestFlush_SpillsAfterRetriesAndReplaysLater(t *testing.T) {
	dir := t.TempDir()
	ins := &fakeInserter{fail: -1}
	w := testWorker(t, ins, dir)
	batch := events(4)
	w.flush(batch)

	if len(ins.attempts) != 4 {
		t.Fatalf("attempts = %d, want 4 (1 + 3 retries)", len(ins.attempts))
	}
	names, _ := w.spill.List()
	if len(names) != 1 {
		t.Fatalf("spilled batches = %d, want 1", len(names))
	}

	// ClickHouse is still down: the replay keeps the file.
	w.replaySpilled()
	if w.spill.Pending() != 1 {
		t.Fatal("batch removed although the replay failed")
	}

	// ClickHouse is back.
	ins.mu.Lock()
	ins.fail = 0
	ins.mu.Unlock()
	w.replaySpilled()
	if w.spill.Pending() != 0 {
		t.Fatal("batch still pending after a successful replay")
	}
	token := ins.attempts[0]
	if ins.attempts[len(ins.attempts)-1] != token {
		t.Fatal("replay used a different deduplication token")
	}
	got := ins.stored[token]
	if len(got) != len(batch) {
		t.Fatalf("replayed %d events, want %d", len(got), len(batch))
	}
	for i := range batch {
		if got[i].EventID != batch[i].EventID || !got[i].Timestamp.Equal(batch[i].Timestamp) || got[i].Path != "/p" {
			t.Fatalf("event %d changed through the spill: %+v vs %+v", i, got[i], batch[i])
		}
	}
}

func TestFlush_StoppingSpillsWithoutWaitingForRetries(t *testing.T) {
	dir := t.TempDir()
	ins := &fakeInserter{fail: -1}
	w := testWorker(t, ins, dir)
	w.stopping.Store(true)
	w.flush(events(2))
	if len(ins.attempts) != 1 {
		t.Fatalf("attempts = %d, want 1 while stopping", len(ins.attempts))
	}
	if w.spill.Pending() != 1 {
		t.Fatal("batch not spilled on shutdown")
	}
}

func TestWorker_StopFlushesBufferAndReplaysOnNextStart(t *testing.T) {
	dir := t.TempDir()
	down := &fakeInserter{fail: -1}
	w := testWorker(t, down, dir)
	w.Start()
	for _, e := range events(5) {
		if err := w.Enqueue(e); err != nil {
			t.Fatal(err)
		}
	}
	w.Stop() // ClickHouse down during shutdown: everything goes to disk

	up := &fakeInserter{}
	next := testWorker(t, up, dir)
	next.Start() // replays what the previous run left
	deadline := time.Now().Add(2 * time.Second)
	for next.spill.Pending() > 0 {
		if time.Now().After(deadline) {
			t.Fatal("previous run's batches not replayed on start")
		}
		time.Sleep(10 * time.Millisecond)
	}
	next.Stop()
	total := 0
	for _, evs := range up.stored {
		total += len(evs)
	}
	if total != 5 {
		t.Fatalf("replayed %d events, want 5", total)
	}
}

func TestSpill_IgnoresTempFilesAndQuarantinesBadOnes(t *testing.T) {
	dir := t.TempDir()
	s, err := NewFileSpill(dir)
	if err != nil {
		t.Fatal(err)
	}
	_ = os.WriteFile(filepath.Join(dir, ".tmp-123"), []byte("partial"), 0o640)
	_ = os.WriteFile(filepath.Join(dir, "00000000000000000001-tok.jsonl"), []byte("{not json"), 0o640)
	if s.Pending() != 1 {
		t.Fatalf("pending = %d, want 1 (temp files are not batches)", s.Pending())
	}

	ins := &fakeInserter{}
	w := testWorker(t, ins, dir)
	w.replaySpilled()
	if s.Pending() != 0 {
		t.Fatal("unreadable batch still pending")
	}
	if _, err := os.Stat(filepath.Join(dir, "00000000000000000001-tok.jsonl.bad")); err != nil {
		t.Fatal("unreadable batch not kept aside for inspection")
	}
}
