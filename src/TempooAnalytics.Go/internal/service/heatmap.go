package service

import (
	"context"
	"crypto/sha256"
	"encoding/json"
	"fmt"
	"regexp"
	"strings"
	"time"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/model"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/repository"
	"github.com/google/uuid"
)

const (
	heatmapEventType        = "heatmap"
	maxHeatmapClicks        = 50
	maxHeatmapSelectorChars = 200
	// maxHeatmapBatchesPerMinute caps the batches one IP can send to one site per minute. A visitor sends
	// one batch per page view, plus one more every 50 records.
	maxHeatmapBatchesPerMinute = 30
)

// HeatmapClickDto is one record of a heatmap batch, as sent by the tracker (decision 2 of add-heatmaps).
type HeatmapClickDto struct {
	X *int   `json:"x"` // thousandths of the document width
	Y *int   `json:"y"` // document pixels from the top
	W *int   `json:"w"` // viewport width in pixels
	S string `json:"s"` // selector
	K string `json:"k"` // c (click), r (rage), d (dead)
}

type heatmapDataDto struct {
	Clicks []HeatmapClickDto `json:"clicks"`
}

var heatmapKinds = map[string]string{
	"c": model.HeatmapKindClick,
	"r": model.HeatmapKindRage,
	"d": model.HeatmapKindDead,
}

// Same rule as the tracker: tokens with 4+ consecutive digits or an @ may identify someone.
var personalSelectorToken = regexp.MustCompile(`\d{4,}|@`)

// processHeatmap stores a heatmap batch in heatmap_clicks. It never touches events, the monthly quota
// or the visitor hash. Batches from plans without advanced analytics and from bots are dropped silently
// (the caller still answers 202).
func (s *IngestionService) processHeatmap(ctx context.Context, dto *AnalyticsEventDto, config *repository.SiteConfig, ipStr, userAgentStr string) error {
	if !s.HeatmapsEnabled || !config.HasAdvancedAnalytics {
		return nil
	}

	enriched := s.enrichEvent(ipStr, userAgentStr, dto, nil)
	if enriched.TrafficType != model.TrafficTypeHuman {
		return nil
	}

	clicks := parseHeatmapClicks(dto.Data)
	if len(clicks) == 0 {
		return nil
	}

	siteUUID, err := uuid.Parse(config.ID)
	if err != nil {
		return err
	}
	now := time.Now().UTC()
	date := time.Date(now.Year(), now.Month(), now.Day(), 0, 0, 0, 0, time.UTC)

	rows := make([]*model.HeatmapClick, 0, len(clicks))
	for _, c := range clicks {
		row, ok := validateHeatmapClick(c)
		if !ok {
			continue
		}
		row.SiteID = siteUUID
		row.Date = date
		row.Path = enriched.Path
		rows = append(rows, row)
	}
	if len(rows) == 0 {
		return nil
	}
	allowCtx, cancel := s.withDependencyTimeout(ctx)
	allowed := s.heatmapAllow(allowCtx, config.ID, ipStr, len(rows), config.HeatmapMonthlyClicks)
	cancel()
	if !allowed {
		return nil
	}
	return s.eventRepo.InsertHeatmapClicks(ctx, rows)
}

// allowHeatmapBatch applies the heatmap limits, which are separate from the events quota: at most
// maxHeatmapBatchesPerMinute batches per IP and site, and at most the plan's monthly events in records
// per site and month. Without Redis, or when Redis fails, it lets the batch through, like the events quota (QuotaCounter).
func (s *IngestionService) allowHeatmapBatch(ctx context.Context, siteID, ipStr string, clicks int, monthlyLimit int64) bool {
	if s.redis == nil {
		return true
	}
	now := time.Now().UTC()

	// The IP is only kept hashed, and for a minute.
	ipHash := sha256.Sum256([]byte(siteID + "|" + ipStr))
	ipKey := fmt.Sprintf("heatmap:%s:ip:%x:%s", siteID, ipHash[:8], now.Format("200601021504"))
	batches, err := s.redis.Incr(ctx, ipKey).Result()
	if err == nil {
		if batches == 1 {
			s.redis.Expire(ctx, ipKey, 2*time.Minute)
		}
		if batches > maxHeatmapBatchesPerMinute {
			return false
		}
	}

	monthKey := fmt.Sprintf("heatmap:%s:clicks:%s", siteID, now.Format("200601"))
	total, err := s.redis.IncrBy(ctx, monthKey, int64(clicks)).Result()
	if err != nil {
		return true
	}
	if total == int64(clicks) {
		s.redis.Expire(ctx, monthKey, 32*24*time.Hour)
	}
	return total <= monthlyLimit
}

// parseHeatmapClicks reads data.clicks and keeps at most the first 50 records.
func parseHeatmapClicks(data any) []HeatmapClickDto {
	if data == nil {
		return nil
	}
	raw, err := json.Marshal(data)
	if err != nil {
		return nil
	}
	if str, ok := data.(string); ok {
		raw = []byte(str)
	}
	var parsed heatmapDataDto
	if err := json.Unmarshal(raw, &parsed); err != nil {
		return nil
	}
	if len(parsed.Clicks) > maxHeatmapClicks {
		parsed.Clicks = parsed.Clicks[:maxHeatmapClicks]
	}
	return parsed.Clicks
}

func validateHeatmapClick(c HeatmapClickDto) (*model.HeatmapClick, bool) {
	if c.X == nil || c.Y == nil || c.W == nil {
		return nil, false
	}
	x, y, w := *c.X, *c.Y, *c.W
	if x < 0 || x > 1000 || y < 0 || y > 100000 || w < 1 || w > 10000 {
		return nil, false
	}
	kind, ok := heatmapKinds[c.K]
	if !ok {
		return nil, false
	}
	return &model.HeatmapClick{
		DeviceType:    heatmapDeviceType(w),
		Kind:          kind,
		X:             uint16(x),
		Y:             uint32(y),
		ViewportWidth: uint16(w),
		Selector:      cleanHeatmapSelector(c.S),
	}, true
}

func heatmapDeviceType(viewportWidth int) string {
	switch {
	case viewportWidth < 768:
		return "mobile"
	case viewportWidth < 1024:
		return "tablet"
	default:
		return "desktop"
	}
}

// cleanHeatmapSelector repeats the tracker's cleanup in case an old or modified script sends
// identifiers, and applies the 200 character cap.
func cleanHeatmapSelector(selector string) string {
	parts := strings.Split(selector, ">")
	kept := make([]string, 0, len(parts))
	for _, part := range parts {
		// A part is tag#id.class1.class2; drop the tokens that look personal.
		var b strings.Builder
		token := strings.Builder{}
		flush := func() {
			t := token.String()
			token.Reset()
			if t == "" || personalSelectorToken.MatchString(t) {
				return
			}
			b.WriteString(t)
		}
		for _, r := range part {
			if (r == '#' || r == '.') && token.Len() > 0 {
				flush()
			}
			token.WriteRune(r)
		}
		flush()
		if b.Len() > 0 {
			kept = append(kept, b.String())
		}
	}
	out := strings.Join(kept, ">")
	if len([]rune(out)) > maxHeatmapSelectorChars {
		out = string([]rune(out)[:maxHeatmapSelectorChars])
	}
	return out
}
