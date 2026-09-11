using BlogIt.MauiAdmin.Core.Sites;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Multi-site is the reason this app exists in the shape it does: one author, several blogs,
/// switched between constantly. These cover the rules that make that safe — which site is
/// active, whose token is whose, and what happens to the rest when one site goes away.
/// </summary>
public class SiteProfileStoreTests
{
    private sealed class FakeSecureStore : ISecureStore
    {
        public Dictionary<string, string> Values { get; } = [];

        public int RemoveCount { get; private set; }

        public Task<string?> GetAsync(string key) =>
            Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);

        public Task SetAsync(string key, string value)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
            RemoveCount++;
            Values.Remove(key);
        }
    }

    private static SiteProfile Profile(string name, string host) =>
        new() { Name = name, Host = host };

    private static (SiteProfileStore Store, FakeSecureStore Storage) NewStore()
    {
        var storage = new FakeSecureStore();
        return (new SiteProfileStore(storage), storage);
    }

    [Fact]
    public async Task TheFirstSiteAddedBecomesActive()
    {
        var (store, _) = NewStore();
        var first = Profile("Travel blog", "travel.example");

        await store.AddOrUpdateProfileAsync(first);

        (await store.GetActiveProfileAsync())!.Id.Should().Be(first.Id,
            "there is nothing else the active site could be");
    }

    [Fact]
    public async Task AddingMoreSitesDoesNotStealTheActiveOne()
    {
        var (store, _) = NewStore();
        var first = Profile("Travel blog", "travel.example");
        var second = Profile("Food blog", "food.example");

        await store.AddOrUpdateProfileAsync(first);
        await store.AddOrUpdateProfileAsync(second);

        (await store.GetActiveProfileAsync())!.Id.Should().Be(first.Id,
            "adding a blog must not silently redirect the next post to it");
        (await store.GetProfilesAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task SwitchingSitesRaisesChangeSoOpenScreensCanReload()
    {
        var (store, _) = NewStore();
        var first = Profile("Travel blog", "travel.example");
        var second = Profile("Food blog", "food.example");
        await store.AddOrUpdateProfileAsync(first);
        await store.AddOrUpdateProfileAsync(second);

        var changes = 0;
        store.OnChanged += () => changes++;

        await store.SetActiveAsync(second.Id);

        (await store.GetActiveProfileAsync())!.Id.Should().Be(second.Id);
        changes.Should().Be(1, "the switcher and any open list need to know the site moved");
    }

    [Fact]
    public async Task EachSiteKeepsItsOwnToken()
    {
        var (store, _) = NewStore();
        var first = Profile("Travel blog", "travel.example");
        var second = Profile("Food blog", "food.example");
        await store.AddOrUpdateProfileAsync(first);
        await store.AddOrUpdateProfileAsync(second);

        await store.SaveTokenAsync(first.Id, "token-one", DateTime.UtcNow.AddHours(1), "amy", "Amy");
        await store.SaveTokenAsync(second.Id, "token-two", DateTime.UtcNow.AddHours(1), "amy", "Amy");

        (await store.GetTokenAsync(first.Id)).Should().Be("token-one");
        (await store.GetTokenAsync(second.Id)).Should().Be("token-two",
            "tokens are stored per site so one bad secret cannot sign the author out everywhere");
    }

    [Fact]
    public async Task SigningOutOfOneSiteLeavesTheOtherSignedIn()
    {
        var (store, _) = NewStore();
        var first = Profile("Travel blog", "travel.example");
        var second = Profile("Food blog", "food.example");
        await store.AddOrUpdateProfileAsync(first);
        await store.AddOrUpdateProfileAsync(second);
        await store.SaveTokenAsync(first.Id, "token-one", DateTime.UtcNow.AddHours(1), "amy", "Amy");
        await store.SaveTokenAsync(second.Id, "token-two", DateTime.UtcNow.AddHours(1), "amy", "Amy");

        await store.ClearTokenAsync(first.Id);

        (await store.GetTokenAsync(first.Id)).Should().BeNull();
        (await store.GetTokenAsync(second.Id)).Should().Be("token-two");
    }

    [Fact]
    public async Task DeletingTheActiveSiteFallsBackToAnotherAndDropsItsToken()
    {
        var (store, storage) = NewStore();
        var first = Profile("Travel blog", "travel.example");
        var second = Profile("Food blog", "food.example");
        await store.AddOrUpdateProfileAsync(first);
        await store.AddOrUpdateProfileAsync(second);
        await store.SaveTokenAsync(first.Id, "token-one", DateTime.UtcNow.AddHours(1), "amy", "Amy");

        await store.DeleteProfileAsync(first.Id);

        (await store.GetActiveProfileAsync())!.Id.Should().Be(second.Id,
            "removing the active blog must leave the app pointing at a real one");
        storage.Values.Should().NotContainKey(SiteProfileStore.TokenKey(first.Id),
            "a deleted site's credentials must not linger in secure storage");
    }

    [Fact]
    public async Task DeletingTheLastSiteLeavesNoActiveSite()
    {
        var (store, _) = NewStore();
        var only = Profile("Travel blog", "travel.example");
        await store.AddOrUpdateProfileAsync(only);

        await store.DeleteProfileAsync(only.Id);

        (await store.GetActiveProfileAsync()).Should().BeNull();
        (await store.GetProfilesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ProfilesSurviveARestart()
    {
        var storage = new FakeSecureStore();
        var first = Profile("Travel blog", "travel.example");
        var second = Profile("Food blog", "food.example");

        var before = new SiteProfileStore(storage);
        await before.AddOrUpdateProfileAsync(first);
        await before.AddOrUpdateProfileAsync(second);
        await before.SetActiveAsync(second.Id);

        var after = new SiteProfileStore(storage);

        (await after.GetProfilesAsync()).Should().HaveCount(2);
        (await after.GetActiveProfileAsync())!.Id.Should().Be(second.Id,
            "the app should reopen on the blog the author was last working in");
    }

    [Fact]
    public async Task AnUnreadableProfileBlobStartsEmptyRatherThanFailing()
    {
        var storage = new FakeSecureStore();
        await storage.SetAsync("blogit_site_profiles", "{ this is not json");

        var store = new SiteProfileStore(storage);

        (await store.GetProfilesAsync()).Should().BeEmpty("a corrupt blob must not stop the app launching");
    }

    [Fact]
    public async Task EditingASiteReplacesItRatherThanDuplicatingIt()
    {
        var (store, _) = NewStore();
        var profile = Profile("Travel blog", "travel.example");
        await store.AddOrUpdateProfileAsync(profile);

        profile.Name = "Travel notes";
        await store.AddOrUpdateProfileAsync(profile);

        var profiles = await store.GetProfilesAsync();
        profiles.Should().ContainSingle();
        profiles[0].Name.Should().Be("Travel notes");
    }
}
