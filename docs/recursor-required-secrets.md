# Required secrets and environment variables (Server)

`Server/appsettings.json` no longer contains real credentials. It ships with empty
placeholders for every secret-bearing key. The application fails fast at startup with a
clear error (see `RequireSecret` in `Server/Program.cs`) if a required value is missing —
the error names the missing configuration key, it never logs the value.

## Required values

| Configuration key | Purpose | Environment variable (double-underscore binds to nested config) | Secret? |
|---|---|---|---|
| `Jwt:Key` | Symmetric key used to sign and validate API JWTs | `Jwt__Key` | Yes |
| `ConnectionStrings:DefaultConnection` | SQL Server connection string for `AppDbContext` (user accounts) | `ConnectionStrings__DefaultConnection` | Yes |

## Non-secret but environment-specific

| Configuration key | Purpose |
|---|---|
| `Jwt:Issuer`, `Jwt:Audience` | Token issuer/audience validation — not secret, safe to keep in `appsettings.json`. |
| `Adx:ClusterUri`, `Adx:IngestUri`, `Adx:Database`, `Adx:TenantId` | ADX cluster identity — not secret. |
| `Adx:ClientId`, `Adx:ClientSecret` | Only required when `Adx:AuthMode` is `ServicePrincipal`. Leave empty for `UserPrompt` (local dev) or `ManagedIdentity` (Azure hosting). If set, treat `Adx:ClientSecret` as a secret using the same mechanisms below. |
| `Recursor:Models:*ModelPath`, `Recursor:Models:*ModelVersion` | Local file paths / version labels for trained ML.NET artifacts — not secret. See `docs/recursor-model-versioning.md` for the immutable-version layout. |

## How to supply secrets

**Local development** — use .NET user-secrets (already wired via `<UserSecretsId>` in
`Server/NCATAIBlazorFrontendTest.Server.csproj`); values live outside the repo under your
user profile and are never committed:

```bash
cd Server
dotnet user-secrets set "Jwt:Key" "<a long random string>"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=...;User ID=...;Password=...;..."
```

**Deployed environments (Azure App Service)** — set as App Service Application Settings
(they surface as environment variables using the `__` separator):

```
Jwt__Key
ConnectionStrings__DefaultConnection
```

Prefer Azure Key Vault references or managed identity where the hosting platform supports
it, rather than plaintext App Service settings, for production deployments.

## Credential rotation — action required

The following credentials were previously committed in plaintext to `Server/appsettings.json`
in this repository's git history (and copied into tracked `bin`/`obj` build output files) and
**must be rotated manually** — removing them from the working tree does not invalidate them,
and they remain visible in git history until history is rewritten separately:

- The SQL login `ncat` on `ncat-sql-server.database.windows.net` (`Initial Catalog=NCATDb`) —
  rotate the password in Azure SQL and update the secret store above.
- The JWT signing key (`A_Very_Long_Random_Secret_Key_At_Least_32_Chars`) — treat as
  compromised; generate a new random key and update the secret store above. Rotating this
  invalidates all previously issued tokens (users will need to log in again).

This corrective pass only removes the plaintext values from tracked source files going
forward; it cannot rotate the external credentials themselves.
