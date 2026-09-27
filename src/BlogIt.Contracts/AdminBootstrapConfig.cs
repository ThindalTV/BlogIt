namespace BlogIt.Contracts;

public sealed record BlogItAdminBootstrapConfig(string ApiPath)
{
    public const string RelativePath = "_blogit/config";
}
