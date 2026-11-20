package telemetry

import (
	"context"
	"testing"

	semconv "go.opentelemetry.io/otel/semconv/v1.24.0"
)

func TestSamplerFromEnv(t *testing.T) {
	cases := []struct {
		sampler, arg, want string
	}{
		{"", "", "ParentBased{root:TraceIDRatioBased{0.01},"},
		{"parentbased_traceidratio", "0.1", "ParentBased{root:TraceIDRatioBased{0.1},"},
		{"traceidratio", "0.5", "TraceIDRatioBased{0.5}"},
		{"always_on", "", "AlwaysOnSampler"},
		{"always_off", "", "AlwaysOffSampler"},
		{"parentbased_always_on", "", "ParentBased{root:AlwaysOnSampler,"},
		{"parentbased_traceidratio", "nope", "ParentBased{root:TraceIDRatioBased{0.01},"},
		{"parentbased_traceidratio", "2", "ParentBased{root:TraceIDRatioBased{0.01},"},
		{"jaeger_remote", "", "ParentBased{root:TraceIDRatioBased{0.01},"},
	}
	for _, c := range cases {
		t.Setenv("OTEL_TRACES_SAMPLER", c.sampler)
		t.Setenv("OTEL_TRACES_SAMPLER_ARG", c.arg)
		got := samplerFromEnv().Description()
		if len(got) < len(c.want) || got[:len(c.want)] != c.want {
			t.Errorf("sampler %q arg %q: got %q, want prefix %q", c.sampler, c.arg, got, c.want)
		}
	}
}

func TestResourceHonoursServiceNameFromEnv(t *testing.T) {
	t.Setenv("OTEL_SERVICE_NAME", "tempoo-ingestion")
	t.Setenv("OTEL_RESOURCE_ATTRIBUTES", "deployment.environment=stress")
	res, err := newResource(context.Background(), "ingestion-service")
	if err != nil {
		t.Fatal(err)
	}
	attrs := map[string]string{}
	for _, kv := range res.Attributes() {
		attrs[string(kv.Key)] = kv.Value.Emit()
	}
	if got := attrs[string(semconv.ServiceNameKey)]; got != "tempoo-ingestion" {
		t.Errorf("service.name = %q, want tempoo-ingestion", got)
	}
	if got := attrs["deployment.environment"]; got != "stress" {
		t.Errorf("deployment.environment = %q, want stress", got)
	}

	t.Setenv("OTEL_SERVICE_NAME", "")
	res, err = newResource(context.Background(), "ingestion-service")
	if err != nil {
		t.Fatal(err)
	}
	for _, kv := range res.Attributes() {
		if kv.Key == semconv.ServiceNameKey && kv.Value.Emit() != "ingestion-service" {
			t.Errorf("without OTEL_SERVICE_NAME, service.name = %q, want the default", kv.Value.Emit())
		}
	}
}

func TestMetricTargetNeverDefaultsToTracesWhenMetricsEndpointIsSet(t *testing.T) {
	traces := exporterTarget{endpoint: "http://tempo:4317", protocol: "grpc", headers: map[string]string{"Authorization": "secret"}}

	t.Setenv("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT", "http://prometheus:9090/api/v1/otlp/v1/metrics")
	t.Setenv("OTEL_EXPORTER_OTLP_METRICS_PROTOCOL", "http/protobuf")
	got, send := metricTargetFromEnv(traces)
	if !send || got.endpoint != "http://prometheus:9090/api/v1/otlp/v1/metrics" || got.protocol != "http/protobuf" {
		t.Fatalf("got %+v send=%v", got, send)
	}
	if len(got.headers) != 0 {
		t.Errorf("trace headers leaked to the metrics endpoint: %v", got.headers)
	}

	t.Setenv("OTEL_METRICS_EXPORTER", "none")
	if _, send := metricTargetFromEnv(traces); send {
		t.Error("OTEL_METRICS_EXPORTER=none still sends metrics")
	}
}
