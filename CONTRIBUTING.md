# Contributing to TempooAnalytics core

Thanks for helping. This repository is the open-source core of TempooAnalytics and is licensed under the
[GNU AGPL-3.0](LICENSE). By contributing you agree that your contribution is released under the same license.

## Developer Certificate of Origin (DCO)

Every commit of a pull request must be signed off to certify the [Developer Certificate of Origin](https://developercertificate.org/):
you wrote the change, or have the right to submit it under the project license. Add the sign-off with `-s`:

```bash
git commit -s -m "fix: describe the change"
```

This appends a `Signed-off-by: Your Name <you@example.com>` line that must match your Git `user.name` and `user.email`.
The `dco` job of the CI fails the pull request if a commit has no such line. To fix commits you already made:

```bash
git rebase --signoff origin/main   # sign off every commit of the branch
git push --force-with-lease
```

## Pull requests

- Branch from `main` and open a pull request; `main` is protected and requires the `ci-ok` check.
- Keep each pull request focused, add or update tests, and write code, comments and documentation in English.
- Run the build and tests locally before pushing (`dotnet test`, `npm test`, `go test ./...` for the part you touched).
- Do not report security vulnerabilities in public issues: use the repository's private vulnerability reporting
  (Security tab → Report a vulnerability).
