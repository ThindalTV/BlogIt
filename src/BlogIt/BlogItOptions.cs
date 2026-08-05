namespace BlogIt;

public sealed class BlogItOptions
{
    private readonly List<IBlogItDatabaseProviderRegistration> _databaseProviders = [];
    private readonly List<IBlogItStorageProviderRegistration> _storageProviders = [];
    private string _adminPath = BlogItDefaults.AdminPath;
    private string _apiPath = BlogItDefaults.ApiPath;
    private string _mediaPath = BlogItDefaults.MediaPath;
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

    public BlogItOptions UseDatabaseProvider(IBlogItDatabaseProviderRegistration provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        EnsureMutable();

        if (_databaseProviders.Count != 0)
        {
            throw new InvalidOperationException(
                $"BlogIt requires exactly one database provider. '{ProviderName(_databaseProviders[0])}' is already configured; '{ProviderName(provider)}' cannot also be configured.");
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
                $"BlogIt requires exactly one storage provider. '{ProviderName(_storageProviders[0])}' is already configured; '{ProviderName(provider)}' cannot also be configured.");
        }

        _storageProviders.Add(provider);
        return this;
    }

    internal IBlogItDatabaseProviderRegistration DatabaseProvider => _databaseProviders.Single();

    internal IBlogItStorageProviderRegistration StorageProvider => _storageProviders.Single();

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

    private static string ProviderName(IBlogItDatabaseProviderRegistration provider) =>
        string.IsNullOrWhiteSpace(provider.Name) ? "<unnamed>" : provider.Name;

    private static string ProviderName(IBlogItStorageProviderRegistration provider) =>
        string.IsNullOrWhiteSpace(provider.Name) ? "<unnamed>" : provider.Name;

    private void EnsureMutable()
    {
        if (_isReadOnly)
        {
            throw new InvalidOperationException("BlogIt options cannot be changed after AddBlogIt has completed.");
        }
    }
}
