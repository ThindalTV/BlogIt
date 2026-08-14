# BlogIt — Follow-up Audit & Action List

**Date:** 2026-08-14
**Scope:** `src/BlogIt`, `src/BlogIt.Admin`, `src/BlogIt.Contracts`, `src/BlogIt.AzureStorage`, `samples/BlogIt.Sample`. Excludes `src/BlogIt.MauiAdmin`.
**Relation to prior audit:** this follows [`AUDIT_REPORT.md`](AUDIT_REPORT.md) (2026-08-09/08-11). Seven findings from that report were fixed on 2026-08-14 (#3b, #7, #10c, #10d, #15, #16, #18) and four were explicitly decided to be accepted risk rather than bugs (#0, #1, #6, #20). **This document is self-contained** — everything you need to act on what's left is below; you shouldn't need to open the older file.

**Status: all findings below are fixed as of 2026-08-14.** Every item (#1–#14) has been resolved and verified — 169/169 tests passing. Kept as a record of what changed and why; nothing is currently open.

**Threat model driving the "accepted" decisions below:** every authenticated user (the site owner or anyone they invite as an author) is fully trusted. Only anonymous visitors are not. That's narrower than "trust nobody," and it's why some findings that look severe in isolation are marked accepted rather than open.

---

## Accepted risks (not bugs — will not be fixed under the current trust model)

| Finding | What it is | Where | Why it's accepted | Revisit if… |
|---|---|---|---|---|
| Media upload trusts the client's `Content-Type` (a mislabeled upload can execute as HTML, same-origin) | Any authenticated user can upload a `.html` file that runs a `<script>` when visited | [`MediaApi.cs:66,73`](src/BlogIt/Api/MediaApi.cs) (comment added in place) | Upload already requires auth — only a trusted user can trigger it, not a visitor | You ever let an untrusted/low-value account upload media |
| Stored XSS via Markdown's raw-HTML passthrough | Post/page authors can embed `<script>`/`<iframe>`/etc. in their own content | [`MarkdownHelper.cs`](src/BlogIt/Helpers/MarkdownHelper.cs) (comment added in place) | Same tradeoff as WordPress's `unfiltered_html` capability for admins — authors are trusted with their own content | You want to sanitize by default with an opt-out, e.g. because you're no longer the only author |
| Same unsanitized HTML propagates into RSS/Atom feeds | `<content:encoded>` / `type="html"` fields carry whatever HTML a post contains, including scripts, to external feed readers | [`FeedService.cs:61-62,104-105,221-227`](src/BlogIt/Services/FeedService.cs) | Same root cause and reasoning as the Markdown finding above | Same as above |
| No role/permission tiers — every `AppUser` is fully privileged | Inviting a second user grants them full site control (edit/delete anyone's content, see all settings, no ownership checks) | [`UsersApi.cs`](src/BlogIt/Api/UsersApi.cs) (comment added in place) | By design for now — matches the "every authenticated user is trusted" model | You want to invite someone you don't fully trust with the whole site — see "Foundation for roles" below first |

**Foundation for adding roles later, if you do:** `BlogPost.AuthorId` and `MediaFile.UploadedByUserId` already exist and are populated — ownership-based policies on Posts/Media need no schema change. `Page` has no owner FK yet, so ownership checks there need one small migration first. Authorization is standard ASP.NET Core policy-based (`BlogItDefaults.AdminAuthorizationPolicy`), so adding a stricter policy is additive, not a rewrite.

---

## Open — work through top to bottom

Ordered most to least severe. Each item is independent — fix and move to the next.

### 1. [x] ~~Setup wizard has a TOCTOU race — two simultaneous first-run requests can both create an admin user~~ — Fixed 2026-08-14
**Files:** [`SetupApi.cs`](src/BlogIt/Api/SetupApi.cs), [`SetupLock.cs`](src/BlogIt/Entities/SetupLock.cs), [`BlogItDbContext.cs`](src/BlogIt/Data/BlogItDbContext.cs), migration `20260814204048_AddSetupLock`.
Considered wrapping the check + insert in a `Serializable` transaction first, but that's a bad fit for Azure SQL: it needs isolation-level tuning that increases lock/blocking under Azure SQL's DTU-based throttling, and a bare `Database.BeginTransactionAsync()` breaks outright if a consumer later enables `EnableRetryOnFailure` (which Microsoft recommends for Azure SQL) — EF requires retry-wrapped transactions to go through `CreateExecutionStrategy().ExecuteAsync(...)`.

Went with a sentinel row instead: `SetupLock` has a single fixed-value primary key (`Id = 1`), inserted alongside the new `AppUser` in the same `SaveChangesAsync` call. At most one of two racing requests can win that insert — the loser hits a PK violation, which is caught and converted to `409 Conflict`. No isolation-level tuning, works under default `READ COMMITTED`, and needs no special handling if retries are enabled later since it's a single `SaveChangesAsync` call (EF Core retries those automatically without any wrapping).

Also added, since it came up in the same conversation: [`UseAzureSql(...)`](src/BlogIt/Providers/BlogItAzureSqlOptionsExtensions.cs) — same SQL Server provider as `UseSqlServer`, but with `EnableRetryOnFailure` turned on by default — and [`BlogItDbContext.ExecuteInTransactionAsync(...)`](src/BlogIt/Data/BlogItDbContextTransactionExtensions.cs), a documented helper for any *future* multi-step write that needs real atomicity, wrapping `CreateExecutionStrategy().ExecuteAsync(...)` correctly so it's safe to use once retries are on.

**Testing note:** EF Core's InMemory provider (used in the test suite) doesn't emulate this faithfully in two ways, both documented in code comments at the call sites — worth knowing if you touch this code later: (1) it throws a bare `ArgumentException` for a duplicate-key insert instead of wrapping it in `DbUpdateException` like a real relational provider does; (2) a single `SaveChangesAsync` call isn't atomic there — a failure partway through doesn't roll back entities already written earlier in the same call, unlike a real relational provider's implicit per-`SaveChanges` transaction. Both are known InMemory-provider limitations, not something specific to this fix. Regression test: `SetupApiTests.Initialize_WhenSetupLockAlreadyClaimed_ReturnsConflictAndDoesNotCreateUser`.

### 2. [x] ~~Site URL accepted with no validation, client or server~~ — Fixed 2026-08-14
**Files:** [`UrlValidator.cs`](src/BlogIt/Helpers/UrlValidator.cs) (new shared helper), [`SetupApi.cs`](src/BlogIt/Api/SetupApi.cs), [`SettingsApi.cs`](src/BlogIt/Api/SettingsApi.cs), [`Setup.razor`](src/BlogIt.Admin/Pages/Setup.razor), [`SiteSettings.razor`](src/BlogIt.Admin/Pages/Settings/SiteSettings.razor).
`UrlValidator.IsValidAbsoluteHttpUrl` (server) checks for an absolute `http`/`https` URL; `SetupApi`'s `/initialize` and `SettingsApi`'s `PUT /settings` both reject an invalid Site URL with a `400` validation problem before saving. `BlogIt.Admin` can't reference the core `BlogIt` project (only `BlogIt.Contracts`), so the client-side checks in `Setup.razor` and `SiteSettings.razor` are a small local duplicate of the same logic rather than a shared reference. Tests: `SetupApiTests.Initialize_WithInvalidSiteUrl_ReturnsValidationProblemAndCreatesNoUser`, `SettingsApiTests.UpdateSettings_WithInvalidSiteUrl_ReturnsBadRequest`/`_WithValidSiteUrl_Persists`.

### 3. [x] ~~No server-side password strength/length enforcement~~ — Fixed 2026-08-14
**Files:** [`PasswordPolicy.cs`](src/BlogIt/Helpers/PasswordPolicy.cs) (new shared helper), [`SetupApi.cs`](src/BlogIt/Api/SetupApi.cs), [`UsersApi.cs`](src/BlogIt/Api/UsersApi.cs), [`AuthApi.cs`](src/BlogIt/Api/AuthApi.cs).
Policy (per explicit decision): minimum 8 characters, at least one uppercase letter, one lowercase letter, one digit. `PasswordPolicy.Validate(string)` returns the first unmet rule as a message; called from setup, user creation, and change-password, each returning a `400` validation problem on failure. Tests: weak-password cases (too short, no uppercase, no lowercase, no digit) added across `SetupApiTests`, `UsersApiTests`, `AuthApiTests`.

### 4. [x] ~~Shared, mutable `TokenValidationParameters` re-fetched and mutated on every authenticated request~~ — Fixed 2026-08-14
**Files:** [`JwtSigningKeyCache.cs`](src/BlogIt/Services/JwtSigningKeyCache.cs) (new), [`BlogItServiceCollectionExtensions.cs`](src/BlogIt/BlogItServiceCollectionExtensions.cs).
Replaced the per-request mutation of the shared `TokenValidationParameters.IssuerSigningKey` with `TokenValidationParameters.IssuerSigningKeyResolver`, backed by a singleton `JwtSigningKeyCache` that only rebuilds the `SymmetricSecurityKey` when the secret actually changes (an atomic snapshot swap — no partial-update races for concurrent readers). `JwtBearerOptions` is now configured via `services.AddOptions<JwtBearerOptions>(scheme).Configure<JwtSigningKeyCache>(...)` (the DI-aware overload) specifically so the cache can be captured once by reference — the resolver delegate itself has no access to `HttpContext.RequestServices`. Verified via the full auth-dependent test suite (all `WithAuth`-based integration tests exercise this path); no dedicated unit test since the fix is purely internal wiring with no new observable behavior.

### 5. [x] ~~No title validation on posts — client or server~~ — Fixed 2026-08-14
**Files:** [`PostsApi.cs`](src/BlogIt/Api/PostsApi.cs), [`PostEdit.razor`](src/BlogIt.Admin/Pages/Posts/PostEdit.razor).
`CreatePost`/`UpdatePost` both reject a blank/whitespace `Title` with a `400` validation problem before touching the database, matching the existing slug/schedule validation pattern; `PostEdit.razor`'s `Save` throws client-side before calling the API. Tests: `PostsApiTests.CreatePost_WithBlankTitle_ReturnsBadRequest`, `UpdatePost_WithBlankTitle_ReturnsBadRequestAndDoesNotClearTitle` (confirms the original title survives a rejected update).

### 6. [x] ~~`/sitemap.xml` and `/robots.txt` trust the raw `Host` header instead of the configured Site URL~~ — Fixed 2026-08-14
**Files:** [`SiteUrlResolver.cs`](src/BlogIt/Helpers/SiteUrlResolver.cs) (new shared helper, extracted from `FeedService`'s private `ResolveSiteUrl`), [`SitemapApi.cs`](src/BlogIt/Api/SitemapApi.cs), [`FeedService.cs`](src/BlogIt/Services/FeedService.cs).
Both `/sitemap.xml` and `/robots.txt` now resolve the base URL through `SiteUrlResolver.Resolve` — `ISettingsService`'s `SiteUrl` first, then `IConfiguration`, only falling back to the request's `Host` header when neither is configured — the exact precedence `FeedService.ResolveSiteUrl` already used for `/rss.xml`/`/atom.xml`, now shared instead of duplicated. `SitemapApi`'s inline lambdas were also refactored into named `GetSitemapAsync`/`GetRobotsAsync` static methods (matching `FeedsApi`'s existing pattern) so they're directly unit-testable without a running server. Tests: `SitemapApiTests` — reproduces the audit's exact live-exploited scenario (spoofed `Host: evil.attacker.example` header with `SiteUrl` configured) and confirms the spoofed host never appears in the output, plus a fallback-to-request-origin case when nothing is configured.

### 7. [x] ~~Public media endpoint has no `X-Content-Type-Options: nosniff`, and caches for a year regardless of type~~ — Fixed 2026-08-14
**File:** [`MediaProxyApi.cs`](src/BlogIt/Api/MediaProxyApi.cs).
Added `X-Content-Type-Options: nosniff` unconditionally in `ServeMedia`, matching the header `AdminAssetMiddlewareContributor` already sets elsewhere. Per explicit decision, the year-long cache was left unscoped (not narrowed to a safe-type allow-list) — this was the minimal fix, not the optional extended one. Test: `MediaStorageIntegrationTests` now asserts the header on a real upload-then-download round trip.

### 8. [x] ~~Admin shell loads a third-party CDN script/stylesheet with no version pin and no Subresource Integrity~~ — Fixed 2026-08-14
**Files:** `src/BlogIt.Admin/wwwroot/lib/easymde/dist/easymde.min.{js,css}` (new, vendored), [`index.html`](src/BlogIt.Admin/wwwroot/index.html).
Per explicit decision, vendored rather than pinned-with-SRI: downloaded EasyMDE 2.21.0 (the version `unpkg.com/easymde` currently resolves to; verified as the legitimate MIT-licensed package before saving) into `wwwroot/lib/easymde/dist/`, matching the vendoring pattern already used for Bootstrap elsewhere in the repo. `index.html` now references `lib/easymde/dist/easymde.min.{js,css}` instead of the `unpkg.com` CDN — no runtime third-party dependency for the admin shell at all, so no SRI hash to maintain either. These files are served through the same static-file pipeline as the rest of `wwwroot`, which already sets `nosniff` and immutable caching for fingerprinted assets. Test: `AdminAssetIntegrationTests` confirms the shell no longer references `unpkg.com` and that the vendored script is actually servable with the `nosniff` header present.

### 9. [x] ~~Admin sidebar is completely inaccessible below 768px viewport width~~ — Fixed 2026-08-14
**Files:** [`AdminLayout.razor`](src/BlogIt.Admin/Layout/AdminLayout.razor), [`admin.css`](src/BlogIt.Admin/wwwroot/css/admin.css)
Added a hamburger toggle button to the topbar, wired to a `sidebarOpen` boolean that applies a `sidebar-open` class overriding `left: -240px` back to `0` below 768px, plus a click-outside overlay and auto-close on navigation. Verified: the CSS cascade (`.sidebar.sidebar-open { left: 0; }`) was validated directly against the served stylesheet, and the toggle correctly flips `sidebarOpen`/the DOM class on click; the toggle button itself is confirmed hidden (`display: none`) above 768px so desktop is unaffected. Live visual screenshot wasn't obtainable in this session (Browser pane wasn't in a compositing/visible state), so give it a manual look on a phone-width window to confirm.

### 10. [x] ~~Unbounded public search — no pagination, no result cap~~ — Fixed 2026-08-14
**Files:** [`PublicContentService.cs`](src/BlogIt/Services/PublicContentService.cs) (`SearchPostsAsync`), [`Search.razor`](samples/BlogIt.Sample/Components/Pages/Search.razor).
`SearchPostsAsync` now takes `page`/`pageSize` and returns the same `PublicPostPage` shape as `GetPostsAsync`, with an explicit `.Select()` projection so the (potentially large) `Content` column is never loaded — only a SQL-translatable `Content != null` check for the existing `HasFullContent` flag. `Search.razor` gained the same pagination UI already used on `/archive`. Test: `PublicContentServiceTests.SearchPostsAsync_PaginatesResultsAndExcludesFullContent`.

### 11. [x] ~~Preview-token store grows unboundedly for links that are never opened~~ — Fixed 2026-08-14
**Files:** [`PreviewTokenService.cs`](src/BlogIt/Services/PreviewTokenService.cs) (`SweepExpired`), [`PublicationSchedulingService.cs`](src/BlogIt/Services/PublicationSchedulingService.cs).
Added `IPreviewTokenService.SweepExpired()`, which removes expired grants directly from the backing dictionary. Piggybacked on `PublicationSchedulingService`'s existing 30-second timer (as the audit suggested) rather than adding a second `IHostedService` — it now calls `SweepExpired()` once per tick alongside its own schedule processing. Test: `PreviewTokenServiceTests.SweepExpired_RemovesOnlyExpiredNeverLookedUpGrants`, via a test-only `internal GrantCount` property (`InternalsVisibleTo` already existed for this project).

### 12. [x] ~~AI endpoints have no exception handling — internal error details can leak~~ — Fixed 2026-08-14
**File:** [`AiApi.cs`](src/BlogIt/Api/AiApi.cs) (`SendMessage`, `ExportDraft`, new `HandleAiFailure` helper).
Both handlers now catch around the `IAiService` call: a `KeyNotFoundException` (conversation deleted between the existence check and the call) maps to `404`; `InvalidOperationException` — the only exception type `AiService` throws intentionally, always with a safe, non-sensitive message ("AI API key is not configured.", "The AI provider completed the request without returning any text.") — maps to `400` with that message surfaced; anything else (provider HTTP failures, network errors) is logged server-side with the full exception and returns a generic `502` with no exception details in the response body. Tests: `AiApiTests` — one confirms the `400`+message path, two confirm a deliberately sensitive-looking exception message (fake internal IP/secret string) never appears in the client-visible response body.

---

## Testing note (applies throughout)

EF Core's InMemory provider (used across the test suite) doesn't fully emulate a real relational provider in two ways that came up while fixing #1 and are worth knowing if you touch related code later: it throws a bare `ArgumentException` instead of `DbUpdateException` for a duplicate-key insert, and a single `SaveChangesAsync` call isn't atomic there — a failure partway through doesn't roll back entities already written earlier in the same call. Both are documented in code comments at the relevant call sites (`SetupApi.cs`).

### 13. [x] ~~Unbounded AI conversation history sent on every message~~ — Fixed 2026-08-14
**Files:** [`AiService.cs`](src/BlogIt/Services/AiService.cs), [`AiConversation.cs`](src/BlogIt/Entities/AiConversation.cs), migration `20260814205906_AddAiConversationSummary`.
Rolling compaction, chosen over a plain sliding window (loses old context silently) or full token-budget accounting (more precise but needs a tokenizer dependency, judged unnecessary for what's a short brainstorming aid, not a long-running assistant): once a conversation reaches `HistoryCompactionThreshold` (20) messages, the oldest half is sent to the LLM to summarize, folded into `AiConversation.Summary`, and those rows are deleted. From then on, the summary rides along as a system message ahead of the remaining raw messages on every turn — the compacted messages are never sent again. Because only half compacts each round, the conversation sits at 10 messages right after; it takes another 10 new messages to trigger the next round, not 20 — matches the "N, then N/2, then N/2 again" policy discussed. `ExportToDraftAsync` (brainstorm → draft post) also prepends the summary, so exporting a long conversation doesn't silently lose the compacted portion.

The "which messages to compact, and when" decision (`AiService.SelectCompactionBatch`) is deliberately a pure, `internal`-visible static method so it's unit-testable without a real LLM — the actual summarization call isn't independently tested, consistent with the rest of `AiService`, which has no direct unit tests today (only exercised through `AiApiTests` via a fake `IAiService` substitute). Tests: `AiHistoryCompactionTests` (6 cases covering below-threshold, at-threshold, odd counts, and repeated rounds).

### 14. [x] ~~Dead code and an obsolete API call~~ — Fixed 2026-08-14
**Files:**
- [`MediaList.razor`](src/BlogIt.Admin/Pages/Media/MediaList.razor) — unused `pendingFile` field deleted.
- [`AnalyticsService.cs`](src/BlogIt/Services/AnalyticsService.cs) — migrated off `GoogleCredential.FromJson`. Note: the deprecation message's suggested replacement, `GoogleCredential.FromJsonParameters`, turned out to be *also* obsolete with the identical warning — the actual non-obsolete path is `CredentialFactory.FromJson<ServiceAccountCredential>(json).ToGoogleCredential()`. Solution build is now warning-free (`0 Warning(s)`).

---

## Closed out

All 14 findings in this document are fixed and verified (169/169 tests passing as of the last run). The "accepted risks" table above is the only remaining open decision — those are intentional, not bugs, and stay that way unless the trust model changes.
