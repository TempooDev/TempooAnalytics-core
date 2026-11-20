package main

import (
	"context"
	"fmt"
	"log"
	"net/http"
	"os"
	"os/signal"
	"strconv"
	"strings"
	"syscall"
	"time"

	"github.com/ClickHouse/clickhouse-go/v2"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/config"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/handler"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/metrics"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/repository"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/service"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/telemetry"
	"github.com/exaring/otelpgx"
	"github.com/gin-gonic/gin"
	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/oschwald/geoip2-golang"
	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/geoip"
	"github.com/redis/go-redis/v9"
	"github.com/prometheus/client_golang/prometheus"
	"go.opentelemetry.io/contrib/instrumentation/github.com/gin-gonic/gin/otelgin"
)

func main() {
	// Configuration
	serviceName := "ingestion-service"

	// 1. Initialize Telemetry
	// OTEL_SERVICE_NAME overrides serviceName. The Prometheus metrics of /metrics are also pushed over OTLP
	// (OTEL_EXPORTER_OTLP_METRICS_ENDPOINT), for environments where nothing scrapes this service.
	shutdown, err := telemetry.InitTelemetry(serviceName, telemetry.WithPrometheusGatherer(prometheus.DefaultGatherer))
	if err != nil {
		log.Printf("Warning: Failed to initialize telemetry (continuing without it): %v", err)
	} else {
		defer shutdown(context.Background())
	}

	clickHouseHost := config.GetEnv("CLICKHOUSE_HOST", "localhost")
	clickHousePort := config.GetEnv("CLICKHOUSE_PORT", "9000")
	clickHouseAddr := config.GetEnv("CLICKHOUSE_ADDR", fmt.Sprintf("%s:%s", clickHouseHost, clickHousePort))
	clickHouseUser := config.GetEnv("CLICKHOUSE_USER", "default")
	clickHousePassword := config.GetEnv("CLICKHOUSE_PASSWORD", "")
	clickHouseDB := config.GetEnv("CLICKHOUSE_DB", "tempoo_analytics")

	redisConnStr := config.GetEnv("ConnectionStrings__redis", config.GetEnv("REDIS_ADDR", "localhost:6379"))
	postgresConnString := config.GetEnv("ConnectionStrings__analytics-db", config.GetEnv("POSTGRES_CONN_STRING", "postgres://user:password@localhost:5432/tempoo_analytics"))
	
	log.Printf("Postgres Connection String: %s", config.RedactSecrets(postgresConnString))
	log.Printf("Redis Connection String: %s", config.RedactSecrets(redisConnStr))

	geoIpCityPath := config.GetEnv("GEOIP_CITY_DB_PATH", "Data/GeoLite2-Country.mmdb")
	geoIpAsnPath := config.GetEnv("GEOIP_ASN_DB_PATH", "Data/GeoLite2-ASN.mmdb")
	port := config.GetEnv("PORT", "8081")

	// 2. Initialize ClickHouse Connection
	conn, err := clickhouse.Open(&clickhouse.Options{
		Addr: []string{clickHouseAddr},
		Auth: clickhouse.Auth{
			Database: clickHouseDB,
			Username: clickHouseUser,
			Password: clickHousePassword,
		},
		ClientInfo: clickhouse.ClientInfo{
			Products: []struct {
				Name    string
				Version string
			}{
				{Name: "tempoo-ingestion", Version: "1.0"},
			},
		},
		Debug: false,
	})
	if err != nil {
		log.Fatalf("Failed to open ClickHouse connection: %v", err)
	}
	if err := conn.Ping(context.Background()); err != nil {
		log.Fatalf("Failed to ping ClickHouse: %v", err)
	}
	log.Println("Connected to ClickHouse")

	// 3. Initialize Redis
	var rdb *redis.Client
	if redisConnStr != "" {
		opts, err := config.ParseRedisOptions(redisConnStr)
		if err != nil {
			log.Printf("Warning: Failed to parse Redis connection string: %v", err)
		} else {
			rdb = redis.NewClient(opts)
			if _, err := rdb.Ping(context.Background()).Result(); err != nil {
				log.Printf("Warning: Failed to connect to Redis at %s: %v", opts.Addr, err)
			} else {
				log.Println("Connected to Redis")
			}
		}
	}

	// 4. Initialize PostgreSQL
	log.Println("Connecting to PostgreSQL...")
	
	var cleanDSN string
	if strings.HasPrefix(postgresConnString, "postgres://") || strings.HasPrefix(postgresConnString, "postgresql://") {
		// It's already a valid DSN URL, use it directly
		cleanDSN = postgresConnString
	} else if strings.Contains(postgresConnString, ";") {
		// Convert .NET connection string (semicolon separated) to Postgres DSN (space separated)
		parts := strings.Split(postgresConnString, ";")
		for _, part := range parts {
			kv := strings.SplitN(part, "=", 2)
			if len(kv) != 2 { continue }
			key := strings.ToLower(strings.TrimSpace(kv[0]))
			val := kv[1] 
			
			switch key {
			case "host", "server": cleanDSN += fmt.Sprintf("host=%s ", val)
			case "port": cleanDSN += fmt.Sprintf("port=%s ", val)
			case "username", "user id", "user": cleanDSN += fmt.Sprintf("user=%s ", val)
			case "password": cleanDSN += fmt.Sprintf("password=%s ", val)
			case "database", "initial catalog": cleanDSN += fmt.Sprintf("dbname=%s ", val)
			}
		}
	} else {
		// Fallback to original string
		cleanDSN = postgresConnString
	}

	pgConfig, err := pgxpool.ParseConfig(cleanDSN)
	if err != nil {
		log.Fatalf("Unable to parse postgres connection string: %v. Original: %s", err, postgresConnString)
	}
	pgConfig.ConnConfig.Tracer = otelpgx.NewTracer()
	pgPool, err := pgxpool.NewWithConfig(context.Background(), pgConfig)
	if err != nil {
		log.Fatalf("Unable to create postgres pool: %v", err)
	}
	
	// Verify connection
	if err := pgPool.Ping(context.Background()); err != nil {
		log.Fatalf("Unable to connect to PostgreSQL: %v. DSN used: %s", err, cleanDSN)
	}
	log.Println("Connected to PostgreSQL")

	// 5. Initialize GeoIP and ASN. Missing files are downloaded when MAXMIND_ACCOUNT_ID / MAXMIND_LICENSE_KEY
	// are set (self-host); otherwise geolocation is disabled below, exactly as before.
	geoProvisioner := &geoip.Provisioner{
		AccountID:  config.GetEnv("MAXMIND_ACCOUNT_ID", ""),
		LicenseKey: config.GetEnv("MAXMIND_LICENSE_KEY", ""),
	}
	geoProvisioner.Ensure(context.Background(), "GeoLite2-Country", geoIpCityPath)
	geoProvisioner.Ensure(context.Background(), "GeoLite2-ASN", geoIpAsnPath)
	var geoCity *geoip2.Reader
	var geoAsn *geoip2.Reader
	if g, err := geoip2.Open(geoIpCityPath); err != nil {
		log.Printf("Warning: GeoIP City DB not found at %s. Geolocation will be disabled. Error: %v", geoIpCityPath, err)
	} else {
		geoCity = g
		log.Println("Loaded GeoIP City Database")
	}
	if a, err := geoip2.Open(geoIpAsnPath); err != nil {
		log.Printf("Warning: GeoIP ASN DB not found at %s. ASN enrichment will be disabled. Error: %v", geoIpAsnPath, err)
	} else {
		geoAsn = a
		log.Println("Loaded GeoIP ASN Database")
	}

	// 6. Initialize Components
	eventRepo := repository.NewClickHouseRepository(conn, repository.WorkerConfig{
		BatchSize:      envInt("INGEST_BATCH_SIZE", repository.BatchSize),
		FlushInterval:  envDuration("INGEST_FLUSH_INTERVAL", repository.FlushInterval),
		BufferCapacity: envInt("INGEST_BUFFER_CAPACITY", repository.BufferCapacity),
		NumWorkers:     envInt("INGEST_WORKERS", repository.NumWorkers),

		InsertRetries:       envInt("INGEST_INSERT_RETRIES", repository.InsertRetries),
		InsertTimeout:       envDuration("INGEST_INSERT_TIMEOUT", repository.InsertTimeout),
		SpillDir:            spillDir(),
		SpillReplayInterval: envDuration("INGEST_SPILL_REPLAY_INTERVAL", repository.SpillReplayInterval),
	})
	// Persistent queue in a Redis stream, off by default (INGEST_QUEUE=memory). Without Redis or with it
	// unreachable at startup, ingestion keeps working with the in-memory buffer.
	if strings.EqualFold(os.Getenv("INGEST_QUEUE"), "redis") {
		if rdb == nil {
			log.Println("WARNING: INGEST_QUEUE=redis but there is no Redis; using the in-memory buffer")
		} else if err := eventRepo.EnableStream(context.Background(), rdb, repository.StreamConfig{
			Key:       os.Getenv("INGEST_STREAM_KEY"),
			MaxLen:    int64(envInt("INGEST_STREAM_MAXLEN", repository.StreamMaxLen)),
			ClaimIdle: envDuration("INGEST_STREAM_CLAIM_IDLE", repository.StreamClaimIdle),
		}); err != nil {
			log.Printf("WARNING: Redis stream queue not available (%v); using the in-memory buffer", err)
		}
	}
	metrics.RegisterQueueDepth(eventRepo.QueueDepth, eventRepo.QueueCapacity())
	siteRepo := repository.NewSiteRepository(pgPool, rdb)
	siteRepo.MemoryTTL = envDuration("SITE_CONFIG_MEMORY_TTL", repository.DefaultSiteMemoryTTL)
	siteRepo.StaleTTL = envDuration("SITE_CONFIG_STALE_TTL", repository.DefaultSiteStaleTTL)
	// Self-hosted (community) has no plans: DISABLE_PLAN_LIMITS=true removes the 15-day trial, the monthly event quota
	// and the heatmap plan gate. Unset (cloud), the plan of each site's owner applies as always.
	siteRepo.Unlimited = config.GetEnv("DISABLE_PLAN_LIMITS", "false") == "true"
	if siteRepo.Unlimited {
		log.Printf("Plan limits disabled (DISABLE_PLAN_LIMITS=true): no trial expiry, no monthly event quota")
	}
	// Load every site's config now and keep it fresh in the background (SITE_CONFIG_PRELOAD_INTERVAL, default half of
	// the memory TTL; 0 turns the refresh off), so requests rarely wait for Redis/Postgres. A failed preload is not
	// fatal: requests fall back to the lookup.
	preloadCtx, cancelPreload := context.WithTimeout(context.Background(), 10*time.Second)
	if n, err := siteRepo.Preload(preloadCtx); err != nil {
		log.Printf("Warning: preloading site configs: %v (requests will look them up)", err)
	} else {
		log.Printf("Preloaded %d site configs", n)
	}
	cancelPreload()
	preloadInterval := envDuration("SITE_CONFIG_PRELOAD_INTERVAL", siteRepo.MemoryTTL/2)
	refreshCtx, stopRefresh := context.WithCancel(context.Background())
	defer stopRefresh()
	go siteRepo.RunPreloader(refreshCtx, preloadInterval)
	svc := service.NewIngestionService(eventRepo, siteRepo, geoCity, geoAsn, rdb)
	svc.DependencyTimeout = envDuration("INGEST_DEPENDENCY_TIMEOUT", service.DefaultDependencyTimeout)
	svc.DedupTTL = envDuration("INGEST_DEDUP_TTL", service.DefaultDedupTTL)
	log.Printf("Dependency timeout %s, site config in memory %s (stale up to %s)", svc.DependencyTimeout, siteRepo.MemoryTTL, siteRepo.StaleTTL)
	// Heatmaps ship behind a flag, off until every heatmap change is merged (same as FeatureFlags:Heatmaps in the API).
	svc.HeatmapsEnabled = config.GetEnv("FEATURE_HEATMAPS", "false") == "true"
	// Off by default: sites whose write key was revoked or rotated still send by allowed origin. Check
	// ingestion_origin_only_auth_total before turning it on.
	svc.RequireWriteKey = config.GetEnv("INGEST_REQUIRE_WRITE_KEY", "false") == "true"
	log.Printf("Write key required: %t", svc.RequireWriteKey)
	h := handler.NewHandler(svc)

	// Monthly quota: Redis counters reconciled with ClickHouse every QUOTA_RECONCILE_INTERVAL (default 5m).
	svc.Quota().ReconcileEvery = envDuration("QUOTA_RECONCILE_INTERVAL", svc.Quota().ReconcileEvery)
	reconcileCtx, stopReconcile := context.WithCancel(context.Background())
	defer stopReconcile()
	go svc.Quota().RunReconciler(reconcileCtx)

	// 7. Setup Router
	gin.SetMode(gin.ReleaseMode)
	r := gin.New()
	r.Use(otelgin.Middleware(serviceName))
	r.Use(gin.LoggerWithWriter(gin.DefaultWriter, "/analytics/health", "/metrics"))
	r.Use(gin.Recovery())
	r.Use(metrics.GinMiddleware())

	r.Use(func(c *gin.Context) {
		// Any site may send events (the tracker runs on customer sites) but never with credentials: the write key travels in the
		// URL, so there are no cookies to protect, and "Allow-Credentials" must not be offered to an arbitrary origin.
		c.Writer.Header().Set("Access-Control-Allow-Origin", "*")

		c.Writer.Header().Set("Access-Control-Allow-Methods", "POST, OPTIONS")
		c.Writer.Header().Set("Access-Control-Allow-Headers", "Content-Type, Authorization, X-API-Key")
		// Lets the tracker read when to retry after a 429 or 503.
		c.Writer.Header().Set("Access-Control-Expose-Headers", "Retry-After")
		
		if c.Request.Method == "OPTIONS" {
			c.AbortWithStatus(http.StatusNoContent)
			return
		}
		c.Next()
	})

	metricsHandler := metrics.Init()
	r.GET("/metrics", gin.WrapH(metricsHandler))
	maxInFlight := envInt("INGEST_MAX_IN_FLIGHT", 1000)
	log.Printf("Ingest requests in flight limited to %d", maxInFlight)
	r.POST("/analytics/events", handler.InFlightLimit(maxInFlight), h.HandleIngest)
	r.GET("/analytics/health", func(c *gin.Context) {
		c.JSON(http.StatusOK, gin.H{"status": "ok"})
	})

	// 8. Start Server
	// Timeouts so a slow or stuck client cannot hold a connection forever. An event body is at most 64 KB.
	srv := &http.Server{
		Addr:              ":" + port,
		Handler:           r,
		ReadHeaderTimeout: envDuration("HTTP_READ_HEADER_TIMEOUT", 5*time.Second),
		ReadTimeout:       envDuration("HTTP_READ_TIMEOUT", 10*time.Second),
		WriteTimeout:      envDuration("HTTP_WRITE_TIMEOUT", 10*time.Second),
		IdleTimeout:       envDuration("HTTP_IDLE_TIMEOUT", 60*time.Second),
	}

	serverErrors := make(chan error, 1)
	go func() {
		log.Printf("Server started on port %s", port)
		if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			serverErrors <- err
		}
	}()

	// 9. Graceful Shutdown
	quit := make(chan os.Signal, 1)
	signal.Notify(quit, os.Interrupt, syscall.SIGTERM)

	select {
	case err := <-serverErrors:
		log.Printf("Fatal error from HTTP server: %v", err)
	case sig := <-quit:
		log.Printf("Shutdown signal received: %v. Starting graceful shutdown...", sig)
	}

	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()

	log.Println("1. Stopping HTTP server...")
	if err := srv.Shutdown(ctx); err != nil {
		log.Printf("Warning: HTTP server forced shutdown after timeout: %v", err)
	}

	stopReconcile()

	log.Println("2. Flushing event buffer to ClickHouse...")
	if err := svc.Close(); err != nil {
		log.Printf("Error flushing events: %v", err)
	}

	log.Println("3. Closing database connections...")
	_ = conn.Close()
	pgPool.Close()
	if rdb != nil {
		_ = rdb.Close()
	}
	if geoCity != nil {
		_ = geoCity.Close()
	}
	if geoAsn != nil {
		_ = geoAsn.Close()
	}

	log.Println("Shutdown completed safely")
}

// spillDir is where batches that failed every INSERT retry are saved: INGEST_SPILL_DIR, "spill" (relative to
// the working directory, /app/spill in the image) by default, "off" to disable.
func spillDir() string {
	dir := config.GetEnv("INGEST_SPILL_DIR", "")
	switch dir {
	case "":
		return repository.SpillDir
	case "off", "none":
		return ""
	}
	return dir
}

// envInt reads an integer from the environment, or returns fallback.
func envInt(key string, fallback int) int {
	raw := config.GetEnv(key, "")
	if raw == "" {
		return fallback
	}
	n, err := strconv.Atoi(raw)
	if err != nil {
		log.Printf("Warning: invalid %s=%q, using %d: %v", key, raw, fallback, err)
		return fallback
	}
	return n
}

// envDuration reads a duration such as "150ms" or "5m" from the environment, or returns fallback.
func envDuration(key string, fallback time.Duration) time.Duration {
	raw := config.GetEnv(key, "")
	if raw == "" {
		return fallback
	}
	d, err := time.ParseDuration(raw)
	if err != nil {
		log.Printf("Warning: invalid %s=%q, using %s: %v", key, raw, fallback, err)
		return fallback
	}
	return d
}
