# BlogIt.OpenAi

`BlogIt.OpenAi` adds AI brainstorming and export-to-draft to the BlogIt ASP.NET Core
engine, using the official OpenAI .NET client. It depends transitively on the same
package version of `BlogIt`; do not install a separate `BlogIt` version alongside it.

Install this only if you want the admin's AI screens. Without it, `BlogIt` carries no
reference to any AI SDK, and the AI endpoints report that AI is not configured.

## Requirements and install

Use .NET 10, SQL Server, and either an OpenAI-compatible API key or a GitHub Copilot
personal access token.

```powershell
dotnet add package BlogIt.OpenAi
```

## AI startup

```csharp
using BlogIt;

var builder = WebApplication.CreateBuilder(args);

var sqlConnection = builder.Configuration.GetConnectionString("BlogItDb")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:BlogItDb is required.");

builder.Services.AddBlogIt(options =>
{
    options.UseSqlServer(sqlConnection);
    options.UseFileSystemStorage();
    options.UseOpenAi();
});

var app = builder.Build();
await app.MigrateBlogItAsync();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseBlogIt();

app.MapBlogIt();
app.Run();
```

`UseOpenAi()` takes no arguments on purpose. Every value the provider needs is stored per
site and edited in the admin's Settings screen, so there is nothing to configure at
startup and nothing that can be configured in two places:

| Setting | Meaning |
| --- | --- |
| Provider | `openai-compatible` (default) or `github-copilot` |
| API key | OpenAI-compatible key, or a GitHub PAT for `github-copilot` |
| Base URL | Optional; overrides `api.openai.com` for `openai-compatible` only |
| Model | Chat model for brainstorming; defaults to `gpt-4o-mini` |
| Export model | Model used for export-to-draft; defaults to `gpt-4o` |

For `github-copilot` the base URL is fixed to GitHub's Azure-hosted models endpoint and
the Base URL setting is ignored.

The Base URL must be an absolute `http(s)` URL, and by default it may not point at a
loopback, link-local, or private address: the stored API key is sent to whatever it names,
so anyone with blog admin credentials could otherwise turn it into a request against the
cloud metadata service or an internal host. Both the settings route and this provider check
it, the second so a value stored before the check existed is refused rather than used. To
run a model on the machine or the private network, set
`options.AllowPrivateAiEndpoints = true` in `AddBlogIt` — a host startup decision, not a
portal setting.

Because none of that is supplied at startup, `UseOpenAi()` cannot validate credentials
the way `UseAzureStorage` validates a connection string. A missing or rejected key
surfaces the first time an admin sends a message, as `400` carrying the reason.

## Long conversations

Brainstorm history is compacted automatically: once a conversation reaches 20
non-compacted messages, the oldest half is folded into a running summary by one extra
model call and excluded from later requests. Those messages stay visible in the admin's
chat UI — they are marked compacted, not deleted — so nothing the author wrote is lost,
and the conversation can never grow past the provider's context window.

## Replacing the provider

`IAiService` is a public abstraction in `BlogIt`. Register your own implementation before
`AddBlogIt` and it wins over both this package and the engine's not-configured fallback.

## Not installing this package

`BlogIt` alone still serves the AI conversation list, and reading, creating and deleting
conversations, because those touch only the database. The two endpoints that call a
provider — `POST /api/ai/conversations/{id}/messages` and
`POST /api/ai/conversations/{id}/export-draft` — return `400` with a problem response
naming this package. That is the same shape of response the admin already renders for a
provider whose API key has not been entered yet.
