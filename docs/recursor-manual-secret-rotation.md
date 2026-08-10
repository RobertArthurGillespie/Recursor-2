# Manual credential rotation required (corrective pass, Stage 2)

This corrective pass removed hard-coded credentials from tracked source and configuration
files and moved them to configuration (user-secrets locally; App Service configuration / Key
Vault / managed identity in deployed environments). **Removing the literal from source does
not invalidate the credential itself** — every value listed below was present in this Git
history and must be rotated or revoked manually by whoever owns each account. No values are
repeated here; only the credential type and where it was found.

## Credentials requiring manual rotation/revocation

| Credential | Previously hard-coded in | Owner action required |
|---|---|---|
| Azure OpenAI key (`manuscriptgenerator.openai.azure.com`) | `Server/Recursor/Services/ExplanationGenerationService.cs`, `Server/Recursor/Services/MedicalSupplyDebriefService.cs`, `Server/appsettings.json` (`OpenAIConfig:OpenAIKey`) | Regenerate the key in the Azure OpenAI resource; update `AzureOpenAi:ManuscriptGenerator:Key` via user-secrets/App Service config. |
| Azure OpenAI key (`ncatopenai.openai.azure.com`) | `Server/Controllers/ChatController.cs` | Regenerate the key in the Azure OpenAI resource; update `AzureOpenAi:ChatController:Key`. |
| Azure Cognitive Search API key (`ncatsearch.search.windows.net`) | `Server/Controllers/ChatController.cs` | Regenerate the query/admin key in the Search resource; update `AzureSearch:ApiKey`. |
| Azure Storage account key (`avrservicestorage`) | `Server/Recursor/Services/BlobStateService.cs` | Rotate the storage account key (or switch to managed identity); update `AzureStorage:AvrServiceData:ConnectionString`. |
| Azure Storage account key (`ncataistorage`) | `Server/Controllers/TestingController.cs` | Rotate the storage account key; update `AzureStorage:PipelineContent:ConnectionString`. |
| Dropbox refresh token | `Server/Controllers/TestingController.cs` (multiple occurrences, including inline comments) | Revoke the linked app session in the Dropbox account's connected-apps settings and issue a new refresh token; update `Dropbox:RefreshToken`. |
| Dropbox app client ID / client secret | `Server/Controllers/TestingController.cs` | Rotate the app secret in the Dropbox App Console; update `Dropbox:ClientId` / `Dropbox:ClientSecret`. |
| Dropbox long-lived access token pasted in a code comment | `Server/Controllers/TestingController.cs` | Revoke via the Dropbox account's connected-apps settings (this token was never read by code, only left in a comment, but it is still live until revoked). |

Rotation is an action against the live Azure/Dropbox accounts and cannot be performed by
editing this repository — it requires access to those account consoles and is intentionally
left as a manual, human step per this pass's Stage 13 constraint (no live Azure actions
without explicit instruction).

## New required configuration keys

All of the following must be set via `dotnet user-secrets set "<key>" "<value>"` locally, or
App Service configuration / Azure Key Vault in deployed environments, before the corresponding
feature will work. None of these are required for the app to start — each failure is local to
the feature that needs it (GPT explanations/debriefs degrade gracefully to a fallback; the
`/Chat` and `/Testing` endpoints fail clearly with an `InvalidOperationException` naming the
missing key):

- `AzureOpenAi:ManuscriptGenerator:Key`
- `AzureOpenAi:ChatController:Key`
- `AzureSearch:ApiKey`
- `AzureStorage:AvrServiceData:ConnectionString`
- `AzureStorage:PipelineContent:ConnectionString`
- `Dropbox:RefreshToken`
- `Dropbox:ClientId`
- `Dropbox:ClientSecret`
- `Dropbox:SelectUser`
- `OpenAIConfig:OpenAIKey` (currently unused by any active code path — the only consumers in
  `Server/Program.cs` are commented out — but the key name is kept reserved since it was
  previously populated with a live-looking value)

## Deferred hardening (documented, not fixed in this pass)

`SimulationStateController` (`GET`/`POST /api/recursor/state`) still accepts a caller-supplied
`userId`/`simId` without authenticating the caller. Unity sim clients are not participants in
the JWT-based login used by the internal dashboard (`AuthController` issues only `Name`/`Role`
claims for dashboard admins/users), so there is currently no per-learner identity to authorize
against without a broader, out-of-scope change to how the Unity client authenticates. This pass
closes the immediate path-traversal / arbitrary-blob-overwrite risk by validating `userId`/
`simId` against a safe identifier charset (`^[A-Za-z0-9_.-]{1,128}$`), but a caller who knows or
guesses another learner's `userId` can still read/overwrite that learner's simulation state.
Introducing real per-learner authentication for the Unity-facing endpoints is a follow-up, not
part of this corrective pass.
