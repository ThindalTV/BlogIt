# BlogIt.Contracts

`BlogIt.Contracts` is the wire format of the BlogIt admin API: the request and response
records the endpoints accept and return, the well-known setting keys, the length limits
the server enforces, and the URL helper that builds a post's public path.

It has **no dependencies at all** — no Entity Framework Core, no SQL Server client, no
BCrypt, no ASP.NET Core framework reference. Install it if you are writing a client and
want the DTOs without the engine. If you are hosting the blog, install `BlogIt` instead;
it depends on this package, so the DTOs come with it and you should not reference both.

```powershell
dotnet add package BlogIt.Contracts
```

This package targets `net10.0` and is browser-safe: the bundled Blazor WebAssembly admin
compiles against exactly these types, which is the standing proof that nothing
server-only has leaked in.

## What is in here

| Namespace | Contents |
| --- | --- |
| `BlogIt.Shared.DTOs` | Request/response records for posts, pages, tags, media, redirects, users, settings, setup, auth, previews, AI and analytics. |
| `BlogIt.Shared` | `SettingKeys`, and the `ContentLimits`, `SeoLimits` and `RedirectLimits` length ceilings. |
| `BlogIt.Shared.Helpers` | `BlogUrlHelper`, so a client builds the same public post paths the server routes; `OptionalText`, the null-vs-empty convention for optional fields; `PasswordPolicy`; `AnalyticsPolicy`. |

The namespaces are `BlogIt.Shared.*` while the assembly and package are
`BlogIt.Contracts`. That mismatch is known and deliberate as of now: renaming the
namespaces would touch nearly every file in the engine, the admin and the tests for a
cosmetic gain, and renaming the assembly would leave the package id as the odd one out.
It is a candidate for the 1.0 cut, not before.

## Timestamps are UTC instants

Every timestamp on these records is a `DateTimeOffset` at offset zero. Format with `"o"`
where an offset is required — Open Graph's `article:published_time`, Atom — or call
`ToLocalTime()` to display it.

They were `DateTime` before 0.2.0, and came back with `DateTimeKind.Unspecified`: correct
instants wearing the wrong label. Formatting one with `"o"` produced a string with no
offset, and serialising one produced JSON with no `Z` that clients read as local time.
Nothing threw; the output was simply wrong. If you have client code compensating for that
— `DateTime.SpecifyKind(value, DateTimeKind.Utc)` before formatting or converting — it can
be deleted.

## Validating before you send

The records carry `System.ComponentModel.DataAnnotations` attributes for the limits whose
constants live in this package, so a client can check a payload without a round trip:

```csharp
using System.ComponentModel.DataAnnotations;
using BlogIt.Shared.DTOs;

var request = new CreateBlogPostRequest(
    Title: title,
    Summary: summary,
    Content: markdown,
    SeoTitle: null,
    SeoDescription: null,
    SeoKeywords: null,
    OgImageUrl: null,
    TagNames: []);

List<ValidationResult> failures = [];
if (!Validator.TryValidateObject(
        request, new ValidationContext(request), failures, validateAllProperties: true))
{
    // Fix these before calling the API; the server rejects the same values with a 400.
}
```

The attributes are a **subset**, not the whole rule set, and passing them is not a
guarantee the server will accept the payload. They cover the ceilings this package
already declares as constants — title, slug, tag, SEO and redirect lengths — and
nothing else. Rules whose authority is a server-side validator (slug character rules, URL scheme
checks, settings coherence) are deliberately **not** restated here: copying those numbers
across an assembly boundary would create a second source of truth that drifts silently.
Treat a `400` with a problem-details body as the last word.

`PasswordPolicy` and `AnalyticsPolicy` are the exceptions, and they are not restatements.
Each is the single definition, living here so the client and the server run *the same code*
rather than two copies of the same rules. Call `PasswordPolicy.Validate` before you post so
the user hears about a weak password immediately. Call `AnalyticsPolicy.Validate` before
writing settings: analytics reporting — the property ID and service-account JSON — may only
be configured on a site that has a Google Tag Manager container ID, because the container
is what collects the traffic and where visitor consent is obtained. It also checks that the
container ID is shaped like one, so a GA4 `G-…` measurement ID is caught here. Pass it the
*effective* values, meaning what will be stored once your partial update lands. The server
calls both again on arrival, because a client-side check is advice, not enforcement.

## Version pairing

The engine takes an exact dependency — a bracketed `[version]` range, so a mismatched pair
fails restore rather than resolving quietly — on the matching `BlogIt.Contracts` version,
because the DTOs are the format both halves serialise against. A client may lag the server's
contracts version, but read the release notes first: these records grow by appending
parameters, which is source-compatible and **binary-breaking**. See
`docs/publishing.md` in the repository for the compatibility policy.

## Licence

MIT, same as the rest of BlogIt.
