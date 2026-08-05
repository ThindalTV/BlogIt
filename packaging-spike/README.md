# BlogIt package proof

This local proof packs the production `src/BlogIt` project and restores the
package into a clean ASP.NET Core consumer with no source reference to
`BlogIt.Admin` or `BlogIt.Contracts`.

Run from the repository root:

```powershell
.\packaging-spike\verify.ps1
```

The proof uses a repository-local NuGet package cache, inspects the nupkg, and
then builds and publishes the clean consumer. It verifies:

- `BlogIt.dll` and `BlogIt.Contracts.dll` are consumer-visible in `lib/net10.0`
  without an external `BlogIt.Contracts` package dependency.
- The complete published admin tree, browser assemblies, framework loader, and
  build-transitive target are present.
- Browser assets exclude EF Core, SQL Server, Azure SDK, OpenAI, and Google
  Analytics assemblies.
- Private assets are copied beside the host under `BlogItAdminAssets`, not into
  a fixed public `wwwroot/blogit` route.
- Default `/blogit` + `/api` and custom `/control-panel` + `/backend/v2` paths
  serve the shell, bootstrap configuration, framework assets, API, and deep
  links.

The original isolated package project has been retired; production packing no
longer depends on the spike.
