package repository

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"math"
	"sync"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/redis/go-redis/v9"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/trace"
	"golang.org/x/sync/singleflight"
)

// ErrSiteNotFound: no site has that site key or id.
var ErrSiteNotFound = errors.New("site not found")

// siteQuerier is the part of pgxpool.Pool the site lookup uses; tests replace it.
type siteQuerier interface {
	QueryRow(ctx context.Context, sql string, args ...any) pgx.Row
}

// Defaults of the in-process site config cache.
const (
	// DefaultSiteMemoryTTL: how long a config is served from memory without asking Redis or Postgres.
	DefaultSiteMemoryTTL = 30 * time.Second
	// DefaultSiteStaleTTL: how long an expired config can still be served when Redis and Postgres fail.
	DefaultSiteStaleTTL  = 10 * time.Minute
	siteMemoryMaxEntries = 10000
	siteRedisTTL         = 5 * time.Minute

	defaultSiteLookupTimeout = 2 * time.Second
	// defaultSitePreloadLimit: sites loaded at startup and by the background refresh. Two memory entries each,
	// so it stays well under siteMemoryMaxEntries. Sites past the cap are resolved by the normal lookup.
	defaultSitePreloadLimit = 4000
)

type memorySiteEntry struct {
	config   *SiteConfig
	storedAt time.Time
}

type SiteConfig struct {
	ID               string    `json:"id"`
	WriteKey         string    `json:"write_key"`
	AllowedOrigins   []string  `json:"allowed_origins"`
	MaxMonthlyEvents int64     `json:"max_monthly_events"`
	OwnerID          string    `json:"owner_id"`
	UserCreatedAt    time.Time `json:"user_created_at"`
	Plan             string    `json:"plan"`
	// HasAdvancedAnalytics gates heatmap ingestion (add-heatmaps).
	HasAdvancedAnalytics bool `json:"has_advanced_analytics"`
	// HeatmapMonthlyClicks caps the heatmap records a site can store per month: the plan's monthly events.
	HeatmapMonthlyClicks int64 `json:"heatmap_monthly_clicks"`
}

// SiteRepository resolves a site's config: memory (MemoryTTL), then Redis (5 min), then Postgres. When Redis
// and Postgres fail or time out, it serves the last config it had in memory for up to StaleTTL.
type SiteRepository struct {
	db    siteQuerier
	pool  *pgxpool.Pool
	redis *redis.Client

	MemoryTTL     time.Duration
	StaleTTL      time.Duration
	LookupTimeout time.Duration // limit of the shared Redis/Postgres lookup, detached from the requests

	// Unlimited switches the plan limits off (self-hosted/community: there are no plans): no trial expiry, no monthly
	// event quota, heatmaps allowed. Off by default, which is the cloud behaviour (the plan comes from billing.*).
	Unlimited bool

	// PreloadLimit caps how many sites Preload loads (each site takes two memory entries: site key and id).
	PreloadLimit int
	// listSites loads up to limit sites straight from Postgres; tests replace it.
	listSites func(ctx context.Context, limit int) ([]preloadedSite, error)

	lookups singleflight.Group

	mu     sync.RWMutex
	memory map[string]memorySiteEntry
	now    func() time.Time
}

// CommunityPlan is the plan slug a site gets when the plan limits are off. It is not "free", so the trial check does not apply.
const CommunityPlan = "community"

// applyUnlimitedPlan replaces what the (absent) subscription said with "no limits": the community edition has no plans.
func applyUnlimitedPlan(c *SiteConfig) {
	c.Plan = CommunityPlan
	c.MaxMonthlyEvents = math.MaxInt64
	c.HasAdvancedAnalytics = true
	c.HeatmapMonthlyClicks = math.MaxInt64
}

func NewSiteRepository(db *pgxpool.Pool, redis *redis.Client) *SiteRepository {
	r := newSiteRepository(db, redis)
	r.pool = db
	r.listSites = r.listSitesFromPostgres
	return r
}

func newSiteRepository(db siteQuerier, redis *redis.Client) *SiteRepository {
	return &SiteRepository{
		db:        db,
		redis:     redis,
		MemoryTTL: DefaultSiteMemoryTTL,
		StaleTTL:  DefaultSiteStaleTTL,

		LookupTimeout: defaultSiteLookupTimeout,
		PreloadLimit:  defaultSitePreloadLimit,
		memory:        make(map[string]memorySiteEntry),
		now:           time.Now,
	}
}

func (r *SiteRepository) fromMemory(siteKey string, maxAge time.Duration) *SiteConfig {
	r.mu.RLock()
	entry, ok := r.memory[siteKey]
	r.mu.RUnlock()
	if !ok || maxAge <= 0 || r.now().Sub(entry.storedAt) > maxAge {
		return nil
	}
	return entry.config
}

func (r *SiteRepository) remember(siteKey string, config *SiteConfig) {
	if r.MemoryTTL <= 0 && r.StaleTTL <= 0 {
		return
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	if len(r.memory) >= siteMemoryMaxEntries {
		// Bounded: drop the oldest entries past the stale window, or everything if none expired.
		cutoff := r.now().Add(-r.StaleTTL)
		for k, e := range r.memory {
			if e.storedAt.Before(cutoff) {
				delete(r.memory, k)
			}
		}
		if len(r.memory) >= siteMemoryMaxEntries {
			r.memory = make(map[string]memorySiteEntry)
		}
	}
	r.memory[siteKey] = memorySiteEntry{config: config, storedAt: r.now()}
}

// GetSiteConfig returns the site's config. Errors: ErrSiteNotFound when the site does not exist, any other
// error when it could not be looked up (Redis and Postgres failing or past ctx's deadline) and no copy in
// memory is recent enough to serve instead.
func (r *SiteRepository) GetSiteConfig(ctx context.Context, siteKey string, writeKey string) (*SiteConfig, error) {
	span := trace.SpanFromContext(ctx)
	if config := r.fromMemory(siteKey, r.MemoryTTL); config != nil {
		span.SetAttributes(attribute.String(siteConfigSourceAttr, "memory"))
		return config, nil
	}
	// Stale-while-revalidate: an expired copy (within StaleTTL) is served at once and refreshed in the
	// background, so a request never waits for Redis/Postgres when it has a copy to answer with.
	if stale := r.fromMemory(siteKey, r.StaleTTL); stale != nil {
		span.SetAttributes(attribute.String(siteConfigSourceAttr, "memory_stale"))
		r.refreshInBackground(siteKey)
		return stale, nil
	}
	config, source, err := r.sharedLookup(ctx, siteKey)
	if err == nil {
		span.SetAttributes(attribute.String(siteConfigSourceAttr, source))
		return config, nil
	}
	return nil, err
}

// siteConfigSourceAttr says where the site config came from: memory, redis, postgres or memory_stale (Redis
// and Postgres failed, served the last copy).
const siteConfigSourceAttr = "tempoo.site_config.source"

type lookupResult struct {
	config *SiteConfig
	source string
}

// sharedLookup runs one Redis/Postgres lookup per site at a time, shared by every request that needs it
// (after a restart or a cache expiry, hundreds of requests would otherwise each query Postgres and queue
// on its pool). The lookup is detached from the request with its own LookupTimeout, so it completes and
// fills the memory cache even if the request that started it stops waiting; each request waits only
// until its own deadline.
func (r *SiteRepository) sharedLookup(ctx context.Context, siteKey string) (*SiteConfig, string, error) {
	// Redis gets half of the time the first request has left, so a stuck Redis still leaves that request
	// time to get the config from Postgres.
	redisBudget := r.LookupTimeout / 2
	if deadline, ok := ctx.Deadline(); ok {
		redisBudget = time.Until(deadline) / 2
	}
	ch := r.lookups.DoChan(siteKey, func() (any, error) {
		lctx, cancel := context.WithTimeout(context.WithoutCancel(ctx), r.LookupTimeout)
		defer cancel()
		config, source, err := r.lookup(lctx, siteKey, redisBudget)
		if err == nil {
			r.remember(siteKey, config)
		}
		return lookupResult{config, source}, err
	})
	select {
	case res := <-ch:
		if res.Err != nil {
			return nil, "", res.Err
		}
		lr := res.Val.(lookupResult)
		return lr.config, lr.source, nil
	case <-ctx.Done():
		return nil, "", fmt.Errorf("site lookup: %w", ctx.Err())
	}
}

// siteSelect is the site query shared by the single lookup (WHERE) and Preload (ORDER BY ... LIMIT). The
// last column is the site key, which Preload needs to index the config.
const siteSelect = `
		SELECT s.id, s.write_key, s.allowed_origins, s.owner_id,
		       COALESCE((sub.features_json::jsonb->>'MaxMonthlyEvents')::bigint, 10000) as max_events,
		       CASE WHEN u.created_at = '-infinity' THEN '1970-01-01'::timestamptz ELSE u.created_at END as created_at, sub.plan,
		       COALESCE(tier.has_advanced_analytics, false) as has_advanced_analytics,
		       COALESCE(tier.max_monthly_events, 10000) as heatmap_monthly_clicks,
		       s.site_key
		FROM analytics_sites s
		INNER JOIN analytics_users u ON s.owner_id = u.id
		LEFT JOIN billing.analytics_subscriptions sub ON s.owner_id = sub.user_id
		LEFT JOIN billing.analytics_subscription_tiers tier ON tier.slug = sub.plan`

// siteSelectCommunity is siteSelect without the billing schema, which the community edition does not have (its tables belong to the
// cloud billing module): same columns, plan fields as constants. applyUnlimitedPlan overrides them anyway.
const siteSelectCommunity = `
		SELECT s.id, s.write_key, s.allowed_origins, s.owner_id,
		       10000::bigint as max_events,
		       CASE WHEN u.created_at = '-infinity' THEN '1970-01-01'::timestamptz ELSE u.created_at END as created_at, NULL::text as plan,
		       false as has_advanced_analytics,
		       10000::bigint as heatmap_monthly_clicks,
		       s.site_key
		FROM analytics_sites s
		INNER JOIN analytics_users u ON s.owner_id = u.id`

// selectSites returns the site query for the edition: with the billing joins (cloud) or without them (Unlimited, community).
func (r *SiteRepository) selectSites() string {
	if r.Unlimited {
		return siteSelectCommunity
	}
	return siteSelect
}

type siteRowFields struct {
	id, writeKey, ownerID string
	allowedOrigins        []string
	maxEvents             int64
	userCreatedAt         time.Time
	plan                  *string
	hasAdvancedAnalytics  bool
	heatmapMonthlyClicks  int64
}

func (f *siteRowFields) config() *SiteConfig {
	planStr := "free"
	if f.plan != nil {
		planStr = *f.plan
	}
	return &SiteConfig{
		ID:               f.id,
		WriteKey:         f.writeKey,
		AllowedOrigins:   f.allowedOrigins,
		MaxMonthlyEvents: f.maxEvents,
		OwnerID:          f.ownerID,
		UserCreatedAt:    f.userCreatedAt,
		Plan:             planStr,

		HasAdvancedAnalytics: f.hasAdvancedAnalytics,
		HeatmapMonthlyClicks: f.heatmapMonthlyClicks,
	}
}

func (r *SiteRepository) lookup(ctx context.Context, siteKey string, redisBudget time.Duration) (*SiteConfig, string, error) {
	// Cache by siteKey only. The service will check the writeKey and Origin against the cached config.
	// v3: SiteConfig gained the plan's heatmap fields, so entries cached before that are not read.
	cacheKey := fmt.Sprintf("site:%s:config:v3", siteKey)

	// 1. Try Cache, within redisBudget.
	if r.redis != nil {
		redisCtx, cancel := context.WithTimeout(ctx, redisBudget)
		val, err := r.redis.Get(redisCtx, cacheKey).Result()
		cancel()
		if err == nil {
			var config SiteConfig
			if err := json.Unmarshal([]byte(val), &config); err == nil {
				if r.Unlimited {
					applyUnlimitedPlan(&config)
				}
				return &config, "redis", nil // Cache hit
			}
		}
	}

	// 2. Query DB
	// Fetch ID, write_key and allowed_origins from Postgres, join with subscriptions and users
	var f siteRowFields
	var siteKeyCol *string // selected for Preload, not needed here
	err := r.db.QueryRow(ctx, r.selectSites()+` WHERE s.site_key = $1 OR s.id::text = $1`, siteKey).Scan(&f.id, &f.writeKey, &f.allowedOrigins, &f.ownerID, &f.maxEvents, &f.userCreatedAt, &f.plan, &f.hasAdvancedAnalytics, &f.heatmapMonthlyClicks, &siteKeyCol)

	if errors.Is(err, pgx.ErrNoRows) {
		return nil, "", ErrSiteNotFound
	}
	if err != nil {
		return nil, "", fmt.Errorf("site lookup in Postgres: %w", err)
	}

	config := f.config()
	if r.Unlimited {
		applyUnlimitedPlan(config)
	}

	// 3. Set Cache, off the request path: the request may be close to its deadline.
	if r.redis != nil {
		data, _ := json.Marshal(config)
		go func() {
			setCtx, cancel := context.WithTimeout(context.Background(), time.Second)
			defer cancel()
			if err := r.redis.Set(setCtx, cacheKey, data, siteRedisTTL).Err(); err != nil {
				log.Printf("Warning: caching site config %s in Redis: %v", cacheKey, err)
			}
		}()
	}

	return config, "postgres", nil
}

// refreshInBackground refreshes one site's config off the request path. One refresh per site at a time (it
// shares the singleflight group with sharedLookup). A failed refresh keeps the stale copy; a site that no
// longer exists is dropped from memory.
func (r *SiteRepository) refreshInBackground(siteKey string) {
	ch := r.lookups.DoChan(siteKey, func() (any, error) {
		lctx, cancel := context.WithTimeout(context.Background(), r.LookupTimeout)
		defer cancel()
		config, source, err := r.lookup(lctx, siteKey, r.LookupTimeout/2)
		if err == nil {
			r.remember(siteKey, config)
		}
		return lookupResult{config, source}, err
	})
	go func() {
		res := <-ch
		switch {
		case res.Err == nil:
		case errors.Is(res.Err, ErrSiteNotFound):
			r.mu.Lock()
			delete(r.memory, siteKey)
			r.mu.Unlock()
		default:
			log.Printf("Warning: refreshing site config %s, serving the stale copy: %v", siteKey, res.Err)
		}
	}()
}

// preloadedSite is a site as read by Preload: its public site key plus its config.
type preloadedSite struct {
	siteKey string
	config  *SiteConfig
}

// Preload loads the configs of up to PreloadLimit sites straight from Postgres into memory (under both the
// site key and the id, the two keys a request can use), so the request path almost never waits for a lookup.
// On error the memory is left untouched. When the whole table fit under the limit, entries of sites that
// no longer exist are dropped. It returns the number of sites loaded.
func (r *SiteRepository) Preload(ctx context.Context) (int, error) {
	if r.listSites == nil || r.PreloadLimit <= 0 {
		return 0, nil
	}
	sites, err := r.listSites(ctx, r.PreloadLimit)
	if err != nil {
		return 0, err
	}
	for _, s := range sites {
		if r.Unlimited {
			applyUnlimitedPlan(s.config)
		}
		r.remember(s.config.ID, s.config)
		if s.siteKey != "" && s.siteKey != s.config.ID {
			r.remember(s.siteKey, s.config)
		}
	}
	if len(sites) < r.PreloadLimit {
		known := make(map[string]struct{}, len(sites)*2)
		for _, s := range sites {
			known[s.config.ID] = struct{}{}
			known[s.siteKey] = struct{}{}
		}
		r.mu.Lock()
		for k := range r.memory {
			if _, ok := known[k]; !ok {
				delete(r.memory, k)
			}
		}
		r.mu.Unlock()
	}
	return len(sites), nil
}

// RunPreloader refreshes every site's config every interval until ctx ends, so entries never expire while
// the service runs and the Postgres connection stays warm. interval <= 0 disables it. The first Preload is
// the caller's (it is worth waiting for at startup).
func (r *SiteRepository) RunPreloader(ctx context.Context, interval time.Duration) {
	if interval <= 0 {
		return
	}
	t := time.NewTicker(interval)
	defer t.Stop()
	for {
		select {
		case <-ctx.Done():
			return
		case <-t.C:
			pctx, cancel := context.WithTimeout(ctx, r.LookupTimeout)
			if _, err := r.Preload(pctx); err != nil {
				log.Printf("Warning: refreshing the site configs, keeping the cached ones: %v", err)
			}
			cancel()
		}
	}
}

func (r *SiteRepository) listSitesFromPostgres(ctx context.Context, limit int) ([]preloadedSite, error) {
	if r.pool == nil {
		return nil, errors.New("no Postgres pool")
	}
	rows, err := r.pool.Query(ctx, r.selectSites()+` ORDER BY s.id LIMIT $1`, limit)
	if err != nil {
		return nil, fmt.Errorf("listing sites in Postgres: %w", err)
	}
	defer rows.Close()
	var out []preloadedSite
	for rows.Next() {
		var f siteRowFields
		var siteKey *string
		if err := rows.Scan(&f.id, &f.writeKey, &f.allowedOrigins, &f.ownerID, &f.maxEvents, &f.userCreatedAt, &f.plan, &f.hasAdvancedAnalytics, &f.heatmapMonthlyClicks, &siteKey); err != nil {
			return nil, fmt.Errorf("reading a site row: %w", err)
		}
		ps := preloadedSite{config: f.config()}
		if siteKey != nil {
			ps.siteKey = *siteKey
		}
		out = append(out, ps)
	}
	if err := rows.Err(); err != nil {
		return nil, fmt.Errorf("listing sites in Postgres: %w", err)
	}
	return out, nil
}

func (r *SiteRepository) Close() {
	if r.pool != nil {
		r.pool.Close()
	}
}
