package service

import (
	"context"
	"fmt"
	"log"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/redis/go-redis/v9"
	"golang.org/x/sync/singleflight"
)

// MonthlyEventCounter counts the events stored in ClickHouse for a month. The quota only asks it when a
// site's counter does not exist yet, and during reconciliation; never once per event.
type MonthlyEventCounter interface {
	// CountMonthlyEvents returns the events of each site with a date in the month of monthStart (UTC).
	// Sites without events are left out of the map.
	CountMonthlyEvents(ctx context.Context, siteIDs []string, monthStart time.Time) (map[string]int64, error)
}

// QuotaDecision says what the quota check did with an event.
type QuotaDecision int

const (
	// QuotaAllowed: the counter was checked and the event fits in the month.
	QuotaAllowed QuotaDecision = iota
	// QuotaUnchecked: the quota could not be checked (no Redis, Redis or ClickHouse failing) and the
	// event was let through (fail-open).
	QuotaUnchecked
	// QuotaBlocked: the site is over its monthly limit.
	QuotaBlocked
)

func (d QuotaDecision) String() string {
	switch d {
	case QuotaAllowed:
		return "checked"
	case QuotaUnchecked:
		return "unchecked"
	case QuotaBlocked:
		return "blocked"
	}
	return "unknown"
}

// Defaults of QuotaCounter.
const (
	defaultQuotaCounterTTL     = 40 * 24 * time.Hour
	defaultQuotaBlockDuration  = 5 * time.Minute
	defaultQuotaInitBackoff    = 30 * time.Second
	defaultQuotaInitTimeout    = 5 * time.Second
	defaultQuotaReconcileEvery = 5 * time.Minute
	quotaWarnEvery             = time.Minute
)

// quotaCheckScript checks the block flag and the counter and, if the events fit, adds them, in one round trip.
//
//	KEYS[1] counter quota:{site}:{yyyymm}   KEYS[2] block flag quota:{site}:limit_reached
//	ARGV[1] events to add   ARGV[2] monthly limit   ARGV[3] block duration in seconds
//
// Returns {1, new value} when accepted, {0, current} when over the limit (and sets the block flag),
// {0, -1} when the site is already blocked, and {-1, 0} when the counter does not exist yet.
var quotaCheckScript = redis.NewScript(`
if redis.call('EXISTS', KEYS[2]) == 1 then return {0, -1} end
local cur = redis.call('GET', KEYS[1])
if not cur then return {-1, 0} end
cur = tonumber(cur)
local n = tonumber(ARGV[1])
if cur + n > tonumber(ARGV[2]) then
  redis.call('SET', KEYS[2], '1', 'EX', tonumber(ARGV[3]))
  return {0, cur}
end
return {1, redis.call('INCRBY', KEYS[1], n)}
`)

// quotaReconcileScript replaces the counter with the ClickHouse count, keeping the events counted while
// ClickHouse was being asked: new = ARGV[1] (ClickHouse) + (current - ARGV[2] (value before asking)).
// Leaves the counter alone if it disappeared meanwhile (expired); the next event initialises it again.
var quotaReconcileScript = redis.NewScript(`
local cur = redis.call('GET', KEYS[1])
if not cur then return -1 end
local delta = tonumber(cur) - tonumber(ARGV[2])
if delta < 0 then delta = 0 end
local v = tonumber(ARGV[1]) + delta
redis.call('SET', KEYS[1], v, 'KEEPTTL')
return v
`)

// QuotaCounter keeps the events accepted per site and month in Redis (quota:{site}:{yyyymm}), so the
// monthly quota is checked without asking ClickHouse for every event. A counter is initialised once from
// ClickHouse (SET NX) and reconciled with it periodically to correct drift.
type QuotaCounter struct {
	redis   *redis.Client
	counter MonthlyEventCounter

	TTL            time.Duration // counter TTL; covers the month plus margin
	BlockDuration  time.Duration // how long a site stays blocked after going over its limit
	InitBackoff    time.Duration // after ClickHouse fails to initialise a counter, how long to skip retrying
	InitTimeout    time.Duration // limit of the ClickHouse count that initialises a counter
	ReconcileEvery time.Duration

	now func() time.Time

	initGroup   singleflight.Group
	initFailMu  sync.Mutex
	initFailed  map[string]time.Time
	lastWarning atomic.Int64
}

// NewQuotaCounter returns a counter. With a nil Redis client every check is QuotaUnchecked.
func NewQuotaCounter(rdb *redis.Client, counter MonthlyEventCounter) *QuotaCounter {
	return &QuotaCounter{
		redis:          rdb,
		counter:        counter,
		TTL:            defaultQuotaCounterTTL,
		BlockDuration:  defaultQuotaBlockDuration,
		InitBackoff:    defaultQuotaInitBackoff,
		InitTimeout:    defaultQuotaInitTimeout,
		ReconcileEvery: defaultQuotaReconcileEvery,
		now:            time.Now,
		initFailed:     make(map[string]time.Time),
	}
}

func monthStart(t time.Time) time.Time {
	t = t.UTC()
	return time.Date(t.Year(), t.Month(), 1, 0, 0, 0, 0, time.UTC)
}

func quotaCounterKey(siteID string, month time.Time) string {
	return fmt.Sprintf("quota:%s:%s", siteID, month.Format("200601"))
}

func quotaBlockKey(siteID string) string {
	return fmt.Sprintf("quota:%s:limit_reached", siteID)
}

// Check counts n events for the site against its monthly limit. It only returns an error together with
// QuotaUnchecked: the caller lets the event through (fail-open).
func (q *QuotaCounter) Check(ctx context.Context, siteID string, limit int64, n int64) (QuotaDecision, error) {
	if q == nil || q.redis == nil {
		return QuotaUnchecked, nil
	}
	month := monthStart(q.now())
	key := quotaCounterKey(siteID, month)
	blockKey := quotaBlockKey(siteID)

	decision, initialised, err := q.runCheck(ctx, key, blockKey, limit, n)
	if err != nil {
		return QuotaUnchecked, q.warn(err)
	}
	if initialised {
		return decision, nil
	}

	// First event of the month for this site (or the counter expired): initialise it from ClickHouse once.
	if err := q.initialise(ctx, siteID, month, key); err != nil {
		return QuotaUnchecked, q.warn(err)
	}
	decision, initialised, err = q.runCheck(ctx, key, blockKey, limit, n)
	if err != nil {
		return QuotaUnchecked, q.warn(err)
	}
	if !initialised {
		return QuotaUnchecked, q.warn(fmt.Errorf("quota counter %s missing right after initialising it", key))
	}
	return decision, nil
}

func (q *QuotaCounter) runCheck(ctx context.Context, key, blockKey string, limit, n int64) (QuotaDecision, bool, error) {
	blockSeconds := int64(q.BlockDuration / time.Second)
	if blockSeconds < 1 {
		blockSeconds = 1
	}
	res, err := quotaCheckScript.Run(ctx, q.redis, []string{key, blockKey}, n, limit, blockSeconds).Int64Slice()
	if err != nil {
		return QuotaUnchecked, false, fmt.Errorf("quota check in Redis: %w", err)
	}
	if len(res) != 2 {
		return QuotaUnchecked, false, fmt.Errorf("quota check in Redis: unexpected reply %v", res)
	}
	switch res[0] {
	case 1:
		return QuotaAllowed, true, nil
	case 0:
		return QuotaBlocked, true, nil
	default:
		return QuotaUnchecked, false, nil
	}
}

// initialise sets the counter to the ClickHouse count with SET NX. Concurrent requests for the same site
// share one ClickHouse query, and after a failure the site is not retried for InitBackoff, so a ClickHouse
// outage does not turn into one query per event. The query runs detached from the request with InitTimeout:
// if the request gives up first (its dependency timeout), the event goes through unchecked and the counter
// is still ready for the next ones.
func (q *QuotaCounter) initialise(ctx context.Context, siteID string, month time.Time, key string) error {
	q.initFailMu.Lock()
	failedAt, failed := q.initFailed[key]
	q.initFailMu.Unlock()
	if failed && q.now().Sub(failedAt) < q.InitBackoff {
		return fmt.Errorf("quota counter %s not initialised: ClickHouse failed less than %s ago", key, q.InitBackoff)
	}

	ch := q.initGroup.DoChan(key, func() (any, error) {
		initCtx, cancel := context.WithTimeout(context.WithoutCancel(ctx), q.InitTimeout)
		defer cancel()
		counts, err := q.counter.CountMonthlyEvents(initCtx, []string{siteID}, month)
		if err != nil {
			q.initFailMu.Lock()
			q.initFailed[key] = q.now()
			q.initFailMu.Unlock()
			return nil, fmt.Errorf("initialising quota counter %s from ClickHouse: %w", key, err)
		}
		q.initFailMu.Lock()
		delete(q.initFailed, key)
		q.initFailMu.Unlock()
		if err := q.redis.SetNX(initCtx, key, counts[siteID], q.TTL).Err(); err != nil {
			return nil, fmt.Errorf("initialising quota counter %s in Redis: %w", key, err)
		}
		return nil, nil
	})
	select {
	case res := <-ch:
		return res.Err
	case <-ctx.Done():
		return fmt.Errorf("initialising quota counter %s: %w", key, ctx.Err())
	}
}

// Refund takes back n events counted by Check for events that were not accepted after all (for example,
// the buffer was full). Best effort.
func (q *QuotaCounter) Refund(ctx context.Context, siteID string, n int64) {
	if q == nil || q.redis == nil {
		return
	}
	key := quotaCounterKey(siteID, monthStart(q.now()))
	if err := q.redis.DecrBy(ctx, key, n).Err(); err != nil {
		q.warn(fmt.Errorf("refunding quota counter %s: %w", key, err))
	}
}

// Reconcile sets every counter of the current month to the ClickHouse count, with one ClickHouse query for
// all sites. Only one ingestion instance reconciles per interval (Redis lock).
func (q *QuotaCounter) Reconcile(ctx context.Context) (int, error) {
	if q == nil || q.redis == nil {
		return 0, nil
	}
	lockTTL := q.ReconcileEvery - time.Second
	if lockTTL < time.Second {
		lockTTL = time.Second
	}
	got, err := q.redis.SetNX(ctx, "quota:reconcile:lock", "1", lockTTL).Result()
	if err != nil {
		return 0, fmt.Errorf("quota reconcile lock: %w", err)
	}
	if !got {
		return 0, nil // another instance reconciled in this interval
	}

	month := monthStart(q.now())
	suffix := ":" + month.Format("200601")
	var keys []string
	iter := q.redis.Scan(ctx, 0, "quota:*"+suffix, 500).Iterator()
	for iter.Next(ctx) {
		keys = append(keys, iter.Val())
	}
	if err := iter.Err(); err != nil {
		return 0, fmt.Errorf("listing quota counters: %w", err)
	}
	if len(keys) == 0 {
		return 0, nil
	}

	// Snapshot before asking ClickHouse, so the events counted meanwhile are kept.
	before, err := q.redis.MGet(ctx, keys...).Result()
	if err != nil {
		return 0, fmt.Errorf("reading quota counters: %w", err)
	}
	siteIDs := make([]string, 0, len(keys))
	snapshot := make(map[string]string, len(keys))
	for i, key := range keys {
		siteID := strings.TrimSuffix(strings.TrimPrefix(key, "quota:"), suffix)
		siteIDs = append(siteIDs, siteID)
		if s, ok := before[i].(string); ok {
			snapshot[key] = s
		} else {
			snapshot[key] = "0"
		}
	}

	counts, err := q.counter.CountMonthlyEvents(ctx, siteIDs, month)
	if err != nil {
		return 0, fmt.Errorf("counting monthly events in ClickHouse: %w", err)
	}

	reconciled := 0
	for i, key := range keys {
		if err := quotaReconcileScript.Run(ctx, q.redis, []string{key}, counts[siteIDs[i]], snapshot[key]).Err(); err != nil {
			return reconciled, fmt.Errorf("reconciling %s: %w", key, err)
		}
		reconciled++
	}
	return reconciled, nil
}

// RunReconciler reconciles every ReconcileEvery until ctx is done.
func (q *QuotaCounter) RunReconciler(ctx context.Context) {
	if q == nil || q.redis == nil || q.ReconcileEvery <= 0 {
		return
	}
	ticker := time.NewTicker(q.ReconcileEvery)
	defer ticker.Stop()
	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			rctx, cancel := context.WithTimeout(ctx, q.ReconcileEvery/2)
			n, err := q.Reconcile(rctx)
			cancel()
			if err != nil {
				log.Printf("⚠️ Quota reconciliation failed: %v", err)
			} else if n > 0 {
				log.Printf("Quota reconciliation: %d counters set from ClickHouse", n)
			}
		}
	}
}

// warn logs a quota problem at most once per minute and returns the error unchanged.
func (q *QuotaCounter) warn(err error) error {
	if err == nil {
		return nil
	}
	now := q.now().UnixNano()
	last := q.lastWarning.Load()
	if now-last >= int64(quotaWarnEvery) && q.lastWarning.CompareAndSwap(last, now) {
		log.Printf("⚠️ Quota not checked, accepting events (fail-open): %v", err)
	}
	return err
}

// BlockedFor returns how long the site stays blocked for being over its quota (the TTL of the block flag),
// or BlockDuration if Redis does not say.
func (q *QuotaCounter) BlockedFor(ctx context.Context, siteID string) time.Duration {
	if q == nil || q.redis == nil {
		return defaultQuotaBlockDuration
	}
	ttl, err := q.redis.PTTL(ctx, quotaBlockKey(siteID)).Result()
	if err != nil || ttl <= 0 {
		return q.BlockDuration
	}
	return ttl
}
