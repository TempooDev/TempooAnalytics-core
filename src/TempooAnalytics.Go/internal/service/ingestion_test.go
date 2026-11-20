package service

import (
	"context"
	"errors"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/model"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/repository"
)

// ── Mock implementations ────────────────────────────────────────────

type mockEventRepo struct {
	events        []*model.AnalyticsEvent
	heatmapClicks []*model.HeatmapClick
	err           error
}

func (m *mockEventRepo) InsertHeatmapClicks(_ context.Context, clicks []*model.HeatmapClick) error {
	if m.err != nil {
		return m.err
	}
	m.heatmapClicks = append(m.heatmapClicks, clicks...)
	return nil
}

func (m *mockEventRepo) InsertEvent(_ context.Context, e *model.AnalyticsEvent) error {
	if m.err != nil {
		return m.err
	}
	m.events = append(m.events, e)
	return nil
}

func (m *mockEventRepo) CountMonthlyEvents(_ context.Context, _ []string, _ time.Time) (map[string]int64, error) {
	return map[string]int64{}, nil
}

func (m *mockEventRepo) Close() error { return nil }

type mockSiteRepo struct {
	config *repository.SiteConfig
	err    error
}

func (m *mockSiteRepo) GetSiteConfig(_ context.Context, _ string, _ string) (*repository.SiteConfig, error) {
	return m.config, m.err
}

// ── Helper to build a minimal IngestionService for unit tests ────────

func newTestService(eventRepo EventRepository, siteRepo SiteRepository) *IngestionService {
	return NewIngestionService(eventRepo, siteRepo, nil, nil, nil)
}

// ── TestEnrichEvent ─────────────────────────────────────────────────

func TestEnrichEvent_URLPath(t *testing.T) {
	svc := newTestService(nil, nil)

	tests := []struct {
		name     string
		url      string
		wantPath string
	}{
		{"simple path", "https://example.com/about", "/about"},
		{"root path", "https://example.com/", "/"},
		{"nested path", "https://example.com/blog/post-1", "/blog/post-1"},
		{"no path", "https://example.com", "/"},
		{"with query params", "https://example.com/page?foo=bar", "/page"},
		{"empty URL yields root path", "", "/"},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			dto := &AnalyticsEventDto{Url: tt.url}
			req := httptest.NewRequest(http.MethodPost, "/", nil)
			enriched := svc.enrichEvent("127.0.0.1", "Mozilla/5.0", dto, req)
			if enriched.Path != tt.wantPath {
				t.Errorf("Path = %q, want %q", enriched.Path, tt.wantPath)
			}
		})
	}
}

func TestEnrichEvent_ReferrerDomain(t *testing.T) {
	svc := newTestService(nil, nil)

	tests := []struct {
		name       string
		referrer   string
		wantDomain string
	}{
		{"google referrer", "https://www.google.com/search?q=test", "www.google.com"},
		{"empty referrer", "", ""},
		{"direct traffic (no referrer)", "https://example.com", "example.com"},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			dto := &AnalyticsEventDto{Url: "https://mysite.com", Referrer: tt.referrer}
			req := httptest.NewRequest(http.MethodPost, "/", nil)
			enriched := svc.enrichEvent("127.0.0.1", "Mozilla/5.0", dto, req)
			if enriched.ReferrerDomain != tt.wantDomain {
				t.Errorf("ReferrerDomain = %q, want %q", enriched.ReferrerDomain, tt.wantDomain)
			}
		})
	}
}

func TestEnrichEvent_DeviceDetection(t *testing.T) {
	svc := newTestService(nil, nil)

	tests := []struct {
		name       string
		ua         string
		wantDevice string
	}{
		{"desktop Chrome", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36", "desktop"},
		{"mobile Safari", "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1", "mobile"},
		{"Googlebot", "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", "bot"},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			dto := &AnalyticsEventDto{Url: "https://mysite.com"}
			req := httptest.NewRequest(http.MethodPost, "/", nil)
			enriched := svc.enrichEvent("127.0.0.1", tt.ua, dto, req)
			if enriched.DeviceType != tt.wantDevice {
				t.Errorf("DeviceType = %q, want %q", enriched.DeviceType, tt.wantDevice)
			}
		})
	}
}

func TestEnrichEvent_TrafficType(t *testing.T) {
	svc := newTestService(nil, nil)

	tests := []struct {
		name        string
		ua          string
		wantTraffic int8
	}{
		{"human desktop", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0", model.TrafficTypeHuman},
		{"human mobile", "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Safari/604.1", model.TrafficTypeHuman},
		// Note: AI bot detection depends on ua.Bot() first. The mssola/user_agent lib
		// only flags UAs with "bot" (case-insensitive) or "compatible;" patterns as bots.
		{"AI bot GPTBot", "Mozilla/5.0 (compatible; GPTBot/1.0; +https://openai.com/gptbot)", model.TrafficTypeBotIA},
		{"AI bot Claude", "Mozilla/5.0 (compatible; Claude-Web/1.0; +https://claude.ai/bot)", model.TrafficTypeBotIA},
		{"AI bot OAI-SearchBot", "Mozilla/5.0 (compatible; OAI-SearchBot/1.0; +https://openai.com/searchbot)", model.TrafficTypeBotIA},
		{"AI bot CCBot", "CCBot/2.0 (https://commoncrawl.org/faq/)", model.TrafficTypeBotIA},
		{"AI bot ByteSpider", "Mozilla/5.0 (compatible; Bytespider; https://zhanzhang.toutiao.com/)", model.TrafficTypeBotIA},
		{"AI bot Amazonbot", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_10_1) AppleWebKit/600.2.5 (KHTML, like Gecko) Version/8.0.2 Safari/600.2.5 (Amazonbot/0.1; +https://developer.amazon.com/support/amazonbot)", model.TrafficTypeBotIA},
		{"crawler Googlebot", "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", model.TrafficTypeCrawler},
		{"crawler Bingbot", "Mozilla/5.0 (compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm)", model.TrafficTypeCrawler},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			dto := &AnalyticsEventDto{Url: "https://mysite.com"}
			req := httptest.NewRequest(http.MethodPost, "/", nil)
			enriched := svc.enrichEvent("127.0.0.1", tt.ua, dto, req)
			if enriched.TrafficType != tt.wantTraffic {
				t.Errorf("TrafficType = %d, want %d", enriched.TrafficType, tt.wantTraffic)
			}
		})
	}
}

func TestEnrichEvent_BrowserAndOS(t *testing.T) {
	svc := newTestService(nil, nil)

	dto := &AnalyticsEventDto{Url: "https://mysite.com"}
	req := httptest.NewRequest(http.MethodPost, "/", nil)
	ua := "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"

	enriched := svc.enrichEvent("127.0.0.1", ua, dto, req)

	if enriched.BrowserName == "" {
		t.Error("BrowserName should not be empty for Chrome UA")
	}
	if enriched.OSInfo == "" {
		t.Error("OSInfo should not be empty for Windows UA")
	}
}

func TestEnrichEvent_NoGeoIP(t *testing.T) {
	svc := newTestService(nil, nil) // No geo DB

	dto := &AnalyticsEventDto{Url: "https://mysite.com"}
	req := httptest.NewRequest(http.MethodPost, "/", nil)
	enriched := svc.enrichEvent("8.8.8.8", "Mozilla/5.0", dto, req)

	if enriched.CountryCode != "XX" {
		t.Errorf("CountryCode = %q, want %q (no GeoIP DB)", enriched.CountryCode, "XX")
	}
	if enriched.ASN != 0 {
		t.Errorf("ASN = %d, want 0 (no ASN DB)", enriched.ASN)
	}
}

// ── TestProcessEvent ────────────────────────────────────────────────

func TestProcessEvent_Timestamp(t *testing.T) {
	eventRepo := &mockEventRepo{}
	siteRepo := &mockSiteRepo{
		config: &repository.SiteConfig{
			ID:             "550e8400-e29b-41d4-a716-446655440000",
			WriteKey:       "wk_test",
			AllowedOrigins: []string{"https://mysite.com"},
		},
	}
	svc := newTestService(eventRepo, siteRepo)

	// Test with provided timestamp
	tsStr := "2024-05-17T12:00:00Z"
	wantTs, _ := time.Parse(time.RFC3339, tsStr)
	
	dto := &AnalyticsEventDto{
		SiteId:    "site-key",
		Type:      "pageview",
		Url:       "https://mysite.com",
		Timestamp: tsStr,
	}
	req := httptest.NewRequest(http.MethodPost, "/", nil)
	req.Header.Set("Origin", "https://mysite.com")
	req.Header.Set("X-API-Key", "wk_test")

	err := svc.ProcessEvent(context.Background(), dto, req)
	if err != nil {
		t.Fatalf("ProcessEvent failed: %v", err)
	}

	if len(eventRepo.events) != 1 {
		t.Fatalf("Expected 1 event, got %d", len(eventRepo.events))
	}

	event := eventRepo.events[0]
	if !event.Timestamp.Equal(wantTs.UTC()) {
		t.Errorf("Timestamp = %v, want %v", event.Timestamp, wantTs.UTC())
	}
	
	expectedDate := time.Date(wantTs.Year(), wantTs.Month(), wantTs.Day(), 0, 0, 0, 0, time.UTC)
	if !event.Date.Equal(expectedDate) {
		t.Errorf("Date = %v, want %v", event.Date, expectedDate)
	}
}

// ── TestGetIP ───────────────────────────────────────────────────────

func TestGetIP(t *testing.T) {
	tests := []struct {
		name   string
		setup  func(r *http.Request)
		wantIP string
	}{
		{
			"CF-Connecting-IP takes priority",
			func(r *http.Request) {
				r.Header.Set("CF-Connecting-IP", "1.2.3.4")
				r.Header.Set("X-Forwarded-For", "5.6.7.8")
				r.RemoteAddr = "9.10.11.12:8080"
			},
			"1.2.3.4",
		},
		{
			"X-Forwarded-For used when no CF header",
			func(r *http.Request) {
				r.Header.Set("X-Forwarded-For", "10.0.0.1, 10.0.0.2")
				r.RemoteAddr = "9.10.11.12:8080"
			},
			"10.0.0.1",
		},
		{
			"RemoteAddr fallback with port stripping",
			func(r *http.Request) {
				r.RemoteAddr = "192.168.1.1:12345"
			},
			"192.168.1.1",
		},
		{
			"RemoteAddr without port",
			func(r *http.Request) {
				r.RemoteAddr = "192.168.1.1"
			},
			"192.168.1.1",
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			req := httptest.NewRequest(http.MethodPost, "/", nil)
			tt.setup(req)
			got := getIP(req)
			if got != tt.wantIP {
				t.Errorf("getIP() = %q, want %q", got, tt.wantIP)
			}
		})
	}
}

// ── TestExtractWriteKey ─────────────────────────────────────────────

func TestExtractWriteKey(t *testing.T) {
	tests := []struct {
		name    string
		setup   func(r *http.Request)
		wantKey string
	}{
		{
			"Bearer token",
			func(r *http.Request) { r.Header.Set("Authorization", "Bearer wk_abc123") },
			"wk_abc123",
		},
		{
			"X-API-Key header",
			func(r *http.Request) { r.Header.Set("X-API-Key", "wk_xyz789") },
			"wk_xyz789",
		},
		{
			"query param key",
			nil,
			"wk_query_key",
		},
		{
			"no credentials",
			func(r *http.Request) {},
			"",
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			path := "/"
			if tt.name == "query param key" {
				path = "/?key=wk_query_key"
			}
			req := httptest.NewRequest(http.MethodPost, path, nil)
			if tt.setup != nil {
				tt.setup(req)
			}
			got := extractWriteKey(req)
			if got != tt.wantKey {
				t.Errorf("extractWriteKey() = %q, want %q", got, tt.wantKey)
			}
		})
	}
}

// ── TestExtractOrigin ───────────────────────────────────────────────

func TestExtractOrigin(t *testing.T) {
	tests := []struct {
		name       string
		origin     string
		referer    string
		wantOrigin string
	}{
		{"Origin header present", "https://mysite.com", "", "https://mysite.com"},
		{"Referer fallback", "", "https://google.com/search", "https://google.com/search"},
		{"Origin takes priority over Referer", "https://mysite.com", "https://google.com", "https://mysite.com"},
		{"no headers", "", "", ""},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			req := httptest.NewRequest(http.MethodPost, "/", nil)
			if tt.origin != "" {
				req.Header.Set("Origin", tt.origin)
			}
			if tt.referer != "" {
				req.Header.Set("Referer", tt.referer)
			}
			got := extractOrigin(req)
			if got != tt.wantOrigin {
				t.Errorf("extractOrigin() = %q, want %q", got, tt.wantOrigin)
			}
		})
	}
}

// ── TestOriginAllowed ───────────────────────────────────────────────

func TestOriginAllowed(t *testing.T) {
	tests := []struct {
		name    string
		allowed []string
		origin  string
		want    bool
	}{
		// Legit formats on both sides.
		{"exact bare host", []string{"example.com"}, "https://example.com", true},
		{"exact allowed with scheme", []string{"https://example.com"}, "https://example.com", true},
		{"allowed with trailing slash", []string{"https://example.com/"}, "https://example.com", true},
		{"allowed with path", []string{"https://example.com/blog"}, "https://example.com", true},
		{"allowed with port", []string{"http://localhost:3000"}, "http://localhost:3000", true},
		{"bare localhost, any port", []string{"localhost"}, "http://localhost:4200", true},
		{"allowed host:port without scheme", []string{"example.com:8080"}, "http://example.com:8080", true},
		{"origin with port", []string{"example.com"}, "https://example.com:8443", true},
		{"other scheme", []string{"http://example.com"}, "https://example.com", true},
		{"www origin for bare allowed", []string{"example.com"}, "https://www.example.com", true},
		{"www origin for allowed with scheme", []string{"https://example.com"}, "https://www.example.com", true},
		{"subdomain", []string{"example.com"}, "https://shop.eu.example.com", true},
		{"bare origin for www allowed", []string{"https://www.example.com"}, "https://example.com", true},
		{"www origin for www allowed", []string{"www.example.com"}, "https://www.example.com", true},
		{"wildcard subdomain entry", []string{"*.example.com"}, "https://app.example.com", true},
		{"uppercase allowed", []string{"HTTPS://Example.COM"}, "https://example.com", true},
		{"uppercase origin", []string{"example.com"}, "HTTPS://WWW.EXAMPLE.COM", true},
		{"trailing dot", []string{"example.com."}, "https://example.com.", true},
		{"allowed with spaces", []string{"  example.com  "}, "https://example.com", true},
		{"referer with path and query", []string{"example.com"}, "https://example.com/pricing?utm_source=x", true},
		{"second entry matches", []string{"other.org", "example.com"}, "https://example.com", true},
		{"ipv6 with port", []string{"http://[::1]:3000"}, "http://[::1]:5173", true},

		// Attacks the substring check let through.
		{"suffix append", []string{"example.com"}, "https://example.com.evil.io", false},
		{"suffix append, allowed with scheme", []string{"https://example.com"}, "https://example.com.evil.io", false},
		{"prefix lookalike", []string{"example.com"}, "https://notexample.com", false},
		{"prefix lookalike with dash", []string{"example.com"}, "https://my-example.com", false},
		{"allowed in path", []string{"example.com"}, "https://evil.io/example.com", false},
		{"allowed in query", []string{"example.com"}, "https://evil.io/?ref=https://example.com", false},
		{"allowed in fragment", []string{"example.com"}, "https://evil.io/#example.com", false},
		{"allowed as userinfo", []string{"example.com"}, "https://example.com@evil.io", false},
		{"allowed glued to attacker domain", []string{"example.com"}, "https://example.com-evil.io", false},
		{"parent domain of allowed", []string{"shop.example.com"}, "https://example.com", false},
		{"sibling subdomain", []string{"shop.example.com"}, "https://blog.example.com", false},

		// Values that must never match anything.
		{"empty allowed entry", []string{""}, "https://evil.io", false},
		{"bare wildcard entry", []string{"*"}, "https://evil.io", false},
		{"no allowed origins", nil, "https://example.com", false},
		{"null origin", []string{"example.com"}, "null", false},
		{"empty origin", []string{"example.com"}, "", false},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			if got := originAllowed(tt.origin, tt.allowed); got != tt.want {
				t.Errorf("originAllowed(%q, %q) = %v, want %v", tt.origin, tt.allowed, got, tt.want)
			}
		})
	}
}

func TestAuthorizeRequest_OriginOnly(t *testing.T) {
	svc := newTestService(nil, nil)
	config := &repository.SiteConfig{WriteKey: "wk_test", AllowedOrigins: []string{"example.com"}}

	tests := []struct {
		name    string
		key     string
		origin  string
		referer string
		wantErr error
	}{
		{"origin allowed", "", "https://www.example.com", "", nil},
		{"referer fallback allowed", "", "", "https://example.com/page", nil},
		{"suffix-append origin rejected", "", "https://example.com.evil.io", "", ErrOriginNotAllowed},
		{"lookalike referer rejected", "", "", "https://notexample.com/", ErrOriginNotAllowed},
		{"wrong key with bad origin", "wk_wrong", "https://example.com.evil.io", "", ErrUnauthorized},
		{"valid key ignores origin", "wk_test", "https://evil.io", "", nil},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			req := httptest.NewRequest(http.MethodPost, "/", nil)
			if tt.origin != "" {
				req.Header.Set("Origin", tt.origin)
			}
			if tt.referer != "" {
				req.Header.Set("Referer", tt.referer)
			}
			if err := svc.authorizeRequest(config, tt.key, req); !errors.Is(err, tt.wantErr) {
				t.Errorf("authorizeRequest() = %v, want %v", err, tt.wantErr)
			}
		})
	}
}

// ── TestGetQueryParam ───────────────────────────────────────────────

func TestGetQueryParam(t *testing.T) {
	tests := []struct {
		name     string
		rawURL   string
		key      string
		wantVal  string
	}{
		{"utm_source present", "https://example.com?utm_source=google", "utm_source", "google"},
		{"utm_medium present", "https://example.com?utm_medium=cpc&utm_source=fb", "utm_medium", "cpc"},
		{"missing param", "https://example.com?utm_source=google", "utm_campaign", ""},
		{"no query string", "https://example.com/page", "utm_source", ""},
		{"malformed URL", "://bad", "key", ""},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got := getQueryParam(tt.rawURL, tt.key)
			if got != tt.wantVal {
				t.Errorf("getQueryParam(%q, %q) = %q, want %q", tt.rawURL, tt.key, got, tt.wantVal)
			}
		})
	}
}

// ── TestGenerateUserHash ────────────────────────────────────────────

func TestGenerateUserHash_Deterministic(t *testing.T) {
	ts := time.Now()
	h1 := generateUserHash("1.2.3.4", "Mozilla/5.0", ts)
	h2 := generateUserHash("1.2.3.4", "Mozilla/5.0", ts)

	if h1 != h2 {
		t.Errorf("Same inputs should produce same hash, got %q and %q", h1, h2)
	}
	if len(h1) != 64 { // SHA-256 hex length
		t.Errorf("Hash length = %d, want 64", len(h1))
	}
}

func TestGenerateUserHash_DifferentInputs(t *testing.T) {
	ts := time.Now()
	h1 := generateUserHash("1.2.3.4", "Mozilla/5.0", ts)
	h2 := generateUserHash("5.6.7.8", "Mozilla/5.0", ts)

	if h1 == h2 {
		t.Error("Different IPs should produce different hashes")
	}
}

// ── TestGetUserAgent ────────────────────────────────────────────────

func TestGetUserAgent(t *testing.T) {
	tests := []struct {
		name      string
		dtoUA     string
		reqUA     string
		wantUA    string
	}{
		{"DTO override", "CustomBot/1.0", "Mozilla/5.0", "CustomBot/1.0"},
		{"request fallback", "", "Mozilla/5.0 Chrome", "Mozilla/5.0 Chrome"},
		{"both empty", "", "", ""},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			req := httptest.NewRequest(http.MethodPost, "/", nil)
			if tt.reqUA != "" {
				req.Header.Set("User-Agent", tt.reqUA)
			}
			got := getUserAgent(tt.dtoUA, req)
			if got != tt.wantUA {
				t.Errorf("getUserAgent() = %q, want %q", got, tt.wantUA)
			}
		})
	}
}

// ── TestHash ────────────────────────────────────────────────────────

func TestHash(t *testing.T) {
	h := hash("hello")
	if len(h) != 64 {
		t.Errorf("hash length = %d, want 64 (SHA-256 hex)", len(h))
	}
	// SHA-256 of "hello"
	expected := "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824"
	if h != expected {
		t.Errorf("hash(\"hello\") = %q, want %q", h, expected)
	}
}

// ── TestProcessEvent ────────────────────────────────────────────────

func TestProcessEvent_Pageview(t *testing.T) {
	eventRepo := &mockEventRepo{}
	siteRepo := &mockSiteRepo{
		config: &repository.SiteConfig{
			ID:             "550e8400-e29b-41d4-a716-446655440000",
			WriteKey:       "wk_test",
			AllowedOrigins: []string{"https://mysite.com"},
		},
	}
	svc := newTestService(eventRepo, siteRepo)

	dto := &AnalyticsEventDto{
		SiteId:    "550e8400-e29b-41d4-a716-446655440000",
		Type:      "pageview",
		Url:       "https://mysite.com/about?utm_source=google&utm_medium=cpc",
		Title:     "About us",
		Referrer:  "https://www.google.com/search?q=test",
		Viewport:  &ViewportDto{Width: 1920, Height: 1080},
		VisitorId: "visitor-abc",
		SessionId: "660e8400-e29b-41d4-a716-446655440000",
	}

	req := httptest.NewRequest(http.MethodPost, "/analytics/events", nil)
	req.Header.Set("Origin", "https://mysite.com")
	req.Header.Set("Authorization", "Bearer wk_test")
	req.Header.Set("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0")
	req.RemoteAddr = "192.168.1.100:12345"

	err := svc.ProcessEvent(context.Background(), dto, req)
	if err != nil {
		t.Fatalf("ProcessEvent() unexpected error: %v", err)
	}

	if len(eventRepo.events) != 1 {
		t.Fatalf("Expected 1 event saved, got %d", len(eventRepo.events))
	}

	event := eventRepo.events[0]

	// Verify core fields
	if event.SiteID.String() != "550e8400-e29b-41d4-a716-446655440000" {
		t.Errorf("SiteID = %v, want 550e8400-...", event.SiteID)
	}
	if event.VisitorID != "visitor-abc" {
		t.Errorf("VisitorID = %q, want %q", event.VisitorID, "visitor-abc")
	}
	if event.SessionID.String() != "660e8400-e29b-41d4-a716-446655440000" {
		t.Errorf("SessionID = %v, want 660e8400-...", event.SessionID)
	}
	if event.EventName != "pageview" {
		t.Errorf("EventName = %q, want %q", event.EventName, "pageview")
	}
	if event.Path != "/about" {
		t.Errorf("Path = %q, want %q", event.Path, "/about")
	}
	// The tracker sends document.title as "title" with each page view.
	if event.PageTitle != "About us" {
		t.Errorf("PageTitle = %q, want %q", event.PageTitle, "About us")
	}
	if event.ReferrerDomain != "www.google.com" {
		t.Errorf("ReferrerDomain = %q, want %q", event.ReferrerDomain, "www.google.com")
	}
	if event.UtmSource != "google" {
		t.Errorf("UtmSource = %q, want %q", event.UtmSource, "google")
	}
	if event.UtmMedium != "cpc" {
		t.Errorf("UtmMedium = %q, want %q", event.UtmMedium, "cpc")
	}
	if event.ScreenWidth != 1920 || event.ScreenHeight != 1080 {
		t.Errorf("Screen = %dx%d, want 1920x1080", event.ScreenWidth, event.ScreenHeight)
	}
	if event.TrafficType != model.TrafficTypeHuman {
		t.Errorf("TrafficType = %d, want %d (human)", event.TrafficType, model.TrafficTypeHuman)
	}
	if event.UserHash == "" {
		t.Error("UserHash should not be empty")
	}
	if event.EventID.String() == "00000000-0000-0000-0000-000000000000" {
		t.Error("EventID should be a generated UUID")
	}
}

func TestProcessEvent_CustomEvent(t *testing.T) {
	eventRepo := &mockEventRepo{}
	siteRepo := &mockSiteRepo{
		config: &repository.SiteConfig{
			ID:             "550e8400-e29b-41d4-a716-446655440000",
			WriteKey:       "wk_test",
			AllowedOrigins: []string{"https://mysite.com"},
		},
	}
	svc := newTestService(eventRepo, siteRepo)

	dto := &AnalyticsEventDto{
		SiteId:    "550e8400-e29b-41d4-a716-446655440000",
		Type:      "custom",
		EventName: "button_click",
		Url:       "https://mysite.com/pricing",
		Data:      map[string]any{"plan": "pro"},
	}

	req := httptest.NewRequest(http.MethodPost, "/analytics/events", nil)
	req.Header.Set("Origin", "https://mysite.com")
	req.Header.Set("Authorization", "Bearer wk_test")
	req.RemoteAddr = "10.0.0.1:5555"

	err := svc.ProcessEvent(context.Background(), dto, req)
	if err != nil {
		t.Fatalf("ProcessEvent() error: %v", err)
	}

	event := eventRepo.events[0]
	if event.EventName != "button_click" {
		t.Errorf("EventName = %q, want %q (custom override)", event.EventName, "button_click")
	}
}

func TestProcessEvent_GoalEvent(t *testing.T) {
	eventRepo := &mockEventRepo{}
	siteRepo := &mockSiteRepo{
		config: &repository.SiteConfig{
			ID:             "550e8400-e29b-41d4-a716-446655440000",
			WriteKey:       "wk_test",
			AllowedOrigins: []string{"https://mysite.com"},
		},
	}
	svc := newTestService(eventRepo, siteRepo)

	// analytics.js sends data-tempoo-goal clicks as type "goal" with the attribute value as eventName
	dto := &AnalyticsEventDto{
		SiteId:    "550e8400-e29b-41d4-a716-446655440000",
		Type:      "goal",
		EventName: "test_goal",
		Url:       "https://mysite.com/pricing",
	}

	req := httptest.NewRequest(http.MethodPost, "/analytics/events", nil)
	req.Header.Set("Origin", "https://mysite.com")
	req.Header.Set("Authorization", "Bearer wk_test")
	req.RemoteAddr = "10.0.0.1:5555"

	err := svc.ProcessEvent(context.Background(), dto, req)
	if err != nil {
		t.Fatalf("ProcessEvent() error: %v", err)
	}

	event := eventRepo.events[0]
	if event.EventName != "test_goal" {
		t.Errorf("EventName = %q, want %q (goal override)", event.EventName, "test_goal")
	}
}

func TestProcessEvent_InvalidSiteId(t *testing.T) {
	svc := newTestService(&mockEventRepo{}, &mockSiteRepo{})

	dto := &AnalyticsEventDto{SiteId: "not-a-uuid", Type: "pageview", Url: "https://mysite.com"}
	req := httptest.NewRequest(http.MethodPost, "/", nil)

	err := svc.ProcessEvent(context.Background(), dto, req)
	if err == nil {
		t.Error("Expected error for invalid SiteId UUID")
	}
}

func TestProcessEvent_UnauthorizedOrigin(t *testing.T) {
	siteRepo := &mockSiteRepo{
		config: &repository.SiteConfig{
			ID:             "550e8400-e29b-41d4-a716-446655440000",
			WriteKey:       "wk_test",
			AllowedOrigins: []string{"https://mysite.com"},
		},
	}
	svc := newTestService(&mockEventRepo{}, siteRepo)

	dto := &AnalyticsEventDto{
		SiteId: "550e8400-e29b-41d4-a716-446655440000",
		Type:   "pageview",
		Url:    "https://evil.com",
	}

	req := httptest.NewRequest(http.MethodPost, "/", nil)
	req.Header.Set("Origin", "https://evil.com")

	err := svc.ProcessEvent(context.Background(), dto, req)
	if err == nil {
		t.Error("Expected authorization error for disallowed origin")
	}
}

func TestProcessEvent_NoViewport(t *testing.T) {
	eventRepo := &mockEventRepo{}
	siteRepo := &mockSiteRepo{
		config: &repository.SiteConfig{
			ID:             "550e8400-e29b-41d4-a716-446655440000",
			WriteKey:       "wk_test",
			AllowedOrigins: []string{"https://mysite.com"},
		},
	}
	svc := newTestService(eventRepo, siteRepo)

	dto := &AnalyticsEventDto{
		SiteId:   "550e8400-e29b-41d4-a716-446655440000",
		Type:     "pageview",
		Url:      "https://mysite.com/",
		Viewport: nil, // no viewport
	}

	req := httptest.NewRequest(http.MethodPost, "/", nil)
	req.Header.Set("Origin", "https://mysite.com")
	req.Header.Set("Authorization", "Bearer wk_test")
	req.RemoteAddr = "10.0.0.1:5555"

	err := svc.ProcessEvent(context.Background(), dto, req)
	if err != nil {
		t.Fatalf("ProcessEvent() error: %v", err)
	}

	event := eventRepo.events[0]
	if event.ScreenWidth != 0 || event.ScreenHeight != 0 {
		t.Errorf("Screen = %dx%d, want 0x0 when no viewport", event.ScreenWidth, event.ScreenHeight)
	}
}

func TestProcessEvent_RepositoryError(t *testing.T) {
	eventRepo := &mockEventRepo{err: errors.New("clickhouse down")}
	siteRepo := &mockSiteRepo{
		config: &repository.SiteConfig{
			ID:             "550e8400-e29b-41d4-a716-446655440000",
			WriteKey:       "wk_test",
			AllowedOrigins: []string{"https://mysite.com"},
		},
	}
	svc := newTestService(eventRepo, siteRepo)

	dto := &AnalyticsEventDto{
		SiteId: "550e8400-e29b-41d4-a716-446655440000",
		Type:   "pageview",
		Url:    "https://mysite.com",
	}

	req := httptest.NewRequest(http.MethodPost, "/", nil)
	req.Header.Set("Origin", "https://mysite.com")
	req.Header.Set("Authorization", "Bearer wk_test")
	req.RemoteAddr = "10.0.0.1:5555"

	err := svc.ProcessEvent(context.Background(), dto, req)
	if err == nil {
		t.Error("Expected error when repository fails")
	}
}
