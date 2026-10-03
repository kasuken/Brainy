# Brainy production runbook

## Release gate

The production site is served at `https://www.brainy-me.com`. Merging to `main` never
deploys: production changes only through the [Release workflow](../.github/workflows/release.yml),
which runs the shared kasuken pipeline described in
[RELEASING.md](https://github.com/kasuken/.github/blob/main/RELEASING.md). The workflow
never writes App Service settings; hosted-only configuration (origin, SEO, billing) and
secrets are App Service application settings in the double-underscore form consumed by
.NET configuration.

To release:

1. Merge through protected `main` with the `build-and-test` check passing.
2. Review the generated EF migration. Brainy applies pending migrations at application
   startup because the production SQL endpoint is private and the GitHub-hosted runner
   cannot connect to it directly.
3. Optionally move the `## [Unreleased]` entries in `CHANGELOG.md` under
   `## [X.Y.Z] - date`; that section becomes the release notes.
4. Run `gh workflow run release.yml -R kasuken/Brainy -f bump=patch` (or `minor` /
   `major`). It waits for the full CI run (NuGet audit, unit, SQL Server integration and
   e2e tests, pending-model check) to pass on the commit, builds once, deploys through the
   `production` environment (deployments only from `main` or `v*` tags), checks
   `/health/ready`, `/Account/Login` and the HTTP to HTTPS redirect, and only then tags
   and publishes the GitHub release.
5. Run `scripts/Test-Production.ps1` after deployment.

## Rollback

The current B1 App Service plan does not provide deployment slots. To roll back,
redeploy the last known-good tag with
`gh workflow run release.yml -R kasuken/Brainy -f redeploy=vX.Y.Z`: it rebuilds that tag
and deploys it without creating a new release. Startup migrations are not reversed, so
roll back only to a version that works with the current schema, and restore the database
only when a schema/data rollback is actually required. Never reverse a migration by deleting production data without a tested
restore point.

Upgrading to a Standard or Premium plan is required before Brainy can use a
stage-and-swap deployment with immediate slot rollback.

## Database recovery

- SQL public network access should remain disabled. Access uses App Service VNet
  integration, a private endpoint, and the linked private DNS zone.
- Perform a point-in-time restore drill before relying on the current backup policy.
- The present serverless database uses local backup redundancy and seven-day
  short-term retention. Geo/zone redundancy and long-term retention require an
  explicit cost and recovery-objective decision.

## Identity and secrets

- Azure deployment uses GitHub OIDC with the `id-brainy-github-deploy` user-assigned
  identity, federated to the repository's `production` environment, with Website
  Contributor on the web app only. Basic-auth (FTP/SCM) publishing credentials stay disabled.
- A system-assigned App Service identity exists, but the application connection is
  still password-based. Moving SQL to managed-identity authentication requires a
  database user/role grant and a validated connection-string change.
- Keep only `DefaultConnection`; do not reintroduce duplicate connection strings.
- `appsettings.json` ships `Billing:Provider=None` for self-hosted instances. The hosted
  service sets `Billing__Provider=Stripe` as a permanent App Service application setting;
  removing it turns billing off in production on the next restart.
- Keep `Identity__AllowRegistration=false` for private deployments. Enabling public
  registration requires an explicit abuse, email-verification, and account-recovery decision.
- Brainy requires 10-character passwords, locks sign-in after five failures for
  15 minutes, and rate-limits login and registration POSTs.

## Observability (OpenTelemetry)

OpenTelemetry traces, metrics, and logs are wired in `Program.cs`
(`Brainy.Web/Telemetry/DependencyInjection.cs`) behind `Telemetry:*` configuration
and are **disabled by default** — a self-hosted deployment sees no added behavior,
dependencies, or overhead unless it opts in.

To enable, set the following (as App Service application settings, using the same
double-underscore environment-variable form as the existing `Seo__SiteOrigin`
setting):

- `Telemetry__Enabled=true`
- `Telemetry__OtlpEndpoint=<collector URL>` — required whenever `Enabled` is true;
  startup validation (`ValidateOnStart`) refuses to start rather than silently
  exporting nowhere if this is missing.
- `Telemetry__Protocol=grpc` (default, collector port 4317) or `httpprotobuf`
  (port 4318) — must match what the endpoint accepts.
- `Telemetry__OtlpHeaders` — optional collector authentication (e.g. an ingestion
  key), in the same `key1=value1,key2=value2` form as the OpenTelemetry SDK's own
  `OTEL_EXPORTER_OTLP_HEADERS` environment variable. Set this as a secret App
  Service setting, never in a checked-in appsettings file — it authenticates *to*
  the collector and is never itself exported as telemetry.
- `Telemetry__SamplingRatio` — fraction of traces to sample, `0.0`–`1.0` (default
  `1.0`, appropriate for Brainy's traffic volume). Applied as
  `ParentBased(TraceIdRatioBased(...))`, so a trace that already started upstream
  is always kept regardless of ratio. Metrics and logs are unaffected — only trace
  volume (and export cost) scales with this setting. Export always runs on the
  OpenTelemetry SDK's own batching/background pipeline in every case, so enabling
  telemetry does not add synchronous work to the request path.

What gets instrumented: ASP.NET Core, EF Core (SQL Server), and outbound
`HttpClient` calls — including the Stripe SDK's own HTTP requests when
`Billing:Provider=Stripe` — get automatic traces and metrics from the standard
OpenTelemetry instrumentation libraries. A Blazor Server request is correlated
end to end (HTTP request → Blazor circuit event dispatch → application service
calls → the SQL query they issue) via the `Microsoft.AspNetCore.Components` /
`Microsoft.AspNetCore.Components.Server.Circuits` ActivitySource and Meter that
ASP.NET Core itself publishes once `EnableRazorComponentsSupport` and
`EnableAspNetCoreSignalRSupport` are turned on (both are, whenever telemetry is
enabled at all) — no hand-rolled `CircuitHandler` needed.

Brainy also emits its own metrics (`Brainy.Application.Telemetry.BrainyTelemetry`)
for the flows that carry cost or risk and fail silently/asynchronously by nature:

- `brainy.cache.lookups` — application cache hit/miss ratio (the cache layer added
  in 5.13.2), tagged `cache.result` = `hit`/`miss`.
- `brainy.sync.batch_size` / `brainy.sync.items` — offline-capture sync backlog
  size and per-item outcome (`created` / `duplicate_ignored` / `already_synced` /
  `rejected`).
- `brainy.billing.webhook_events` — inbound billing webhook outcome (`applied` /
  `ignored_event_type` / `already_processed` / `invalid_signature`).
- `brainy.markdown_export.jobs` / `brainy.markdown_export.job.duration` —
  Markdown/Obsidian export background job outcome and duration.

AI request count/latency/failure-rate is deliberately **not** instrumented yet — AI
is out of scope for this release.

**Privacy is enforced, not just requested.** No note or output content, titles,
search terms, or email addresses are ever placed into a span attribute, log
attribute, or metric tag anywhere in Brainy's own instrumentation — every metric
tag Brainy adds comes from a small, fixed, developer-controlled vocabulary (an
outcome enum, or `hit`/`miss`), never user-supplied text, mirroring the discipline
`SearchService` already established for analytics (record that something happened,
never the content involved). As defense in depth against whatever the ASP.NET
Core/EF Core/HttpClient instrumentation libraries themselves capture by default —
today or in a future version — a `PrivacyRedactionProcessor` strips `db.statement`,
`db.query.text`, `url.query`, and the `Authorization`/`Cookie` request headers from
every span before it reaches the exporter. No BYOK key or billing secret (Stripe
API key, webhook signing secret) is ever placed in an attribute either.

On the B1, single-instance App Service plan (no deployment slots — see Rollback
above), telemetry adds one outbound dependency (the collector) and a small,
constant background export overhead; there is no in-cluster collector to run
alongside the app, so `Telemetry__OtlpEndpoint` must point at a reachable external
collector or ingestion endpoint (e.g. an Azure Monitor OTLP endpoint, or a hosted
OpenTelemetry Collector) rather than `localhost`.

## Incident checks

1. Confirm HTTP login redirects to HTTPS.
2. Check `/health/live` and `/health/ready` separately.
3. Review Application Insights failures, exceptions, dependency failures, and p95
   request duration without logging note content or credentials.
4. Confirm SQL private-endpoint and DNS status before enabling public access as a
   diagnostic shortcut.
5. If a deployment introduced the problem, redeploy the last known-good release
   (`-f redeploy=vX.Y.Z`, see Rollback) before attempting broad data repairs.
6. When `Telemetry:Enabled` is on, use the trace spanning the web request, the
   Blazor circuit event, and the database query (see Observability above) to
   localize which layer actually failed before guessing.
