namespace BlogIt.MauiAdmin.Services;

/// <summary>Resolves DI services from page/view code-behind constructors. Shell's XAML
/// {DataTemplate} markup extension (used for every top-level tab/flyout item) and its
/// route factory both construct pages via Activator.CreateInstance, bypassing the DI
/// container — so pages take no constructor parameters and instead pull their
/// ViewModel from here and assign it as BindingContext. This is the standard MAUI
/// workaround for that gap.</summary>
public static class ServiceHelper
{
    public static IServiceProvider Services =>
        IPlatformApplication.Current?.Services
        ?? throw new InvalidOperationException("The MAUI service provider is not yet available.");

    public static T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();
}
