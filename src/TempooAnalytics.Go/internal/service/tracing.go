package service

import (
	"context"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/codes"
	"go.opentelemetry.io/otel/trace"
)

// tracer creates the spans of each ingestion step, children of the HTTP server span from otelgin (which
// continues the gateway's traceparent).
var tracer = otel.Tracer("github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/service")

// startStep starts the span of one ingestion step.
func startStep(ctx context.Context, name string, attrs ...attribute.KeyValue) (context.Context, trace.Span) {
	return tracer.Start(ctx, name, trace.WithAttributes(attrs...))
}

// endStep records err (if any) on the span and ends it.
func endStep(span trace.Span, err error) {
	if err != nil {
		span.RecordError(err)
		span.SetStatus(codes.Error, err.Error())
	}
	span.End()
}
