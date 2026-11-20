package service

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"reflect"
	"testing"

	"github.com/alicebob/miniredis/v2"
	"github.com/redis/go-redis/v9"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/repository"
)

// withRedis gives the service a Redis client backed by miniredis.
func withRedis(t *testing.T, svc *jobService) *miniredis.Miniredis {
	t.Helper()
	mr := miniredis.RunT(t)
	svc.redis = redis.NewClient(&redis.Options{Addr: mr.Addr()})
	t.Cleanup(func() { _ = svc.redis.Close() })
	return mr
}

// notifications returns the messages of queue:notifications, oldest first.
func notifications(t *testing.T, mr *miniredis.Miniredis) []map[string]string {
	t.Helper()
	if !mr.Exists(notificationQueue) {
		return nil
	}
	raw, err := mr.List(notificationQueue)
	if err != nil {
		t.Fatal(err)
	}
	var out []map[string]string
	// LPUSH stores the newest first.
	for i := len(raw) - 1; i >= 0; i-- {
		var m map[string]string
		if err := json.Unmarshal([]byte(raw[i]), &m); err != nil {
			t.Fatalf("message is not a flat string map: %v (%s)", err, raw[i])
		}
		out = append(out, m)
	}
	return out
}

func TestProcessImport_Completed_PublishesImportCompleted(t *testing.T) {
	svc, repo, _ := newTestJobService(t, map[string]string{"imports/s/a.zip": "plausible_export.zip"})
	mr := withRedis(t, svc)

	if err := svc.ProcessImport(context.Background(), plausibleTask("imports/s/a.zip")); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	got := notifications(t, mr)
	want := []map[string]string{{
		"type":          "IMPORT_COMPLETED",
		"jobId":         testJobID,
		"siteId":        testSiteID,
		"rowsProcessed": itoa(len(repo.daily) + len(repo.dims)),
	}}
	if !reflect.DeepEqual(got, want) {
		t.Errorf("notifications = %v, want %v", got, want)
	}
}

func TestProcessImport_Failed_PublishesImportFailedWithTheStoredMessage(t *testing.T) {
	svc, repo, _ := newTestJobService(t, map[string]string{"imports/s/bad.zip": "plausible_export_no_visitors.zip"})
	mr := withRedis(t, svc)

	if err := svc.ProcessImport(context.Background(), plausibleTask("imports/s/bad.zip")); err == nil {
		t.Fatal("expected an error")
	}

	got := notifications(t, mr)
	want := []map[string]string{{
		"type":         "IMPORT_FAILED",
		"jobId":        testJobID,
		"siteId":       testSiteID,
		"errorMessage": repo.errorMsg,
	}}
	if repo.errorMsg == "" || !reflect.DeepEqual(got, want) {
		t.Errorf("notifications = %v, want %v", got, want)
	}
}

// statusOrderRepo records whether a notification was already queued when each status was stored.
type statusOrderRepo struct {
	*fakeImporterRepo
	mr             *miniredis.Miniredis
	queuedAtStatus []bool
}

func (r *statusOrderRepo) UpdateJobStatus(ctx context.Context, id string, status repository.ImportJobStatus, msg string) error {
	r.queuedAtStatus = append(r.queuedAtStatus, r.mr.Exists(notificationQueue))
	return r.fakeImporterRepo.UpdateJobStatus(ctx, id, status, msg)
}

func TestProcessImport_PublishesAfterStoringTheStatus(t *testing.T) {
	svc, repo, _ := newTestJobService(t, map[string]string{"imports/s/a.zip": "plausible_export.zip"})
	mr := withRedis(t, svc)
	ordered := &statusOrderRepo{fakeImporterRepo: repo, mr: mr}
	svc.repo = ordered

	if err := svc.ProcessImport(context.Background(), plausibleTask("imports/s/a.zip")); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if want := []bool{false, false}; !reflect.DeepEqual(ordered.queuedAtStatus, want) {
		t.Errorf("queued when storing statuses = %v, want %v", ordered.queuedAtStatus, want)
	}
	if len(notifications(t, mr)) != 1 {
		t.Error("expected one notification after the job finished")
	}
}

func TestProcessImport_WhenRedisFails_KeepsTheJobStatus(t *testing.T) {
	svc, repo, _ := newTestJobService(t, map[string]string{"imports/s/a.zip": "plausible_export.zip"})
	mr := withRedis(t, svc)
	mr.SetError("connection refused")

	if err := svc.ProcessImport(context.Background(), plausibleTask("imports/s/a.zip")); err != nil {
		t.Fatalf("a lost notification must not fail the import: %v", err)
	}

	if got := repo.statuses; len(got) != 2 || got[1] != repository.ImportStatusCompleted {
		t.Errorf("statuses = %v, want [Processing Completed]", got)
	}
}

func TestProcessImport_WithoutRedis_DoesNotPanic(t *testing.T) {
	svc, _, _ := newTestJobService(t, map[string]string{"imports/s/a.zip": "plausible_export.zip"})

	if err := svc.ProcessImport(context.Background(), plausibleTask("imports/s/a.zip")); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
}

// exportRepo records export statuses.
type exportRepo struct {
	*fakeImporterRepo
	exportStatuses []repository.ExportJobStatus
}

func (r *exportRepo) UpdateExportStatus(_ context.Context, _ string, status repository.ExportJobStatus, _ string, _ string) error {
	r.exportStatuses = append(r.exportStatuses, status)
	return nil
}

func exportTask() ExportTask {
	return ExportTask{JobId: testJobID, SiteId: testSiteID, From: "2024-01-01T00:00:00Z", To: "2024-01-31T00:00:00Z", Email: "user@example.com"}
}

func TestProcessExport_Completed_PublishesExportCompletedWithSiteId(t *testing.T) {
	svc, repo, storage := newTestJobService(t, nil)
	svc.repo = &exportRepo{fakeImporterRepo: repo}
	svc.storage = storage
	mr := withRedis(t, svc)

	if err := svc.ProcessExport(context.Background(), exportTask()); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	got := notifications(t, mr)
	want := []map[string]string{{
		"type":        "EXPORT_COMPLETED",
		"jobId":       testJobID,
		"siteId":      testSiteID,
		"email":       "user@example.com",
		"downloadUrl": "",
	}}
	if !reflect.DeepEqual(got, want) {
		t.Errorf("notifications = %v, want %v", got, want)
	}
}

type failingUploadStorage struct{ *fakeStorage }

func (s *failingUploadStorage) UploadFile(context.Context, string, io.Reader, string) error {
	return errors.New("bucket unavailable")
}

func TestProcessExport_Failed_PublishesExportFailed(t *testing.T) {
	svc, repo, storage := newTestJobService(t, nil)
	exports := &exportRepo{fakeImporterRepo: repo}
	svc.repo = exports
	svc.storage = &failingUploadStorage{storage}
	mr := withRedis(t, svc)

	if err := svc.ProcessExport(context.Background(), exportTask()); err == nil {
		t.Fatal("expected an error")
	}

	if got := exports.exportStatuses; len(got) != 2 || got[1] != repository.ExportStatusFailed {
		t.Errorf("export statuses = %v, want [Processing Failed]", got)
	}
	got := notifications(t, mr)
	want := []map[string]string{{
		"type":         "EXPORT_FAILED",
		"jobId":        testJobID,
		"siteId":       testSiteID,
		"errorMessage": exportFailedMessage,
	}}
	if !reflect.DeepEqual(got, want) {
		t.Errorf("notifications = %v, want %v", got, want)
	}
}

func itoa(n int) string {
	b, _ := json.Marshal(n)
	return string(b)
}
