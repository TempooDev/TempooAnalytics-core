package service

import (
	"context"
	"errors"
	"fmt"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/model"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/repository"
)

const (
	heatmapSiteID = "550e8400-e29b-41d4-a716-446655440000"
	humanUA       = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0"
)

func heatmapService(advanced bool) (*IngestionService, *mockEventRepo) {
	eventRepo := &mockEventRepo{}
	siteRepo := &mockSiteRepo{config: &repository.SiteConfig{
		ID:                   heatmapSiteID,
		WriteKey:             "wk_test",
		Plan:                 "boutique",
		HasAdvancedAnalytics: advanced,
	}}
	svc := newTestService(eventRepo, siteRepo)
	svc.HeatmapsEnabled = true
	return svc, eventRepo
}

func heatmapRequest(ua string) *http.Request {
	req := httptest.NewRequest(http.MethodPost, "/analytics/events?key=wk_test", nil)
	req.Header.Set("User-Agent", ua)
	req.RemoteAddr = "192.168.1.100:12345"
	return req
}

func click(x, y, w int, s, k string) map[string]any {
	return map[string]any{"x": x, "y": y, "w": w, "s": s, "k": k}
}

func heatmapDto(clicks ...map[string]any) *AnalyticsEventDto {
	list := make([]any, len(clicks))
	for i, c := range clicks {
		list[i] = c
	}
	return &AnalyticsEventDto{
		SiteId:   heatmapSiteID,
		Type:     "heatmap",
		Url:      "https://mysite.com/precios?utm_source=x",
		Viewport: &ViewportDto{Width: 1280, Height: 800},
		Data:     map[string]any{"clicks": list},
	}
}

func TestProcessEvent_Heatmap_AdvancedPlanStoresClicks(t *testing.T) {
	svc, repo := heatmapService(true)
	dto := heatmapDto(
		click(512, 1340, 1280, "main>section.pricing>a#buy", "c"),
		click(100, 20, 1280, "nav>a.logo", "r"),
		click(900, 300, 1280, "div.card", "d"),
	)

	if err := svc.ProcessEvent(context.Background(), dto, heatmapRequest(humanUA)); err != nil {
		t.Fatalf("ProcessEvent() unexpected error: %v", err)
	}

	if len(repo.events) != 0 {
		t.Errorf("heatmap batch must not be stored in events, got %d", len(repo.events))
	}
	if len(repo.heatmapClicks) != 3 {
		t.Fatalf("expected 3 heatmap rows, got %d", len(repo.heatmapClicks))
	}
	first := repo.heatmapClicks[0]
	if first.SiteID.String() != heatmapSiteID || first.Path != "/precios" || first.DeviceType != "desktop" {
		t.Errorf("unexpected row %+v", first)
	}
	if first.X != 512 || first.Y != 1340 || first.ViewportWidth != 1280 || first.Kind != model.HeatmapKindClick {
		t.Errorf("unexpected coordinates %+v", first)
	}
	if repo.heatmapClicks[1].Kind != model.HeatmapKindRage || repo.heatmapClicks[2].Kind != model.HeatmapKindDead {
		t.Errorf("kinds not mapped: %s, %s", repo.heatmapClicks[1].Kind, repo.heatmapClicks[2].Kind)
	}
	today := time.Now().UTC().Truncate(24 * time.Hour)
	if !first.Date.Equal(today) {
		t.Errorf("Date = %v, want %v", first.Date, today)
	}
}

func TestProcessEvent_Heatmap_PlanWithoutAdvancedAnalyticsStoresNothing(t *testing.T) {
	svc, repo := heatmapService(false)

	err := svc.ProcessEvent(context.Background(), heatmapDto(click(10, 10, 1280, "a", "c")), heatmapRequest(humanUA))

	if err != nil {
		t.Fatalf("expected no error (202), got %v", err)
	}
	if len(repo.heatmapClicks) != 0 || len(repo.events) != 0 {
		t.Errorf("expected nothing stored, got %d clicks and %d events", len(repo.heatmapClicks), len(repo.events))
	}
}

func TestProcessEvent_Heatmap_FlagOffStoresNothing(t *testing.T) {
	svc, repo := heatmapService(true)
	svc.HeatmapsEnabled = false

	err := svc.ProcessEvent(context.Background(), heatmapDto(click(10, 10, 1280, "a", "c")), heatmapRequest(humanUA))

	if err != nil {
		t.Fatalf("expected no error (202), got %v", err)
	}
	if len(repo.heatmapClicks) != 0 {
		t.Errorf("expected nothing stored with the flag off, got %d clicks", len(repo.heatmapClicks))
	}
}

func TestProcessEvent_Heatmap_OverLimitStoresNothing(t *testing.T) {
	svc, repo := heatmapService(true)
	var gotSite, gotIP string
	var gotClicks int
	svc.heatmapAllow = func(_ context.Context, siteID, ipStr string, clicks int, _ int64) bool {
		gotSite, gotIP, gotClicks = siteID, ipStr, clicks
		return false
	}

	err := svc.ProcessEvent(context.Background(), heatmapDto(click(10, 10, 1280, "a", "c"), click(20, 20, 1280, "b", "c")), heatmapRequest(humanUA))

	if err != nil {
		t.Fatalf("expected no error (202), got %v", err)
	}
	if len(repo.heatmapClicks) != 0 {
		t.Errorf("expected nothing stored over the limit, got %d clicks", len(repo.heatmapClicks))
	}
	if gotSite != heatmapSiteID || gotIP != "192.168.1.100" || gotClicks != 2 {
		t.Errorf("limit checked with site %q, ip %q, clicks %d", gotSite, gotIP, gotClicks)
	}
}

func TestAllowHeatmapBatch_WithoutRedisLetsThrough(t *testing.T) {
	svc, _ := heatmapService(true)
	if !svc.allowHeatmapBatch(context.Background(), heatmapSiteID, "1.2.3.4", 50, 0) {
		t.Error("expected fail-open without Redis")
	}
}

func TestProcessEvent_Heatmap_DropsOutOfRangeRecords(t *testing.T) {
	svc, repo := heatmapService(true)
	dto := heatmapDto(
		click(5000, 10, 1280, "a", "c"),
		click(10, 10, 1280, "a", "c"),
		click(20, 10, 1280, "a", "c"),
		click(20, 200000, 1280, "a", "c"),
		click(20, 10, 0, "a", "c"),
		click(20, 10, 1280, "a", "x"),
	)

	if err := svc.ProcessEvent(context.Background(), dto, heatmapRequest(humanUA)); err != nil {
		t.Fatalf("ProcessEvent() unexpected error: %v", err)
	}
	if len(repo.heatmapClicks) != 2 {
		t.Errorf("expected only the 2 valid records, got %d", len(repo.heatmapClicks))
	}
}

func TestProcessEvent_Heatmap_KeepsAtMost50Records(t *testing.T) {
	svc, repo := heatmapService(true)
	clicks := make([]map[string]any, 80)
	for i := range clicks {
		clicks[i] = click(i, i, 1280, "a", "c")
	}

	if err := svc.ProcessEvent(context.Background(), heatmapDto(clicks...), heatmapRequest(humanUA)); err != nil {
		t.Fatalf("ProcessEvent() unexpected error: %v", err)
	}
	if len(repo.heatmapClicks) != 50 {
		t.Fatalf("expected 50 records, got %d", len(repo.heatmapClicks))
	}
	if repo.heatmapClicks[49].X != 49 {
		t.Errorf("expected the first 50 records, last x = %d", repo.heatmapClicks[49].X)
	}
}

func TestProcessEvent_Heatmap_BotStoresNothing(t *testing.T) {
	svc, repo := heatmapService(true)
	req := heatmapRequest("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)")

	if err := svc.ProcessEvent(context.Background(), heatmapDto(click(10, 10, 1280, "a", "c")), req); err != nil {
		t.Fatalf("ProcessEvent() unexpected error: %v", err)
	}
	if len(repo.heatmapClicks) != 0 {
		t.Errorf("bot batch must be dropped, got %d rows", len(repo.heatmapClicks))
	}
}

func TestProcessEvent_Heatmap_DoesNotCountTowardsQuota(t *testing.T) {
	svc, repo := heatmapService(true)
	// A pageview first, so the comparison shows heatmap batches add no event rows.
	pageview := &AnalyticsEventDto{SiteId: heatmapSiteID, Type: "pageview", Url: "https://mysite.com/precios"}
	if err := svc.ProcessEvent(context.Background(), pageview, heatmapRequest(humanUA)); err != nil {
		t.Fatalf("pageview: %v", err)
	}

	for i := 0; i < 3; i++ {
		if err := svc.ProcessEvent(context.Background(), heatmapDto(click(10, 10, 1280, "a", "c")), heatmapRequest(humanUA)); err != nil {
			t.Fatalf("heatmap: %v", err)
		}
	}

	if len(repo.events) != 1 {
		t.Errorf("events (the quota count) = %d, want 1", len(repo.events))
	}
	if len(repo.heatmapClicks) != 3 {
		t.Errorf("heatmap rows = %d, want 3", len(repo.heatmapClicks))
	}
}

func TestProcessEvent_Heatmap_ExpiredTrial(t *testing.T) {
	eventRepo := &mockEventRepo{}
	siteRepo := &mockSiteRepo{config: &repository.SiteConfig{
		ID: heatmapSiteID, WriteKey: "wk_test", Plan: "free", HasAdvancedAnalytics: true,
		UserCreatedAt: time.Now().Add(-30 * 24 * time.Hour),
	}}
	svc := newTestService(eventRepo, siteRepo)

	err := svc.ProcessEvent(context.Background(), heatmapDto(click(10, 10, 1280, "a", "c")), heatmapRequest(humanUA))

	if !errors.Is(err, ErrTrialExpired) {
		t.Errorf("expected ErrTrialExpired, got %v", err)
	}
	if len(eventRepo.heatmapClicks) != 0 {
		t.Errorf("expected nothing stored")
	}
}

func TestHeatmapDeviceType(t *testing.T) {
	tests := []struct {
		width int
		want  string
	}{{375, "mobile"}, {767, "mobile"}, {768, "tablet"}, {1023, "tablet"}, {1024, "desktop"}, {2560, "desktop"}}
	for _, tt := range tests {
		t.Run(fmt.Sprint(tt.width), func(t *testing.T) {
			if got := heatmapDeviceType(tt.width); got != tt.want {
				t.Errorf("heatmapDeviceType(%d) = %q, want %q", tt.width, got, tt.want)
			}
		})
	}
}

func TestCleanHeatmapSelector(t *testing.T) {
	tests := []struct {
		name string
		in   string
		want string
	}{
		{"keeps a clean selector", "main>section.pricing>a#buy", "main>section.pricing>a#buy"},
		{"drops a numeric id", "button#order-123456.btn", "button.btn"},
		{"drops an email class", "div.ana@correo.card", "div.card"},
		{"drops a whole level when nothing is left", "main>#12345678>a", "main>a"},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			if got := cleanHeatmapSelector(tt.in); got != tt.want {
				t.Errorf("cleanHeatmapSelector(%q) = %q, want %q", tt.in, got, tt.want)
			}
		})
	}

	long := cleanHeatmapSelector(strings.Repeat("div.a>", 100))
	if len(long) != maxHeatmapSelectorChars {
		t.Errorf("selector length = %d, want %d", len(long), maxHeatmapSelectorChars)
	}
}
