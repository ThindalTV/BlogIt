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
