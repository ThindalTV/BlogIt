# BlogIt Admin — Code Audit & Manual Test Report

> **Update (2026-08-14):** See the [Resolution Log](#resolution-log-2026-08-14) at the end of
> this document for what's been fixed since this audit, and for the project's explicit,
> deliberate decisions to accept several of these findings as by-design rather than fix them.

**Date:** 2026-08-09
**Scope:** `src/BlogIt` (core engine, API, services, hosting), `src/BlogIt.Admin` (Blazor WASM admin front-end), `src/BlogIt.Contracts`, `src/BlogIt.AzureStorage`. **Excluded per request:** `src/BlogIt.MauiAdmin` (incomplete/WIP).
**Method:** Static read-only review of all API endpoints, services, middleware, hosting glue, and the admin Blazor app, plus two rounds of live manual/integration testing of the running app — full startup wizard, login/logout, dashboard, post creation/editing/publishing/scheduling/tagging, page creation/editing/publishing, redirects, users, settings, change password, and media upload — using `samples/BlogIt.Sample` against a local SQL Server (LocalDB) instance. No source files were modified. No git actions taken. All test data created during the audit (test posts, pages, users, media files) was deleted afterward, restoring the database to a clean single-admin state.

Findings are grouped by severity. Each includes file:line, the defect, and a concrete failure scenario. Where something was actually exercised live (not just read in source), that's noted — and in this round, several were **live-exploited with a working proof of concept**, not just read in source.

---

## Critical

### 0. Confirmed working exploit: uploading a file with a spoofed `Content-Type` gets same-origin script execution
**Files:** [src/BlogIt/Api/MediaApi.cs:48-86](src/BlogIt/Api/MediaApi.cs), served by [src/BlogIt/Api/MediaProxyApi.cs](src/BlogIt/Api/MediaProxyApi.cs)

This started as finding #2 in the first pass (code-review only). I went back and actually exploited it, per your suggestion to test media upload with a file the UI doesn't claim to support. The admin Media page's upload control declares `accept="image/*,video/*,application/pdf"` ([MediaList.razor:11](src/BlogIt.Admin/Pages/Media/MediaList.razor)) — but that HTML `accept` attribute is purely a client-side picker-dialog hint; it is not enforced anywhere, client or server.

Live repro:
1. Uploaded a `File` object named `not-an-image.txt` with `type: "text/plain"` (bypassing the `accept` filter — trivial via drag-and-drop or "All Files" in the native picker even without JS, but I did it via JS `DataTransfer` to keep the test scriptable). **Result: uploaded successfully**, server stored and served it back with `Content-Type: text/plain` verbatim (confirmed via `curl -D -`) — no validation, no sniffing, no rejection.
2. Then uploaded a second file, `innocuous.html`, with `type: "text/html"` and body `<script>window.__mediaXss = true;</script>Hello from uploaded HTML`. **Result: uploaded successfully.** Navigated directly to the returned public URL (`/media/0309c448393a48f8b0c084640386a829.html`) and confirmed via `window.__mediaXss === true` that **the script executed**, same-origin, as a normal HTML page — indistinguishable from a real page on the site.

This is a stronger and more clear-cut finding than the Markdown/HTML-passthrough issue (finding #1 below): it doesn't depend on "authors are trusted to write their own content" — it's a file-upload feature whose own UI claims to restrict to images/video/PDF, silently accepting and executing arbitrary HTML instead. Any authenticated user (today: any `AppUser`, all equally privileged per finding #6) can do this, and the resulting URL can be shared/linked/embedded anywhere.

**Recommendation:** validate the upload server-side — derive `Content-Type` from actual file content (magic-byte sniffing) or a fixed extension allow-list, reject anything outside a known-safe set (or at minimum `text/html`, `image/svg+xml`, and other browser-executable types), and add `X-Content-Type-Options: nosniff` to the media-serving response regardless.

---

## High

### 1. Stored XSS via post/page Markdown content — confirmed live
**File:** [src/BlogIt/Helpers/MarkdownHelper.cs:7-10](src/BlogIt/Helpers/MarkdownHelper.cs)

The Markdig pipeline (`UseAdvancedExtensions()`, `UseEmojiAndSmiley()`) leaves Markdig's default raw-HTML passthrough enabled, and the result is injected into the public page as `@((MarkupString)...)` with no HTML sanitizer. **I verified this live**: I created a post with content containing `<script>window.__xssFired = true;</script>` and `<img src=x onerror="window.__xssFired2 = true">`, published it, and loaded the public post page (`/2026/test-post-xss-check`). Both scripts executed — confirmed via `window.__xssFired` / `window.__xssFired2` both evaluating to `true` in the live page.

**⚠️ Scoping note per your feedback:** every `AppUser` today is fully and equally privileged (see finding #6) — there's no "trusted admin" vs. "lower-trust author" distinction anywhere in the code. If BlogIt's intended model is "a single trusted operator authors everything," then unsanitized HTML in your own content is arguably a *feature*, not a bug (same tradeoff WordPress makes with the `unfiltered_html` capability for admins). I'm not asking you to sanitize just because `<script>` "shouldn't be allowed" in the abstract — I'm flagging it because:
- The admin UI already supports creating additional users with zero role distinction, so the moment you invite a second person to help write posts, they get the same script-injection blast radius as the owner, with no setting to prevent it.
- It compounds with finding #10 (JWT in `localStorage`): if any author (trusted or not) pastes in un-reviewed content (e.g. copy-pasted from an AI tool, a contributor's draft, an embed snippet from an untrusted source), a mistake there becomes full session-token theft for whoever views it, including other admins.

**Recommendation to consider, not a mandate:** either (a) accept this as by-design and document it ("BlogIt authors are fully trusted; do not grant author access to anyone you wouldn't give shell access to"), or (b) sanitize by default with an explicit opt-out setting for people who intentionally want raw HTML/embeds in content.

### 2. (See Critical #0 above — now confirmed live) Media upload trusts client-supplied `Content-Type` with no allow-list
This was originally written up here as a code-review-only finding; it's been promoted to Critical #0 above after live exploitation confirmed full same-origin script execution via a spoofed-Content-Type upload. Left as a pointer so the numbering stays stable for anyone who read the first version of this report.

### 3. TOCTOU race in first-run setup (`/setup/initialize`)
**File:** [src/BlogIt/Api/SetupApi.cs:25-40](src/BlogIt/Api/SetupApi.cs)

```csharp
if (await db.Users.AnyAsync())
    return Results.Conflict("Setup has already been completed.");
// ...builds user...
await db.SaveChangesAsync();
```

This is a classic check-then-act with no transaction/serializable isolation and no unique constraint preventing "more than one user created during first run." I confirmed the *sequential* case works correctly — after finishing the wizard, a follow-up `POST /api/setup/initialize` correctly returns `409 Conflict "Setup has already been completed."` and the client-side route also redirects away from `/blogit/setup` once already configured. What's **not** protected is genuine concurrency: two simultaneous `POST /setup/initialize` requests can both pass the `AnyAsync()` check before either commits, since there's no unique index or transaction serializing the check-and-insert. In practice this is a narrow window (the gap between a fresh deploy going live and the real operator completing the wizard), but it's a real gap, not a false positive — I did not attempt to actually win the race live since that would require precise request timing against the exact deploy moment, which isn't practical to demonstrate safely in this environment.

**Recommendation:** wrap the check+insert in a serializable transaction, or rely on the existing unique index on `Username` plus a small retry, or simplest: add an application-level distributed lock keyed on a fixed "setup" resource.

### 3b. Confirmed reproducible server crash (500) when adding a new tag to a post that already has tags
**Files:** [src/BlogIt/Api/PostsApi.cs:119-162](src/BlogIt/Api/PostsApi.cs) (`UpdatePost`), [src/BlogIt/Helpers/TagResolver.cs](src/BlogIt/Helpers/TagResolver.cs)

Found this doing ordinary editorial workflow testing, not edge-case probing. **Clean, isolated, 100%-reproducible repro:**
1. Create a new post with tags `alpha, beta` via the admin UI → saves fine, tags persist correctly.
2. Edit that same post, change the tags field to `alpha, beta, gamma` (i.e. keep the two existing tags, add one brand-new one), click Save Draft (or Publish) → **the request throws an unhandled `DbUpdateException`** and the admin UI shows `Save failed: net_http_message_not_success_statuscode_reason, 500, Internal Server Error`.

Server-side stack trace (captured directly from the running dev server):
```
Microsoft.EntityFrameworkCore.DbUpdateException: An error occurred while saving the entity changes. See the inner exception for details.
 ---> Microsoft.Data.SqlClient.SqlException: The INSERT statement conflicted with the FOREIGN KEY constraint
      "FK_BlogPostTag_Tags_TagsId". The conflict occurred in database "BlogIt", table "dbo.Tags", column 'Id'.
   at BlogIt.Api.PostsApi.UpdatePost(Guid id, UpdateBlogPostRequest req, BlogItDbContext db) in .../PostsApi.cs:line 160
```
I reproduced this twice independently (once as a side effect while testing something else, once in a clean isolated repro as described above) — both times the exact same FK violation on `BlogPostTag → Tags`. I did **not** reproduce it on `CreatePost` (a brand-new post created with 2 brand-new tags in one request saves fine) — it's specific to `UpdatePost`, where the post entity is already tracked by EF Core (loaded via `.Include(p => p.Tags)`) and `TagResolver.ResolveAsync` builds a replacement collection mixing already-tracked existing `Tag` entities with newly-constructed, not-yet-tracked ones (`TagResolver.cs:26-30`). My best-effort read is that EF Core's change tracker isn't picking up the new, untracked `Tag` object as `Added` when it's merged into a reassigned collection on an already-tracked parent this way — but I'd treat that as a working theory for whoever investigates, not a confirmed root cause; the important, verified fact is the crash itself and its exact trigger condition.

**Impact:** this breaks a core, everyday editorial action — adding one more tag to an existing tagged post — with a full 500 and a raw, unfriendly error message, for any post whose tag set already contains at least one prior tag. Pages are unaffected (Pages have no tags at all).

**Recommendation:** reproduce under a unit/integration test isolating `TagResolver.ResolveAsync` + `UpdatePost`, then either explicitly `db.Tags.Add()` the newly-constructed tags before assigning the collection, or restructure `TagResolver` to attach new tags to the context explicitly rather than relying on implicit cascade tracking through a reassigned navigation property.

---

## Medium

### 4. `PublicContentService.GetPageAsync`/`GetPostAsync` don't filter by publish state
**File:** [src/BlogIt/Services/PublicContentService.cs:196-205](src/BlogIt/Services/PublicContentService.cs)

Unlike the list methods, which filter to published-only, the single-item lookups return the row purely by slug — the caller must remember to check `IsPublished` itself outside the preview-token flow. The shipped sample does this correctly (`BlogPostPage.razor`/`CustomPage.razor` both check), but it's an easy footgun for any other consumer of this library: forget the check once, and draft/unpublished content leaks to anonymous visitors. Inconsistent with the multi-item methods, which enforce it themselves — I'd make the single-item methods consistent with that pattern (or at minimum rename them / add an XML-doc warning) rather than relying on every caller remembering.

### 5. Client-side-only URL validation on Setup wizard "Site URL" — confirmed live
**File:** [src/BlogIt.Admin/Pages/Setup.razor](src/BlogIt.Admin/Pages/Setup.razor) (Step 2), persisted via [src/BlogIt/Api/SetupApi.cs:44](src/BlogIt/Api/SetupApi.cs)

Live test: entering `not-a-valid-url` in the Site URL field during setup was accepted with no validation error, saved, and later appeared verbatim in Settings → Site → Site URL. There's no server-side URL-format check either (`SetupApi.cs` stores `request.SiteUrl` unvalidated). This value is used for SEO/OG tags, sitemap generation, and canonical URLs — a malformed value here will silently break those features rather than fail loudly at setup time.

### 6. No role/permission tiers — every `AppUser` is fully privileged — confirmed live with a real second account
**Files:** [src/BlogIt/Api/UsersApi.cs](src/BlogIt/Api/UsersApi.cs), [src/BlogIt.Admin/Pages/Users/UserList.razor](src/BlogIt.Admin/Pages/Users/UserList.razor)

`CreateUser` (`UsersApi.cs:33-54`) has no role field at all, and I confirmed live that the Users list UI shows no role column — just Username / Display Name / Created / Actions. I went further than reading the code: I created a second account (`contributor`) through the admin UI exactly the way an operator would when inviting a collaborator, logged in as it, and confirmed it could immediately: see and edit/publish/delete posts authored by `admin`; see the Dashboard's aggregate counts across all content regardless of author; and open Settings and see/edit the AI provider config, Google Analytics credentials, and JWT expiry — the exact same page `admin` sees, no restrictions. Every endpoint gated by `BlogItDefaults.AdminAuthorizationPolicy` only checks "is authenticated," not any finer permission — there is no ownership check anywhere in `PostsApi`/`PagesApi`/`MediaApi`. As noted in finding #1, this means "invite a second author" and "grant full site compromise" are currently the same action. If multi-author support with restricted roles is on your roadmap, this is the place to start; if BlogIt is meant to always be single-owner, this and finding #1 should probably be documented together as an explicit trust boundary. (Test account deleted after confirming.)

### 7. Username-enumeration / timing side channel on login
**File:** [src/BlogIt/Services/AuthService.cs:16-20](src/BlogIt/Services/AuthService.cs)

```csharp
var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
    return null;
```
Short-circuit evaluation means the deliberately-slow `BCrypt.Verify` (~50-100ms) only runs when the username exists. A non-existent username returns almost instantly; a valid username with a wrong password takes measurably longer. The **response body** is identical either way (I confirmed the UI shows a generic "Invalid username or password." for both cases), but the timing difference is still present in the code and is a well-known side channel for username enumeration.

**Recommendation:** always run a (dummy) BCrypt verify even when the user isn't found, to equalize timing.

### 8. No server-side password strength/length enforcement
**Files:** [src/BlogIt/Api/SetupApi.cs:25-40](src/BlogIt/Api/SetupApi.cs), [src/BlogIt/Api/UsersApi.cs:33-54](src/BlogIt/Api/UsersApi.cs), [src/BlogIt/Api/AuthApi.cs](src/BlogIt/Api/AuthApi.cs) (change-password)

I tested this live during setup: entering a single-character password (`a`) for the admin account is accepted by the client as long as confirm-password matches (client only checks non-empty + match, [Setup.razor NextStep()]). I did not carry that single-char password through to `Finish Setup` (used a strong password instead to keep the rest of the walkthrough realistic), but the server-side code path (`SetupApi.cs:37`, `BCrypt.Net.BCrypt.HashPassword(request.Password)`) has no length/complexity check at all, so calling the API directly would accept it. Same absence of server-side checks applies to `CreateUserRequest.Password` and `ChangePasswordRequest.NewPassword`.

### 9. Shared, mutable `TokenValidationParameters` re-fetched and mutated on every request
**File:** [src/BlogIt/BlogItServiceCollectionExtensions.cs:71-84](src/BlogIt/BlogItServiceCollectionExtensions.cs)

`OnMessageReceived` re-fetches the JWT secret from settings and mutates `context.Options.TokenValidationParameters.IssuerSigningKey` on the shared singleton `JwtBearerOptions` for every incoming authenticated request, rather than building/caching validation parameters once (or per secret-version). Adds an avoidable settings-cache round trip to every authenticated request, and mutates shared state from concurrently-executing request pipelines — if the JWT secret is ever rotated via `SettingsApi.SaveSettings` while requests are in flight, in-flight validations can race non-deterministically against the mutation.

### 10. Unbounded AI conversation history sent on every message
**File:** [src/BlogIt/Services/AiService.cs](src/BlogIt/Services/AiService.cs) (`SendMessageAsync`)

The full message history is sent to the chat-completion call on every turn with no truncation, token-budget accounting, or message-count cap. A long-running conversation will eventually exceed the provider's context window, at which point every subsequent message in that conversation throws and the conversation becomes permanently unusable (no error recovery/graceful truncation). (Not exercised live per your instruction to skip AI testing since no provider is configured.)

### 10b. No title validation on posts (client or server) — confirmed live, with a visible symptom
**Files:** [src/BlogIt.Admin/Pages/Posts/PostEdit.razor:44](src/BlogIt.Admin/Pages/Posts/PostEdit.razor), [src/BlogIt/Api/PostsApi.cs:147](src/BlogIt/Api/PostsApi.cs) (`post.Title = req.Title;`)

Cleared the Title field on an existing post and clicked Save Draft — it saved successfully with no validation error either client- or server-side (`UpdatePost`/`CreatePost` assign `Title` directly with zero null/empty checks, and the Blazor form has no required-field validation on it either). The visible symptom: the Posts list then shows a table row with **completely blank title text and no link** — `<a href="posts/{id}"></a>` renders as nothing clickable. The post's browser tab title also goes blank (`— BlogIt Admin`). This would also break SEO fallback (title tag) and any public listing that renders `post.Title` as link text.

**Recommendation:** add a required, non-whitespace check for `Title` server-side in both `CreatePost` and `UpdatePost` (returning a validation problem like the existing slug/schedule checks do), and mirror it client-side before submit.

### 10c. Clicking "Save Draft" on an already-published post silently unpublishes it — no confirmation
**File:** [src/BlogIt.Admin/Pages/Posts/PostEdit.razor:249-250](src/BlogIt.Admin/Pages/Posts/PostEdit.razor)

```csharp
if (publish && post?.IsPublished != true) await Api.PublishPostAsync(Id.Value);
else if (!publish && post?.IsPublished == true) await Api.UnpublishPostAsync(Id.Value);
```

This is intentional per the code — "Save Draft" literally means "ensure this post's state is Draft" — but it's a real safety hazard in the UI as shipped: the "Save Draft" and "Update & Publish" buttons sit right next to each other in the header ([PostEdit.razor:18-21](src/BlogIt.Admin/Pages/Posts/PostEdit.razor)), and clicking the wrong one on a **live, publicly-visible post** takes it offline immediately, with zero confirmation dialog and no "you're about to unpublish a live post" warning — contrast with Delete actions elsewhere in the app, which do use `ConfirmDialog`. I verified this live: editing a published post and clicking Save Draft flipped its status straight to Draft (confirmed via the Posts list showing `DRAFT` immediately after, no dialog ever appeared).

**Recommendation:** either rename the button to make the state-change explicit when editing a published post (e.g. "Unpublish & Save" ), or gate it behind the same `ConfirmDialog` pattern used for destructive actions elsewhere in the app.

### 10d. Public post/page "not found" pages return HTTP 200 instead of 404 ("soft 404")
**Files:** [samples/BlogIt.Sample/Components/Pages/CustomPage.razor:14](samples/BlogIt.Sample/Components/Pages/CustomPage.razor), [samples/BlogIt.Sample/Components/Pages/BlogPostPage.razor:15](samples/BlogIt.Sample/Components/Pages/BlogPostPage.razor)

Confirmed live: unpublishing a page and requesting its old public URL correctly shows "Page Not Found" content, but `curl -o /dev/null -w '%{http_code}'` against the same URL returns `200`, not `404`. Neither Razor component sets `HttpContext.Response.StatusCode = 404` when rendering the not-found branch. This is in the **shipped sample host**, not the BlogIt library itself, but since it's the reference implementation most consumers will copy from, it's worth fixing there — a "soft 404" like this is a well-known SEO problem (search engines index the not-found page as if it were real content) and breaks any monitoring/uptime tooling that checks status codes rather than body content.

---

## Low

### 11. JWT stored in `localStorage`
**Files:** [src/BlogIt.Admin/Services/LocalStorageService.cs](src/BlogIt.Admin/Services/LocalStorageService.cs), [src/BlogIt.Admin/Services/AuthStateProvider.cs](src/BlogIt.Admin/Services/AuthStateProvider.cs)

Standard practice for Blazor WASM SPAs and low-risk on its own, but combines with finding #1: if the admin panel and public blog share an origin, a stored-XSS payload in post content could read `localStorage` and exfiltrate the token. Worth keeping in mind if you do decide to sanitize content per finding #1's recommendation — this is the concrete reason it would matter, not just theoretical hardening.

### 12. Media upload has no explicit size limit or extension allow-list
**File:** [src/BlogIt/Api/MediaApi.cs:48-86](src/BlogIt/Api/MediaApi.cs)

Beyond the Content-Type trust issue (#2), there's no `[RequestSizeLimit]`/length cap and no extension allow-list, relying entirely on Kestrel's default body-size limit. Any authenticated user can fill storage with arbitrarily large or numerous files.

### 13. Unhandled exceptions can surface internal error text
**Files:** [src/BlogIt/Api/AiApi.cs](src/BlogIt/Api/AiApi.cs), [src/BlogIt/Services/AiService.cs:33](src/BlogIt/Services/AiService.cs)

No global exception handler/problem-details filter is wired up for the AI endpoints (or generally, in the sample host). Exceptions like `"AI API key is not configured."`, provider HTTP errors, or a race-condition `KeyNotFoundException` (conversation deleted between an ownership check and use) propagate as unhandled 500s, potentially exposing internal exception details depending on host configuration.

### 14. Dead code: unused field, obsolete API usage
- **File:** [src/BlogIt.Admin/Pages/Media/MediaList.razor:100](src/BlogIt.Admin/Pages/Media/MediaList.razor) — `pendingFile` field (`IBrowserFile?`) is declared and never used; confirmed by the compiler (`CS0169` warning) and by reading the surrounding upload-handling code, which uses a different local variable path. Minor, but worth deleting since it reads as if file-upload state tracking is happening there when it isn't.
- **File:** [src/BlogIt/Services/AnalyticsService.cs:21](src/BlogIt/Services/AnalyticsService.cs) — `GoogleCredential.FromJson(string)` is obsolete per its own deprecation message ("potential security risk... use CredentialFactory instead"), confirmed via build warning `CS0618`. Not urgent, but the deprecation message specifically calls out a security concern, so it's worth tracking rather than ignoring indefinitely.

### 15. Redirect deletion has no confirmation dialog — inconsistent with the rest of the app
**File:** [src/BlogIt.Admin/Pages/Redirects/RedirectList.razor:85](src/BlogIt.Admin/Pages/Redirects/RedirectList.razor)

Confirmed live: clicking "Delete" next to a redirect deletes it immediately with no confirmation of any kind. Posts, Pages, and Users all use the shared `ConfirmDialog` component for their Delete actions (`PostList.razor:99-103`, `PageList.razor:70-74`, `UserList.razor:91-95`) — Redirects is the one list in the app that skips it (`Delete(redirect)` at `RedirectList.razor:85` calls `Api.DeleteRedirectAsync` directly with no `ConfirmDelete`/`showConfirm` step). Low severity since redirects are easy to recreate and not otherwise destructive, but it's an inconsistency a user is likely to get bitten by exactly because every other Delete button in the app pauses for confirmation and this one doesn't.

### 16. Redirect validation error messages aren't surfaced to the user
**File:** [src/BlogIt.Admin/Pages/Redirects/RedirectList.razor](src/BlogIt.Admin/Pages/Redirects/RedirectList.razor)

Confirmed live: submitting a protocol-relative redirect target (`//evil.com/phish`) is correctly rejected server-side with `400 Bad Request` and a specific message ("Internal targets must be valid local paths." per `RedirectPathValidator`), but the admin UI only shows the generic `net_http_message_not_success_statuscode_reason, 400, Bad Request` — the actual validation reason from `Results.ValidationProblem(...)`'s response body isn't parsed and displayed. Purely a UX gap (the validation itself is correct and working, see below) — a user gets a rejection with no clue why.

---

## Confirmed Bugs (found via manual UI testing, not the static pass)

### 17. Admin sidebar navigation is completely inaccessible below 768px viewport width
**Files:** [src/BlogIt.Admin/wwwroot/css/admin.css:1173-1182](src/BlogIt.Admin/wwwroot/css/admin.css), [src/BlogIt.Admin/Layout/AdminLayout.razor](src/BlogIt.Admin/Layout/AdminLayout.razor)

Found this by accident: my first browser window was 758px wide (just under the 768px breakpoint), and after logging in, the Dashboard rendered with **no visible navigation at all** — no way to reach Posts, Pages, Media, Users, Settings, or even Logout via the sidebar. I initially suspected a client-side routing bug, but inspection of the live DOM showed the `<nav class="sidebar">` element is present and populated correctly (`display: flex`, `visibility: visible`) but positioned at `x: -240` — off-screen to the left.

Root cause: the `@media (max-width: 768px)` rule in `admin.css:1173-1182` switches `.sidebar` to `position: fixed; left: -240px;` — an off-canvas pattern clearly intended to be paired with a hamburger-menu toggle button that slides it back in. **That toggle button does not exist anywhere in `AdminLayout.razor`** — the only element in the topbar is a static `✦` logo span with no click handler, and there's no JS/C# code anywhere that adds a class to bring the sidebar back on-screen. I confirmed this isn't just my window size being unusual: resizing to 1280×800 makes the sidebar render normally with `x: 0`, so the bug is specifically the sub-768px breakpoint.

**Impact:** on any device/window narrower than 768px — which includes essentially all phones and many tablets in portrait orientation, and any desktop browser window a user happens to narrow past that point — the admin panel is unusable beyond the Dashboard. There's no keyboard-accessible or URL-based workaround discovered (direct navigation via URL, e.g. `/blogit/posts`, does still work, so it's specifically a discoverability/UX-breaking bug, not a hard data-access block).

**Recommendation:** add a hamburger toggle button to `AdminLayout.razor`'s `<header class="topbar">` and wire it to toggle a class (e.g. `.sidebar-open`) that overrides `left: -240px` back to `left: 0`.

---

## What I checked and found sound (confirmed by reading the code, and in most cases live)

- **`RedirectPathValidator`** (`src/BlogIt/Api/RedirectsApi.cs:71-168`) — correctly blocks protocol-relative (`//`) targets, restricts external targets to `http`/`https` schemes only, rejects reserved/framework paths, caps lengths, and rejects self-referential redirects. Confirmed live: a `//evil.com/phish` redirect target was rejected with `400`; a valid internal redirect (`/old-page` → `/about-us-weird-slug`) was created and confirmed via `curl` to actually issue a real `301`; toggling "Permanent" off and re-saving correctly switched it to a real `302`. No open-redirect found; this is genuinely well-written validation.
- **Setup re-invocation is blocked server-side in the sequential case** — confirmed live via `curl` against `/api/setup/initialize` after setup completed: returns `409 Conflict "Setup has already been completed."` (the *concurrent* race described in finding #3 is a separate, narrower concern).
- **Login rejects wrong credentials correctly** — confirmed live, with a generic error message that doesn't leak whether the username or password was wrong.
- **Session persists correctly across full page reloads** (JWT round-trips through `localStorage` and is picked up by `AuthStateProvider` on a fresh WASM boot) — confirmed live by navigating directly to `/blogit/users` via URL after login and seeing it load authenticated rather than bouncing to `/login`.
- **Logout works correctly** — confirmed live: clicking Logout clears the `blogit_token` from `localStorage` and immediately redirects to `/login`; a subsequent direct navigation to a protected route (`/blogit/settings`) correctly bounces back to `/login` rather than showing stale cached content.
- **Already-authenticated users are redirected away from `/login`** — confirmed live: navigating to `/blogit/login` while a valid session exists redirects straight to the Dashboard instead of showing the login form again.
- **Change Password works correctly end-to-end** — confirmed live: wrong current-password is rejected (`400`), correct current-password + matching new-password succeeds, and I verified the change actually took effect by logging out and back in — the old password was then rejected and the new one accepted.
- **No raw SQL/string-concatenated queries found** anywhere in the reviewed code; all data access goes through EF Core LINQ, so classic SQL injection isn't a concern here.
- **`FileSystemMediaStorage`/`AzureBlobMediaStorage`** storage-key generation uses GUIDs with single-segment enforcement — no path-traversal vector found in how files are stored or retrieved by key (separate from the Content-Type trust issue in Critical #0, which is about what's served, not where it's stored).
- **Delete-own-account is blocked** (`UsersApi.cs:62-63`) — confirmed by reading the code: `DeleteUser` explicitly rejects `id == currentUserId` with `400 Bad Request`.
- **Duplicate username rejected** — confirmed live: creating a user with an already-taken username returns `409 Conflict` and the UI surfaces it.
- **Post and Page slugs are correctly normalized server-side** even when the admin sends garbage — confirmed live: typing `About Us !! Weird Slug` into the Page slug field produced a stored slug of `about-us-weird-slug` (`SlugHelper.Slugify` applied correctly), and the slug field is correctly disabled/locked in the UI once a post or page has been published for the first time, matching the server-side "can't change slug after first publish" rule.
- **Delete confirmation dialogs work correctly where present** — confirmed live for Posts, Pages, Users, and Media: each shows a modal naming the specific item before deleting, and Cancel/confirm both behave as expected. (Redirects is the one exception — see finding #15.)
- **Dashboard stat counts and Recent Posts are accurate** — confirmed live across several states (0 posts, 1 published, 1 published + N drafts) that the Published/Draft/Pages/Media counts and the Recent Posts table matched the actual database state exactly.
- **Publish/Unpublish quick-actions from the Posts list work correctly** and immediately reflect in both the list's status badge and the public site's HTTP response (verified a freshly-unpublished post's old URL starts returning "not found" content, and re-publishing brings it back).

---

## Environment note

The sample app (`samples/BlogIt.Sample`) is hardcoded to require a SQL Server connection string named `BlogItDb` and refuses to start without one (`Program.cs:26-31`), with an extra guard that blocks LocalDB/Trusted-Connection strings specifically when Aspire-related environment variables are present (`Program.cs:33-44`) — presumably to force local devs through the Aspire AppHost rather than accidentally testing against LocalDB. For this audit, LocalDB was used directly via `dotnet user-secrets` (a per-developer, non-repo config store) rather than editing any committed config file, to comply with "no file edits." Migrations applied cleanly with no errors on a fresh database. No source files, git history, or committed config was touched during this session.

**Testing coverage note:** per your instructions, AI (`/blogit/ai`) was skipped since no provider is configured in this environment, and Analytics was only tested for absence-of-integration behavior (dashboard correctly shows "Analytics not configured or no data available," the `/api/analytics/summary` endpoint correctly returns `404` when unconfigured) rather than actual metrics rendering. Everything else — Dashboard, Posts (create/edit/publish/unpublish/schedule fields/tags/SEO fields/search/filter/delete), Pages (create/edit/publish/unpublish/slug-locking/delete), Redirects (create/edit/delete/301 vs 302/validation), Media (upload/delete/content-type handling), Users (create/delete/duplicate-detection/privilege check), Settings, My Account (change password), and Logout — was exercised live via the running app, not just read in source. All test data was deleted afterward; the database was left with just the single `admin` account (password was changed during Change Password testing — final working password is `TestPassword123!New`, noted here since this is a disposable local test database, not a shared or production environment).

---

## Resolution Log (2026-08-14)

This section records what changed since the audit above, and the project's explicit decisions
on findings that were deliberately **not** changed. The threat model driving these decisions:
**every authenticated user (admin or otherwise-invited author) is fully trusted; only anonymous
visitors are not.** That's a narrower trust boundary than "trust nobody," and it changes which
findings above are bugs versus accepted tradeoffs.

### Fixed

| # | Finding | Fix |
|---|---|---|
| 3b | Tag-update crash (FK violation adding a new tag to an already-tagged post) | [`TagResolver.cs`](src/BlogIt/Helpers/TagResolver.cs) now explicitly calls `db.Tags.Add(...)` for newly-created tags instead of relying on EF Core to auto-track them when merged into an already-tracked post's navigation collection. |
| 18 | No rate limiting/lockout on `/api/auth/login` | Added ASP.NET Core's built-in rate limiter ([`BlogItServiceCollectionExtensions.cs`](src/BlogIt/BlogItServiceCollectionExtensions.cs)), partitioned per client IP, 10 attempts / 5 minutes, `429` on rejection. Applied to the login route only ([`AuthApi.cs`](src/BlogIt/Api/AuthApi.cs)). |
| 7 | Username-enumeration timing side channel on login | [`AuthService.cs`](src/BlogIt/Services/AuthService.cs) now always runs a `BCrypt.Verify` — against a precomputed dummy hash when the username isn't found — so a nonexistent username takes the same time as a real one with a wrong password. |
| 10c | "Save Draft" silently unpublishes a live post/page with no warning | [`PostEdit.razor`](src/BlogIt.Admin/Pages/Posts/PostEdit.razor) now shows a confirmation dialog before Save Draft unpublishes an already-published post. [`PageEdit.razor`](src/BlogIt.Admin/Pages/Pages/PageEdit.razor) shows an inline warning when unchecking "Published" on an already-published page. |
| 10d | Public not-found pages return HTTP `200` instead of `404` | Fixed, but this took three attempts because of real framework limitations in this .NET version's Blazor static SSR pipeline — documented in full in [`NotFoundResponseMiddleware.cs`](samples/BlogIt.Sample/NotFoundResponseMiddleware.cs)'s doc comment. Summary: (1) setting `HttpContext.Response.StatusCode = 404` directly in a page component silently discards the rendered body — verified empirically, the identical code with status `410` works fine, only `404` is special-cased; (2) `NavigationManager.NotFound()` + `UseStatusCodePagesWithReExecute` (Microsoft's documented pairing for this) throws `'HttpNavigationManager' already initialized` on the re-executed request. The working fix: `BlogPostPage.razor`/`CustomPage.razor` flag the request via `HttpContext.Items`, and `NotFoundResponseMiddleware` buffers the response body into memory for the duration of the request (so the real response never "starts"), then either passes the buffered content through unchanged or discards it and renders [`NotFoundDocument.razor`](samples/BlogIt.Sample/Components/NotFoundDocument.razor) fresh via `HtmlRenderer` (its own DI scope, so no NavigationManager conflict) with a real `404` status. Verified live: status is `404`, body is the full site-styled page. |
| 15 | Redirect deletion has no confirmation dialog | [`RedirectList.razor`](src/BlogIt.Admin/Pages/Redirects/RedirectList.razor) now uses the same `ConfirmDialog` pattern as Posts/Pages/Users/Media. |
| 16 | Redirect validation error messages aren't surfaced to the user | [`ApiClient.cs`](src/BlogIt.Admin/Services/ApiClient.cs)'s redirect create/update calls now read the actual server-returned validation message and throw it, instead of the generic `EnsureSuccessStatusCode` status-line text. |

A pre-existing test (`PreviewApiTests.PostPreview_RefreshesInRedeemingBrowserButRejectsReplay`)
asserted on the old `200`-with-"Post Not Found"-body behavior via `GetStringAsync` (which throws
on non-2xx); updated to assert `404` + body content via `GetAsync`, consistent with the finding
#10d fix. Full suite passes after the change.

### Accepted as intentional — not bugs, given the trust model above

| # | Finding | Decision |
|---|---|---|
| 0 / 2 | Media upload trusts client-supplied `Content-Type` (same-origin script execution via a mislabeled upload) | **Accepted.** The upload endpoint already requires authentication — only a trusted user can trigger this, not an anonymous visitor. Documented in a code comment at [`MediaApi.cs`](src/BlogIt/Api/MediaApi.cs). |
| 1 | Stored XSS via Markdown's raw-HTML passthrough in posts/pages | **Accepted.** Authors are trusted to write their own content, same tradeoff as WordPress's `unfiltered_html` admin capability. Documented in a code comment at [`MarkdownHelper.cs`](src/BlogIt/Helpers/MarkdownHelper.cs). |
| 20 | The same unsanitized HTML propagates into RSS/Atom feeds | **Accepted**, same root cause and reasoning as #1. |
| 6 | No role/permission tiers — every `AppUser` is fully privileged | **Accepted for now.** Inviting a second user is, by design, equivalent to granting full site control — document this before inviting anyone you don't fully trust. Documented in a code comment at [`UsersApi.cs`](src/BlogIt/Api/UsersApi.cs). Note: the codebase already has the ownership foreign keys (`BlogPost.AuthorId`, `MediaFile.UploadedByUserId`) needed to add real role/ownership checks later without a schema rework, should the trust model change — `Page` would need a similar owner FK added first. |

**Important scoping note:** finding #18 (login rate limiting) and #19 (sitemap trusting a
spoofed `Host` header, not yet fixed — see below) remain real, unrelated to this trust model:
they protect the login boundary and public SEO surface *from anonymous visitors*, which the
"admin content is trusted" decision does not cover.

### Still open (not addressed in this pass)

Findings #3 (setup TOCTOU race), #5 (Site URL validation), #8 (password strength), #9 (shared
mutable `TokenValidationParameters`), #10b (post title validation), #13 (unhandled AI exceptions),
#14 (dead code / obsolete API), #17 (admin sidebar broken below 768px), #19 (sitemap trusts
spoofed `Host` header), #21 (`nosniff` header on media responses), #22 (CDN script with no SRI),
#23 (unbounded public search), and #24 (preview-token memory growth) were not addressed in this
pass and remain open.

---

# Addendum (2026-08-11): Public-Facing Surface Audit

**Scope:** the anonymous/unauthenticated-facing surface specifically, which the pass above touched only in passing (finding #10d) while otherwise scoped to admin/API/services: the public sample site (`samples/BlogIt.Sample/Components/Pages/*.razor` — Home, Archive, Search, TagFilter, BlogPostPage, CustomPage — and `Components/Layout/MainLayout.razor`), the anonymous API endpoints (`SitemapApi`, `FeedsApi`, `MediaProxyApi`, `AuthApi`'s login, `SetupApi`), `PublicContentService`, `FeedService`, `UrlRedirectService` + `UrlRedirectMiddleware`, `PreviewTokenService`, the shared `SeoHead`/`GaScript` components, and (because it's served pre-authentication) the admin SPA shell `src/BlogIt.Admin/wwwroot/index.html`.

**Method:** same as above — static read-only review plus live testing against the same running app and LocalDB instance the first pass used (same single `admin` account, same password). New test data created to exercise these findings — one published post carrying script payloads, one uploaded `.txt` file, and two temporarily-changed settings — was deleted/reset immediately after use; verified the database was back to 0 posts, 0 media files, and both settings blank before finishing. No source files were modified and no git actions were taken.

All findings below are numbered continuing from the pass above and cross-reference it where relevant.

---

## High

### 18. No rate limiting, throttling, or lockout on `/api/auth/login`
**Files:** [src/BlogIt/Api/AuthApi.cs:13-19](src/BlogIt/Api/AuthApi.cs), [src/BlogIt/Services/AuthService.cs:14-29](src/BlogIt/Services/AuthService.cs)

Confirmed live: 12 consecutive `POST /api/auth/login` requests against the `admin` account with wrong passwords, fired back-to-back with no delay between them, all returned `401` in a uniform ~185–200ms (consistent with finding #7's BCrypt-verify timing signature — i.e., a real username). Nothing throttled, slowed, or blocked the sequence: no `429`, no increasing delay, no lockout after N attempts, no CAPTCHA. `AuthApi`'s `/login` route (necessarily `AllowAnonymous()` — you can't authenticate to log in) has no attempt counter, and `AuthService.LoginAsync` does a single unconditional DB lookup plus BCrypt verify with no failure-tracking state anywhere.

This compounds directly with finding #6 (every `AppUser` is fully privileged — there is no low-value account here to brute-force, only the keys to the whole site) and findings #1/#11 (owning the account gets you script-injection-capable content authorship, plus the session token). For a CMS whose entire trust model assumes "the operator is the only user, and they're fully trusted," this login endpoint is the single control protecting that entire boundary, and today nothing stands between an internet-facing deployment and unlimited password guesses. To be precise about the actual risk: this doesn't threaten a strong, unique, randomly-generated password (BCrypt plus network latency caps guessing at roughly single-digit requests/second per connection — brute-forcing true randomness this way is infeasible). What it does threaten is the realistic case — a reused or leaked password, or a weak one — via credential stuffing, which is how the overwhelming majority of real-world account compromises actually happen.

**Recommendation:** add a failed-attempt counter (per-username and/or per-IP) with escalating delay or temporary lockout past a small threshold (5–10 attempts), and/or apply ASP.NET Core's built-in rate-limiting middleware to the `/auth/login` route specifically.

---

## Medium

### 19. `/sitemap.xml` and `/robots.txt` always trust the raw `Host` header and never consult the configured Site URL
**Files:** [src/BlogIt/Api/SitemapApi.cs:10-12,56-62](src/BlogIt/Api/SitemapApi.cs) — contrast with [src/BlogIt/Services/FeedService.cs:187-210](src/BlogIt/Services/FeedService.cs) (`ResolveSiteUrl`), which gets this right for `/rss.xml`/`/atom.xml`

```csharp
app.MapGet("/sitemap.xml", async (BlogItDbContext db, IConfiguration config, HttpContext http) =>
{
    var baseUrl = config["SiteUrl"] ?? $"{http.Request.Scheme}://{http.Request.Host}";
```

`config["SiteUrl"]` reads from `IConfiguration` (appsettings.json / environment variables). But the Site URL an operator actually sets — via the Setup wizard or the Settings page — is persisted through `ISettingsService` into the DB-backed `SiteSettings` table ([SetupApi.cs:44](src/BlogIt/Api/SetupApi.cs), [SettingsApi.cs](src/BlogIt/Api/SettingsApi.cs)), a store `IConfiguration` never reads. Neither `appsettings.json` nor `appsettings.Development.json` in the shipped sample defines a `SiteUrl` key. The result: `config["SiteUrl"]` is `null` in every standard deployment, so `baseUrl` always falls through to the raw incoming `Host` header — unauthenticated, attacker-controlled, with no allow-list in front (`"AllowedHosts": "*"` at [appsettings.json:13](samples/BlogIt.Sample/appsettings.json)).

I confirmed this live with a decisive before/after test:
1. Before configuring a Site URL: `curl http://localhost:5107/sitemap.xml` → `<loc>http://localhost:5107/</loc>`. `curl -H "Host: evil.attacker.example" .../sitemap.xml` → `<loc>http://evil.attacker.example/</loc>`. Same pattern for `/robots.txt`'s `Sitemap:` line.
2. Logged in and set `SiteUrl` to `https://real-configured-site.example` via `PUT /api/settings` (confirmed saved via a follow-up `GET /api/settings`).
3. Re-ran both requests: **`/sitemap.xml` and `/robots.txt` output was byte-for-byte unchanged** — still reflecting whatever `Host` header was sent, completely ignoring the setting just configured.
4. For comparison, I sent the identical spoofed-`Host` request to `/rss.xml`: it **correctly ignored the spoofed Host** and used the configured Site URL instead (`https://real-configured-site.example/`), because `FeedService.ResolveSiteUrl` checks the DB-backed setting first and only falls back to the request origin when genuinely unconfigured — a fallback that's intentional and already covered by an existing test ([FeedsApiTests.cs:75-119](tests/BlogIt.Tests/Integration/FeedsApiTests.cs), `Atom_UsesRequestOriginFallbackAndLimitsItems`).

So this isn't a missing edge case — `SitemapApi` is reading the wrong configuration source entirely, unconditionally, even when Site URL is fully configured. Impact: any reverse proxy, CDN, or load balancer in front that doesn't forward the exact public-facing `Host` (common — and this app never calls `UseForwardedHeaders`, so it isn't even set up to try) produces a sitemap full of wrong internal URLs, which harms SEO on its own. Worse, if any caching layer in front caches `/sitemap.xml`/`/robots.txt` by path without varying on `Host` (a common caching misconfiguration), a single attacker request with a spoofed `Host` could poison the cached response for every later visitor and crawler — steering search-engine indexing, and `robots.txt`'s own `Sitemap:` directive, at an attacker-controlled domain.

**Recommendation:** have `SitemapApi.cs` check `ISettingsService`'s `SiteUrl` first, exactly like `FeedService.ResolveSiteUrl` does — ideally by extracting that method into a shared helper both call, so the two can't drift apart like this again.

### 20. Unsanitized post/page HTML (finding #1) also propagates into the public RSS/Atom feeds
**File:** [src/BlogIt/Services/FeedService.cs:61-62,104-105,221-227](src/BlogIt/Services/FeedService.cs)

Finding #1 covers stored XSS via Markdown's raw-HTML passthrough, scoped there to the public post/page HTML pages. I checked whether the same unsanitized content reaches the feeds too, since RSS/Atom is a second, independent public distribution channel with a different — and potentially lower-trust — audience than a browser visiting the site directly: feed readers, planet aggregators, email-digest tools.

Confirmed live: published a post with `summary = "...<script>window.__feedXss1=true;</script>"` and `content = "...<img src=x onerror=\"window.__feedXss2=true\">"`, then fetched `/rss.xml`:

```xml
<description>&lt;p&gt;Summary with a payload &lt;script&gt;window.__feedXss1=true;&lt;/script&gt;&lt;/p&gt;</description>
<content:encoded>&lt;p&gt;Body content with payload &lt;img src=x onerror="window.__feedXss2=true"&gt;&lt;/p&gt;</content:encoded>
```

The XML itself is well-formed — `XmlWriter.WriteElementString` correctly entity-escapes `<`/`>` at the XML layer, so this is not an XML-injection bug. But `<description>` and especially `<content:encoded>` are RSS's standard fields for full-fidelity HTML content (that's the entire purpose of the `content` module), and the Atom equivalent (`WriteTypedAtomElement`, FeedService.cs:221-227) goes further and explicitly marks both `<summary>` and `<content>` with `type="html"`. A feed reader doing exactly what these fields are specified for — decoding the entities and rendering the result as HTML — reconstitutes the live `<script>`/`onerror=` payload. Whether it executes depends entirely on that downstream reader's own sanitization (many do; not all do), which is a trust decision BlogIt is silently making on every subscriber's behalf.

This shares finding #1's root cause rather than introducing a new one, but it matters for remediation: a fix applied only where finding #1 was found (e.g., sanitizing at the Razor view layer, around the `MarkupString` usages) would leave this feed path exposed. Fixing it at the shared source — `MarkdownHelper.ToHtml`, which post/page rendering *and* feed generation both call — closes both at once.

**Recommendation:** same as finding #1's option (b): sanitize `MarkdownHelper.ToHtml`'s output by default (with an explicit opt-out for intentional embeds), applied inside the helper itself rather than per-caller, so every consumer inherits the fix automatically.

### 21. Public media-serving endpoint still has no `X-Content-Type-Options: nosniff`, and caches for a year
**Files:** [src/BlogIt/Api/MediaProxyApi.cs:37-52](src/BlogIt/Api/MediaProxyApi.cs) — contrast with [src/BlogIt/Hosting/AdminAssetMiddlewareContributor.cs:55](src/BlogIt/Hosting/AdminAssetMiddlewareContributor.cs)

Critical #0 above is a confirmed, live-exploited same-origin-script-execution bug served through this exact endpoint (`GET /media/{**path}`, `AllowAnonymous`), caused by trusting the client-supplied `Content-Type` at upload with no validation. I checked what defenses exist on the serving side specifically — independent of whether upload-time validation ever gets fixed — and confirmed live via response headers:

```
GET /media/<key>.txt  →  Content-Type: text/plain
                          Cache-Control: public, max-age=31536000
                          (no X-Content-Type-Options)
```

There is no `X-Content-Type-Options: nosniff` anywhere on this response. That header is exactly the defense-in-depth layer that matters here: even with upload validation in place, `nosniff` is what stops a browser from re-sniffing a mislabeled file's actual bytes and rendering it as something more dangerous than its declared type. The codebase already has this pattern — [AdminAssetMiddlewareContributor.cs:55](src/BlogIt/Hosting/AdminAssetMiddlewareContributor.cs) sets exactly this header for the admin WASM static assets — it just wasn't applied to the one public endpoint that serves untrusted, user-uploaded content.

Separately, [MediaProxyApi.cs:43](src/BlogIt/Api/MediaProxyApi.cs) sets `Cache-Control: public, max-age=31536000` (one year, cacheable by any shared/CDN cache in front) on every media response regardless of content type. Reasonable for genuine images, but it means a successful Critical #0-style exploit doesn't execute just once — the malicious response is aggressively cached client- and server-side for a year, continuing to be served long after the offending file might be deleted from the admin Media list.

**Recommendation:** add `X-Content-Type-Options: nosniff` to `ServeMedia`'s response unconditionally (cheap, no downside), and once Critical #0 is fixed with a real content-type allow-list, consider scoping the year-long immutable cache to verified-safe types only.

### 22. Admin shell loads a third-party CDN script/stylesheet with no version pin and no Subresource Integrity — served pre-authentication
**File:** [src/BlogIt.Admin/wwwroot/index.html:9,27](src/BlogIt.Admin/wwwroot/index.html)

```html
<link rel="stylesheet" href="https://unpkg.com/easymde/dist/easymde.min.css" />
...
<script src="https://unpkg.com/easymde/dist/easymde.min.js"></script>
```

The admin shell pulls the EasyMDE Markdown editor from `unpkg.com` unpinned (no `@version` in the URL, so unpkg resolves to whatever's latest *at request time*) and with no `integrity`/`crossorigin` Subresource Integrity attributes. I'm including this in the public-facing pass specifically because of when it loads: [AdminAssetMiddlewareContributor.cs:19-40](src/BlogIt/Hosting/AdminAssetMiddlewareContributor.cs) serves this shell for any `GET`/`HEAD` to `/blogit/` or `/blogit/index.html` with no authorization check — confirmed live via a plain unauthenticated `curl http://localhost:5107/blogit/index.html` (no `Authorization` header sent), which returns the shell, including these two tags, with a `200`. Blazor WASM's auth check is client-side and only runs *after* the WASM runtime and its referenced assets have already loaded, so every visitor who ever loads `/blogit/*` — logged in or not — fetches this CDN content.

That means a supply-chain compromise of the `easymde` npm package, or of unpkg's delivery for that package, or a MITM on that one connection, gets same-origin script execution in the admin panel for every visitor, with no BlogIt-specific vulnerability needed at all — and it reaches straight into finding #11's JWT-in-`localStorage` for anyone currently logged in. Subresource Integrity (a `sha384-` hash pinned to a known-good build) is the standard defense against exactly this and isn't used here; nor is version-pinning, without which there isn't even a fixed artifact to hash against. `BlogIt.MauiAdmin`'s own `wwwroot/lib/bootstrap/` shows the project already has a pattern for vendoring a front-end dependency locally elsewhere in the repo — this is the one place that doesn't follow it.

**Recommendation:** vendor EasyMDE into `wwwroot/lib/` at a pinned version (consistent with how Bootstrap is already vendored for MauiAdmin), or at minimum pin the unpkg URL to an exact version and add `integrity`/`crossorigin="anonymous"` to both tags.

---

## Low

### 23. Public search has no pagination, result cap, or rate limit
**File:** [src/BlogIt/Services/PublicContentService.cs:98-118](src/BlogIt/Services/PublicContentService.cs) (`SearchPostsAsync`)

Every other public listing method in this file pages its results — `GetPostsAsync` and `GetPostsByTagAsync` both take `page`/`pageSize` and cap the query with `.Skip()`/`.Take()`. `SearchPostsAsync` doesn't:

```csharp
var posts = await PublishedPosts(db)
    .Where(post => post.Title.Contains(searchTerm)
        || post.Summary.Contains(searchTerm)
        || (post.Content != null && post.Content.Contains(searchTerm)))
    .OrderByDescending(post => post.PublishedAt)
    ...
    .ToListAsync(cancellationToken);
```

No `.Take()`, no page size, backing the public, unauthenticated `GET /search?q=` page ([Search.razor](samples/BlogIt.Sample/Components/Pages/Search.razor)). Every matching post's full row — including the entire `Content` field — comes back in one response, and the `Contains()` match against `Content` is a leading-wildcard `LIKE '%term%'` that can't use a normal index, forcing a scan proportional to total content volume. On a blog with any meaningful amount of content, a single-character or common-word query becomes a cheap, repeatable, unauthenticated way to trigger the largest, most expensive query the public site can run — no cache, no rate limit, no cap on repetition. Low severity for a blog the size this sample ships with (I confirmed the endpoint itself works correctly against real data), but worth fixing before it's a live problem rather than after.

**Recommendation:** give `SearchPostsAsync` the same `page`/`pageSize` treatment as its siblings, and consider excluding `Content` from the result projection — none of the public views that render search results show more than the summary.

### 24. Preview-token store grows unboundedly for links that are never opened
**File:** [src/BlogIt/Services/PreviewTokenService.cs:24,90-106](src/BlogIt/Services/PreviewTokenService.cs)

Worth noting the parts of `PreviewTokenService` that are solid, since it's the kind of thing worth confirming rather than assuming: tokens are unguessable GUIDs, single-use via `Interlocked.CompareExchange` ([PreviewTokenService.cs:79](src/BlogIt/Services/PreviewTokenService.cs)) so a token can't be replayed to mint a second cookie, the resulting cookie is `HttpOnly` + `SameSite=Strict` + scoped to the exact content path, and expiry is checked on every lookup. The one gap: the backing `ConcurrentDictionary<Guid, PreviewGrant>` (line 24) only ever removes an entry when something looks it up *after* it's expired (`TryGetValidGrant`, line 101's `tokens.TryRemove(...)`) — there's no background sweep. A preview link generated (e.g., via the admin "Preview" button) and never clicked stays in memory for the lifetime of the process. Not exploitable — it's a slow, unbounded memory leak proportional to how often "Preview" gets clicked and abandoned, not an attack surface — but a cheap fix.

**Follow-up, confirmed live with an actual cookie jar (2026-08-11):** minted a preview link for an unpublished draft and drove the full flow with `curl`'s cookie jar rather than reasoning from source alone. First request against a fresh token correctly returned `Set-Cookie: BlogItPreview_Post_<id>=<token>; path=/2026/<slug>; samesite=strict; httponly` (no `Secure` — correct, since this was plain HTTP on localhost; `PreviewTokenService.cs:55` ties `Secure` to `httpContext.Request.IsHttps`, so it will be set automatically once served over HTTPS). Confirmed the same already-redeemed token, replayed from a second client with no cookie (simulating the link being viewed by someone other than whoever opened it first), correctly fails — the post renders as not-found. Confirmed the bare canonical post URL (no `?preview=` at all) does **not** honor the cookie even though it's present and valid for that exact path — `BlogPostPage.razor`'s branching only ever consults `PreviewTokens.TryAuthorize` when `Preview.HasValue`, so a leaked/inspectable preview cookie can't be used to view draft content through the normal URL. And confirmed revisiting the exact `?preview=<token>` URL a second time, cookie now present, correctly still renders the draft — the spent token doesn't need to be valid again because the cookie carries the authorization from here. All four behaviors matched the source-level reasoning exactly; nothing new to fix here, but this had only been reasoned through before, not exercised, so it's worth recording as verified rather than assumed.

**Recommendation:** a periodic sweep (an `IHostedService` timer, or piggybacking on `PublicationSchedulingService`'s existing background loop) that clears expired entries regardless of whether they're ever looked up again.

---

## What I additionally checked and found sound

- **Authorization boundaries across every API route group.** Re-verified every `MapGroup`/`MapGet`/`MapPost`/etc. across all of `src/BlogIt/Api/*.cs`. Exactly five things are (correctly) reachable without authentication: `SetupApi` (`/status`, `/initialize` — necessarily pre-auth, covered by finding #3), `AuthApi`'s `/login`, `SitemapApi` (`/sitemap.xml`, `/robots.txt`), `FeedsApi` (`/rss.xml`, `/atom.xml`), and `MediaProxyApi` (`/media/**`). Every other group — Posts, Pages, Media upload/list/delete, Users, Settings, Analytics, AI, Redirects, Previews, and `/auth/change-password` — correctly carries `.RequireAuthorization(BlogItDefaults.AdminAuthorizationPolicy)`. No accidental public exposure found.
- **`GaScript.razor`'s inline `gtag('config', '@measurementId')` call is safe against script injection — tested live with two payloads.** Set the (fully-trusted, per finding #6) Google Analytics Measurement ID setting to `G-TEST'});alert(document.cookie);//`, then separately to `G-X</script><script>window.__gaXss=true;</script>`, and fetched the rendered homepage both times. Blazor's renderer emitted the value using JavaScript-safe `\uXXXX` escaping (`'` for the quote, `<`/`>` for the angle brackets) rather than HTML-entity encoding — so even a literal `</script>` sequence in the setting can't terminate the script block, since the browser's HTML tokenizer never sees the literal bytes `</script>`. Flagging this because interpolating into inline JS (rather than HTML markup) is exactly the kind of context that's easy to get wrong, and it deserved an actual test rather than an assumption.
- **`SeoHead.razor`'s JSON-LD structured-data block** ([SeoHead.razor:49,75-118](src/BlogIt/Components/Shared/SeoHead.razor)) is injected via `MarkupString` (unescaped by Blazor) but is safe regardless, because it's built with `System.Text.Json.JsonSerializer.Serialize`, whose default encoder HTML/JS-escapes `<`, `>`, `&`, and quotes before Blazor ever sees the string — post titles, SEO descriptions, and author names can't break out of the `<script type="application/ld+json">` block.
- **The search page's reflected query string is safe.** `Search.razor` renders the raw `Q` query parameter directly (`for "<strong>@Q</strong>"`, and `<input value="@Q">`) with no manual encoding — but Blazor's ordinary `@expression` text/attribute rendering is HTML-encoded by default regardless of render mode, so this isn't exploitable. Confirmed by reading the rendering model rather than assuming; no `MarkupString` appears anywhere on this path.
- **Sitemap/robots XML escaping, slug normalization, and redirect validation** (all previously reviewed) were spot-checked again on this pass and are unchanged/still sound.
- **Soft-404 behavior** (finding #10d) still reproduces unchanged: `curl -o /dev/null -w '%{http_code}' .../2026/nonexistent-slug` → `200`. Not re-detailed here since it's fully documented above; re-checked only to confirm it still applies on this pass.

## Environment note (addendum)

Reused the same LocalDB instance (`(localdb)\BlogItTestInstance`) and the single `admin` account left by the prior pass (password unchanged: `TestPassword123!New`). Ran the app directly via `dotnet run` against the connection string in user-secrets — not the AppHost/Aspire launch path, and for the same reason as the first pass: `Program.cs`'s Aspire guard rejects LocalDB-shaped connection strings whenever it detects Aspire-shaped environment variables, which is exactly what `.claude/launch.json`'s configured `env` block triggers; running directly via `dotnet run` with the connection string only in user-secrets avoids that guard. New test data created to exercise these findings — one published post containing script payloads, one uploaded `.txt` file, and two temporarily-changed settings (`SiteUrl`, `GoogleAnalyticsMeasurementId`) — was deleted/reset immediately after use; confirmed the database was back to 0 posts, 0 media files, and both settings blank before finishing. No source files were modified and no git actions were taken during this pass.
