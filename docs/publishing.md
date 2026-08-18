# Publishing BlogIt packages

The release workflow publishes only on a pushed `vMAJOR.MINOR.PATCH` tag. The
tag is normalized to the package version, the reusable installed-package SQL
smoke packs and validates both products, and the publish job downloads and
checksum-verifies those exact artifacts. A release contains:

- `BlogIt.<version>.nupkg`
- `BlogIt.<version>.snupkg`
- `BlogIt.AzureStorage.<version>.nupkg`
- `BlogIt.AzureStorage.<version>.snupkg`
- `BlogIt.OpenAi.<version>.nupkg`
- `BlogIt.OpenAi.<version>.snupkg`
- `BlogIt.GoogleAnalytics.<version>.nupkg`
- `BlogIt.GoogleAnalytics.<version>.snupkg`
- `BlogIt.Contracts.<version>.nupkg`
- `BlogIt.Contracts.<version>.snupkg`

## Version stamping

Every project whose output ships imports `build/BlogIt.Versioning.props`, which
turns the packed `PackageVersion` into `AssemblyVersion`, `FileVersion`, and
`InformationalVersion`. Nothing in the SDK does this by default — `PackageVersion`
is derived *from* `Version`, never the reverse — so without it a release packed
as `1.2.3` shipped assemblies stamped `1.0.0.0` and customer stack traces could
not identify the build. `build/package-layout-tests/verify.ps1` asserts the
stamps of all five shipped assemblies against the packed version.

`AssemblyVersion` and `FileVersion` carry the four-part numeric core, so
`1.2.3-rc.1` stamps `1.2.3.0`. `InformationalVersion` keeps the full version and
has the commit SHA appended by SourceLink.

## BlogIt.Contracts is its own package

`BlogIt.Contracts` packs and publishes independently, and `BlogIt` takes an
exact-version dependency on it. It was previously `IsPackable=false`, with an
`IncludeBlogItContractsInPackage` target injecting `BlogIt.Contracts.dll` into
`BlogIt`'s own `lib` folder — so the assembly reached customers with no
independent version and no way to take it alone. Anyone writing a separate
client had to reference all of `BlogIt` and restore EF Core, SQL Server and
BCrypt for a handful of records.

It has **zero dependencies** and **no framework reference**, which is what makes
it cheap to take from a console app, a MAUI app or another service.
`verify.ps1` asserts both, plus that `BlogIt`'s `lib` folder no longer contains
the assembly (two copies from two sources is worse than one smuggled copy), and
builds a `ContractsConsumer` fixture on the plain `Microsoft.NET.Sdk` whose
entire restore graph is that one library.

Release it from the same tag as the engine, at the same version. The engine
depends on it exactly because the DTOs are the wire format both halves
serialise against, so a mismatched pair is a silent serialisation bug rather
than a load failure.

### Namespace and assembly name do not match, deliberately

The package and assembly are `BlogIt.Contracts`; the namespaces are
`BlogIt.Shared.*`, and `BlogIt.Shared.Helpers` spans this assembly (via
`BlogUrlHelper`) and the engine (the other twelve helpers). This is a known
wart, left alone on purpose:

- Renaming the namespaces would touch nearly every file in the engine, the
  Blazor admin, the MAUI admin, the sample and the tests, for a cosmetic gain
  and a large rebase hazard against any in-flight work.
- Renaming the *assembly* to `BlogIt.Shared` would leave the package id as the
  odd one out, or force a third name into circulation.
- A namespace spanning two assemblies is legal and common, and there is no type
  collision between the two halves — the cost here is confusion, not breakage.

Reconsider at the 1.0 cut, where a namespace change is a single documented
breaking change rather than churn. Until then the mismatch is documented in the
package README so a client author is not surprised by it.

## Contract compatibility policy

The contract records grow by appending parameters with defaults —
`ScheduledPublishAt`, `ScheduleState`, `HasBeenPublished` and `ConcurrencyStamp`
all arrived that way on `BlogPostSummaryDto`, `BlogPostDetailDto`, `PageDto`
and the update requests. That is **source-compatible and binary-breaking**: a
client compiled against the old record calls a constructor arity that no longer
exists, and the failure is a runtime `MissingMethodException`, not a build
error. Recompiling fixes it; that is the whole remedy.

Nothing has been published yet, so there is no compatibility to preserve today.
The policy exists so this stops being a hazard once something is:

- **Before 1.0.** Appending defaulted parameters is allowed. `BlogIt.Contracts`
  and `BlogIt` ship from the same tag at the same version, and the engine's
  dependency is exact, so the pair is always recompiled together. Note the
  appended parameter in the release notes; a client author reading them is the
  only mitigation available.
- **From 1.0 on.** Appending a parameter to a published record is a **minor**
  version bump for `BlogIt.Contracts`, not a patch, and the release notes must
  say so. Patches must be binary-compatible. Removing or reordering a
  parameter, or changing its type, is a **major** bump.
- **Prefer an init-only property** to a new positional parameter for anything
  optional. `public Guid ConcurrencyStamp { get; init; }` on the record body
  adds no constructor overload, so it is binary-compatible in both directions
  and object initialisers keep working. The positional list should hold only
  what a caller must always supply.
- **Never reuse a position.** If a parameter is dropped, later parameters keep
  their positions; a positional call that silently binds to a different meaning
  is worse than a `MissingMethodException`.
- **Call these records with named arguments.** The `ContractsConsumer` fixture
  does, which is why it keeps compiling as parameters are appended. Positional
  construction of a long record is what turns an append into a caller-side
  surprise.

No tooling enforces this. A real binary-compatibility gate needs a published
baseline package to diff against, and there is none yet; adding one before the
first release would assert against a moving target. Revisit when 1.0 ships —
at that point a package-validation baseline (`PackageValidationBaselineVersion`)
becomes the right mechanism and can be wired into `verify.ps1`.

## BlogIt.GoogleAnalytics releases as a prerelease

`BlogIt.GoogleAnalytics` depends on `Google.Analytics.Data.V1Beta`, and Google
publishes no stable Analytics Data client. Tagging that package stable would
raise `NU5104` and be rejected by feeds that block prerelease transitives, so it
ships with a prerelease label (for example `1.0.0-beta.1`) until Google ships a
stable `Google.Analytics.Data.V1`.

Isolating that in a satellite is deliberate: `BlogIt`, `BlogIt.AzureStorage`, and
`BlogIt.OpenAi` all release stable from the same tag, and only hosts that want
analytics reporting opt into a prerelease dependency.

## License

All packages declare `PackageLicenseExpression` of `MIT`, matching `LICENSE.txt`
in the repository root, and set `PackageRequireLicenseAcceptance` to `false`.
`verify.ps1` asserts the expression on every produced package, because a missing
one makes NuGet.org render "License not specified" — indistinguishable from
proprietary. Fill in the copyright holder and year in `LICENSE.txt` before
publishing.

`PackageProjectUrl` is intentionally absent because no canonical project URL is
configured. It is optional and is not an owner-selected publication requirement.
Repository URL, branch, and commit metadata are left to the SDK's source-control
integration when a real remote is available.

## Feed configuration

Configure these repository settings:

| Kind | Name | Purpose |
| --- | --- | --- |
| Variable | `NUGET_SOURCE` | Package endpoint; the workflow default is NuGet.org. |
| Secret | `NUGET_API_KEY` | API key for the package endpoint. |
| Variable (optional) | `NUGET_SYMBOL_SOURCE` | Separate symbol endpoint; defaults to `NUGET_SOURCE`. |
| Secret (optional) | `NUGET_SYMBOL_API_KEY` | Separate symbol key; defaults to `NUGET_API_KEY`. |
| Variable (optional) | `NUGET_USERNAME` | Username for feeds requiring basic authentication. |

For GitHub Packages, the workflow can use the repository owner as the username
and `GITHUB_TOKEN` as the API key. Its `packages: write` permission is already
scoped to the publish job.

## Publish

After the license preflight, create and push a SemVer tag to an already
configured remote:

```powershell
git tag v1.2.3
git push <configured-remote-name> v1.2.3
```

Do not rebuild packages outside the workflow for that release. The publish job
uses the checksummed outputs produced by the successful smoke job.
