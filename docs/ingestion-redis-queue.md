# Persistent queue on Redis Streams (`INGEST_QUEUE=redis`)

Off by default (`INGEST_QUEUE=memory`, the behaviour it has always had). With `INGEST_QUEUE=redis`, accepted events are kept in a Redis stream until ClickHouse has them, instead of living only in the process memory.

## What changes

| | `memory` (today) | `redis` |
|---|---|---|
| Where events wait | in-memory channel of each instance | `tempoo:ingest:events` stream in Redis |
| If the process dies (OOM, kill, abrupt deploy) | whatever was in memory is lost (up to ~2 s of events, or the whole buffer if ClickHouse was slow) | nothing is lost: another instance (or the same one on restart) picks it up |
| Several ingestion instances | each with its own buffer | they share the work (`ingest` consumer group) |
| Response to the client | 202 once put in memory | 202 after the `XADD` (~0.2 ms on a local Redis) |
| If Redis fails or takes longer than 150 ms | not applicable | the event is stored in the in-memory buffer (counter `ingestion_stream_fallback_events_total`) and keeps being accepted |
| Limit | `INGEST_BUFFER_CAPACITY` (100,000) → 503 | `INGEST_STREAM_MAXLEN` (1,000,000) → 503 `overloaded` |

## How it works

1. `Enqueue` does an `XADD` (one field `e` with the event JSON) before responding 202.
2. Each worker (`INGEST_WORKERS`) reads with `XREADGROUP` up to `INGEST_BATCH_SIZE` or `INGEST_FLUSH_INTERVAL`, does the INSERT into
   ClickHouse (same retries and disk spill as the in-memory buffer) and **only then** does `XACK` + `XDEL`.
3. If the batch could be neither inserted nor saved to disk, it is not acknowledged: the entries stay pending and are retried.
4. Entries that were read but not acknowledged for `INGEST_STREAM_CLAIM_IDLE` (60 s) are taken over by another consumer (`XAUTOCLAIM`),
   and on startup each consumer resumes its own.
5. **At-least-once** delivery: if the process dies between the INSERT and the `XACK`, the batch is replayed. ClickHouse's deduplication
   token is derived from the entry ids, so an identical batch is ignored; if it is regrouped differently, there may be duplicates
   (it requires a crash right during an INSERT; the client's UUIDv7 `eventId` makes it possible to clean them up).

## What the Redis operator has to do

Without this the queue is no safer than memory:

- **AOF enabled**: `appendonly yes` and `appendfsync everysec`. Without AOF, restarting Redis empties the stream. With `everysec` the loss window
  is ~1 s of Redis writes. Ingestion warns on startup if `appendonly` is not `yes`.
- **Enabling AOF on a Redis that already has data: do it live.** In Redis 7, if it starts with `--appendonly yes` and no AOF file
  exists yet, it **does not load `dump.rdb`** and starts empty (this happened in production on 2026-11-10 and had to be reverted).
  Correct order: `redis-cli -a "$REDIS_PASSWORD" CONFIG SET appendonly yes`, wait for `INFO persistence` to show
  `aof_rewrite_in_progress:0`, and only then put `--appendonly yes` in the startup command and redeploy.
- If the startup command uses `sh -c`, begin it with `exec redis-server ...`: without `exec`, `sh` does not forward the stop signal and Redis
  dies without saving (exit code 137).
- **`noeviction` memory policy** (or a dedicated Redis). With `allkeys-lru` Redis may **evict the stream itself**
  when it fills up. With `noeviction`, if Redis runs out of memory the `XADD` fails and events fall back to the in-memory buffer.
- **Memory**: an event takes ~0.5–1 KB in the stream. `INGEST_STREAM_MAXLEN` (1,000,000) can mean 0.5–1 GB: adjust it
  to the memory you really give Redis. In the stress environment (96 MB) use, for example, 50,000.
- If Redis is the same one used for quota, deduplication and the site cache, a large stream competes with them for memory.
  Consider a separate Redis for the queue if you enable it in production.

## Variables

| Variable | Default | |
|---|---|---|
| `INGEST_QUEUE` | `memory` | `redis` to enable. Without Redis, or if it is unreachable at startup, memory is used and a warning is logged. |
| `INGEST_STREAM_KEY` | `tempoo:ingest:events` | |
| `INGEST_STREAM_MAXLEN` | `1000000` | at this length, 503 `overloaded` |
| `INGEST_STREAM_CLAIM_IDLE` | `60s` | time after which the entries of a dead consumer are taken over |

### Dependency timeout and the site config cache

Independent of the queue: `INGEST_DEPENDENCY_TIMEOUT` (default `500ms`; empty means the default) bounds each dependency call made while the client waits (site config, quota, deduplication). Only an unresolved site config returns a 503 (`Retry-After: 1`); quota and deduplication fail open. The `[INGEST ERROR]` line names the dependency and its elapsed time against the budget. To keep cold lookups off the request path, ingestion loads all site configs (up to 4,000) at startup and refreshes them every `SITE_CONFIG_PRELOAD_INTERVAL` (default half of `SITE_CONFIG_MEMORY_TTL`, i.e. 15 s; `0` disables the refresh). An expired config is served stale (up to `SITE_CONFIG_STALE_TTL`, 10 min) while it is refreshed in the background, and a failed refresh keeps serving the stale copy.

Metrics: `ingestion_queue_depth` and `ingestion_queue_capacity` add up stream and memory; `ingestion_stream_fallback_events_total`
(events that went to memory because Redis failed) and `ingestion_stream_recovered_events_total` (reprocessed after a crash).
