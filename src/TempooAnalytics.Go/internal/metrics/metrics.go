package metrics

import (
	"log"
	"net/http"

	"github.com/prometheus/client_golang/prometheus"
	"github.com/prometheus/client_golang/prometheus/promhttp"
)

var (
	// EventsIngested tracks total events ingested, labeled by event_type
	EventsIngested = prometheus.NewCounterVec(
		prometheus.CounterOpts{
			Name: "ingestion_events_total",
			Help: "Total number of events ingested",
		},
		[]string{"event_type", "site_id"},
	)

	// BatchFlushDuration tracks how long each ClickHouse batch flush takes
	BatchFlushDuration = prometheus.NewHistogram(
		prometheus.HistogramOpts{
			Name:    "ingestion_batch_flush_duration_seconds",
			Help:    "Duration of batch flushes to ClickHouse",
			Buckets: prometheus.ExponentialBuckets(0.001, 2, 15), // 1ms to ~16s
		},
	)

	// BatchSize tracks the number of events in each flushed batch
	BatchSize = prometheus.NewHistogram(
		prometheus.HistogramOpts{
			Name:    "ingestion_batch_size",
			Help:    "Number of events per batch flush",
			Buckets: prometheus.ExponentialBuckets(1, 2, 14), // 1, 2, 4, ... 8192 (INGEST_BATCH_SIZE is 5000 by default)
		},
	)

	// QuotaDecisions counts what the monthly quota check did with each event: checked (counted in Redis),
	// unchecked (Redis/ClickHouse failing or slow: accepted without counting, fail-open) or blocked.
	QuotaDecisions = prometheus.NewCounterVec(
		prometheus.CounterOpts{
			Name: "ingestion_quota_decisions_total",
			Help: "Monthly quota decisions per event: checked, unchecked (fail-open) or blocked",
		},
		[]string{"decision"},
	)

	// InsertErrors counts failed INSERT attempts into ClickHouse (each retry counts).
	InsertErrors = prometheus.NewCounter(prometheus.CounterOpts{
		Name: "ingestion_insert_errors_total",
		Help: "Failed INSERT attempts of event batches into ClickHouse",
	})

	// EventsLost counts accepted events that could not be stored: the batch failed every retry and could
	// not be saved to the spill directory, or ClickHouse rejected the row.
	EventsLost = prometheus.NewCounter(prometheus.CounterOpts{
		Name: "ingestion_events_lost_total",
		Help: "Accepted events that could not be stored in ClickHouse nor saved for later",
	})

	// SpillPendingBatches is the number of batches saved on disk waiting to be replayed into ClickHouse.
	SpillPendingBatches = prometheus.NewGauge(prometheus.GaugeOpts{
		Name: "ingestion_spill_pending_batches",
		Help: "Event batches saved on disk after failing every INSERT retry, waiting to be replayed",
	})

	// SpillReplayedEvents counts events inserted from the spill directory.
	SpillReplayedEvents = prometheus.NewCounter(prometheus.CounterOpts{
		Name: "ingestion_spill_replayed_events_total",
		Help: "Events inserted into ClickHouse from saved batches",
	})

	// DuplicateEvents counts events whose event_id was already accepted (client retries): answered 202,
	// not stored again.
	DuplicateEvents = prometheus.NewCounter(prometheus.CounterOpts{
		Name: "ingestion_duplicate_events_total",
		Help: "Events with an event_id already accepted in the deduplication window",
	})

	// IngestRequests counts POST /analytics/events by outcome (accepted or rejected) and reason: the error
	// code of the response body (overloaded, unavailable, quota_exceeded...), "accepted" for a 202.
	IngestRequests = prometheus.NewCounterVec(
		prometheus.CounterOpts{
			Name: "ingestion_requests_total",
			Help: "Ingest requests by outcome (accepted, rejected) and reason (the response error code)",
		},
		[]string{"outcome", "reason"},
	)

	// OriginOnlyAuth counts events let in by the Origin fallback without a valid write key (reason missing_key
	// or wrong_key) while INGEST_REQUIRE_WRITE_KEY is off: the traffic that turning it on would reject.
	OriginOnlyAuth = prometheus.NewCounterVec(
		prometheus.CounterOpts{
			Name: "ingestion_origin_only_auth_total",
			Help: "Events accepted by allowed origin without a valid write key (missing_key, wrong_key)",
		},
		[]string{"reason"},
	)

	// StreamFallbackEvents counts events accepted into the in-memory buffer because the Redis stream
	// (INGEST_QUEUE=redis) did not take them: Redis failing or slow. They have no persistence until inserted.
	StreamFallbackEvents = prometheus.NewCounter(prometheus.CounterOpts{
		Name: "ingestion_stream_fallback_events_total",
		Help: "Events kept in memory because the Redis stream was unavailable",
	})

	// StreamRecoveredEvents counts events read again from the stream after a crash or a stuck consumer
	// (pending entries of this or another instance).
	StreamRecoveredEvents = prometheus.NewCounter(prometheus.CounterOpts{
		Name: "ingestion_stream_recovered_events_total",
		Help: "Stream events processed again after being left pending by a crashed or stuck consumer",
	})

	// StreamRedeliveredSkipped counts redelivered events that ClickHouse already had (insert done, ack lost).
	StreamRedeliveredSkipped = prometheus.NewCounter(prometheus.CounterOpts{
		Name: "ingestion_stream_redelivered_skipped_total",
		Help: "Redelivered stream events skipped because ClickHouse already stored them",
	})

	// HTTPRequestDuration tracks HTTP request latencies
	HTTPRequestDuration = prometheus.NewHistogramVec(
		prometheus.HistogramOpts{
			Name:    "http_server_request_duration_seconds",
			Help:    "HTTP request duration in seconds",
			Buckets: prometheus.DefBuckets,
		},
		[]string{"method", "route", "status_code"},
	)

	// HTTPRequestsTotal tracks total HTTP requests
	HTTPRequestsTotal = prometheus.NewCounterVec(
		prometheus.CounterOpts{
			Name: "http_server_requests_total",
			Help: "Total HTTP requests processed",
		},
		[]string{"method", "route", "status_code"},
	)
)

// RegisterQueueDepth publishes ingestion_queue_depth: events accepted and waiting in the buffer for a worker.
// When it reaches INGEST_BUFFER_CAPACITY new events get 503 overloaded.
func RegisterQueueDepth(depth func() int, capacity int) {
	capacityGauge := prometheus.NewGauge(prometheus.GaugeOpts{
		Name: "ingestion_queue_capacity",
		Help: "Size of the event buffer (INGEST_BUFFER_CAPACITY)",
	})
	capacityGauge.Set(float64(capacity))
	prometheus.MustRegister(
		prometheus.NewGaugeFunc(prometheus.GaugeOpts{
			Name: "ingestion_queue_depth",
			Help: "Accepted events waiting in the buffer to be inserted into ClickHouse",
		}, func() float64 { return float64(depth()) }),
		capacityGauge,
	)
}

// Init registers all custom metrics and returns the HTTP handler for /metrics
func Init() http.Handler {
	prometheus.MustRegister(
		EventsIngested,
		BatchFlushDuration,
		BatchSize,
		QuotaDecisions,
		InsertErrors,
		EventsLost,
		SpillPendingBatches,
		SpillReplayedEvents,
		DuplicateEvents,
		IngestRequests,
		OriginOnlyAuth,
		StreamFallbackEvents,
		StreamRecoveredEvents,
		StreamRedeliveredSkipped,
		HTTPRequestDuration,
		HTTPRequestsTotal,
	)

	log.Println("Prometheus metrics initialized")
	return promhttp.Handler()
}
