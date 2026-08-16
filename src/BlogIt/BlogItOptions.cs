namespace BlogIt;

public sealed class BlogItOptions
{
    private readonly List<IBlogItDatabaseProviderRegistration> _databaseProviders = [];
    private readonly List<IBlogItStorageProviderRegistration> _storageProviders = [];
    private readonly List<IBlogItAiProviderRegistration> _aiProviders = [];
    private readonly List<IBlogItAnalyticsProviderRegistration> _analyticsProviders = [];
    private string _adminPath = BlogItDefaults.AdminPath;
    private string _apiPath = BlogItDefaults.ApiPath;
    private string _mediaPath = BlogItDefaults.MediaPath;
    private bool _serveRssFeed = true;
    private bool _serveAtomFeed = true;
    private bool _serveSitemap = true;
    private bool _serveRobotsTxt = true;
    private bool _isReadOnly;

    public string AdminPath
    {
        get => _adminPath;
        set
        {
            EnsureMutable();
            _adminPath = value;
        }
    }

    public string ApiPath
    {
        get => _apiPath;
        set
        {
            EnsureMutable();
            _apiPath = value;
        }
    }

    public string MediaPath
    {
        get => _mediaPath;
        set
        {
            EnsureMutable();
            _mediaPath = value;
        }
    }

    /// <summary>
    /// Whether BlogIt maps <c>GET /rss.xml</c>. Default <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="AdminPath"/> and friends these four documents live at fixed, conventional
    /// URLs that a site usually already owns, so the switch is on/off rather than a path. Turning
    /// one off unmaps the route entirely — BlogIt stops claiming the URL, so a host static file or
    /// host endpoint on the same path works instead of being shadowed or colliding at request time
    /// with an <c>AmbiguousMatchException</c>.
    /// </para>
    /// <para>
    /// Turning a document off does not lose its content: <see cref="Services.ISiteMetadataService"/>
    /// exposes the same feed items, sitemap entries and robots directives as data, so the host can
    /// serve them from its own route or fold them into a larger document.
    /// </para>
    /// </remarks>
    public bool ServeRssFeed
    {
        get => _serveRssFeed;
        set
        {
            EnsureMutable();
            _serveRssFeed = value;
        }
    }

    /// <summary>
    /// Whether BlogIt maps <c>GET /atom.xml</c>. Default <see langword="true"/>.
    /// See <see cref="ServeRssFeed"/> for what turning one of these off means.
    /// </summary>
    public bool ServeAtomFeed
    {
        get => _serveAtomFeed;
        set
        {
            EnsureMutable();
            _serveAtomFeed = value;
        }
    }

    /// <summary>
    /// Whether BlogIt maps <c>GET /sitemap.xml</c>. Default <see langword="true"/>.
    /// See <see cref="ServeRssFeed"/> for what turning one of these off means.
    /// </summary>
    /// <remarks>
    /// Turning this off also drops the <c>Sitemap:</c> line from BlogIt's <c>robots.txt</c>, since
    /// there is then no such document to point crawlers at.
    /// </remarks>
    public bool ServeSitemap
    {
        get => _serveSitemap;
        set
        {
            EnsureMutable();
            _serveSitemap = value;
        }
    }

    /// <summary>
    /// Whether BlogIt maps <c>GET /robots.txt</c>. Default <see langword="true"/>.
    /// See <see cref="ServeRssFeed"/> for what turning one of these off means.
    /// </summary>
    /// <remarks>
    /// BlogIt's generated document only disallows <see cref="AdminPath"/>. Any other robots rule
    /// is inexpressible through it, so a site with real crawler requirements should turn this off
    /// and merge <see cref="Services.ISiteMetadataService.GetRobotsDirectivesAsync"/> into its own
    /// <c>robots.txt</c>.
    /// </remarks>
    public bool ServeRobotsTxt
    {
        get => _serveRobotsTxt;
        set
        {
            EnsureMutable();
            _serveRobotsTxt = value;
        }
    }

    public BlogItOptions UseDatabaseProvider(IBlogItDatabaseProviderRegistration provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        EnsureMutable();

        if (_databaseProviders.Count != 0)
        {
            throw new InvalidOperationException(
                $"BlogIt requires exactly one database provider. '{ProviderName(_databaseProviders[0].Name)}' is already configured; '{ProviderName(provider.Name)}' cannot also be configured.");
        }

        _databaseProviders.Add(provider);
        return this;
    }

    public BlogItOptions UseStorageProvider(IBlogItStorageProviderRegistration provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        EnsureMutable();

        if (_storageProviders.Count != 0)
        {
            throw new InvalidOperationException(
                $"BlogIt requires exactly one storage provider. '{ProviderName(_storageProviders[0].Name)}' is already configured; '{ProviderName(provider.Name)}' cannot also be configured.");
        }

        _storageProviders.Add(provider);
        return this;
    }

    /// <summary>
    /// Configures the AI provider that backs the admin's brainstorm and export-to-draft screens.
    /// Optional; at most one provider may be configured.
    /// </summary>
    /// <remarks>
    /// Hosts normally call the satellite package's own extension, for example
    /// <c>options.UseOpenAi()</c> from <c>BlogIt.OpenAi</c>, rather than this method directly.
    /// Unlike the database and storage providers this one may be left unset: the AI endpoints then
    /// answer <c>400</c> with installation instructions (see
    /// <c>BlogIt.Services.NotConfiguredAiService</c>) instead of failing to activate.
    /// </remarks>
    /// <param name="provider">The provider registration to use.</param>
    /// <returns>The same options instance, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// An AI provider is already configured, or <c>AddBlogIt</c> has already completed.
    /// </exception>
    public BlogItOptions UseAiProvider(IBlogItAiProviderRegistration provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        EnsureMutable();

        if (_aiProviders.Count != 0)
        {
            throw new InvalidOperationException(
                $"BlogIt supports at most one AI provider. '{ProviderName(_aiProviders[0].Name)}' is already configured; '{ProviderName(provider.Name)}' cannot also be configured.");
        }

        _aiProviders.Add(provider);
        return this;
    }

    /// <summary>
    /// Configures the analytics provider that backs the admin dashboard's analytics panel.
    /// Optional; at most one provider may be configured.
    /// </summary>
    /// <remarks>
    /// Hosts normally call the satellite package's own extension, for example
    /// <c>options.UseGoogleAnalytics()</c> from <c>BlogIt.GoogleAnalytics</c>, rather than this
    /// method directly. With no provider configured the analytics summary endpoint reports
    /// <c>404 "Analytics is not configured."</c> - the same answer as a configured provider whose
    /// credentials have not been entered.
    /// </remarks>
    /// <param name="provider">The provider registration to use.</param>
    /// <returns>The same options instance, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// An analytics provider is already configured, or <c>AddBlogIt</c> has already completed.
    /// </exception>
    public BlogItOptions UseAnalyticsProvider(IBlogItAnalyticsProviderRegistration provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        EnsureMutable();

        if (_analyticsProviders.Count != 0)
        {
            throw new InvalidOperationException(
                $"BlogIt supports at most one analytics provider. '{ProviderName(_analyticsProviders[0].Name)}' is already configured; '{ProviderName(provider.Name)}' cannot also be configured.");
        }

        _analyticsProviders.Add(provider);
        return this;
    }

    internal IBlogItDatabaseProviderRegistration DatabaseProvider => _databaseProviders.Single();

    internal IBlogItStorageProviderRegistration StorageProvider => _storageProviders.Single();

    /// <summary>The configured AI provider, or <see langword="null"/> when none was configured.</summary>
    internal IBlogItAiProviderRegistration? AiProvider => _aiProviders.SingleOrDefault();

    /// <summary>The configured analytics provider, or <see langword="null"/> when none was configured.</summary>
    internal IBlogItAnalyticsProviderRegistration? AnalyticsProvider => _analyticsProviders.SingleOrDefault();

    internal void NormalizeValidateAndFreeze()
    {
        EnsureMutable();

        _adminPath = NormalizePath(nameof(AdminPath), _adminPath);
        _apiPath = NormalizePath(nameof(ApiPath), _apiPath);
        _mediaPath = NormalizePath(nameof(MediaPath), _mediaPath);

        if (new[] { _adminPath, _apiPath, _mediaPath }.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
        {
            throw new InvalidOperationException(
                "BlogIt AdminPath, ApiPath, and MediaPath must resolve to distinct paths.");
        }

        if (_databaseProviders.Count != 1)
        {
            throw new InvalidOperationException(
                "BlogIt requires exactly one database provider. Configure one in AddBlogIt, for example options.UseSqlServer(...).");
        }

        if (_storageProviders.Count != 1)
        {
            throw new InvalidOperationException(
                "BlogIt requires exactly one storage provider. Configure one in AddBlogIt, for example options.UseFileSystemStorage(...) or options.UseAzureStorage(...).");
        }

        ValidateProviderName("database", DatabaseProvider.Name);
        ValidateProviderName("storage", StorageProvider.Name);

        // No count check for these two: unlike database and storage, "none configured" is a
        // supported deployment - the engine falls back to its documented not-configured services.
        // The name is still validated when one is present so a broken satellite is caught here
        // rather than surfacing as an empty string in a later error message.
        if (AiProvider is not null)
        {
            ValidateProviderName("AI", AiProvider.Name);
        }

        if (AnalyticsProvider is not null)
        {
            ValidateProviderName("analytics", AnalyticsProvider.Name);
        }

        _isReadOnly = true;
    }

    internal static string NormalizePath(string optionName, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"BlogIt {optionName} must not be empty.");
        }

        path = path.Trim();

        if (path.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"BlogIt {optionName} must use '/' separators.");
        }

        if (path.Contains('?', StringComparison.Ordinal) || path.Contains('#', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"BlogIt {optionName} must be a path only and cannot contain a query string or fragment.");
        }

        if (path.Any(char.IsControl))
        {
            throw new InvalidOperationException($"BlogIt {optionName} cannot contain control characters.");
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException($"BlogIt {optionName} cannot contain '.' or '..' path segments.");
        }

        return segments.Length == 0 ? "/" : $"/{string.Join('/', segments)}";
    }

    private static void ValidateProviderName(string kind, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"The configured BlogIt {kind} provider must have a non-empty name.");
        }
    }

    // Takes the name rather than the registration: there are now four unrelated provider
    // interfaces, and one overload each would be four identical bodies.
    private static string ProviderName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "<unnamed>" : name;

    private void EnsureMutable()
    {
        if (_isReadOnly)
        {
            throw new InvalidOperationException("BlogIt options cannot be changed after AddBlogIt has completed.");
        }
    }
}
