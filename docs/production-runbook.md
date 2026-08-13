# Brainy production runbook

## Release gate

Before publishing a GitHub release:

1. Merge through protected `main` with the `build-and-test` check passing.
2. Confirm NuGet audit, application tests, web integration tests, SQL Server
   migration tests, and the pending-model check are green.
3. Review the generated EF migration. Brainy currently applies pending migrations
   at application startup because the production SQL endpoint is private and the
   GitHub-hosted runner cannot connect to it directly.
4. Publish the release and approve the protected `production` environment.
5. Run `scripts/Test-Production.ps1` after deployment.

## Rollback

The current B1 App Service plan does not provide deployment slots. Keep the last
known-good workflow artifact/release available, redeploy it if the readiness probe
fails, and restore the database only when a schema/data rollback is actually
required. Never reverse a migration by deleting production data without a tested
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

- Azure deployment uses GitHub OIDC.
- A system-assigned App Service identity exists, but the application connection is
  still password-based. Moving SQL to managed-identity authentication requires a
  database user/role grant and a validated connection-string change.
- Keep only `DefaultConnection`; do not reintroduce duplicate connection strings.
- Keep `Identity__AllowRegistration=false` for private deployments. Enabling public
  registration requires an explicit abuse, email-verification, and account-recovery decision.
- Brainy requires 10-character passwords, locks sign-in after five failures for
  15 minutes, and rate-limits login and registration POSTs.

## Incident checks

1. Confirm HTTP login redirects to HTTPS.
2. Check `/health/live` and `/health/ready` separately.
3. Review Application Insights failures, exceptions, dependency failures, and p95
   request duration without logging note content or credentials.
4. Confirm SQL private-endpoint and DNS status before enabling public access as a
   diagnostic shortcut.
5. If a deployment introduced the problem, redeploy the last known-good release
   before attempting broad data repairs.
