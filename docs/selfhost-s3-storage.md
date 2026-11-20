# Self-host: S3-compatible object storage

Tempoo Analytics uses one S3-compatible bucket as a hand-off area between the API and the Go worker (`importer`). It is **not** where your analytics data lives (that is ClickHouse and Postgres), and nothing in it is long-lived: objects are temporary files.

Any provider that speaks the S3 API with path-style addressing should work: Cloudflare R2, MinIO, SeaweedFS, AWS S3, Backblaze B2, and so on. The variable names say `R2` for historical reasons only.

## What uses it

| Feature | Who writes | Who reads | Object key |
|---|---|---|---|
| Plausible import (file upload) | API | Go worker, which deletes the file when the job ends | `imports/{siteId}/{guid}.zip` or `.csv` |
| Google Analytics 4 import | API (stores the normalised export) | Go worker | `imports/...` |
| Data export (CSV) | Go worker | The user, through a presigned download link valid for 24 hours | `exports/{siteId}/{timestamp}_export.csv` |

Import uploads are limited to 50 MB.

## Configuration

Both the API and the worker need the same bucket and credentials, with different variable names. With the self-host compose file
set only `STORAGE_R2_ENDPOINT`, `STORAGE_R2_ACCESS_KEY`, `STORAGE_R2_SECRET_KEY` and `STORAGE_R2_BUCKET` in `.env`: it passes them to both.

| API (.NET) | Worker (Go) | Meaning | Default |
|---|---|---|---|
| `Storage__R2__Endpoint` | `STORAGE_R2_ENDPOINT` | Full URL of the S3 endpoint, including `https://` | none |
| `Storage__R2__AccessKey` | `STORAGE_R2_ACCESS_KEY` | Access key id | none |
| `Storage__R2__SecretKey` | `STORAGE_R2_SECRET_KEY` | Secret access key | none |
| `Storage__R2__BucketName` | `STORAGE_R2_BUCKET` | Bucket name | `tempoo-exports` |

Notes that matter in practice:

- **Path-style addressing is always used** (`https://endpoint/bucket/key`), so the provider does not need wildcard DNS for bucket subdomains.
- **The endpoint must be reachable by your users' browsers**, not only by the containers. Export download links are presigned by the worker against `STORAGE_R2_ENDPOINT`, so an internal hostname such as `http://s3:9000` produces links that only work inside your Docker network. Use a public (ideally HTTPS) URL, or put a reverse proxy in front of the storage.
- **Create the bucket yourself** and grant the key read, write and delete on it. The API tries to create a missing bucket on upload and silently ignores failure, but the worker never creates one.
- **The worker signs requests with the fixed region `auto`.** This is what R2 expects, and MinIO and SeaweedFS ignore the region. Real AWS S3 validates the region, so it has not been verified there; prefer one of the other providers unless you are prepared to test it.
- No bucket policy, CORS or public access is needed: downloads use presigned URLs.
- Uploaded import files are deleted after processing and exports are only needed for a day, so a lifecycle rule that expires objects after a few days is a safe way to clean up leftovers.

## Provider examples

All values below are placeholders. Put real values in your secrets store or an untracked `.env` file, never in git. The same values go in the worker's `STORAGE_R2_*` variables.

### Cloudflare R2

```env
Storage__R2__Endpoint=https://<account-id>.r2.cloudflarestorage.com
Storage__R2__AccessKey=<r2-access-key-id>
Storage__R2__SecretKey=<r2-secret-access-key>
Storage__R2__BucketName=<your-bucket>
```

Create the bucket and an API token with Object Read and Write permission scoped to that bucket. R2 shows the access key id and secret only once.

### MinIO

```env
Storage__R2__Endpoint=https://s3.<your-domain>
Storage__R2__AccessKey=<minio-access-key>
Storage__R2__SecretKey=<minio-secret-key>
Storage__R2__BucketName=<your-bucket>
```

Create the bucket with `mc mb` and a dedicated user restricted to it. MinIO listens on port 9000 by default.

### SeaweedFS

```env
Storage__R2__Endpoint=https://s3.<your-domain>
Storage__R2__AccessKey=<seaweedfs-access-key>
Storage__R2__SecretKey=<seaweedfs-secret-key>
Storage__R2__BucketName=<your-bucket>
```

Start the S3 gateway with `weed server -s3` (default port 8333). Without an `-s3.config` file SeaweedFS does not check credentials, which is convenient locally but means anyone who can reach the port can read and write: define identities in an S3 config file for anything exposed to a network.

### AWS S3

```env
Storage__R2__Endpoint=https://s3.<region>.amazonaws.com
Storage__R2__AccessKey=<iam-access-key-id>
Storage__R2__SecretKey=<iam-secret-access-key>
Storage__R2__BucketName=<your-bucket>
```

Use an IAM user limited to `s3:GetObject`, `s3:PutObject` and `s3:DeleteObject` on `arn:aws:s3:::<your-bucket>/*`. See the region caveat above.

## Behaviour without storage

Leaving the four variables unset is allowed: the application starts and everything that does not touch files keeps working (tracking, dashboards, reports, sites, goals). Only the file-based features above are affected, and how depends on the environment:

| Environment | What happens |
|---|---|
| Development, `Testing` or CI | The API silently falls back to `http://localhost:9000` with the credentials `minioadmin` / `minioadmin`. Run a local MinIO or SeaweedFS there, or file operations fail with a connection error. |
| Production (anything else) | The API does **not** fail at startup. The storage service is created on first use and throws "R2 credentials missing in production" when a request needs it. |

In the community edition with no storage configured (production), the API says so instead of failing:

- **Plausible import, GA4 import and CSV export** answer `501` with "not available on this server", before anything is uploaded and without creating a job, so nothing is left running.
- `GET /api/features` reports `dataStorageUnavailable: true` and the web app disables the import button, replaces the import page cards with an explanation (in Spanish) and does not offer exports.
- The hosted service is not affected: there the storage is always expected to be configured.

The self-host compose file starts the Go `importer` worker and passes it the same `STORAGE_R2_*` values as the API (set them in `.env`), so configuring the bucket there is all the jobs need.

If you do not need imports or exports, leave the `PlausibleImport` and `Ga4Import` feature flags off and the storage variables unset. If you do, set the variables on both the API and the worker, then run a first job and check the worker log for `failed to download file from storage` or upload errors.
