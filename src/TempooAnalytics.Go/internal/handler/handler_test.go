package handler

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/metrics"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/model"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/repository"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/service"
	"github.com/alicebob/miniredis/v2"
	"github.com/gin-gonic/gin"
	"github.com/prometheus/client_golang/prometheus/testutil"
	"github.com/redis/go-redis/v9"
)

// ── Mock repos for handler-level tests ──────────────────────────────

type mockEventRepo struct {
	events []*model.AnalyticsEvent
	err    error
}

func (m *mockEventRepo) InsertEvent(_ context.Context, e *model.AnalyticsEvent) error {
	if m.err != nil {
		return m.err
	}
	m.events = append(m.events, e)
	return nil
}

func (m *mockEventRepo) InsertHeatmapClicks(_ context.Context, _ []*model.HeatmapClick) error {
	return m.err
}

func (m *mockEventRepo) CountMonthlyEvents(_ context.Context, _ []string, _ time.Time) (map[string]int64, error) {
	return map[string]int64{}, nil
}

func (m *mockEventRepo) Close() error { return nil }

type mockSiteRepo struct {
	allowedOrigins []string
	err            error
	config         *repository.SiteConfig // overrides the default config when set
}

func (m *mockSiteRepo) GetSiteConfig(_ context.Context, _ string, _ string) (*repository.SiteConfig, error) {
	if m.err != nil {
		return nil, m.err
	}
	if m.config != nil {
		return m.config, nil
	}
	return &repository.SiteConfig{
		WriteKey:       "wk_test",
		AllowedOrigins: m.allowedOrigins,
	}, nil
}

func init() {
	gin.SetMode(gin.TestMode)
}

func setupRouter(eventErr error, origins []string) *gin.Engine {
	eventRepo := &mockEventRepo{err: eventErr}
	siteRepo := &mockSiteRepo{allowedOrigins: origins}
	svc := service.NewIngestionService(eventRepo, siteRepo, nil, nil, nil)
	h := NewHandler(svc)

	r := gin.New()
	r.POST("/analytics/events", h.HandleIngest)
	return r
}

// ── Tests ───────────────────────────────────────────────────────────

func TestHandleIngest_InvalidJSON(t *testing.T) {
	r := setupRouter(nil, []string{"https://mysite.com"})

	req := httptest.NewRequest(http.MethodPost, "/analytics/events", bytes.NewBufferString("not json"))
	req.Header.Set("Content-Type", "application/json")
	w := httptest.NewRecorder()

	r.ServeHTTP(w, req)

	if w.Code != http.StatusBadRequest {
		t.Errorf("Status = %d, want %d", w.Code, http.StatusBadRequest)
	}

	var resp map[string]string
	json.Unmarshal(w.Body.Bytes(), &resp)
	if resp["error"] == "" {
		t.Error("Expected error message in response body")
	}
}

func TestHandleIngest_BodyTooLarge(t *testing.T) {
	r := setupRouter(nil, []string{"https://mysite.com"})

	body := `{"siteId":"x","type":"heatmap","eventName":"` + strings.Repeat("a", maxIngestBodyBytes) + `"}`
	req := httptest.NewRequest(http.MethodPost, "/analytics/events", bytes.NewBufferString(body))
	req.Header.Set("Content-Type", "application/json")
	w := httptest.NewRecorder()

	r.ServeHTTP(w, req)

	if w.Code != http.StatusRequestEntityTooLarge {
		t.Errorf("Status = %d, want %d", w.Code, http.StatusRequestEntityTooLarge)
	}
}

func TestHandleIngest_ValidPageview(t *testing.T) {
	r := setupRouter(nil, []string{"https://mysite.com"})

	body := map[string]any{
		"siteId":    "550e8400-e29b-41d4-a716-446655440000",
		"type":      "pageview",
		"url":       "https://mysite.com/about",
		"referrer":  "https://www.google.com",
		"visitorId": "v1",
		"sessionId": "660e8400-e29b-41d4-a716-446655440000",
	}
	jsonBody, _ := json.Marshal(body)

	req := httptest.NewRequest(http.MethodPost, "/analytics/events", bytes.NewBuffer(jsonBody))
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Origin", "https://mysite.com")
	req.Header.Set("Authorization", "Bearer wk_test")
	req.RemoteAddr = "10.0.0.1:5555"
	w := httptest.NewRecorder()

	r.ServeHTTP(w, req)

	if w.Code != http.StatusAccepted {
		t.Errorf("Status = %d, want %d. Body: %s", w.Code, http.StatusAccepted, w.Body.String())
	}
}

func TestHandleIngest_ServiceError(t *testing.T) {
	r := setupRouter(errors.New("clickhouse down"), []string{"https://mysite.com"})

	body := map[string]any{
		"siteId":    "550e8400-e29b-41d4-a716-446655440000",
		"type":      "pageview",
		"url":       "https://mysite.com/",
		"visitorId": "v1",
		"sessionId": "660e8400-e29b-41d4-a716-446655440000",
	}
	jsonBody, _ := json.Marshal(body)

	req := httptest.NewRequest(http.MethodPost, "/analytics/events", bytes.NewBuffer(jsonBody))
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Origin", "https://mysite.com")
	req.Header.Set("Authorization", "Bearer wk_test")
	req.RemoteAddr = "10.0.0.1:5555"
	w := httptest.NewRecorder()

	r.ServeHTTP(w, req)

	if w.Code != http.StatusInternalServerError {
		t.Errorf("Status = %d, want %d", w.Code, http.StatusInternalServerError)
	}
}

func TestHandleIngest_EmptyBody(t *testing.T) {
	r := setupRouter(nil, []string{"https://mysite.com"})

	req := httptest.NewRequest(http.MethodPost, "/analytics/events", bytes.NewBufferString(""))
	req.Header.Set("Content-Type", "application/json")
	w := httptest.NewRecorder()

	r.ServeHTTP(w, req)

	if w.Code != http.StatusBadRequest {
		t.Errorf("Status = %d, want %d", w.Code, http.StatusBadRequest)
	}
}

func TestHandleIngest_SiteLookupUnavailableIs503WithRetryAfter(t *testing.T) {
	eventRepo := &mockEventRepo{}
	siteRepo := &mockSiteRepo{err: errors.New("postgres: context deadline exceeded")}
	h := NewHandler(service.NewIngestionService(eventRepo, siteRepo, nil, nil, nil))
	r := gin.New()
	r.POST("/analytics/events", h.HandleIngest)

	req := httptest.NewRequest(http.MethodPost, "/analytics/events", bytes.NewBufferString(`{"siteId":"s","type":"pageview","url":"https://mysite.com/"}`))
	req.Header.Set("Content-Type", "application/json")
	w := httptest.NewRecorder()
	r.ServeHTTP(w, req)

	if w.Code != http.StatusServiceUnavailable {
		t.Fatalf("Status = %d, want 503", w.Code)
	}
	if w.Header().Get("Retry-After") != "1" {
		t.Errorf("Retry-After = %q, want 1", w.Header().Get("Retry-After"))
	}
	if len(eventRepo.events) != 0 {
		t.Error("event enqueued without a site config")
	}
}

func TestHandleIngest_BufferFullIs503(t *testing.T) {
	r := setupRouter(repository.ErrBufferFull, []string{"https://mysite.com"})
	req := httptest.NewRequest(http.MethodPost, "/analytics/events", bytes.NewBufferString(`{"siteId":"s","type":"pageview","url":"https://mysite.com/"}`))
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Authorization", "Bearer wk_test")
	w := httptest.NewRecorder()
	r.ServeHTTP(w, req)
	if w.Code != http.StatusServiceUnavailable || w.Header().Get("Retry-After") != "1" {
		t.Fatalf("Status = %d, Retry-After = %q; want 503 and 1", w.Code, w.Header().Get("Retry-After"))
	}
}

func TestInFlightLimit_RejectsOverTheLimitAtOnce(t *testing.T) {
	release := make(chan struct{})
	entered := make(chan struct{})
	r := gin.New()
	r.POST("/analytics/events", InFlightLimit(1), func(c *gin.Context) {
		entered <- struct{}{}
		<-release
		c.Status(http.StatusAccepted)
	})

	first := httptest.NewRecorder()
	done := make(chan struct{})
	go func() {
		r.ServeHTTP(first, httptest.NewRequest(http.MethodPost, "/analytics/events", nil))
		close(done)
	}()
	<-entered

	second := httptest.NewRecorder()
	r.ServeHTTP(second, httptest.NewRequest(http.MethodPost, "/analytics/events", nil))
	if second.Code != http.StatusServiceUnavailable || second.Header().Get("Retry-After") != "1" {
		t.Fatalf("second: %d, Retry-After %q; want 503 and 1", second.Code, second.Header().Get("Retry-After"))
	}

	close(release)
	<-done
	if first.Code != http.StatusAccepted {
		t.Fatalf("first: %d", first.Code)
	}
	// The slot is free again.
	third := httptest.NewRecorder()
	go func() { <-entered }()
	r.ServeHTTP(third, httptest.NewRequest(http.MethodPost, "/analytics/events", nil))
	if third.Code != http.StatusAccepted {
		t.Fatalf("third: %d", third.Code)
	}
}

func TestHandleIngest_ErrorCodes(t *testing.T) {
	const siteID = "550e8400-e29b-41d4-a716-446655440000"
	mr := miniredis.RunT(t)
	rdb := redis.NewClient(&redis.Options{Addr: mr.Addr()})
	defer rdb.Close()
	// Site over its quota, blocked for 200 more seconds.
	_ = mr.Set("quota:"+siteID+":limit_reached", "1")
	mr.SetTTL("quota:"+siteID+":limit_reached", 200*time.Second)

	paid := &repository.SiteConfig{ID: siteID, WriteKey: "wk_test", AllowedOrigins: []string{"mysite.com"}, Plan: "pro", MaxMonthlyEvents: 100}
	revoked := &repository.SiteConfig{ID: siteID, WriteKey: "", AllowedOrigins: []string{"mysite.com"}, Plan: "pro", MaxMonthlyEvents: 100}
	trial := &repository.SiteConfig{ID: siteID, WriteKey: "wk_test", Plan: "free", UserCreatedAt: time.Now().AddDate(0, 0, -30)}
	event := `{"siteId":"s","type":"pageview","url":"https://mysite.com/"}`

	cases := []struct {
		name       string
		siteRepo   *mockSiteRepo
		eventErr   error
		redis      *redis.Client
		body       string
		key        string
		origin     string
		requireKey bool
		status     int
		code       string
		retryAfter string
	}{
		{name: "invalid JSON", siteRepo: &mockSiteRepo{config: paid}, body: "not json", status: 400, code: CodeInvalidJSON},
		{name: "unknown site", siteRepo: &mockSiteRepo{err: repository.ErrSiteNotFound}, key: "wk_test", status: 404, code: CodeSiteNotFound},
		{name: "no key and no origin", siteRepo: &mockSiteRepo{config: paid}, status: 401, code: CodeUnauthorized},
		{name: "wrong key", siteRepo: &mockSiteRepo{config: paid}, key: "nope", status: 401, code: CodeUnauthorized},
		{name: "wrong key and wrong origin", siteRepo: &mockSiteRepo{config: paid}, key: "nope", origin: "https://evil.test", status: 401, code: CodeUnauthorized},
		{name: "origin not allowed", siteRepo: &mockSiteRepo{config: paid}, origin: "https://evil.test", status: 403, code: CodeOriginNotAllowed},
		{name: "trial expired", siteRepo: &mockSiteRepo{config: trial}, key: "wk_test", status: 402, code: CodeTrialExpired},
		{name: "quota exceeded", siteRepo: &mockSiteRepo{config: paid}, redis: rdb, key: "wk_test", status: 429, code: CodeQuotaExceeded, retryAfter: "200"},
		{name: "buffer full", siteRepo: &mockSiteRepo{config: paid}, eventErr: repository.ErrBufferFull, key: "wk_test", status: 503, code: CodeOverloaded, retryAfter: "1"},
		{name: "dependency down", siteRepo: &mockSiteRepo{err: errors.New("postgres: timeout")}, key: "wk_test", status: 503, code: CodeUnavailable, retryAfter: "1"},
		{name: "unexpected error", siteRepo: &mockSiteRepo{config: paid}, eventErr: errors.New("boom"), key: "wk_test", status: 500, code: CodeInternal},
		{name: "accepted by key", siteRepo: &mockSiteRepo{config: paid}, key: "wk_test", status: 202},
		{name: "accepted by origin", siteRepo: &mockSiteRepo{config: paid}, origin: "https://mysite.com", status: 202},
		{name: "wrong key from an allowed origin, key not required", siteRepo: &mockSiteRepo{config: paid}, key: "nope", origin: "https://mysite.com", status: 202},
		// INGEST_REQUIRE_WRITE_KEY=true: the origin no longer stands in for the key.
		{name: "required key: valid key", siteRepo: &mockSiteRepo{config: paid}, key: "wk_test", origin: "https://mysite.com", requireKey: true, status: 202},
		{name: "required key: valid key without origin", siteRepo: &mockSiteRepo{config: paid}, key: "wk_test", requireKey: true, status: 202},
		{name: "required key: missing from an allowed origin", siteRepo: &mockSiteRepo{config: paid}, origin: "https://mysite.com", requireKey: true, status: 401, code: CodeUnauthorized},
		{name: "required key: wrong from an allowed origin", siteRepo: &mockSiteRepo{config: paid}, key: "nope", origin: "https://mysite.com", requireKey: true, status: 401, code: CodeUnauthorized},
		{name: "required key: site whose key was revoked", siteRepo: &mockSiteRepo{config: revoked}, key: "wk_test", origin: "https://mysite.com", requireKey: true, status: 401, code: CodeUnauthorized},
		{name: "required key: unknown site is still 404", siteRepo: &mockSiteRepo{err: repository.ErrSiteNotFound}, requireKey: true, status: 404, code: CodeSiteNotFound},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			svc := service.NewIngestionService(&mockEventRepo{err: tc.eventErr}, tc.siteRepo, nil, nil, tc.redis)
			svc.RequireWriteKey = tc.requireKey
			r := gin.New()
			r.POST("/analytics/events", NewHandler(svc).HandleIngest)

			body := tc.body
			if body == "" {
				body = event
			}
			req := httptest.NewRequest(http.MethodPost, "/analytics/events", bytes.NewBufferString(body))
			if tc.key != "" {
				req.Header.Set("X-API-Key", tc.key)
			}
			if tc.origin != "" {
				req.Header.Set("Origin", tc.origin)
			}
			w := httptest.NewRecorder()
			r.ServeHTTP(w, req)

			if w.Code != tc.status {
				t.Fatalf("status = %d, want %d (body %s)", w.Code, tc.status, w.Body.String())
			}
			if got := w.Header().Get("Retry-After"); got != tc.retryAfter {
				t.Errorf("Retry-After = %q, want %q", got, tc.retryAfter)
			}
			if tc.code == "" {
				return
			}
			var resp map[string]string
			if err := json.Unmarshal(w.Body.Bytes(), &resp); err != nil {
				t.Fatalf("body is not JSON: %s", w.Body.String())
			}
			if resp["code"] != tc.code || resp["error"] == "" {
				t.Errorf("body = %v, want code %q and a message", resp, tc.code)
			}
		})
	}
}

func TestHandleIngest_CountsRequestsByOutcomeAndReason(t *testing.T) {
	count := func(outcome, reason string) float64 {
		return testutil.ToFloat64(metrics.IngestRequests.WithLabelValues(outcome, reason))
	}
	post := func(r *gin.Engine, body string) {
		req := httptest.NewRequest(http.MethodPost, "/analytics/events", bytes.NewBufferString(body))
		req.Header.Set("Content-Type", "application/json")
		req.Header.Set("Authorization", "Bearer wk_test")
		r.ServeHTTP(httptest.NewRecorder(), req)
	}
	const event = `{"siteId":"s","type":"pageview","url":"https://mysite.com/"}`

	accepted, full, invalid := count("accepted", "accepted"), count("rejected", CodeOverloaded), count("rejected", CodeInvalidJSON)
	post(setupRouter(nil, []string{"https://mysite.com"}), event)
	post(setupRouter(repository.ErrBufferFull, []string{"https://mysite.com"}), event)
	post(setupRouter(nil, nil), `{nope`)

	if d := count("accepted", "accepted") - accepted; d != 1 {
		t.Errorf("accepted +%v, want +1", d)
	}
	if d := count("rejected", CodeOverloaded) - full; d != 1 {
		t.Errorf("rejected/overloaded +%v, want +1", d)
	}
	if d := count("rejected", CodeInvalidJSON) - invalid; d != 1 {
		t.Errorf("rejected/invalid_json +%v, want +1", d)
	}
}

// With the key optional, every event let in by origin alone is counted, so the effect of turning on
// INGEST_REQUIRE_WRITE_KEY is known beforehand.
func TestHandleIngest_CountsOriginOnlyAuthorizations(t *testing.T) {
	count := func(reason string) float64 {
		return testutil.ToFloat64(metrics.OriginOnlyAuth.WithLabelValues(reason))
	}
	site := &repository.SiteConfig{ID: "550e8400-e29b-41d4-a716-446655440000", WriteKey: "wk_test", AllowedOrigins: []string{"mysite.com"}, Plan: "pro"}
	post := func(key string) int {
		svc := service.NewIngestionService(&mockEventRepo{}, &mockSiteRepo{config: site}, nil, nil, nil)
		r := gin.New()
		r.POST("/analytics/events", NewHandler(svc).HandleIngest)
		req := httptest.NewRequest(http.MethodPost, "/analytics/events", bytes.NewBufferString(`{"siteId":"s","type":"pageview","url":"https://mysite.com/"}`))
		req.Header.Set("Origin", "https://mysite.com")
		if key != "" {
			req.Header.Set("X-API-Key", key)
		}
		w := httptest.NewRecorder()
		r.ServeHTTP(w, req)
		return w.Code
	}

	missing, wrong := count("missing_key"), count("wrong_key")
	for _, key := range []string{"", "nope", "wk_test"} {
		if code := post(key); code != http.StatusAccepted {
			t.Fatalf("key %q: status = %d, want 202", key, code)
		}
	}
	if d := count("missing_key") - missing; d != 1 {
		t.Errorf("missing_key +%v, want +1", d)
	}
	if d := count("wrong_key") - wrong; d != 1 {
		t.Errorf("wrong_key +%v, want +1", d)
	}
}
