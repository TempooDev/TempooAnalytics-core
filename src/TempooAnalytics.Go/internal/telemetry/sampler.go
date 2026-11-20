package telemetry

import (
	"log"
	"os"
	"strconv"
	"strings"

	sdktrace "go.opentelemetry.io/otel/sdk/trace"
)

// samplerFromEnv builds the trace sampler from OTEL_TRACES_SAMPLER and OTEL_TRACES_SAMPLER_ARG (the
// standard OpenTelemetry names and values). Unset, it keeps defaultSamplerRatio of the traces that start
// here and follows the caller's decision when a traceparent arrives (parentbased_traceidratio), so a trace
// sampled by the gateway is complete here too.
func samplerFromEnv() sdktrace.Sampler {
	name := strings.ToLower(strings.TrimSpace(os.Getenv("OTEL_TRACES_SAMPLER")))
	arg := strings.TrimSpace(os.Getenv("OTEL_TRACES_SAMPLER_ARG"))

	ratio := func() float64 {
		if arg == "" {
			return defaultSamplerRatio
		}
		r, err := strconv.ParseFloat(arg, 64)
		if err != nil || r < 0 || r > 1 {
			log.Printf("OTEL_TRACES_SAMPLER_ARG=%q is not a ratio between 0 and 1, using %v", arg, defaultSamplerRatio)
			return defaultSamplerRatio
		}
		return r
	}

	switch name {
	case "always_on":
		return sdktrace.AlwaysSample()
	case "always_off":
		return sdktrace.NeverSample()
	case "traceidratio":
		return sdktrace.TraceIDRatioBased(ratio())
	case "parentbased_always_on":
		return sdktrace.ParentBased(sdktrace.AlwaysSample())
	case "parentbased_always_off":
		return sdktrace.ParentBased(sdktrace.NeverSample())
	case "", "parentbased_traceidratio":
		return sdktrace.ParentBased(sdktrace.TraceIDRatioBased(ratio()))
	default:
		log.Printf("OTEL_TRACES_SAMPLER=%q is not supported, using parentbased_traceidratio", name)
		return sdktrace.ParentBased(sdktrace.TraceIDRatioBased(ratio()))
	}
}
