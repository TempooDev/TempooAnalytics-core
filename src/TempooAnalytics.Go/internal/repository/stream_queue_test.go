package repository

import (
	"context"
	"os"
	"sync"
	"testing"
	"time"

	"github.com/alicebob/miniredis/v2"
	"github.com/google/uuid"
	"github.com/redis/go-redis/v9"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/model"
)

func newStreamQueue(t *testing.T, ins *fakeInserter, spillDir string, cfg StreamConfig) (*StreamQueue, *miniredis.Miniredis, *redis.Client) {
	t.Helper()
	mr := miniredis.RunT(t)
	addr := mr.Addr()
	if real := os.Getenv("REDIS_TEST_ADDR"); real != "" { // run the same tests against a real Redis
		addr = real
		cfg.Key = "test:" + uuid.NewString()
	}
	rdb := redis.NewClient(&redis.Options{Addr: addr})
	t.Cleanup(func() { _ = rdb.Close() })
	w := NewClickHouseWorker(nil, WorkerConfig{InsertRetries: 2, SpillDir: spillDir, BatchSize: 100, FlushInterval: 30 * time.Millisecond, NumWorkers: 1})
	w.insert = ins.insert
	w.sleep = func(ctx context.Context, _ time.Duration) bool { return ctx.Err() == nil }
	q, err := NewStreamQueue(context.Background(), rdb, cfg, w)
	if err != nil {
		t.Fatal(err)
	}
	return q, mr, rdb
}

func storedCount(f *fakeInserter) int {
	f.mu.Lock()
	defer f.mu.Unlock()
	n := 0
	for _, evs := range f.stored {
		n += len(evs)
	}
	return n
}

func waitFor(t *testing.T, what string, cond func() bool) {
	t.Helper()
	deadline := time.Now().Add(5 * time.Second)
	for time.Now().Before(deadline) {
		if cond() {
			return
		}
		time.Sleep(10 * time.Millisecond)
	}
	t.Fatalf("timeout waiting for %s", what)
}

func TestStream_ConsumesInsertsAndAcknowledges(t *testing.T) {
	ins := &fakeInserter{}
	q, _, rdb := newStreamQueue(t, ins, "", StreamConfig{})
	for _, e := range events(5) {
		if err := q.Enqueue(context.Background(), e); err != nil {
			t.Fatal(err)
		}
	}
	q.Start()
	defer q.Stop()

	waitFor(t, "5 events in ClickHouse", func() bool { return storedCount(ins) == 5 })
	waitFor(t, "stream emptied", func() bool { n, _ := rdb.XLen(context.Background(), q.cfg.Key).Result(); return n == 0 })
	pend, _ := rdb.XPending(context.Background(), q.cfg.Key, q.cfg.Group).Result()
	if pend.Count != 0 {
		t.Fatalf("pending = %d, want 0", pend.Count)
	}
}

func TestStream_RoundTripKeepsTheEvent(t *testing.T) {
	ins := &fakeInserter{}
	q, _, _ := newStreamQueue(t, ins, "", StreamConfig{})
	in := events(1)[0]
	if err := q.Enqueue(context.Background(), in); err != nil {
		t.Fatal(err)
	}
	q.Start()
	defer q.Stop()
	waitFor(t, "event stored", func() bool { return storedCount(ins) == 1 })
	for _, evs := range ins.stored {
		if evs[0].EventID != in.EventID || evs[0].SiteID != in.SiteID || !evs[0].Timestamp.Equal(in.Timestamp) {
			t.Fatalf("got %+v, want %+v", evs[0], in)
		}
	}
}

func TestStream_TakesOverEntriesOfADeadConsumer(t *testing.T) {
	ins := &fakeInserter{}
	q, _, rdb := newStreamQueue(t, ins, "", StreamConfig{ClaimIdle: 100 * time.Millisecond})
	ctx := context.Background()
	for _, e := range events(4) {
		if err := q.Enqueue(ctx, e); err != nil {
			t.Fatal(err)
		}
	}
	// Another instance reads them and dies without acknowledging.
	res, err := rdb.XReadGroup(ctx, &redis.XReadGroupArgs{Group: q.cfg.Group, Consumer: "dead-0", Streams: []string{q.cfg.Key, ">"}, Count: 10}).Result()
	if err != nil || len(res[0].Messages) != 4 {
		t.Fatalf("setup read: %v %v", err, res)
	}
	q.Start()
	defer q.Stop()

	waitFor(t, "takeover inserted", func() bool { return storedCount(ins) == 4 })
	waitFor(t, "stream emptied", func() bool { n, _ := rdb.XLen(ctx, q.cfg.Key).Result(); return n == 0 })
}

func TestStream_BatchThatCannotBeInsertedNorSpilledStaysPending(t *testing.T) {
	ins := &fakeInserter{fail: -1}
	q, _, rdb := newStreamQueue(t, ins, "", StreamConfig{}) // no spill dir: the batch would be lost
	for _, e := range events(3) {
		_ = q.Enqueue(context.Background(), e)
	}
	q.Start()
	waitFor(t, "insert attempted", func() bool { ins.mu.Lock(); defer ins.mu.Unlock(); return len(ins.attempts) > 0 })
	q.Stop()
	if n, _ := rdb.XLen(context.Background(), q.cfg.Key).Result(); n != 3 {
		t.Fatalf("XLEN = %d, want 3: entries must stay in the stream", n)
	}
}

func TestStream_SpilledBatchIsAcknowledged(t *testing.T) {
	ins := &fakeInserter{fail: -1}
	q, _, rdb := newStreamQueue(t, ins, t.TempDir(), StreamConfig{})
	for _, e := range events(3) {
		_ = q.Enqueue(context.Background(), e)
	}
	q.Start()
	defer q.Stop()
	waitFor(t, "stream emptied after spill", func() bool { n, _ := rdb.XLen(context.Background(), q.cfg.Key).Result(); return n == 0 })
}

func TestStream_FullStreamRejectsWithErrBufferFull(t *testing.T) {
	q, _, _ := newStreamQueue(t, &fakeInserter{}, "", StreamConfig{MaxLen: 2})
	q.depth.Store(2)
	if err := q.Enqueue(context.Background(), events(1)[0]); err != ErrBufferFull {
		t.Fatalf("got %v, want ErrBufferFull", err)
	}
}

func TestRepository_FallsBackToMemoryWhenRedisIsDown(t *testing.T) {
	if os.Getenv("REDIS_TEST_ADDR") != "" {
		t.Skip("needs the in-process Redis, which the test can stop")
	}
	ins := &fakeInserter{}
	q, mr, _ := newStreamQueue(t, ins, "", StreamConfig{})
	r := &ClickHouseRepository{worker: q.worker, stream: q}
	mr.Close() // Redis goes away

	if err := r.InsertEvent(context.Background(), events(1)[0]); err != nil {
		t.Fatalf("event must be accepted in memory, got %v", err)
	}
	if q.worker.QueueDepth() != 1 {
		t.Fatalf("memory buffer depth = %d, want 1", q.worker.QueueDepth())
	}
}

func TestStreamToken_StableAndDistinct(t *testing.T) {
	a := streamToken([]string{"1-0", "2-0"})
	if a != streamToken([]string{"1-0", "2-0"}) || a == streamToken([]string{"1-0", "3-0"}) {
		t.Fatal("token must depend only on the entry ids")
	}
}

func TestStream_RedeliveredEventsAlreadyStoredAreSkipped(t *testing.T) {
	ins := &fakeInserter{}
	q, _, rdb := newStreamQueue(t, ins, "", StreamConfig{})
	evs := events(4)
	have := map[uuid.UUID]bool{evs[0].EventID: true, evs[2].EventID: true}
	q.existing = func(_ context.Context, _ []*model.AnalyticsEvent) (map[uuid.UUID]bool, error) { return have, nil }
	for _, e := range evs {
		if err := q.Enqueue(context.Background(), e); err != nil {
			t.Fatal(err)
		}
	}
	// A consumer with this name reads the entries and "dies" before acknowledging them.
	name := q.cfg.Consumer + "-0"
	if _, err := rdb.XReadGroup(context.Background(), &redis.XReadGroupArgs{Group: q.cfg.Group, Consumer: name, Streams: []string{q.cfg.Key, ">"}, Count: 10}).Result(); err != nil {
		t.Fatal(err)
	}
	q.Start()
	defer q.Stop()
	waitFor(t, "2 new events inserted", func() bool { return storedCount(ins) == 2 })
	waitFor(t, "stream emptied", func() bool { n, _ := rdb.XLen(context.Background(), q.cfg.Key).Result(); return n == 0 })
	time.Sleep(100 * time.Millisecond)
	if n := storedCount(ins); n != 2 {
		t.Fatalf("stored %d, want 2 (the other 2 were already in ClickHouse)", n)
	}
}

func TestStream_ConcurrentEnqueueAllArrive(t *testing.T) {
	ins := &fakeInserter{}
	q, _, rdb := newStreamQueue(t, ins, "", StreamConfig{})
	var wg sync.WaitGroup
	for _, e := range events(500) {
		wg.Add(1)
		go func(e *model.AnalyticsEvent) {
			defer wg.Done()
			if err := q.Enqueue(context.Background(), e); err != nil {
				t.Error(err)
			}
		}(e)
	}
	wg.Wait()
	if n, _ := rdb.XLen(context.Background(), q.cfg.Key).Result(); n != 500 {
		t.Fatalf("XLEN = %d, want 500", n)
	}
	q.Stop()
}

func TestStream_EnqueueFailsWhenRedisIsDown(t *testing.T) {
	ins := &fakeInserter{}
	q, mr, _ := newStreamQueue(t, ins, "", StreamConfig{})
	if os.Getenv("REDIS_TEST_ADDR") != "" {
		t.Skip("needs to stop Redis")
	}
	mr.Close()
	if err := q.Enqueue(context.Background(), events(1)[0]); err == nil {
		t.Fatal("Enqueue returned nil with Redis down: the event would be answered 202 and lost")
	}
	q.Stop()
}
