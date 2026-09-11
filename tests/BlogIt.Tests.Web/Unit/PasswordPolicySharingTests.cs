using System.Reflection;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// The password rules are enforced in two places — the admin client before it posts, and the server
/// on arrival — and they have to be the same rules. They were not: the policy lived in the engine
/// assembly, which the WebAssembly admin does not reference, so the client could not check at all.
/// The setup wizard therefore accepted a weak password, walked the user through three more steps and
/// failed on the final submit, leaving them to page back to fix it.
/// <para>
/// The fix was to move <see cref="PasswordPolicy"/> into the contracts assembly both sides already
/// reference, so both call one implementation rather than keeping two copies in step. These tests
/// pin that arrangement and the rules themselves.
/// </para>
/// </summary>
public class PasswordPolicySharingTests
{
    [Fact]
    public void ThePolicyLivesWhereBothTheServerAndTheClientCanReachIt()
    {
        // BlogIt.Admin is a WebAssembly project referencing only BlogIt.Contracts. If the policy
        // ever moves back into the engine, the client silently loses its check — and the only
        // symptom is a worse error experience, which no other test would notice.
        typeof(PasswordPolicy).Assembly
            .Should().BeSameAs(typeof(CreateUserRequest).Assembly,
                "the admin client references the contracts assembly and nothing else of BlogIt's");
    }

    [Fact]
    public void ThePolicyIsCallableWithoutAnyEngineDependency()
    {
        // Guards the other half: a policy in the right assembly is still useless to the client if it
        // grows a dependency on something only the server has.
        var dependencies = typeof(PasswordPolicy).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name);

        dependencies.Should().NotContain(name => name != null && name.StartsWith("BlogIt"));
    }

    [Theory]
    [InlineData(null, "at least 8 characters")]
    [InlineData("", "at least 8 characters")]
    [InlineData("Ab1", "at least 8 characters")]
    [InlineData("lowercase1", "uppercase letter")]
    [InlineData("UPPERCASE1", "lowercase letter")]
    [InlineData("NoDigitsHere", "one digit")]
    public void RejectsWithAMessageNamingTheUnmetRule(string? password, string expected) =>
        PasswordPolicy.Validate(password).Should().Contain(expected);

    [Fact]
    public void RejectsAnOversizedPasswordOnItsLength()
    {
        // Length is checked before the character-class rules so a 200-character value is not
        // rejected on some incidental missing digit far into the string.
        PasswordPolicy.Validate(new string('a', PasswordPolicy.MaxLength + 1))
            .Should().Contain($"at most {PasswordPolicy.MaxLength} characters");
    }

    [Theory]
    [InlineData("TestPass123")]
    [InlineData("Aa1aaaaa")]
    public void AcceptsAPasswordMeetingEveryRule(string password) =>
        PasswordPolicy.Validate(password).Should().BeNull();

    [Fact]
    public void EveryAdminScreenThatSetsAPasswordChecksItBeforePosting()
    {
        // The rule the administrator guide states: "The same rules apply everywhere a password is
        // set." Setup, user creation and change-password are those three places, and the wizard was
        // the one that did not.
        var adminSource = FindAdminPages();

        foreach (var screen in new[]
                 {
                     "Setup.razor",
                     Path.Combine("Users", "UserList.razor"),
                     Path.Combine("Account", "ChangePassword.razor"),
                 })
        {
            var file = Path.Combine(adminSource, screen);
            File.Exists(file).Should().BeTrue($"{screen} should still be where this test expects it");
            File.ReadAllText(file).Should().Contain("PasswordPolicy.Validate",
                $"{screen} collects a password and must check it with the shared policy");
        }
    }

    private static string FindAdminPages()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(
            Assembly.GetExecutingAssembly().Location)!);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the test must be able to find the repository root");
        return Path.Combine(directory!.FullName, "src", "BlogIt.Admin", "Pages");
    }
}
