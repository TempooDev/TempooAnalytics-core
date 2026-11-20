package repository

import (
	"context"
	"time"

	"github.com/ClickHouse/clickhouse-go/v2"
	"github.com/jackc/pgx/v5/pgxpool"
)

// Status values are stored as integers and must match the C# enums
// ImportStatus (Features/Imports/Models/ImportJob.cs) and ExportStatus (Features/Analytics/Models/ExportJob.cs).
type ImportJobStatus int
type ExportJobStatus int

const (
	ImportStatusPending    ImportJobStatus = 0
	ImportStatusProcessing ImportJobStatus = 1
	ImportStatusCompleted  ImportJobStatus = 2
	ImportStatusFailed     ImportJobStatus = 3

	ExportStatusPending    ExportJobStatus = 0
	ExportStatusProcessing ExportJobStatus = 1
	ExportStatusCompleted  ExportJobStatus = 2
	ExportStatusFailed     ExportJobStatus = 3
)

type ImportedDailyStat struct {
	SiteID        string
	Date          time.Time
	Visitors      uint64
	Pageviews     uint64
	Visits        uint64
	Bounces       uint64
	VisitDuration uint64 // total seconds for the day
	Source        string
	JobID         string
}

type ImportedDimensionStat struct {
	SiteID         string
	Date           time.Time
	DimensionName  string
	DimensionValue string
	Visitors       uint64
	Pageviews      uint64
	Visits         uint64
	Source         string
	JobID          string
}

type DailyMetricRow struct {
	Date      time.Time
	Visitors  uint64
	Pageviews uint64
	Events    uint64
}

type ImporterRepository interface {
	UpdateJobStatus(ctx context.Context, jobID string, status ImportJobStatus, errorMsg string) error
	UpdateExportStatus(ctx context.Context, jobID string, status ExportJobStatus, downloadUrl string, errorMsg string) error
	GetSiteDailyMetrics(ctx context.Context, siteID string, from time.Time, to time.Time) ([]DailyMetricRow, error)
	UpdateJobProgress(ctx context.Context, jobID string, rowsProcessed int64) error
	InsertDailyStats(ctx context.Context, stats []ImportedDailyStat) error
	InsertDimensionStats(ctx context.Context, stats []ImportedDimensionStat) error
	DeleteImportedData(ctx context.Context, siteID string) error
}

type sqlImporterRepository struct {
	pg *pgxpool.Pool
	ch clickhouse.Conn
}

func NewImporterRepository(pg *pgxpool.Pool, ch clickhouse.Conn) ImporterRepository {
	return &sqlImporterRepository{
		pg: pg,
		ch: ch,
	}
}

func (r *sqlImporterRepository) UpdateJobStatus(ctx context.Context, jobID string, status ImportJobStatus, errorMsg string) error {
	var query string
	if status == ImportStatusProcessing {
		query = "UPDATE jobs.import_jobs SET status = $1, started_at = NOW() WHERE id = $2"
		_, err := r.pg.Exec(ctx, query, int(status), jobID)
		return err
	} else if status == ImportStatusCompleted || status == ImportStatusFailed {
		query = "UPDATE jobs.import_jobs SET status = $1, completed_at = NOW(), error_message = $2 WHERE id = $3"
		_, err := r.pg.Exec(ctx, query, int(status), errorMsg, jobID)
		return err
	}
	return nil
}

func (r *sqlImporterRepository) UpdateExportStatus(ctx context.Context, jobID string, status ExportJobStatus, downloadUrl string, errorMsg string) error {
	var query string
	if status == ExportStatusProcessing {
		query = "UPDATE jobs.export_jobs SET status = $1 WHERE id = $2"
		_, err := r.pg.Exec(ctx, query, int(status), jobID)
		return err
	} else if status == ExportStatusCompleted || status == ExportStatusFailed {
		query = "UPDATE jobs.export_jobs SET status = $1, completed_at = NOW(), download_url = $2, error_message = $3 WHERE id = $4"
		_, err := r.pg.Exec(ctx, query, int(status), downloadUrl, errorMsg, jobID)
		return err
	}
	return nil
}

func (r *sqlImporterRepository) GetSiteDailyMetrics(ctx context.Context, siteID string, from time.Time, to time.Time) ([]DailyMetricRow, error) {
	query := `
		SELECT 
			toDate(timestamp) as date,
			uniq(user_hash) as visitors,
			countIf(event_name = 'pageview') as pageviews,
			countIf(event_name NOT IN ('pageview', 'leave')) as events
		FROM events 
		WHERE site_id = ? 
		  AND timestamp >= ? 
		  AND timestamp <= ?
		GROUP BY date
		ORDER BY date ASC
	`
	rows, err := r.ch.Query(ctx, query, siteID, from, to)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var metrics []DailyMetricRow
	for rows.Next() {
		var m DailyMetricRow
		if err := rows.Scan(&m.Date, &m.Visitors, &m.Pageviews, &m.Events); err != nil {
			return nil, err
		}
		metrics = append(metrics, m)
	}
	return metrics, nil
}

func (r *sqlImporterRepository) UpdateJobProgress(ctx context.Context, jobID string, rowsProcessed int64) error {
	query := "UPDATE jobs.import_jobs SET rows_processed = $1 WHERE id = $2"
	_, err := r.pg.Exec(ctx, query, rowsProcessed, jobID)
	return err
}

func (r *sqlImporterRepository) InsertDailyStats(ctx context.Context, stats []ImportedDailyStat) error {
	if len(stats) == 0 {
		return nil
	}

	batch, err := r.ch.PrepareBatch(ctx, "INSERT INTO imported_daily_stats (site_id, date, visitors, pageviews, visits, bounces, visit_duration, source, job_id)")
	if err != nil {
		return err
	}

	for _, s := range stats {
		if err := batch.Append(s.SiteID, s.Date, s.Visitors, s.Pageviews, s.Visits, s.Bounces, s.VisitDuration, s.Source, s.JobID); err != nil {
			return err
		}
	}

	return batch.Send()
}

func (r *sqlImporterRepository) InsertDimensionStats(ctx context.Context, stats []ImportedDimensionStat) error {
	if len(stats) == 0 {
		return nil
	}

	batch, err := r.ch.PrepareBatch(ctx, "INSERT INTO imported_dimension_stats (site_id, date, dimension_name, dimension_value, visitors, pageviews, visits, source, job_id)")
	if err != nil {
		return err
	}

	for _, s := range stats {
		if err := batch.Append(s.SiteID, s.Date, s.DimensionName, s.DimensionValue, s.Visitors, s.Pageviews, s.Visits, s.Source, s.JobID); err != nil {
			return err
		}
	}

	return batch.Send()
}

// DeleteImportedData removes everything previously imported for a site, from every source, so a re-import
// replaces the old history instead of adding to it (one imported history per site).
// mutations_sync = 2 waits until the delete is applied.
func (r *sqlImporterRepository) DeleteImportedData(ctx context.Context, siteID string) error {
	for _, table := range []string{"imported_daily_stats", "imported_dimension_stats"} {
		query := "ALTER TABLE " + table + " DELETE WHERE site_id = ? SETTINGS mutations_sync = 2"
		if err := r.ch.Exec(ctx, query, siteID); err != nil {
			return err
		}
	}
	return nil
}
