using BlogIt.Shared.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Direct coverage for <see cref="PasswordPolicy"/>. Every caller of it (setup, user creation,
/// change-password) only ever asserted "a weak password is rejected", which said nothing about
/// where the boundaries actually sit — the missing upper bound went unnoticed for exactly that
/// reason. These tests pin the boundaries themselves.
/// </summary>
public class PasswordPolicyTests
{
    [Fact]
    public void Validate_AcceptsAPasswordExactlyAtTheMinimumLength()
    {
        // Eight characters, one of each required class: the shortest string the policy allows.
        PasswordPolicy.MinLength.Should().Be(8);

        PasswordPolicy.Validate("Abcdefg1").Should().BeNull();
    }

    [Fact]
    public void Validate_RejectsAPasswordOneCharacterBelowTheMinimum()
    {
        PasswordPolicy.Validate("Abcdef1").Should().Be(
            "Password must be at least 8 characters long.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_RejectsMissingPasswords(string? password)
    {
        // Null and empty take the length branch rather than throwing: callers hand over whatever
        // arrived in the request body, and a null there is a client error, not a bug here.
        PasswordPolicy.Validate(password).Should().Be(
            "Password must be at least 8 characters long.");
    }

    [Fact]
    public void Validate_RejectsAPasswordWithNoUppercaseLetter()
    {
        PasswordPolicy.Validate("abcdefg1").Should().Be(
            "Password must contain at least one uppercase letter.");
    }

    [Fact]
    public void Validate_RejectsAPasswordWithNoLowercaseLetter()
    {
        PasswordPolicy.Validate("ABCDEFG1").Should().Be(
            "Password must contain at least one lowercase letter.");
    }

    [Fact]
    public void Validate_RejectsAPasswordWithNoDigit()
    {
        PasswordPolicy.Validate("Abcdefgh").Should().Be(
            "Password must contain at least one digit.");
    }

    [Fact]
    public void Validate_ReportsOnlyTheFirstUnmetRule()
    {
        // "short" fails length, case and digit at once; the caller shows one message, so the
        // ordering is part of the contract rather than an accident of the implementation.
        PasswordPolicy.Validate("ab").Should().Be(
            "Password must be at least 8 characters long.");
    }

    [Fact]
    public void Validate_AcceptsAPasswordExactlyAtTheMaximumLength()
    {
        PasswordPolicy.MaxLength.Should().Be(128);

        var atTheLimit = "Aa1" + new string('x', PasswordPolicy.MaxLength - 3);
        atTheLimit.Length.Should().Be(PasswordPolicy.MaxLength);

        PasswordPolicy.Validate(atTheLimit).Should().BeNull();
    }

    [Fact]
    public void Validate_RejectsAPasswordOneCharacterAboveTheMaximum()
    {
        var overTheLimit = "Aa1" + new string('x', PasswordPolicy.MaxLength - 2);
        overTheLimit.Length.Should().Be(PasswordPolicy.MaxLength + 1);

        PasswordPolicy.Validate(overTheLimit).Should().Be(
            "Password must be at most 128 characters long.");
    }

    [Fact]
    public void Validate_KeepsGenerouslyLongPassphrasesWorking()
    {
        // The cap exists to bound the input, not to push people off passphrases. A four-word
        // diceware phrase plus decoration is nowhere near it.
        PasswordPolicy.Validate("correct-horse-battery-staple-A1").Should().BeNull();
    }

    [Fact]
    public void MaxLength_LeavesRoomAboveWhatBCryptActuallyHashes()
    {
        // Documenting the known limitation rather than hiding it: BCrypt stops at 72 bytes, so
        // two accepted passwords sharing a 72-byte prefix hash identically. The cap does not fix
        // that (see PasswordPolicy's remarks for why pre-hashing was rejected) — it only stops
        // unbounded input reaching the hasher — and this assertion is here so anyone lowering
        // MaxLength to 72 has to come and read that reasoning first.
        PasswordPolicy.MaxLength.Should().BeGreaterThan(72);
    }
}
