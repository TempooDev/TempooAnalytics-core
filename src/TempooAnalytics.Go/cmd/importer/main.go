package main

import (
	"context"
	"encoding/json"
	"fmt"
	"log"
	"net/http"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"

	"github.com/ClickHouse/clickhouse-go/v2"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/config"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/repository"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/service"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/telemetry"
	"github.com/exaring/otelpgx"
	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/redis/go-redis/v9"
)

func main() {
	serviceName := "jobs-service"

	// 1. Initialize Telemetry
	shutdown, err := telemetry.InitTelemetry(serviceName)
	if err != nil {
		log.Printf("Warning: Failed to initialize telemetry: %v", err)
	} else {
		defer shutdown(context.Background())
	}

	// 2. ClickHouse Config
	chAddr := config.GetEnv("CLICKHOUSE_ADDR", fmt.Sprintf("%s:%s", config.GetEnv("CLICKHOUSE_HOST", "localhost"), config.GetEnv("CLICKHOUSE_PORT", "9000")))
	chConn, err := clickhouse.Open(&clickhouse.Options{
		Addr: []string{chAddr},
		Auth: clickhouse.Auth{
			Database: config.GetEnv("CLICKHOUSE_DB", "tempoo_analytics"),
			Username: config.GetEnv("CLICKHOUSE_USER", "default"),
			Password: config.GetEnv("CLICKHOUSE_PASSWORD", ""),
		},
	})
	if err != nil {
		log.Fatalf("ClickHouse error: %v", err)
	}

	// 3. Redis Config
	redisAddr := config.GetEnv("ConnectionStrings__redis", config.GetEnv("REDIS_ADDR", "localhost:6379"))
	opts, _ := config.ParseRedisOptions(redisAddr)

	// IMPORTANT: For blocking operations such as BLPop, the ReadTimeout must be
	// greater than the command's wait time.
	opts.ReadTimeout = 30 * time.Second

	rdb := redis.NewClient(opts)

	// 4. Postgres Config
	pgConnStr := config.GetEnv("ConnectionStrings__analytics-db", config.GetEnv("POSTGRES_CONN_STRING", ""))

	// Convert .NET conn string if needed
	var cleanDSN string
	if strings.Contains(pgConnStr, ";") {
		parts := strings.Split(pgConnStr, ";")
		for _, part := range parts {
			kv := strings.SplitN(part, "=", 2)
			if len(kv) != 2 {
				continue
			}
			key := strings.ToLower(strings.TrimSpace(kv[0]))
			val := kv[1]
			switch key {
			case "host", "server":
				cleanDSN += fmt.Sprintf("host=%s ", val)
			case "port":
				cleanDSN += fmt.Sprintf("port=%s ", val)
			case "username", "user id", "user":
				cleanDSN += fmt.Sprintf("user=%s ", val)
			case "password":
				cleanDSN += fmt.Sprintf("password=%s ", val)
			case "database", "initial catalog":
				cleanDSN += fmt.Sprintf("dbname=%s ", val)
			}
		}
	} else {
		cleanDSN = pgConnStr
	}

	pgConfig, _ := pgxpool.ParseConfig(cleanDSN)
	pgConfig.ConnConfig.Tracer = otelpgx.NewTracer()
	pgPool, _ := pgxpool.NewWithConfig(context.Background(), pgConfig)

	// 5. Initialize Services
	repo := repository.NewImporterRepository(pgPool, chConn)

	r2Endpoint := config.GetEnv("STORAGE_R2_ENDPOINT", "")
	r2AccessKey := config.GetEnv("STORAGE_R2_ACCESS_KEY", "")
	r2SecretKey := config.GetEnv("STORAGE_R2_SECRET_KEY", "")
	r2Bucket := config.GetEnv("STORAGE_R2_BUCKET", "tempoo-exports")

	storageSvc, err := service.NewR2StorageService(context.Background(), r2Endpoint, r2AccessKey, r2SecretKey, r2Bucket)
	if err != nil {
		log.Fatalf("Failed to initialize storage service: %v", err)
	}

	jobSvc := service.NewJobService(repo, storageSvc, rdb)

	log.Println("Jobs worker started...")

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	// 6. Health check server
	port := config.GetEnv("PORT", "8084")
	go func() {
		http.HandleFunc("/health", func(w http.ResponseWriter, r *http.Request) {
			fmt.Fprintf(w, "ok")
		})
		_ = http.ListenAndServe(":"+port, nil)
	}()

	// 7. Worker Loop
	go func() {
		for {
			select {
			case <-ctx.Done():
				return
			default:
				result, err := rdb.BLPop(ctx, 5*time.Second, "queue:imports").Result()
				if err != nil {
					if err != redis.Nil {
						log.Printf("Redis error: %v", err)
						// Back off on a real error (e.g. EOF) so as not to flood the logs and the CPU
						time.Sleep(2 * time.Second)
					}
					continue
				}

				payload := result[1]
				var raw map[string]interface{}
				if err := json.Unmarshal([]byte(payload), &raw); err != nil {
					log.Printf("Invalid task payload: %v", err)
					continue
				}

				// .NET serializes payloads in camelCase ("type"); accept either casing.
				jobType, _ := raw["type"].(string)
				if jobType == "" {
					jobType, _ = raw["Type"].(string)
				}

				if jobType == "EXPORT" {
					var task service.ExportTask
					b, _ := json.Marshal(raw)
					json.Unmarshal(b, &task)
					if err := jobSvc.ProcessExport(ctx, task); err != nil {
						log.Printf("export job %s failed: %v", task.JobId, err)
					}
				} else {
					var task service.ImportTask
					b, _ := json.Marshal(raw)
					json.Unmarshal(b, &task)
					if err := jobSvc.ProcessImport(ctx, task); err != nil {
						log.Printf("import job %s failed: %v", task.JobId, err)
					}
				}
			}
		}
	}()

	// Graceful Shutdown
	stop := make(chan os.Signal, 1)
	signal.Notify(stop, os.Interrupt, syscall.SIGTERM)
	<-stop
	log.Println("Shutting down worker...")
	cancel()
	_ = chConn.Close()
	pgPool.Close()
	_ = rdb.Close()
}
