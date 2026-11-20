package service

import (
	"archive/zip"
	"bytes"
	"encoding/csv"
	"errors"
	"fmt"
	"io"
	"path"
	"sort"
	"strconv"
	"strings"
	"time"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/repository"
)

// Limits for uploaded exports (protection against zip bombs). The API already rejects uploads over 50 MB.
const (
	maxCompressedBytes = 50 << 20
	maxZipEntries      = 50

	importSourcePlausible = "plausible"
	importSourceGA4       = "ga4"
)

// Variable so tests can lower it without building a 500 MB archive.
var maxUncompressedBytes int64 = 500 << 20

var (
	ErrMissingVisitorsTable = errors.New("the export is missing the imported_visitors table")
	ErrExportTooLarge       = errors.New("the export is too large (max 50 files and 500 MB uncompressed)")
)

// plausibleTable describes one table of Plausible's CSV export.
type plausibleTable struct {
	prefix    string
	dimension string // dimension_name in imported_dimension_stats; empty for the daily totals table
	column    string // column holding the dimension value
}

// Order matters only for readability; prefixes don't overlap (imported_entry_pages is not imported_pages).
var plausibleTables = []plausibleTable{
	{prefix: "imported_visitors"},
	{prefix: "imported_sources", dimension: "referrer", column: "source"},
	{prefix: "imported_pages", dimension: "path", column: "page"},
	{prefix: "imported_locations", dimension: "country", column: "country"},
	{prefix: "imported_devices", dimension: "device", column: "device"},
	{prefix: "imported_browsers", dimension: "browser", column: "browser"},
	{prefix: "imported_operating_systems", dimension: "os", column: "operating_system"},
}

// sourceDomains maps Plausible source names to the referrer host Tempoo records for live traffic,
// so a mixed period doesn't show two rows for the same source. Unknown sources are kept as-is.
var sourceDomains = map[string]string{
	"google":         "www.google.com",
	"bing":           "www.bing.com",
	"duckduckgo":     "duckduckgo.com",
	"yahoo!":         "search.yahoo.com",
	"yahoo":          "search.yahoo.com",
	"yandex":         "yandex.ru",
	"baidu":          "www.baidu.com",
	"ecosia":         "www.ecosia.org",
	"qwant":          "www.qwant.com",
	"brave":          "search.brave.com",
	"facebook":       "www.facebook.com",
	"twitter":        "t.co",
	"x":              "t.co",
	"linkedin":       "www.linkedin.com",
	"reddit":         "www.reddit.com",
	"youtube":        "www.youtube.com",
	"instagram":      "l.instagram.com",
	"pinterest":      "www.pinterest.com",
	"github":         "github.com",
	"hacker news":    "news.ycombinator.com",
	"chatgpt":        "chatgpt.com",
	"perplexity":     "www.perplexity.ai",
	"mastodon":       "mastodon.social",
	"product hunt":   "www.producthunt.com",
	"stack overflow": "stackoverflow.com",
}

// ParsedExport is a fully validated export, ready to be inserted.
type ParsedExport struct {
	Daily      []repository.ImportedDailyStat
	Dimensions []repository.ImportedDimensionStat
}

// Rows is the number of rows that will be inserted.
func (p *ParsedExport) Rows() int { return len(p.Daily) + len(p.Dimensions) }

// ParsePlausibleExport reads a Plausible export (zip with one CSV per table, or a single daily-totals CSV)
// fully in memory and maps it to imported rows tagged with source ("plausible", or "ga4" for GA4 reports
// the API normalised to this format). Nothing is written anywhere.
func ParsePlausibleExport(r io.Reader, fileName, siteID, jobID, source string) (*ParsedExport, error) {
	data, err := io.ReadAll(io.LimitReader(r, maxCompressedBytes+1))
	if err != nil {
		return nil, fmt.Errorf("failed to read file: %w", err)
	}
	if len(data) > maxCompressedBytes {
		return nil, fmt.Errorf("the file is too large (max 50 MB)")
	}

	tables := map[string][]string{} // table prefix -> CSV files (raw content)
	if isZip(data, fileName) {
		if err := readZipTables(data, tables); err != nil {
			return nil, err
		}
	} else {
		// A single CSV is treated as imported_visitors (legacy daily totals format).
		tables["imported_visitors"] = []string{string(data)}
	}

	if len(tables["imported_visitors"]) == 0 {
		return nil, ErrMissingVisitorsTable
	}

	b := newExportBuilder(siteID, jobID, source)
	for _, t := range plausibleTables {
		for _, file := range tables[t.prefix] {
			if err := b.addTable(t, file); err != nil {
				return nil, fmt.Errorf("%s: %w", t.prefix, err)
			}
		}
	}
	return b.build(), nil
}

func isZip(data []byte, fileName string) bool {
	return bytes.HasPrefix(data, []byte("PK\x03\x04")) || strings.EqualFold(path.Ext(fileName), ".zip")
}

func readZipTables(data []byte, tables map[string][]string) error {
	zr, err := zip.NewReader(bytes.NewReader(data), int64(len(data)))
	if err != nil {
		return fmt.Errorf("invalid zip file: %w", err)
	}
	if len(zr.File) > maxZipEntries {
		return ErrExportTooLarge
	}
	var declared uint64
	for _, f := range zr.File {
		declared += f.UncompressedSize64
	}
	if declared > uint64(maxUncompressedBytes) {
		return ErrExportTooLarge
	}

	// The declared sizes can lie, so the real bytes read are capped as well.
	remaining := maxUncompressedBytes
	for _, f := range zr.File {
		if f.FileInfo().IsDir() {
			continue
		}
		table := tableForFile(f.Name)
		if table == "" {
			continue
		}
		rc, err := f.Open()
		if err != nil {
			return fmt.Errorf("failed to open %s: %w", path.Base(f.Name), err)
		}
		content, err := io.ReadAll(io.LimitReader(rc, remaining+1))
		rc.Close()
		if err != nil {
			return fmt.Errorf("failed to read %s: %w", path.Base(f.Name), err)
		}
		remaining -= int64(len(content))
		if remaining < 0 {
			return ErrExportTooLarge
		}
		tables[table] = append(tables[table], string(content))
	}
	return nil
}

// tableForFile recognises a Plausible table by its file-name prefix, ignoring folders and the date suffix.
func tableForFile(name string) string {
	base := strings.ToLower(path.Base(strings.ReplaceAll(name, "\\", "/")))
	if !strings.HasSuffix(base, ".csv") {
		return ""
	}
	for _, t := range plausibleTables {
		if base == t.prefix+".csv" || strings.HasPrefix(base, t.prefix+"_") {
			return t.prefix
		}
	}
	return ""
}

type dailyTotals struct {
	visitors, pageviews, visits, bounces, visitDuration uint64
}

type dimensionKey struct {
	date        time.Time
	name, value string
}

type dimensionTotals struct {
	visitors, pageviews, visits uint64
}

type exportBuilder struct {
	siteID, jobID string
	source        string
	daily         map[time.Time]*dailyTotals
	dimensions    map[dimensionKey]*dimensionTotals
}

func newExportBuilder(siteID, jobID, source string) *exportBuilder {
	return &exportBuilder{
		siteID:     siteID,
		jobID:      jobID,
		source:     source,
		daily:      map[time.Time]*dailyTotals{},
		dimensions: map[dimensionKey]*dimensionTotals{},
	}
}

func (b *exportBuilder) addTable(t plausibleTable, content string) error {
	reader := csv.NewReader(strings.NewReader(content))
	reader.FieldsPerRecord = -1
	header, err := reader.Read()
	if err == io.EOF {
		return fmt.Errorf("the file is empty")
	}
	if err != nil {
		return fmt.Errorf("failed to read header: %w", err)
	}
	cols := normalizeHeader(header)

	required := []string{"date"}
	if t.dimension == "" {
		required = append(required, "visitors", "pageviews")
	} else {
		required = append(required, t.column)
	}
	for _, c := range required {
		if _, ok := cols[c]; !ok {
			return fmt.Errorf("required column %q not found", c)
		}
	}

	for line := 2; ; line++ {
		record, err := reader.Read()
		if err == io.EOF {
			return nil
		}
		if err != nil {
			return fmt.Errorf("error reading row %d: %w", line, err)
		}
		date, err := time.Parse("2006-01-02", strings.TrimSpace(field(record, cols, "date")))
		if err != nil {
			continue // skip rows without a valid date
		}
		if t.dimension == "" {
			d := b.daily[date]
			if d == nil {
				d = &dailyTotals{}
				b.daily[date] = d
			}
			d.visitors += number(record, cols, "visitors")
			d.pageviews += number(record, cols, "pageviews")
			d.visits += number(record, cols, "visits")
			d.bounces += number(record, cols, "bounces")
			d.visitDuration += number(record, cols, "visit_duration")
			continue
		}

		value, ok := dimensionValue(t.dimension, field(record, cols, t.column))
		if !ok {
			continue
		}
		key := dimensionKey{date: date, name: t.dimension, value: value}
		d := b.dimensions[key]
		if d == nil {
			d = &dimensionTotals{}
			b.dimensions[key] = d
		}
		d.visitors += number(record, cols, "visitors")
		d.pageviews += number(record, cols, "pageviews")
		d.visits += number(record, cols, "visits")
	}
}

func (b *exportBuilder) build() *ParsedExport {
	out := &ParsedExport{}
	for date, d := range b.daily {
		out.Daily = append(out.Daily, repository.ImportedDailyStat{
			SiteID:        b.siteID,
			Date:          date,
			Visitors:      d.visitors,
			Pageviews:     d.pageviews,
			Visits:        d.visits,
			Bounces:       d.bounces,
			VisitDuration: d.visitDuration,
			Source:        b.source,
			JobID:         b.jobID,
		})
	}
	for k, d := range b.dimensions {
		out.Dimensions = append(out.Dimensions, repository.ImportedDimensionStat{
			SiteID:         b.siteID,
			Date:           k.date,
			DimensionName:  k.name,
			DimensionValue: k.value,
			Visitors:       d.visitors,
			Pageviews:      d.pageviews,
			Visits:         d.visits,
			Source:         b.source,
			JobID:          b.jobID,
		})
	}
	sort.Slice(out.Daily, func(i, j int) bool { return out.Daily[i].Date.Before(out.Daily[j].Date) })
	sort.Slice(out.Dimensions, func(i, j int) bool {
		a, c := out.Dimensions[i], out.Dimensions[j]
		if !a.Date.Equal(c.Date) {
			return a.Date.Before(c.Date)
		}
		if a.DimensionName != c.DimensionName {
			return a.DimensionName < c.DimensionName
		}
		return a.DimensionValue < c.DimensionValue
	})
	return out
}

// dimensionValue maps a Plausible value to the value Tempoo stores for live data.
func dimensionValue(dimension, raw string) (string, bool) {
	v := strings.TrimSpace(raw)
	if v == "" || strings.EqualFold(v, "(not set)") {
		return "", false
	}
	switch dimension {
	case "referrer":
		// Direct traffic has no referrer in live data either.
		if strings.EqualFold(v, "Direct / None") {
			return "", false
		}
		if domain, ok := sourceDomains[strings.ToLower(v)]; ok {
			return domain, true
		}
	case "country":
		v = strings.ToUpper(v)
	case "device":
		v = strings.ToLower(v)
	}
	return v, true
}

// normalizeHeader maps column names (lowercase, trimmed, without UTF-8 BOM) to their index.
func normalizeHeader(header []string) map[string]int {
	cols := make(map[string]int, len(header))
	for i, h := range header {
		name := strings.ToLower(strings.TrimSpace(strings.TrimPrefix(h, "\ufeff")))
		if _, dup := cols[name]; !dup {
			cols[name] = i
		}
	}
	return cols
}

func field(record []string, cols map[string]int, name string) string {
	i, ok := cols[name]
	if !ok || i >= len(record) {
		return ""
	}
	return record[i]
}

// number returns 0 for missing or invalid values: not every Plausible table has every metric.
func number(record []string, cols map[string]int, name string) uint64 {
	n, err := strconv.ParseUint(strings.TrimSpace(field(record, cols, name)), 10, 64)
	if err != nil {
		return 0
	}
	return n
}
