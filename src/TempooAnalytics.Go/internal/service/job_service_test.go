package service

import (
	"bytes"
	"context"
	"io"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/repository"
)

// fakeImporterRepo keeps imported rows in memory, like the ClickHouse tables.
type fakeImporterRepo struct {
	daily    []repository.ImportedDailyStat
	dims     []repository.ImportedDimensionStat
	calls    []string
	statuses []repository.ImportJobStatus
	errorMsg string
	progress int64
}

func (r *fakeImporterRepo) UpdateJobStatus(_ context.Context, _ string, status repository.ImportJobStatus, errorMsg string) error {
	r.statuses = append(r.statuses, status)
	r.errorMsg = errorMsg
	return nil
}

func (r *fakeImporterRepo) UpdateExportStatus(context.Context, string, repository.ExportJobStatus, string, string) error {
	return nil
}

func (r *fakeImporterRepo) GetSiteDailyMetrics(context.Context, string, time.Time, time.Time) ([]repository.DailyMetricRow, error) {
	return nil, nil
}

func (r *fakeImporterRepo) UpdateJobProgress(_ context.Context, _ string, rows int64) error {
	r.progress = rows
	return nil
}

func (r *fakeImporterRepo) InsertDailyStats(_ context.Context, stats []repository.ImportedDailyStat) error {
	r.calls = append(r.calls, "insert-daily")
	r.daily = append(r.daily, stats...)
	return nil
}

func (r *fakeImporterRepo) InsertDimensionStats(_ context.Context, stats []repository.ImportedDimensionStat) error {
	r.calls = append(r.calls, "insert-dims")
	r.dims = append(r.dims, stats...)
	return nil
}

func (r *fakeImporterRepo) DeleteImportedData(_ context.Context, siteID string) error {
	r.calls = append(r.calls, "delete")
	var daily []repository.ImportedDailyStat
	for _, d := range r.daily {
		if d.SiteID != siteID {
			daily = append(daily, d)
		}
	}
	var dims []repository.ImportedDimensionStat
	for _, d := range r.dims {
		if d.SiteID != siteID {
			dims = append(dims, d)
		}
	}
	r.daily, r.dims = daily, dims
	return nil
}

type fakeStorage struct {
	files   map[string][]byte
	deleted []string
}

func (s *fakeStorage) UploadFile(context.Context, string, io.Reader, string) error { return nil }

func (s *fakeStorage) DownloadFile(_ context.Context, key string) (io.ReadCloser, error) {
	return io.NopCloser(bytes.NewReader(s.files[key])), nil
}

func (s *fakeStorage) GeneratePresignedURL(context.Context, string, time.Duration) (string, error) {
	return "", nil
}

func (s *fakeStorage) DeleteFile(_ context.Context, key string) error {
	s.deleted = append(s.deleted, key)
	return nil
}

func newTestJobService(t *testing.T, files map[string]string) (*jobService, *fakeImporterRepo, *fakeStorage) {
	t.Helper()
	storage := &fakeStorage{files: map[string][]byte{}}
	for key, fixture := range files {
		data, err := os.ReadFile(filepath.Join("testdata", fixture))
		if err != nil {
			t.Fatal(err)
		}
		storage.files[key] = data
	}
	repo := &fakeImporterRepo{}
	return &jobService{repo: repo, storage: storage}, repo, storage
}

func plausibleTask(key string) ImportTask {
	return ImportTask{JobId: testJobID, SiteId: testSiteID, Type: "PLAUSIBLE", FilePath: key}
}

func ga4Task(key string) ImportTask {
	return ImportTask{JobId: testJobID, SiteId: testSiteID, Type: "GA4", PropertyId: "properties/123", FilePath: key}
}

func TestProcessImport_PlausibleZip(t *testing.T) {
	svc, repo, storage := newTestJobService(t, map[string]string{"imports/s/a.zip": "plausible_export.zip"})

	if err := svc.ProcessImport(context.Background(), plausibleTask("imports/s/a.zip")); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if got := repo.statuses; len(got) != 2 || got[0] != repository.ImportStatusProcessing || got[1] != repository.ImportStatusCompleted {
		t.Errorf("statuses = %v, want [Processing Completed]", got)
	}
	if repo.calls[0] != "delete" {
		t.Errorf("previous data must be deleted before inserting, calls = %v", repo.calls)
	}
	if len(repo.daily) != 30 || len(repo.dims) != 360 {
		t.Errorf("inserted %d daily and %d breakdown rows, want 30 and 360", len(repo.daily), len(repo.dims))
	}
	if repo.progress != 390 {
		t.Errorf("rows_processed = %d, want 390", repo.progress)
	}
	if len(storage.deleted) != 1 || storage.deleted[0] != "imports/s/a.zip" {
		t.Errorf("uploaded file not deleted: %v", storage.deleted)
	}
}

func TestProcessImport_ReimportDoesNotDuplicate(t *testing.T) {
	svc, repo, _ := newTestJobService(t, map[string]string{
		"imports/s/a.zip": "plausible_export.zip",
		"imports/s/b.zip": "plausible_export.zip",
	})

	if err := svc.ProcessImport(context.Background(), plausibleTask("imports/s/a.zip")); err != nil {
		t.Fatal(err)
	}
	first := totalVisitors(repo.daily)
	if err := svc.ProcessImport(context.Background(), plausibleTask("imports/s/b.zip")); err != nil {
		t.Fatal(err)
	}
	if got := totalVisitors(repo.daily); got != first || len(repo.daily) != 30 {
		t.Errorf("after re-import: %d rows with %d visitors, want 30 rows with %d", len(repo.daily), got, first)
	}
}

func TestProcessImport_FailedParseKeepsPreviousData(t *testing.T) {
	svc, repo, storage := newTestJobService(t, map[string]string{
		"imports/s/a.zip":   "plausible_export.zip",
		"imports/s/bad.zip": "plausible_export_no_visitors.zip",
	})
	if err := svc.ProcessImport(context.Background(), plausibleTask("imports/s/a.zip")); err != nil {
		t.Fatal(err)
	}
	repo.calls = nil

	if err := svc.ProcessImport(context.Background(), plausibleTask("imports/s/bad.zip")); err == nil {
		t.Fatal("expected the import to fail")
	}

	if last := repo.statuses[len(repo.statuses)-1]; last != repository.ImportStatusFailed {
		t.Errorf("last status = %v, want Failed", last)
	}
	if repo.errorMsg != ErrMissingVisitorsTable.Error() {
		t.Errorf("error message = %q", repo.errorMsg)
	}
	if len(repo.calls) != 0 {
		t.Errorf("a failed parse must not touch imported data, calls = %v", repo.calls)
	}
	if len(repo.daily) != 30 {
		t.Errorf("previous import lost: %d daily rows", len(repo.daily))
	}
	if storage.deleted[len(storage.deleted)-1] != "imports/s/bad.zip" {
		t.Errorf("uploaded file must be deleted after a failure too: %v", storage.deleted)
	}
}

// The API delivers GA4 reports already normalised to Plausible's export format.
func TestProcessImport_GA4Zip(t *testing.T) {
	svc, repo, storage := newTestJobService(t, map[string]string{"imports/s/j.ga4.zip": "plausible_export.zip"})

	if err := svc.ProcessImport(context.Background(), ga4Task("imports/s/j.ga4.zip")); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if got := repo.statuses; len(got) != 2 || got[1] != repository.ImportStatusCompleted {
		t.Errorf("statuses = %v, want [Processing Completed]", got)
	}
	if len(repo.daily) != 30 || len(repo.dims) != 360 {
		t.Errorf("inserted %d daily and %d breakdown rows, want 30 and 360", len(repo.daily), len(repo.dims))
	}
	if !onlySource(repo, "ga4") {
		t.Errorf("GA4 rows must be stored with source ga4")
	}
	if len(storage.deleted) != 1 || storage.deleted[0] != "imports/s/j.ga4.zip" {
		t.Errorf("uploaded file not deleted: %v", storage.deleted)
	}
}

func TestProcessImport_GA4ReplacesPlausible(t *testing.T) {
	svc, repo, _ := newTestJobService(t, map[string]string{
		"imports/s/a.zip":     "plausible_export.zip",
		"imports/s/j.ga4.zip": "plausible_export.zip",
	})
	if err := svc.ProcessImport(context.Background(), plausibleTask("imports/s/a.zip")); err != nil {
		t.Fatal(err)
	}
	if err := svc.ProcessImport(context.Background(), ga4Task("imports/s/j.ga4.zip")); err != nil {
		t.Fatal(err)
	}

	if len(repo.daily) != 30 || !onlySource(repo, "ga4") {
		t.Errorf("after a GA4 import only GA4 rows must remain: %d daily rows", len(repo.daily))
	}
}

func onlySource(repo *fakeImporterRepo, source string) bool {
	for _, d := range repo.daily {
		if d.Source != source {
			return false
		}
	}
	for _, d := range repo.dims {
		if d.Source != source {
			return false
		}
	}
	return true
}

func totalVisitors(rows []repository.ImportedDailyStat) uint64 {
	var total uint64
	for _, r := range rows {
		total += r.Visitors
	}
	return total
}
