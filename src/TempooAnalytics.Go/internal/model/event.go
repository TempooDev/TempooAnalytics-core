package model

import (
	"time"

	"github.com/google/uuid"
)

type AnalyticsEvent struct {
	EventID        uuid.UUID `ch:"event_id" json:"event_id"`
	SiteID         uuid.UUID `ch:"site_id" json:"site_id"`
	UserHash       string    `ch:"user_hash" json:"user_hash"`
	VisitorID      string    `ch:"visitor_id" json:"visitor_id"`
	SessionID      uuid.UUID `ch:"session_id" json:"session_id"`
	Timestamp      time.Time `ch:"timestamp" json:"timestamp"`
	Date           time.Time `ch:"date" json:"date"`
	CountryCode    string    `ch:"country_code" json:"country_code"`
	CityName       string    `ch:"city_name" json:"city_name"`
	DeviceType     string    `ch:"device_type" json:"device_type"`
	OS             string    `ch:"os" json:"os"`
	Browser        string    `ch:"browser" json:"browser"`
	ScreenWidth    uint16    `ch:"screen_width" json:"screen_width"`
	ScreenHeight   uint16    `ch:"screen_height" json:"screen_height"`
	Referrer       string    `ch:"referrer" json:"referrer"`
	ReferrerDomain string    `ch:"referrer_domain" json:"referrer_domain"`
	UtmSource      string    `ch:"utm_source" json:"utm_source"`
	UtmMedium      string    `ch:"utm_medium" json:"utm_medium"`
	UtmCampaign    string    `ch:"utm_campaign" json:"utm_campaign"`
	EventName      string    `ch:"event_name" json:"event_name"`
	Path           string    `ch:"path" json:"path"`
	PageTitle      string    `ch:"page_title" json:"page_title"`
	TrafficType    int8      `ch:"traffic_type" json:"traffic_type"` // 1=human, 2=bot_ia, 3=crawler, 4=vpn_proxy
	ASN            uint32    `ch:"asn" json:"asn"`
	UserAgent      string    `ch:"user_agent" json:"user_agent"`
	BotName        string    `ch:"bot_name" json:"bot_name"`
	CompanyName    string    `ch:"company_name" json:"company_name"`
	CompanyDomain  string    `ch:"company_domain" json:"company_domain"`
	Data           string    `ch:"data" json:"data"`
}

const (
	TrafficTypeHuman   int8 = 1
	TrafficTypeBotIA   int8 = 2
	TrafficTypeCrawler int8 = 3
	TrafficTypeVPN     int8 = 4
)
