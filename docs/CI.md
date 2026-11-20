# Contributing and CI

## Contributing

- Open an issue first for anything bigger than a small fix, so the approach can be agreed before you write the code.
- Sign off every commit (`git commit -s`). The sign-off certifies the [Developer Certificate of Origin](https://developercertificate.org/)
  and the CI rejects a pull request with a commit that lacks one. To sign off commits you already made:
  `git rebase --signoff <base-branch>` and push again.
- Code, comments and documentation are in English.
- Do not report security problems in public issues: contact the maintainers privately.

## Running the checks locally

```bash
# .NET solution (Api.Tests starts PostgreSQL with Testcontainers: Docker or Podman must be running)
dotnet build
dotnet test src/TempooAnalytics.Gateway.Tests/TempooAnalytics.Gateway.Tests.csproj
dotnet test src/TempooAnalytics.Api.Tests/TempooAnalytics.Api.Tests.csproj

# Angular app
cd src/TempooAnalytics && npm ci && npx vitest run && npm run build:community

# Go services (ingestion, importer)
cd src/TempooAnalytics.Go && go build ./... && go test ./...

# Tracker
cd tools/tempoo-analytics && npm ci && npm test
```

## Workflows

Every workflow runs on GitHub-hosted runners (`ubuntu-latest`), with read-only permissions and no secrets.

### `ci.yml` (push to the default branch, pull requests, manual)

| Job | What it does |
|---|---|
| `dotnet` | Restores and builds the whole solution (Release), then runs `Gateway.Tests` and `Api.Tests`. |
| `frontend` | `npm ci`, unit tests (`vitest`) and the community build of the Angular app. |
| `go` | `go build ./...` and `go test ./...` of the Go module. |
| `tracker` | Tests of the tracker script (`tools/tempoo-analytics`). |
| `dco` | On pull requests, checks that every commit (merges excluded) has a `Signed-off-by: Name <email>` line. |
| `ci-ok` | Single check for branch protection: fails if any job above failed, was cancelled or did not run. |

A new push to a pull request cancels its previous run.

### `compliance.yml` (push, pull requests, weekly, manual)

| Job | What it does |
|---|---|
| `gitleaks` | Secret scan: the new commits on a pull request, the whole history otherwise. |
| `licenses-npm` | Licences of the production npm dependencies of the app and the tracker against an allow list. |
| `licenses-go` | Licences of the Go dependencies (`go-licenses`). |
| `licenses-nuget` | Licences of the NuGet packages of the solution. |
| `compliance-ok` | Single check for branch protection, like `ci-ok`. |
