namespace BlogIt.Web.Services;

public interface ISettingsService
{
    Task<string?> GetAsync(string key);
    Task<Dictionary<string, string>> GetAllAsync();
    Task SetAsync(string key, string value);
    Task SetManyAsync(Dictionary<string, string> settings);
}
