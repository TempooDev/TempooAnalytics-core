package repository

import (
	"context"
	"sync"
	"testing"
	"time"

	"github.com/alicebob/miniredis/v2"
	"github.com/redis/go-redis/v9"
	"go.opentelemetry.io/otel"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
	"go.opentelemetry.io/otel/sdk/trace/tracetest"
)

var (
	recorderOnce sync.Once
	recorder     *tracetest.SpanRecorder
	recorderTP   *sdktrace.TracerProvider
)

// spanRecorder installs one global provider for the package's tests: the global tracer delegates to the
// first provider set.
func spanRecorder() (*tracetest.SpanRecorder, *sdktrace.TracerProvider) {
	recorderOnce.Do(func() {
		recorder = tracetest.NewSpanRecorder()
		recorderTP = sdktrace.NewTracerProvider(sdktrace.WithSpanProcessor(recorder))
		otel.SetTracerProvider(recorderTP)
	})
	return recorder, recorderTP
}

func spanAttr(s sdktrace.ReadOnlySpan, key string) string {
	for _, kv := range s.Attributes() {
		if string(kv.Key) == key {
			return kv.Value.Emit()
		}
	}
	return ""
}

func TestSiteConfig_SpanSaysWhereTheConfigCameFrom(t *testing.T) {
	rec, tp := spanRecorder()
	mr := miniredis.RunT(t)
	rdb := redis.NewClient(&redis.Options{Addr: mr.Addr()})
	defer rdb.Close()
	db := &fakeQuerier{}

	source := func(r *SiteRepository) string {
		t.Helper()
		ctx, span := tp.Tracer("test").Start(context.Background(), "lookup")
		if _, err := r.GetSiteConfig(ctx, "key", ""); err != nil {
			t.Fatal(err)
		}
		span.End()
		for _, s := range rec.Ended() {
			if s.SpanContext().SpanID() == span.SpanContext().SpanID() {
				return spanAttr(s, siteConfigSourceAttr)
			}
		}
		t.Fatal("span not recorded")
		return ""
	}

	r := newSiteRepository(db, rdb)
	if got := source(r); got != "postgres" {
		t.Errorf("first lookup: source %q, want postgres", got)
	}
	if got := source(r); got != "memory" {
		t.Errorf("second lookup: source %q, want memory", got)
	}
	deadline := time.Now().Add(time.Second)
	for !mr.Exists("site:key:config:v3") {
		if time.Now().After(deadline) {
			t.Fatal("config not cached in Redis")
		}
		time.Sleep(5 * time.Millisecond)
	}
	if got := source(newSiteRepository(db, rdb)); got != "redis" {
		t.Errorf("other instance: source %q, want redis", got)
	}
}

func TestFlush_SpanPerBatch(t *testing.T) {
	rec, _ := spanRecorder()
	ins := &fakeInserter{fail: 1}
	w := testWorker(t, ins, "")
	w.flush(events(7))

	var found bool
	for _, s := range rec.Ended() {
		if s.Name() != "clickhouse.insert_batch" || spanAttr(s, "tempoo.batch.token") != ins.attempts[0] {
			continue
		}
		found = true
		if got := spanAttr(s, "tempoo.batch.size"); got != "7" {
			t.Errorf("batch size %s, want 7", got)
		}
		if got := spanAttr(s, "tempoo.batch.outcome"); got != "inserted" {
			t.Errorf("outcome %s, want inserted", got)
		}
		if got := spanAttr(s, "tempoo.batch.retries"); got != "1" {
			t.Errorf("retries %s, want 1", got)
		}
		if len(s.Events()) != 1 || s.Events()[0].Name != "insert failed" {
			t.Errorf("events %v, want one failed attempt", s.Events())
		}
	}
	if !found {
		t.Fatal("no clickhouse.insert_batch span")
	}
}
