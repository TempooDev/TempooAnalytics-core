package model

import (
	"time"

	"github.com/google/uuid"
)

// HeatmapClick is one row of tempoo_analytics.heatmap_clicks. It carries no visitor, session or
// location data on purpose: heatmaps are anonymous aggregates.
type HeatmapClick struct {
	SiteID        uuid.UUID `ch:"site_id"`
	Date          time.Time `ch:"date"`
	Path          string    `ch:"path"`
	DeviceType    string    `ch:"device_type"`
	Kind          string    `ch:"kind"` // Enum8 in ClickHouse: click, rage, dead
	X             uint16    `ch:"x"`    // thousandths of the document width (0-1000)
	Y             uint32    `ch:"y"`    // pixels from the top of the document
	ViewportWidth uint16    `ch:"viewport_width"`
	Selector      string    `ch:"selector"`
}

const (
	HeatmapKindClick = "click"
	HeatmapKindRage  = "rage"
	HeatmapKindDead  = "dead"
)
