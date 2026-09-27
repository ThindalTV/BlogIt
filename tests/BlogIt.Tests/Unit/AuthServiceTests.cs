using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using BlogIt.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Tests.Unit;

public class AuthServiceTests
{
    private sealed class TestDbContextFactory(
        DbContextOptions<BlogItDbContext> options) : IDbContextFactory<BlogItDbContext>
    {
        public BlogItDbContext CreateDbContext() => new(options);
    }

    private (BlogItDbContext Db, SettingsService Settings) CreateSubject()
    {
        var opts = new DbContextOptionsBuilder<BlogItDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new BlogItDbContext(opts);
        db.SiteSettings.AddRange(
            new SiteSetting { Key = BlogIt.Shared.SettingKeys.JwtSecret, Value = "unit-test-secret-long-enough-for-hmac" },
            new SiteSetting { Key = BlogIt.Shared.SettingKeys.JwtExpiryMinutes, Value = "60" }
        );
        db.SaveChanges();
        return (db, new SettingsService(new TestDbContextFactory(opts)));
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsToken()
    {
        var (db, settings) = CreateSubject();
        db.Users.Add(new AppUser
        {
            Username = "alice",
            DisplayName = "Alice",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret")
        });
        await db.SaveChangesAsync();

        var service = new AuthService(db, settings);
        var result = await service.LoginAsync(new BlogIt.Shared.DTOs.LoginRequest("alice", "secret"));

        result.Should().NotBeNull();
        result!.Token.Should().NotBeEmpty();
        result.Username.Should().Be("alice");
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsNull()
    {
        var (db, settings) = CreateSubject();
        db.Users.Add(new AppUser
        {
            Username = "bob",
            DisplayName = "Bob",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct")
        });
        await db.SaveChangesAsync();

        var service = new AuthService(db, settings);
        var result = await service.LoginAsync(new BlogIt.Shared.DTOs.LoginRequest("bob", "wrong"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_WithUnknownUser_ReturnsNull()
    {
        var (db, settings) = CreateSubject();
        var service = new AuthService(db, settings);
        var result = await service.LoginAsync(new BlogIt.Shared.DTOs.LoginRequest("nobody", "pass"));
        result.Should().BeNull();
    }

    [Fact]
    public async Task ChangePasswordAsync_WithValidCurrentPassword_UpdatesHash()
    {
        var (db, settings) = CreateSubject();
        var user = new AppUser
        {
            Username = "charlie",
            DisplayName = "Charlie",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("old")
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new AuthService(db, settings);
        var success = await service.ChangePasswordAsync(user.Id,
            new BlogIt.Shared.DTOs.ChangePasswordRequest("old", "NewPass1!"));

        success.Should().BeTrue();
        var updated = await db.Users.FindAsync(user.Id);
        BCrypt.Net.BCrypt.Verify("NewPass1!", updated!.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task ChangePasswordAsync_RotatesTheSecurityStamp()
    {
        var (db, settings) = CreateSubject();
        var user = new AppUser
        {
            Username = "carol",
            DisplayName = "Carol",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass1!")
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var originalStamp = user.SecurityStamp;

        await CreateService(db, settings).ChangePasswordAsync(user.Id,
            new BlogIt.Shared.DTOs.ChangePasswordRequest("OldPass1!", "NewPass1!"));

        (await db.Users.FindAsync(user.Id))!.SecurityStamp.Should().NotBe(originalStamp);
    }

    [Theory]
    [InlineData("short1A")]        // one below the minimum
    [InlineData("abc")]            // the case the old test asserted was allowed
    [InlineData("nouppercase1")]
    [InlineData("NOLOWERCASE1")]
    [InlineData("NoDigitsHere")]
    public async Task ChangePasswordAsync_RejectsANewPasswordThePolicyWouldNotAllow(string weak)
    {
        // The service, not just the API, enforces the policy: an embedder resolving IAuthService
        // and calling this directly used to bypass PasswordPolicy entirely. This test previously
        // asserted the opposite — that a 3-character password was accepted — which is exactly the
        // bug it encoded.
        var (db, settings) = CreateSubject();
        var user = new AppUser
        {
            Username = "eve",
            DisplayName = "Eve",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass1!")
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var change = async () => await CreateService(db, settings).ChangePasswordAsync(user.Id,
            new BlogIt.Shared.DTOs.ChangePasswordRequest("OldPass1!", weak));

        await change.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("request");
        BCrypt.Net.BCrypt.Verify("OldPass1!", (await db.Users.FindAsync(user.Id))!.PasswordHash)
            .Should().BeTrue("the stored hash must be untouched when the policy rejects the change");
    }

    [Fact]
    public async Task ChangePasswordAsync_RejectsANewPasswordAboveTheMaximumLength()
    {
        var (db, settings) = CreateSubject();
        var user = new AppUser
        {
            Username = "frank",
            DisplayName = "Frank",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass1!")
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var change = async () => await CreateService(db, settings).ChangePasswordAsync(user.Id,
            new BlogIt.Shared.DTOs.ChangePasswordRequest(
                "OldPass1!",
                "Aa1" + new string('x', BlogIt.Shared.Helpers.PasswordPolicy.MaxLength)));

        await change.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ChangePasswordAsync_AcceptsALongPassphrase()
    {
        // The cap is generous by design: capping at BCrypt's 72-byte ceiling would have locked out
        // anyone already using a longer passphrase.
        var (db, settings) = CreateSubject();
        var user = new AppUser
        {
            Username = "grace",
            DisplayName = "Grace",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass1!")
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        // Deliberately past BCrypt's 72-byte ceiling: the policy must not be the thing that stops
        // it, even though BCrypt itself will ignore the tail (see PasswordPolicy's remarks).
        var passphrase = "Correct1-horse-battery-staple-and-then-some-more-words-for-good-measure-and-a-few-extra";
        passphrase.Length.Should().BeGreaterThan(72);

        var success = await CreateService(db, settings).ChangePasswordAsync(user.Id,
            new BlogIt.Shared.DTOs.ChangePasswordRequest("OldPass1!", passphrase));

        success.Should().BeTrue();
    }

    [Fact]
    public void IAuthService_DoesNotExposeATokenMintingPrimitive()
    {
        // Finding #42: GenerateToken on the public interface let any host code that resolved
        // IAuthService mint a valid token for an arbitrary user id. Nothing outside LoginAsync
        // needs it, so it is no longer part of the contract.
        typeof(IAuthService).GetMethods().Select(method => method.Name)
            .Should().BeEquivalentTo(nameof(IAuthService.LoginAsync), nameof(IAuthService.ChangePasswordAsync));
    }

    private static AuthService CreateService(BlogItDbContext db, SettingsService settings) => new(db, settings);

    [Fact]
    public async Task ChangePasswordAsync_WithWrongCurrent_ReturnsFalse()
    {
        var (db, settings) = CreateSubject();
        var user = new AppUser
        {
            Username = "dave",
            DisplayName = "Dave",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("real")
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new AuthService(db, settings);
        var success = await service.ChangePasswordAsync(user.Id,
            new BlogIt.Shared.DTOs.ChangePasswordRequest("wrong", "NewPass1!"));

        success.Should().BeFalse();
    }

    [Fact]
    public void GenerateToken_ProducesValidJwt()
    {
        var (db, settings) = CreateSubject();
        var service = new AuthService(db, settings);
        var token = service.GenerateToken(
            Guid.NewGuid(),
            "user",
            "User",
            "a-security-stamp",
            "a-secret-that-is-long-enough-for-hs256!!",
            60);
        token.Should().NotBeEmpty();
        token.Split('.').Should().HaveCount(3); // JWT = header.payload.signature
    }
}
