# Tempoo Analytics (core)

Privacy-friendly web analytics you can run on your own server: pageviews, referrers, devices, countries, heatmaps and
goals, with no cookies. This repository is the open **core**: the full analytics product for self-hosting, with no plan
limits and no paid services.

Parts: a Go ingestion service (`/analytics/events`), a Go import/export worker, a .NET API with the admin console, an Angular web app, a gateway,
PostgreSQL (accounts, sites), ClickHouse (events) and Redis.

## Quick start (self-hosting)

```bash
cp .env.example .env
# fill the four required secrets (letters and digits only):  openssl rand -hex 24
$EDITOR .env
docker compose up -d --build        # podman compose works too
```

Open `http://localhost:8000` and sign in with `ADMIN_USERNAME` / `ADMIN_PASSWORD`. Put a TLS reverse proxy in front of the
gateway for real use. Everything else (variables, first run, sending a first event, upgrades, S3 storage for imports) is in
[`docs/self-hosting.md`](docs/self-hosting.md).

## Documentation

- [Self-hosting guide](docs/self-hosting.md) and [first run](docs/selfhost-first-run.md)
- [S3-compatible storage](docs/selfhost-s3-storage.md)
- [Contributing and CI](docs/CI.md)

## License

[GNU Affero General Public License v3.0](LICENSE) (AGPL-3.0). If you run a modified version as a network service you
must offer its source to its users. Third-party notices are in [`NOTICE`](NOTICE).

## Contributing

Issues and pull requests are welcome. Sign off every commit (`git commit -s`) to certify the
[Developer Certificate of Origin](https://developercertificate.org/); the public CI builds the solution, runs the tests
and scans for secrets and licences. Please do not report security problems in public issues: contact the maintainers
privately.
