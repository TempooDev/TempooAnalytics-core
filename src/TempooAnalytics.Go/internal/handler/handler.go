package handler

import (
	"errors"
	"fmt"
	"net/http"
	"strconv"
	"time"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/metrics"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/service"
	"github.com/gin-gonic/gin"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/trace"
)

type Handler struct {
	svc *service.IngestionService
}

func NewHandler(svc *service.IngestionService) *Handler {
	return &Handler{svc: svc}
}

// maxIngestBodyBytes caps an event body before it is parsed. The largest legitimate body is a heatmap
// batch of 50 records, well under 16 KB.
const maxIngestBodyBytes = 64 << 10

// Error codes in the JSON body ({"error": "...", "code": "..."}). They are stable: the tracker and the load
// tests rely on them.
const (
	CodeInvalidJSON      = "invalid_json"
	CodePayloadTooLarge  = "payload_too_large"
	CodeUnauthorized     = "unauthorized"
	CodeTrialExpired     = "trial_expired"
	CodeOriginNotAllowed = "origin_not_allowed"
	CodeSiteNotFound     = "site_not_found"
	CodeQuotaExceeded    = "quota_exceeded"
	CodeOverloaded       = "overloaded"
	CodeUnavailable      = "unavailable"
	CodeInternal         = "internal_error"
)

func errorBody(code, message string) gin.H {
	return gin.H{"error": message, "code": code}
}

func (h *Handler) HandleIngest(c *gin.Context) {
	c.Request.Body = http.MaxBytesReader(c.Writer, c.Request.Body, maxIngestBodyBytes)

	var dto service.AnalyticsEventDto
	if err := c.ShouldBindJSON(&dto); err != nil {
		var tooLarge *http.MaxBytesError
		if errors.As(err, &tooLarge) {
			reject(c, http.StatusRequestEntityTooLarge, CodePayloadTooLarge, errorBody(CodePayloadTooLarge, "Request body too large"))
			return
		}
		reject(c, http.StatusBadRequest, CodeInvalidJSON, errorBody(CodeInvalidJSON, err.Error()))
		return
	}

	if err := h.svc.ProcessEvent(c.Request.Context(), &dto, c.Request); err != nil {
		status, body := errorResponse(c, err)
		reject(c, status, body["code"].(string), body)
		return
	}

	metrics.IngestRequests.WithLabelValues("accepted", "accepted").Inc()
	trace.SpanFromContext(c.Request.Context()).SetAttributes(attribute.String(ingestCodeAttr, "accepted"))
	c.Status(http.StatusAccepted)
}

// ingestCodeAttr on the HTTP server span: "accepted" or the error code of the response.
const ingestCodeAttr = "tempoo.ingest.code"

// reject answers the error, counts it by reason and marks the request span with the code.
func reject(c *gin.Context, status int, reason string, body gin.H) {
	metrics.IngestRequests.WithLabelValues("rejected", reason).Inc()
	trace.SpanFromContext(c.Request.Context()).SetAttributes(attribute.String(ingestCodeAttr, body["code"].(string)))
	c.AbortWithStatusJSON(status, body)
}

// errorResponse maps a ProcessEvent error to its status and body, setting Retry-After where the client
// should retry later. Only unexpected errors and unavailable dependencies are logged: the 4xx are the
// client's and would flood the log under load.
func errorResponse(c *gin.Context, err error) (int, gin.H) {
	var quota *service.QuotaExceededError
	switch {
	case errors.Is(err, service.ErrSiteNotFound):
		return http.StatusNotFound, errorBody(CodeSiteNotFound, "Unknown site")
	case errors.Is(err, service.ErrUnauthorized):
		return http.StatusUnauthorized, errorBody(CodeUnauthorized, "Missing or invalid write key")
	case errors.Is(err, service.ErrOriginNotAllowed):
		return http.StatusForbidden, errorBody(CodeOriginNotAllowed, "Origin not allowed for this site")
	case errors.Is(err, service.ErrTrialExpired):
		return http.StatusPaymentRequired, errorBody(CodeTrialExpired, "Trial expired. Please upgrade your plan.")
	case errors.As(err, &quota):
		c.Header("Retry-After", retryAfterSeconds(quota.RetryAfter))
		return http.StatusTooManyRequests, errorBody(CodeQuotaExceeded, "Monthly event limit reached for this site")
	case errors.Is(err, service.ErrOverloaded):
		c.Header("Retry-After", "1")
		return http.StatusServiceUnavailable, errorBody(CodeOverloaded, "Too busy, retry later")
	case errors.Is(err, service.ErrDependencyUnavailable):
		fmt.Printf("[INGEST ERROR] %v\n", err)
		c.Header("Retry-After", "1")
		return http.StatusServiceUnavailable, errorBody(CodeUnavailable, "Temporarily unavailable, retry later")
	}
	fmt.Printf("[INGEST ERROR] %v\n", err)
	return http.StatusInternalServerError, errorBody(CodeInternal, "Failed to process event")
}

// retryAfterSeconds rounds up to whole seconds, at least 1.
func retryAfterSeconds(d time.Duration) string {
	secs := int64((d + time.Second - 1) / time.Second)
	if secs < 1 {
		secs = 1
	}
	return strconv.FormatInt(secs, 10)
}

// InFlightLimit answers 503 with Retry-After at once when max requests are already being processed, before
// reading the body. It keeps latency and memory bounded when more traffic arrives than ingestion can take:
// the excess gets a fast 503 instead of queueing until the client times out. max <= 0 disables it.
func InFlightLimit(max int) gin.HandlerFunc {
	if max <= 0 {
		return func(c *gin.Context) { c.Next() }
	}
	slots := make(chan struct{}, max)
	return func(c *gin.Context) {
		select {
		case slots <- struct{}{}:
			defer func() { <-slots }()
			c.Next()
		default:
			c.Header("Retry-After", "1")
			// Same code as a full buffer for the client; its own reason in the metric, to tell them apart.
			reject(c, http.StatusServiceUnavailable, "in_flight_limit", errorBody(CodeOverloaded, "Too busy, retry later"))
		}
	}
}
