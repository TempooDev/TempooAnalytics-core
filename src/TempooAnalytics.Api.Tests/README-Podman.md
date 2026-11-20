# Running integration tests with Podman

A short guide to running the integration tests that use Testcontainers (Postgres) with Podman instead of Docker.

Requirements
- `podman` installed and working.
- The `dotnet` SDK (to run `dotnet test`).

Quick note: Testcontainers uses the Docker API. For Testcontainers to work with Podman you have to expose Podman's REST API and point `DOCKER_HOST` at it.

Common options

1) Using a Unix socket (Linux / WSL)

```bash
# start the Podman service (runs in the background)
podman system service --time=0 unix:/run/podman/podman.sock &

# export the variable so Testcontainers (and Docker clients) use the Podman socket
export DOCKER_HOST=unix:///run/podman/podman.sock

# run the tests
dotnet test src/TempooAnalytics.Api.Tests/TempooAnalytics.Api.Tests.csproj
```

2) Using TCP (Windows PowerShell, or environments where you prefer TCP)

On Windows/PowerShell it is practical to expose the API over TCP and point `DOCKER_HOST` at `tcp://127.0.0.1:2375`:

```powershell
# start the podman service listening on TCP (runs in the background)
Start-Process -NoNewWindow -FilePath podman -ArgumentList 'system','service','--time=0','tcp:127.0.0.1:2375'

# set the environment variable for the current session only
$env:DOCKER_HOST = 'tcp://127.0.0.1:2375'

# run the tests
dotnet test "e:\github\TempooAnalytics\src\TempooAnalytics.Api.Tests\TempooAnalytics.Api.Tests.csproj"
```

3) If your Podman is configured with TLS

If you expose a TLS endpoint, also set `DOCKER_TLS_VERIFY=1` and `DOCKER_CERT_PATH` pointing at the directory with the certificates, exactly as with Docker.

Tips and notes
- If the tests fail because images cannot be downloaded, check that the Podman engine has Internet access or run `podman pull docker.io/library/postgres:15-alpine` manually.
- In rootless environments you may need to adjust the socket permissions or run `podman system service` with suitable privileges.
- Testcontainers .NET reads `DOCKER_HOST` to find the API; with this, you normally do not need to change anything in the test code.
- If you want to force Testcontainers to use a specific endpoint from within the test code, there are configuration APIs (e.g. `TestcontainersSettings`), but in most cases `DOCKER_HOST` is enough.
