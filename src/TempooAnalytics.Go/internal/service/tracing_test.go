package service

import (
	"context"
	"net/http/httptest"
	"sync"
	"testing"

	"github.com/google/uuid"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
	"go.opentelemetry.io/otel/sdk/trace/tracetest"
	"go.opentelemetry.io/otel/trace"
)

var (
	recorderOnce sync.Once
	recorder     *tracetest.SpanRecorder
	recorderTP   *sdktrace.TracerProvider
)

// spanRecorder installs one global provider for the package's tests (the global tracer delegates to the
// first provider set, so it cannot be swapped per test). Tests tell their spans apart by trace id.
func spanRecorder() (*tracetest.SpanRecorder, trace.Tracer) {
	recorderOnce.Do(func() {
		recorder = tracetest.NewSpanRecorder()
		recorderTP = sdktrace.NewTracerProvider(sdktrace.WithSpanProcessor(recorder))
		otel.SetTracerProvider(recorderTP)
	})
	return recorder, recorderTP.Tracer("test")
}

func spansOf(rec *tracetest.SpanRecorder, traceID trace.TraceID) map[string]sdktrace.ReadOnlySpan {
	out := map[string]sdktrace.ReadOnlySpan{}
	for _, s := range rec.Ended() {
		if s.SpanContext().TraceID() == traceID {
			out[s.Name()] = s
		}
	}
	return out
}

func attr(s sdktrace.ReadOnlySpan, key string) attribute.Value {
	for _, kv := range s.Attributes() {
		if string(kv.Key) == key {
			return kv.Value
		}
	}
	return attribute.Value{}
}

func TestProcessEvent_SpansPerStep(t *testing.T) {
	rec, tr := spanRecorder()
	repo := &mockEventRepo{}
	svc, _ := dedupSetup(t, repo)

	id := uuid.Must(uuid.NewV7()).String()
	for i := 0; i < 2; i++ { // the second is a retry: a duplicate
		ctx, parent := tr.Start(context.Background(), "POST /analytics/events")
		req := httptest.NewRequest("POST", "/analytics/events", nil)
		req.Header.Set("X-API-Key", "wk")
		dto := eventRequest()
		dto.EventId = id
		if err := svc.ProcessEvent(ctx, dto, req); err != nil {
			t.Fatal(err)
		}
		parent.End()

		spans := spansOf(rec, parent.SpanContext().TraceID())
		for _, name := range []string{"ingest.site_config", "ingest.quota", "ingest.dedup"} {
			s, ok := spans[name]
			if !ok {
				t.Fatalf("attempt %d: no %s span, got %v", i, name, keys(spans))
			}
			if s.Parent().SpanID() != parent.SpanContext().SpanID() {
				t.Errorf("%s is not a child of the request span", name)
			}
		}
		if got := attr(spans["ingest.quota"], "tempoo.quota.decision").AsString(); got != QuotaAllowed.String() {
			t.Errorf("quota decision attribute = %q", got)
		}
		dup := attr(spans["ingest.dedup"], "tempoo.event.duplicate").AsBool()
		if dup != (i == 1) {
			t.Errorf("attempt %d: duplicate = %v", i, dup)
		}
		_, enqueued := spans["ingest.enqueue"]
		if enqueued != (i == 0) {
			t.Errorf("attempt %d: enqueue span present = %v (a duplicate is not enqueued)", i, enqueued)
		}
	}
}

func keys(m map[string]sdktrace.ReadOnlySpan) []string {
	var out []string
	for k := range m {
		out = append(out, k)
	}
	return out
}
