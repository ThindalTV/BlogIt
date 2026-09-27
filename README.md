# BlogIt

BlogIt is an embeddable ASP.NET Core blog engine with SQL Server persistence,
filesystem or Azure Blob media storage, a packaged Blazor administration portal,
and services for building a host-owned public site.

## Documentation

- [Technical guide](docs/technical-guide.md) - install BlogIt, configure its
  providers and middleware, and build public post and page views.
- [Administrator guide](docs/administrator-guide.md) - complete first-run setup
  and operate the administration portal.
- [Publishing packages](docs/publishing.md) - validate and release the NuGet
  packages.

## Repository layout

| Path | Contents |
| --- | --- |
| `src/BlogIt.Core/` | The engine, published as the `BlogIt` package |
| `src/BlogIt.Contracts/` | The wire contracts shared with other applications — DTOs, limits, policies |
| `src/admin/` | Admin clients: the packaged Blazor portal, and the MAUI app with its logic library |
| `src/providers/` | Optional satellite packages: Azure Blob storage, OpenAI, Google Analytics |
| `tests/` | Unit and integration tests, plus `PackageLayout/` for package verification |
| `samples/` | Aspire-hosted example application and public blog UI |
| `build/` | Shared MSBuild versioning rules |
| `docs/` | Technical, administrator, and publishing documentation; `history/` holds past audits |

## Quick start

BlogIt targets .NET 10 and requires SQL Server.

```powershell
dotnet add package BlogIt
dotnet run --project .\samples\BlogIt.Sample.AppHost\BlogIt.Sample.AppHost.csproj
```

The sample's Aspire dashboard provides its site URL. Open `/blogit/` on that
site to initialize the admin portal.
