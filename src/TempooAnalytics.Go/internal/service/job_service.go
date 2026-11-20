package service

import (
	"bytes"
	"context"
	"encoding/csv"
	"encoding/json"
	"fmt"
	"log"
	"strconv"
	"time"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/repository"
	"github.com/redis/go-redis/v9"
)

const importBatchSize = 1000

// notificationQueue is read by the .NET API (JobNotificationListener), which turns each message into an
// in-app notification. Messages are flat JSON objects with camelCase keys and string values only.
const notificationQueue = "queue:notifications"

const (
	notificationImportCompleted = "IMPORT_COMPLETED"
	notificationImportFailed    = "IMPORT_FAILED"
	notificationExportCompleted = "EXPORT_COMPLETED"
	notificationExportFailed    = "EXPORT_FAILED"
)

type ImportTask struct {
	JobId      string `json:"JobId"`
	SiteId     string `json:"SiteId"`
	Type       string `json:"Type"`
	PropertyId string `json:"PropertyId"`
	Start      string `json:"Start"`
	End        string `json:"End"`
	FilePath   string `json:"FilePath"`
}

type ExportTask struct {
	JobId  string `json:"JobId"`
	SiteId string `json:"SiteId"`
	From   string `json:"From"`
	To     string `json:"To"`
	Email  string `json:"Email"`
}

type JobService interface {
	ProcessImport(ctx context.Context, task ImportTask) error
	ProcessExport(ctx context.Context, task ExportTask) error
}

type jobService struct {
	repo    repository.ImporterRepository
	storage StorageService
	redis   *redis.Client
}

func NewJobService(repo repository.ImporterRepository, storage StorageService, rdb *redis.Client) JobService {
	return &jobService{
		repo:    repo,
		storage: storage,
		redis:   rdb,
	}
}

func (s *jobService) ProcessImport(ctx context.Context, task ImportTask) error {
	log.Printf("Processing import job %s (Site: %s, Type: %s)", task.JobId, task.SiteId, task.Type)
	if uerr := s.repo.UpdateJobStatus(ctx, task.JobId, repository.ImportStatusProcessing, ""); uerr != nil {
		log.Printf("job %s: failed to update status: %v", task.JobId, uerr)
	}

	var rows int
	var err error
	switch task.Type {
	// GA4 reports are downloaded and normalised to Plausible's export format by the API,
	// so both sources follow the same path and differ only in the source written to the rows.
	case "GA4":
		rows, err = s.processExport(ctx, task, importSourceGA4)
	case "PLAUSIBLE":
		rows, err = s.processExport(ctx, task, importSourcePlausible)
	default:
		err = fmt.Errorf("unknown import type: %s", task.Type)
	}

	// The notification is published after the status is stored, so the API reads the final state.
	if err != nil {
		log.Printf("Job %s failed: %v", task.JobId, err)
		if uerr := s.repo.UpdateJobStatus(ctx, task.JobId, repository.ImportStatusFailed, err.Error()); uerr != nil {
			log.Printf("job %s: failed to update status: %v", task.JobId, uerr)
		}
		s.notify(ctx, task.JobId, map[string]string{
			"type":         notificationImportFailed,
			"jobId":        task.JobId,
			"siteId":       task.SiteId,
			"errorMessage": err.Error(),
		})
		return err
	}

	log.Printf("Job %s completed successfully", task.JobId)
	if uerr := s.repo.UpdateJobStatus(ctx, task.JobId, repository.ImportStatusCompleted, ""); uerr != nil {
		log.Printf("job %s: failed to update status: %v", task.JobId, uerr)
	}
	s.notify(ctx, task.JobId, map[string]string{
		"type":          notificationImportCompleted,
		"jobId":         task.JobId,
		"siteId":        task.SiteId,
		"rowsProcessed": strconv.Itoa(rows),
	})
	return nil
}

// processExport imports a Plausible-format export and returns the number of rows inserted.
func (s *jobService) processExport(ctx context.Context, task ImportTask, source string) (int, error) {
	if task.FilePath == "" {
		return 0, fmt.Errorf("no file path (S3 key) provided for import")
	}
	// The uploaded file is only needed for this job: delete it whether the import succeeds or fails.
	defer func() {
		if err := s.storage.DeleteFile(context.WithoutCancel(ctx), task.FilePath); err != nil {
			log.Printf("job %s: failed to delete uploaded file: %v", task.JobId, err)
		}
	}()

	content, err := s.storage.DownloadFile(ctx, task.FilePath)
	if err != nil {
		return 0, fmt.Errorf("failed to download file from storage: %w", err)
	}
	defer content.Close()

	// 1. Validate and parse everything first: a bad file must not touch the data already imported.
	parsed, err := ParsePlausibleExport(content, task.FilePath, task.SiteId, task.JobId, source)
	if err != nil {
		return 0, err
	}

	// 2. A new import, from any source, replaces everything imported before for the site,
	// so two sources never count the same day twice (only one import can be active per site).
	if err := s.repo.DeleteImportedData(ctx, task.SiteId); err != nil {
		return 0, fmt.Errorf("failed to delete previously imported data: %w", err)
	}

	// 3. Insert in batches, reporting progress.
	rows := 0
	for start := 0; start < len(parsed.Daily); start += importBatchSize {
		batch := parsed.Daily[start:min(start+importBatchSize, len(parsed.Daily))]
		if err := s.repo.InsertDailyStats(ctx, batch); err != nil {
			return rows, fmt.Errorf("failed to insert daily totals: %w", err)
		}
		rows += len(batch)
		s.reportProgress(ctx, task.JobId, rows)
	}
	for start := 0; start < len(parsed.Dimensions); start += importBatchSize {
		batch := parsed.Dimensions[start:min(start+importBatchSize, len(parsed.Dimensions))]
		if err := s.repo.InsertDimensionStats(ctx, batch); err != nil {
			return rows, fmt.Errorf("failed to insert breakdowns: %w", err)
		}
		rows += len(batch)
		s.reportProgress(ctx, task.JobId, rows)
	}

	log.Printf("job %s: imported %d daily rows and %d breakdown rows", task.JobId, len(parsed.Daily), len(parsed.Dimensions))
	return rows, nil
}

func (s *jobService) reportProgress(ctx context.Context, jobID string, rows int) {
	if err := s.repo.UpdateJobProgress(ctx, jobID, int64(rows)); err != nil {
		log.Printf("job %s: failed to update progress: %v", jobID, err)
	}
}

func (s *jobService) ProcessExport(ctx context.Context, task ExportTask) error {
	log.Printf("Starting export job %s for site %s", task.JobId, task.SiteId)
	if uerr := s.repo.UpdateExportStatus(ctx, task.JobId, repository.ExportStatusProcessing, "", ""); uerr != nil {
		log.Printf("job %s: failed to update status: %v", task.JobId, uerr)
	}

	from, _ := time.Parse(time.RFC3339, task.From)
	to, _ := time.Parse(time.RFC3339, task.To)

	metrics, err := s.repo.GetSiteDailyMetrics(ctx, task.SiteId, from, to)
	if err != nil {
		return s.failExport(ctx, task, err)
	}

	var buf bytes.Buffer
	writer := csv.NewWriter(&buf)
	writer.Write([]string{"Date", "Visitors", "Pageviews", "Events"})

	for _, m := range metrics {
		writer.Write([]string{
			m.Date.Format("2006-01-02"),
			strconv.FormatUint(m.Visitors, 10),
			strconv.FormatUint(m.Pageviews, 10),
			strconv.FormatUint(m.Events, 10),
		})
	}
	writer.Flush()

	fileName := fmt.Sprintf("exports/%s/%s_export.csv", task.SiteId, time.Now().Format("20060102150405"))

	err = s.storage.UploadFile(ctx, fileName, bytes.NewReader(buf.Bytes()), "text/csv")
	if err != nil {
		return s.failExport(ctx, task, err)
	}

	url, err := s.storage.GeneratePresignedURL(ctx, fileName, 24*time.Hour)
	if err != nil {
		return s.failExport(ctx, task, err)
	}

	if uerr := s.repo.UpdateExportStatus(ctx, task.JobId, repository.ExportStatusCompleted, url, ""); uerr != nil {
		log.Printf("job %s: failed to update status: %v", task.JobId, uerr)
	}

	// The API sends the export-ready email (only for this type) and stores the in-app notification.
	s.notify(ctx, task.JobId, map[string]string{
		"type":        notificationExportCompleted,
		"jobId":       task.JobId,
		"siteId":      task.SiteId,
		"email":       task.Email,
		"downloadUrl": url,
	})

	return nil
}

// exportFailedMessage is the only error text an EXPORT_FAILED notification carries.
const exportFailedMessage = "export failed"

func (s *jobService) failExport(ctx context.Context, task ExportTask, err error) error {
	log.Printf("job %s: export failed: %v", task.JobId, err)
	if uerr := s.repo.UpdateExportStatus(ctx, task.JobId, repository.ExportStatusFailed, "", err.Error()); uerr != nil {
		log.Printf("job %s: failed to update status: %v", task.JobId, uerr)
	}
	// The raw error stays in the log and the job row: it may hold internal details (paths, hosts).
	s.notify(ctx, task.JobId, map[string]string{
		"type":         notificationExportFailed,
		"jobId":        task.JobId,
		"siteId":       task.SiteId,
		"errorMessage": exportFailedMessage,
	})
	return err
}

// notify publishes a message for the API on queue:notifications. A failure only loses the notification:
// it is logged and never changes the job, whose status is already stored.
func (s *jobService) notify(ctx context.Context, jobID string, message map[string]string) {
	if s.redis == nil {
		return
	}
	payload, err := json.Marshal(message)
	if err != nil {
		log.Printf("job %s: failed to encode %s notification: %v", jobID, message["type"], err)
		return
	}
	if err := s.redis.LPush(context.WithoutCancel(ctx), notificationQueue, payload).Err(); err != nil {
		log.Printf("job %s: failed to enqueue %s notification: %v", jobID, message["type"], err)
	}
}
