using System.Text;
using BlogIt.Services;
using BlogIt.Shared;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Direct coverage for <see cref="JwtSigningKeyCache"/>. Every authenticated request goes through
/// it, and it was only ever exercised indirectly by the integration tests — which cannot see
/// whether the key was rebuilt or reused, the one property the class exists for.
/// </summary>
public class JwtSigningKeyCacheTests
{
    [Fact]
    public void ResolveKeys_IsEmptyBeforeTheFirstRefresh()
    {
        var cache = new JwtSigningKeyCache(new StubSettings());

        cache.ResolveKeys().Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshAsync_PublishesAKeyBuiltFromTheStoredSecret()
    {
        var settings = new StubSettings { [SettingKeys.JwtSecret] = "a-secret-long-enough-for-hs256!!" };
        var cache = new JwtSigningKeyCache(settings);

        await cache.RefreshAsync();

        var key = cache.ResolveKeys().Should().ContainSingle().Subject
            .Should().BeOfType<SymmetricSecurityKey>().Subject;
        key.Key.Should().Equal(Encoding.UTF8.GetBytes("a-secret-long-enough-for-hs256!!"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task RefreshAsync_PublishesNothingWhenNoSecretIsStoredYet(string? secret)
    {
        // A site that has not been through setup has no secret. Publishing no key means token
        // validation fails closed rather than validating against an empty-byte key.
        var cache = new JwtSigningKeyCache(new StubSettings { [SettingKeys.JwtSecret] = secret });

        await cache.RefreshAsync();

        cache.ResolveKeys().Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshAsync_ReusesTheSameKeyInstanceWhileTheSecretIsUnchanged()
    {
        // The reason the class exists: RefreshAsync runs on every incoming token, so an unchanged
        // secret must not allocate a fresh SymmetricSecurityKey each time.
        var settings = new StubSettings { [SettingKeys.JwtSecret] = "a-secret-long-enough-for-hs256!!" };
        var cache = new JwtSigningKeyCache(settings);

        await cache.RefreshAsync();
        var first = cache.ResolveKeys().Single();
        await cache.RefreshAsync();
        var second = cache.ResolveKeys().Single();

        second.Should().BeSameAs(first);
        settings.GetCallCount.Should().Be(2, "the secret is still read, only the key is reused");
    }

    [Fact]
    public async Task RefreshAsync_SwapsInANewKeyWhenTheSecretIsRotated()
    {
        var settings = new StubSettings { [SettingKeys.JwtSecret] = "the-original-secret-for-hs256!!!" };
        var cache = new JwtSigningKeyCache(settings);
        await cache.RefreshAsync();

        settings[SettingKeys.JwtSecret] = "the-rotated-secret-for-hs256!!!!";
        await cache.RefreshAsync();

        var key = cache.ResolveKeys().Should().ContainSingle().Subject
            .Should().BeOfType<SymmetricSecurityKey>().Subject;
        // Only the new key is published: the old one must stop validating, which is what makes
        // rotating the secret end every existing session.
        key.Key.Should().Equal(Encoding.UTF8.GetBytes("the-rotated-secret-for-hs256!!!!"));
    }

    [Fact]
    public async Task RefreshAsync_KeepsTheLastGoodKeyWhenTheSecretDisappears()
    {
        // Deliberate: an empty read is treated as "nothing to update", not as "revoke everything".
        // A transient settings miss must not log every signed-in user out.
        var settings = new StubSettings { [SettingKeys.JwtSecret] = "a-secret-long-enough-for-hs256!!" };
        var cache = new JwtSigningKeyCache(settings);
        await cache.RefreshAsync();

        settings[SettingKeys.JwtSecret] = null;
        await cache.RefreshAsync();

        cache.ResolveKeys().Should().ContainSingle();
    }

    private sealed class StubSettings : ISettingsService
    {
        private readonly Dictionary<string, string?> values = [];

        public int GetCallCount { get; private set; }

        public string? this[string key]
        {
            get => values.GetValueOrDefault(key);
            set => values[key] = value;
        }

        public Task<string?> GetAsync(string key)
        {
            GetCallCount++;
            return Task.FromResult(values.GetValueOrDefault(key));
        }

        public Task<Dictionary<string, string>> GetAllAsync() =>
            throw new NotSupportedException();

        public Task SetAsync(string key, string value) => throw new NotSupportedException();

        public Task SetManyAsync(Dictionary<string, string> settings) =>
            throw new NotSupportedException();
    }
}
