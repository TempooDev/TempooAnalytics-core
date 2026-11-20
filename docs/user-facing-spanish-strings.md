# Spanish strings kept on purpose

Decision (2026-11-17): the public repository is English for code, comments, docs and commits, but the strings that
users and operators see stay in Spanish. Code comments and docstrings were translated; string literals were not.
This page only records where Spanish remains, so nobody "fixes" it by accident. Counts are approximate (a static
search cannot be exhaustive).

| Area | Where | Strings |
| --- | --- | --- |
| Web UI (Angular) | forgot-password page (`.html:2,3,11,12`, `.ts:26`), footer link (`footer.ts:17`, asserted in `footer.spec.ts:20`) | 6 |
| Tracker heatmap overlay | Spanish block of `tools/tempoo-analytics/heatmap-overlay.js:20-45`, chosen by browser language | 14 |
| Transactional e-mails | `ResendEmailService.cs`, the `es ? "…" : "…"` pairs (Spanish is used unless the language is `en`) | 42 |
| Admin console (Razor) | `Pages/Admin/Users.cshtml`, `Feedback.cshtml`, messages in `Users.cshtml.cs` | about 55 |
| Go worker logs and errors | `internal/repository/{worker,stream_queue,heatmap_worker,spill,clickhouse}.go`, `internal/telemetry/otel-telemetry.go` | 28 |
| Operator scripts and tooling output | `scripts/test_ingestion.ps1`, `tools/tempoocli/generators/*.js` | about 45 |

Note: `docs/ingestion-redis-queue.md` quotes two of the Go log lines verbatim, so those literals and that runbook
must stay in sync.
