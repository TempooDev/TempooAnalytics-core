package service

import (
	"archive/zip"
	"bytes"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/repository"
)

const (
	testSiteID = "6f1c2a8e-0000-4000-8000-000000000001"
	testJobID  = "6f1c2a8e-0000-4000-8000-0000000000aa"
)

func openFixture(t *testing.T, name string) *os.File {
	t.Helper()
	f, err := os.Open(filepath.Join("testdata", name))
	if err != nil {
		t.Fatalf("open fixture: %v", err)
	}
	t.Cleanup(func() { f.Close() })
	return f
}

func buildZip(t *testing.T, files map[string]string) []byte {
	t.Helper()
	var buf bytes.Buffer
	zw := zip.NewWriter(&buf)
	for name, content := range files {
		w, err := zw.Create(name)
		if err != nil {
			t.Fatal(err)
		}
		if _, err := w.Write([]byte(content)); err != nil {
			t.Fatal(err)
		}
	}
	if err := zw.Close(); err != nil {
		t.Fatal(err)
	}
	return buf.Bytes()
}

func TestParsePlausibleExport(t *testing.T) {
	manyFiles := map[string]string{"imported_visitors_20250901_20250930.csv": "date,visitors,pageviews\n2025-09-01,1,1\n"}
	for i := 0; i < maxZipEntries; i++ {
		manyFiles[fmt.Sprintf("other_%02d.csv", i)] = "x\n"
	}

	tests := []struct {
		name       string
		fileName   string
		data       func(t *testing.T) []byte
		wantErr    error
		wantErrSub string
		wantDaily  int
		wantDims   int
	}{
		{
			name:      "full Plausible zip",
			fileName:  "imports/site/abc.zip",
			data:      func(t *testing.T) []byte { return readFixture(t, "plausible_export.zip") },
			wantDaily: 30,
			wantDims:  30 * 12, // 2 values for each of the 6 dimensions, every day
		},
		{
			name:      "single CSV with BOM and padded headers",
			fileName:  "imports/site/abc.csv",
			data:      func(t *testing.T) []byte { return readFixture(t, "daily_totals.csv") },
			wantDaily: 2,
		},
		{
			name:     "zip without visitors table",
			fileName: "imports/site/abc.zip",
			data:     func(t *testing.T) []byte { return readFixture(t, "plausible_export_no_visitors.zip") },
			wantErr:  ErrMissingVisitorsTable,
		},
		{
			name:       "single CSV without required columns",
			fileName:   "imports/site/abc.csv",
			data:       func(t *testing.T) []byte { return []byte("date,visitors\n2025-09-01,10\n") },
			wantErrSub: `required column "pageviews" not found`,
		},
		{
			name:     "zip with too many files",
			fileName: "imports/site/abc.zip",
			data:     func(t *testing.T) []byte { return buildZip(t, manyFiles) },
			wantErr:  ErrExportTooLarge,
		},
		{
			name:     "zip that expands over the limit",
			fileName: "imports/site/abc.zip",
			data: func(t *testing.T) []byte {
				// Highly compressible content: a small archive that expands past the (lowered) limit.
				old := maxUncompressedBytes
				maxUncompressedBytes = 1 << 20
				t.Cleanup(func() { maxUncompressedBytes = old })
				body := "date,visitors,pageviews\n" + strings.Repeat("2025-09-01,1,1\n", 200_000)
				return buildZip(t, map[string]string{"imported_visitors_20250901_20250930.csv": body})
			},
			wantErr: ErrExportTooLarge,
		},
		{
			name:     "tables inside a folder are recognised",
			fileName: "imports/site/abc.zip",
			data: func(t *testing.T) []byte {
				return buildZip(t, map[string]string{
					"export/IMPORTED_VISITORS_20250901_20250930.csv": "date,visitors,pageviews\n2025-09-01,5,9\n",
					"export/imported_custom_events_20250901.csv":     "date,name\n2025-09-01,signup\n",
				})
			},
			wantDaily: 1,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got, err := ParsePlausibleExport(bytes.NewReader(tt.data(t)), tt.fileName, testSiteID, testJobID, importSourcePlausible)
			if tt.wantErr != nil || tt.wantErrSub != "" {
				if err == nil {
					t.Fatalf("expected error, got none")
				}
				if tt.wantErr != nil && !errors.Is(err, tt.wantErr) {
					t.Fatalf("expected %v, got %v", tt.wantErr, err)
				}
				if tt.wantErrSub != "" && !strings.Contains(err.Error(), tt.wantErrSub) {
					t.Fatalf("expected error containing %q, got %v", tt.wantErrSub, err)
				}
				return
			}
			if err != nil {
				t.Fatalf("unexpected error: %v", err)
			}
			if len(got.Daily) != tt.wantDaily {
				t.Errorf("daily rows = %d, want %d", len(got.Daily), tt.wantDaily)
			}
			if len(got.Dimensions) != tt.wantDims {
				t.Errorf("dimension rows = %d, want %d", len(got.Dimensions), tt.wantDims)
			}
		})
	}
}

func readFixture(t *testing.T, name string) []byte {
	t.Helper()
	data, err := os.ReadFile(filepath.Join("testdata", name))
	if err != nil {
		t.Fatalf("read fixture: %v", err)
	}
	return data
}

func TestParsePlausibleExport_Mapping(t *testing.T) {
	got, err := ParsePlausibleExport(openFixture(t, "plausible_export.zip"), "abc.zip", testSiteID, testJobID, importSourcePlausible)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	day := time.Date(2025, 9, 1, 0, 0, 0, 0, time.UTC)
	wantDaily := repository.ImportedDailyStat{
		SiteID: testSiteID, Date: day, Visitors: 100, Pageviews: 200, Visits: 110, Bounces: 50,
		VisitDuration: 6600, Source: "plausible", JobID: testJobID,
	}
	if got.Daily[0] != wantDaily {
		t.Errorf("first daily row = %+v, want %+v", got.Daily[0], wantDaily)
	}

	dims := map[string]repository.ImportedDimensionStat{}
	for _, d := range got.Dimensions {
		if d.Date.Equal(day) {
			dims[d.DimensionName+"|"+d.DimensionValue] = d
		}
	}

	tests := []struct {
		key                         string
		visitors, pageviews, visits uint64
	}{
		{"referrer|www.google.com", 60, 120, 62},      // "Google" normalised to the live referrer host
		{"referrer|news.ycombinator.com", 10, 20, 10}, // unknown sources kept as-is
		{"path|/", 100, 120, 100},                     // pages
		{"path|/blog", 20, 80, 20},                    // pages
		{"country|ES", 60, 120, 62},                   // two cities aggregated by country
		{"country|DE", 40, 80, 40},                    // locations
		{"device|desktop", 60, 120, 60},               // device lowercased
		{"device|mobile", 40, 80, 40},                 // device lowercased
		{"browser|Chrome", 70, 140, 70},               // versions aggregated by name
		{"browser|Firefox", 30, 60, 30},               // browsers
		{"os|Windows", 60, 120, 60},                   // operating systems
		{"os|Mac", 40, 80, 40},                        // operating systems
	}
	for _, tt := range tests {
		d, ok := dims[tt.key]
		if !ok {
			t.Errorf("%s: missing", tt.key)
			continue
		}
		if d.Visitors != tt.visitors || d.Pageviews != tt.pageviews || d.Visits != tt.visits {
			t.Errorf("%s = visitors %d, pageviews %d, visits %d; want %d, %d, %d",
				tt.key, d.Visitors, d.Pageviews, d.Visits, tt.visitors, tt.pageviews, tt.visits)
		}
		if d.Source != "plausible" || d.JobID != testJobID || d.SiteID != testSiteID {
			t.Errorf("%s: unexpected source/job/site %q %q %q", tt.key, d.Source, d.JobID, d.SiteID)
		}
	}
	if _, ok := dims["referrer|Direct / None"]; ok {
		t.Errorf("direct traffic must not be stored as a referrer")
	}
	if len(dims) != len(tests) {
		t.Errorf("got %d breakdown rows for the day, want %d (entry pages must be ignored)", len(dims), len(tests))
	}
}

func TestParsePlausibleExport_LegacyCSV(t *testing.T) {
	got, err := ParsePlausibleExport(openFixture(t, "daily_totals.csv"), "abc.csv", testSiteID, testJobID, importSourcePlausible)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if got.Daily[1].Visitors != 15 || got.Daily[1].Pageviews != 30 || got.Daily[1].Visits != 0 {
		t.Errorf("second row = %+v", got.Daily[1])
	}
}

func TestParsePlausibleExport_SourceGA4(t *testing.T) {
	got, err := ParsePlausibleExport(openFixture(t, "plausible_export.zip"), "abc.ga4.zip", testSiteID, testJobID, importSourceGA4)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(got.Daily) == 0 || len(got.Dimensions) == 0 {
		t.Fatalf("expected rows, got %d daily and %d breakdown rows", len(got.Daily), len(got.Dimensions))
	}
	for _, d := range got.Daily {
		if d.Source != "ga4" {
			t.Fatalf("daily row source = %q, want ga4", d.Source)
		}
	}
	for _, d := range got.Dimensions {
		if d.Source != "ga4" {
			t.Fatalf("breakdown row source = %q, want ga4", d.Source)
		}
	}
}

// ga4_export.zip is written by the API's Ga4ExportWriter (see Ga4ExportTests.WrittenExport_MatchesTheGoWorkerFixture).
func TestParsePlausibleExport_Ga4ExportFromTheAPI(t *testing.T) {
	got, err := ParsePlausibleExport(openFixture(t, "ga4_export.zip"), "j.ga4.zip", testSiteID, testJobID, importSourceGA4)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	// Spec "Totales de un día": 120 users, 400 views, 150 sessions, 90 engaged, 60 s average.
	want := repository.ImportedDailyStat{
		SiteID: testSiteID, Date: time.Date(2024, 1, 20, 0, 0, 0, 0, time.UTC), Visitors: 120, Pageviews: 400,
		Visits: 150, Bounces: 60, VisitDuration: 9000, Source: "ga4", JobID: testJobID,
	}
	if len(got.Daily) != 2 || got.Daily[0] != want {
		t.Fatalf("daily rows = %+v, want first %+v", got.Daily, want)
	}

	dims := map[string]repository.ImportedDimensionStat{}
	for _, d := range got.Dimensions {
		if d.Source != "ga4" {
			t.Errorf("%s|%s: source %q, want ga4", d.DimensionName, d.DimensionValue, d.Source)
		}
		if d.Date.Equal(want.Date) {
			dims[d.DimensionName+"|"+d.DimensionValue] = d
		}
	}
	for _, key := range []string{"referrer|www.google.com", "path|/", `path|/blog, "news"`, "country|ES", "device|desktop", "device|mobile", "browser|A", "os|A"} {
		if d, ok := dims[key]; !ok || d.Visitors != 10 || d.Pageviews != 20 {
			t.Errorf("%s = %+v (found %v), want 10 visitors and 20 pageviews", key, d, ok)
		}
	}
	if len(dims) != 8 {
		t.Errorf("got %d breakdown rows for the day, want 8 (GA4 placeholders dropped): %v", len(dims), dims)
	}
}
