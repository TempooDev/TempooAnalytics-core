package telemetry

import (
	"context"
	"crypto/tls"
	"log"
	"os"
	"strings"
	"time"

	"google.golang.org/grpc/credentials" // Needed for TLS in gRPC

	promclient "github.com/prometheus/client_golang/prometheus"
	prombridge "go.opentelemetry.io/contrib/bridges/prometheus"
	"go.opentelemetry.io/contrib/instrumentation/host"
	"go.opentelemetry.io/contrib/instrumentation/runtime"
	"go.opentelemetry.io/otel"

	"go.opentelemetry.io/otel/exporters/otlp/otlpmetric/otlpmetricgrpc"
	"go.opentelemetry.io/otel/exporters/otlp/otlpmetric/otlpmetrichttp"
	"go.opentelemetry.io/otel/exporters/otlp/otlptrace/otlptracegrpc"
	"go.opentelemetry.io/otel/exporters/otlp/otlptrace/otlptracehttp"

	"go.opentelemetry.io/otel/propagation"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/resource"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
	semconv "go.opentelemetry.io/otel/semconv/v1.24.0"
)

// Option changes what InitTelemetry sets up.
type Option func(*options)

type options struct {
	gatherer promclient.Gatherer
}

// WithPrometheusGatherer also sends the metrics of a Prometheus registry (the ones served on /metrics) with
// the OTLP metrics, so they reach Prometheus by push when nothing scrapes the service.
func WithPrometheusGatherer(g promclient.Gatherer) Option {
	return func(o *options) { o.gatherer = g }
}

// defaultSamplerRatio is the share of traces kept when OTEL_TRACES_SAMPLER is not set: enough to see
// examples of every path without sending every request to Tempo.
const defaultSamplerRatio = 0.01

// defaultMetricInterval is how often metrics are pushed when OTEL_METRIC_EXPORT_INTERVAL is not set.
const defaultMetricInterval = 15 * time.Second

// InitTelemetry sets up traces and metrics over OTLP. It is off unless ENABLE_OTEL=true.
//
// Standard variables it honours:
//   - OTEL_SERVICE_NAME and OTEL_RESOURCE_ATTRIBUTES (serviceName is only the default name).
//   - OTEL_TRACES_SAMPLER / OTEL_TRACES_SAMPLER_ARG (default parentbased_traceidratio with 0.01).
//   - OTEL_EXPORTER_OTLP_ENDPOINT / _PROTOCOL / _HEADERS for traces, and for metrics unless the
//     metrics-specific ones are set.
//   - OTEL_EXPORTER_OTLP_METRICS_ENDPOINT / _PROTOCOL / _HEADERS: where metrics go, for example
//     Prometheus' OTLP receiver (http/protobuf, http://prometheus:9090/api/v1/otlp/v1/metrics). Tempo
//     only takes traces, so set this whenever the general endpoint is Tempo.
//   - OTEL_METRICS_EXPORTER=none to send no metrics (the Prometheus /metrics endpoint still works).
//   - OTEL_METRIC_EXPORT_INTERVAL in milliseconds (default 15000).
func InitTelemetry(serviceName string, opts ...Option) (func(context.Context) error, error) {
	// NEW: Option to disable OTEL to avoid log spam in environments without a collector
	if strings.ToLower(os.Getenv("ENABLE_OTEL")) != "true" {
		return func(ctx context.Context) error { return nil }, nil
	}
	var o options
	for _, opt := range opts {
		opt(&o)
	}

	ctx := context.Background()

	res, err := newResource(ctx, serviceName)
	if err != nil {
		return nil, err
	}

	// ---------------------------------------------------------
	// TRACES
	// ---------------------------------------------------------
	traceTarget := exporterTarget{
		endpoint: envOr("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317"),
		protocol: envOr("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc"),
		headers:  parseHeaders(os.Getenv("OTEL_EXPORTER_OTLP_HEADERS")),
	}
	isSentry := strings.Contains(traceTarget.endpoint, "sentry.io")

	traceExporter, err := newTraceExporter(ctx, traceTarget)
	if err != nil {
		return nil, err
	}

	sampler := samplerFromEnv()
	tp := sdktrace.NewTracerProvider(
		sdktrace.WithSampler(sampler),
		sdktrace.WithBatcher(traceExporter,
			sdktrace.WithBatchTimeout(500*time.Millisecond),
			sdktrace.WithMaxExportBatchSize(512),
		),
		sdktrace.WithResource(res),
	)
	otel.SetTracerProvider(tp)

	// traceparent and baggage from the caller (the gateway) become the parent of the server span.
	otel.SetTextMapPropagator(propagation.NewCompositeTextMapPropagator(
		propagation.TraceContext{},
		propagation.Baggage{},
	))

	// ---------------------------------------------------------
	// METRICS
	// ---------------------------------------------------------
	metricTarget, sendMetrics := metricTargetFromEnv(traceTarget)
	// Sentry does not take OTLP metrics.
	if isSentry && os.Getenv("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT") == "" {
		sendMetrics = false
	}

	var mp *sdkmetric.MeterProvider
	if sendMetrics {
		metricExporter, err := newMetricExporter(ctx, metricTarget)
		if err != nil {
			return nil, err
		}
		readerOpts := []sdkmetric.PeriodicReaderOption{}
		if os.Getenv("OTEL_METRIC_EXPORT_INTERVAL") == "" {
			readerOpts = append(readerOpts, sdkmetric.WithInterval(defaultMetricInterval))
		}
		if o.gatherer != nil {
			readerOpts = append(readerOpts, sdkmetric.WithProducer(prombridge.NewMetricProducer(prombridge.WithGatherer(o.gatherer))))
		}
		mp = sdkmetric.NewMeterProvider(
			sdkmetric.WithResource(res),
			sdkmetric.WithReader(sdkmetric.NewPeriodicReader(metricExporter, readerOpts...)),
		)
		otel.SetMeterProvider(mp)

		if err := runtime.Start(runtime.WithMinimumReadMemStatsInterval(10 * time.Second)); err != nil {
			println("⚠️ Error iniciando métricas de runtime: " + err.Error())
		}
		if err := host.Start(); err != nil {
			println("⚠️ Error iniciando métricas de host: " + err.Error())
		}
	} else {
		mp = sdkmetric.NewMeterProvider()
		otel.SetMeterProvider(mp)
	}

	log.Printf("OpenTelemetry: traces to %s (%s, sampler %s), metrics %s", traceTarget.endpoint, traceTarget.protocol,
		sampler.Description(), describeMetrics(sendMetrics, metricTarget))

	return func(ctx context.Context) error {
		shutdownCtx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
		defer cancel()

		if err := tp.Shutdown(shutdownCtx); err != nil {
			println("Error cerrando TracerProvider: " + err.Error())
		}

		return mp.Shutdown(shutdownCtx)
	}, nil
}

// newResource takes the service name from OTEL_SERVICE_NAME when set, serviceName otherwise, and adds
// OTEL_RESOURCE_ATTRIBUTES. Later sources win, so the environment overrides the default.
func newResource(ctx context.Context, serviceName string) (*resource.Resource, error) {
	return resource.New(ctx,
		resource.WithAttributes(semconv.ServiceNameKey.String(serviceName)),
		resource.WithTelemetrySDK(),
		resource.WithFromEnv(),
	)
}

type exporterTarget struct {
	endpoint string
	protocol string
	headers  map[string]string
}

func (t exporterTarget) http() bool { return strings.HasPrefix(t.protocol, "http") }

// metricTargetFromEnv returns where metrics go and whether to send them at all.
func metricTargetFromEnv(traces exporterTarget) (exporterTarget, bool) {
	if strings.EqualFold(strings.TrimSpace(os.Getenv("OTEL_METRICS_EXPORTER")), "none") {
		return exporterTarget{}, false
	}
	endpoint := os.Getenv("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT")
	if endpoint == "" {
		return traces, true
	}
	// A metrics-only endpoint is another server: do not send it the trace headers (they can be credentials).
	return exporterTarget{
		endpoint: endpoint,
		protocol: envOr("OTEL_EXPORTER_OTLP_METRICS_PROTOCOL", traces.protocol),
		headers:  parseHeaders(os.Getenv("OTEL_EXPORTER_OTLP_METRICS_HEADERS")),
	}, true
}

func describeMetrics(send bool, t exporterTarget) string {
	if !send {
		return "off"
	}
	return "to " + t.endpoint + " (" + t.protocol + ")"
}

func newTraceExporter(ctx context.Context, t exporterTarget) (sdktrace.SpanExporter, error) {
	useTLS, hostPort := splitEndpoint(t.endpoint)
	if t.http() {
		opts := []otlptracehttp.Option{
			otlptracehttp.WithEndpoint(hostPort),
			otlptracehttp.WithHeaders(t.headers),
			otlptracehttp.WithCompression(otlptracehttp.GzipCompression),
		}
		if !useTLS {
			opts = append(opts, otlptracehttp.WithInsecure())
		}
		return otlptracehttp.New(ctx, opts...)
	}
	opts := []otlptracegrpc.Option{
		otlptracegrpc.WithEndpoint(hostPort),
		otlptracegrpc.WithHeaders(t.headers),
		otlptracegrpc.WithCompressor("gzip"),
	}
	if useTLS {
		opts = append(opts, otlptracegrpc.WithTLSCredentials(insecureTLS()))
	} else {
		opts = append(opts, otlptracegrpc.WithInsecure())
	}
	return otlptracegrpc.New(ctx, opts...)
}

func newMetricExporter(ctx context.Context, t exporterTarget) (sdkmetric.Exporter, error) {
	useTLS, hostPort := splitEndpoint(t.endpoint)
	if t.http() {
		opts := []otlpmetrichttp.Option{otlpmetrichttp.WithHeaders(t.headers)}
		if os.Getenv("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT") != "" {
			// The metrics endpoint is a full URL, path included (Prometheus: /api/v1/otlp/v1/metrics).
			opts = append(opts, otlpmetrichttp.WithEndpointURL(t.endpoint))
		} else {
			opts = append(opts, otlpmetrichttp.WithEndpoint(hostPort))
		}
		if !useTLS {
			opts = append(opts, otlpmetrichttp.WithInsecure())
		}
		return otlpmetrichttp.New(ctx, opts...)
	}
	opts := []otlpmetricgrpc.Option{
		otlpmetricgrpc.WithEndpoint(hostPort),
		otlpmetricgrpc.WithHeaders(t.headers),
	}
	if useTLS {
		opts = append(opts, otlpmetricgrpc.WithTLSCredentials(insecureTLS()))
	} else {
		opts = append(opts, otlpmetricgrpc.WithInsecure())
	}
	return otlpmetricgrpc.New(ctx, opts...)
}

// splitEndpoint returns whether the endpoint uses TLS and the endpoint without its scheme.
func splitEndpoint(endpoint string) (bool, string) {
	useTLS := strings.HasPrefix(endpoint, "https")
	target := strings.TrimPrefix(endpoint, "https://")
	target = strings.TrimPrefix(target, "http://")
	return useTLS, target
}

func insecureTLS() credentials.TransportCredentials {
	return credentials.NewTLS(&tls.Config{InsecureSkipVerify: true})
}

func envOr(key, def string) string {
	if v := strings.TrimSpace(os.Getenv(key)); v != "" {
		return v
	}
	return def
}

func parseHeaders(raw string) map[string]string {
	headers := make(map[string]string)
	if raw == "" {
		return headers
	}
	pairs := strings.Split(raw, ",")
	for _, pair := range pairs {
		parts := strings.SplitN(pair, "=", 2)
		if len(parts) == 2 {
			headers[strings.TrimSpace(parts[0])] = strings.TrimSpace(parts[1])
		}
	}
	return headers
}
