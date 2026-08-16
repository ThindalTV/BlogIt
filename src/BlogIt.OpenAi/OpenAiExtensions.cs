namespace BlogIt;

/// <summary>
/// Adds the OpenAI-backed AI provider to a BlogIt configuration.
/// </summary>
public static class OpenAiExtensions
{
    /// <summary>
    /// Registers the OpenAI-backed <see cref="Services.IAiService"/>, enabling the admin's
    /// brainstorm and export-to-draft screens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately takes no configuration, which is the one place this differs in shape from
    /// <c>UseAzureStorage</c>. Everything the provider needs - the endpoint flavour
    /// (<c>openai-compatible</c> or <c>github-copilot</c>), the API key, an optional custom base
    /// URL, and the chat and export model names - is stored per site in the BlogIt settings table
    /// and edited through the admin's Settings screen. Accepting a startup callback as well would
    /// mean two sources of truth for the same values, with the saved settings silently winning.
    /// </para>
    /// <para>
    /// So this call cannot fail on bad credentials the way <c>UseAzureStorage</c> can: nothing is
    /// validated here because nothing is supplied here. A key that is missing or rejected surfaces
    /// when the admin first sends a message, as <c>400</c> with the provider's reason.
    /// </para>
    /// </remarks>
    /// <param name="options">The options instance being configured inside <c>AddBlogIt</c>.</param>
    /// <returns>The same options instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">An AI provider is already configured.</exception>
    public static BlogItOptions UseOpenAi(this BlogItOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseAiProvider(new OpenAiProviderRegistration());
    }
}
