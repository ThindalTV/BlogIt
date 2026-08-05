# Publishing BlogIt packages

The release workflow publishes only on a pushed `vMAJOR.MINOR.PATCH` tag. The
tag is normalized to the package version, the reusable installed-package SQL
smoke packs and validates both products, and the publish job downloads and
checksum-verifies those exact artifacts. A release contains:

- `BlogIt.<version>.nupkg`
- `BlogIt.<version>.snupkg`
- `BlogIt.AzureStorage.<version>.nupkg`
- `BlogIt.AzureStorage.<version>.snupkg`

## Owner preflight

Selecting and adding an authorized license is the only deferred publication
metadata. The repository currently has no license grant, so the projects
intentionally omit `PackageLicenseExpression` and `PackageLicenseFile` and set
`PackageRequireLicenseAcceptance` to `false`. Before publishing to a public
feed, the owner must select a license, add its authorized text, and set the
corresponding NuGet license property. No particular license is implied.

`PackageProjectUrl` is also intentionally absent because no canonical project
URL is configured. It is optional and is not an owner-selected publication
requirement. Repository URL, branch, and commit metadata are left to the SDK's
source-control integration when a real remote is available.

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
