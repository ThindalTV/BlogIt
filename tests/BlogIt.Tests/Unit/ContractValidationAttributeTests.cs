using System.ComponentModel.DataAnnotations;
using System.Reflection;
using BlogIt.Shared;
using BlogIt.Shared.DTOs;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Guards the DataAnnotations attributes on the contract records, and — more importantly — the line
/// drawn around them.
/// </summary>
/// <remarks>
/// <para>
/// <c>BlogIt.Contracts</c> is now a package a third party can reference on its own, so its records
/// are the only description of the API such a client gets. Attributes make the length ceilings part
/// of that description instead of something each client rediscovers from a <c>400</c>.
/// </para>
/// <para>
/// The attributes are deliberately a <b>subset</b>. Only the limits whose constants already live in
/// this assembly — <see cref="ContentLimits"/>, <see cref="SeoLimits"/>, <see cref="RedirectLimits"/>
/// — are expressed, and they are expressed by referencing those constants, so there is exactly one
/// source of truth and it cannot drift. Rules whose authority is a server-side validator in the
/// engine assembly (<c>PasswordPolicy</c>, <c>TextFieldValidator</c>, <c>AccountFieldValidator</c>,
/// <c>SiteSettingsValidator</c>, <c>UrlValidator</c>) are <b>not</b> restated here: contracts cannot
/// reference the engine without a circular dependency, so restating them would mean copying numbers
/// across an assembly boundary — a second source of truth that goes stale silently. The two
/// structural tests at the bottom are what stop someone adding that copy later without noticing.
/// </para>
/// <para>
/// Nothing in the engine wires DataAnnotations into model binding, so these attributes are inert at
/// runtime and change no server behaviour. They describe; the validators still decide.
/// </para>
/// </remarks>
public class ContractValidationAttributeTests
{
    /// <summary>
    /// The constants this assembly is allowed to bound a string with. Read reflectively rather than
    /// listed, so adding a constant to one of the limit classes automatically permits its use.
    /// </summary>
    private static readonly IReadOnlySet<int> DeclaredLimits =
        new[] { typeof(ContentLimits), typeof(SeoLimits), typeof(RedirectLimits) }
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(int))
            .Select(field => (int)field.GetRawConstantValue()!)
            .ToHashSet();

    private static PropertyInfo Property(Type type, string name) =>
        type.GetProperty(name)
            ?? throw new InvalidOperationException($"{type.Name} has no property '{name}'.");

    public static TheoryData<Type, string, int> BoundedFields() => new()
    {
        { typeof(CreateBlogPostRequest), nameof(CreateBlogPostRequest.Title), ContentLimits.TitleLength },
        { typeof(CreateBlogPostRequest), nameof(CreateBlogPostRequest.Slug), ContentLimits.SlugLength },
        { typeof(CreateBlogPostRequest), nameof(CreateBlogPostRequest.SeoTitle), SeoLimits.TitleLength },
        { typeof(CreateBlogPostRequest), nameof(CreateBlogPostRequest.SeoDescription), SeoLimits.DescriptionLength },
        { typeof(CreateBlogPostRequest), nameof(CreateBlogPostRequest.SeoKeywords), SeoLimits.KeywordsLength },
        { typeof(CreateBlogPostRequest), nameof(CreateBlogPostRequest.OgImageUrl), SeoLimits.OgImageUrlLength },
        { typeof(UpdateBlogPostRequest), nameof(UpdateBlogPostRequest.Title), ContentLimits.TitleLength },
        { typeof(UpdateBlogPostRequest), nameof(UpdateBlogPostRequest.Slug), ContentLimits.SlugLength },
        { typeof(CreatePageRequest), nameof(CreatePageRequest.Title), ContentLimits.TitleLength },
        { typeof(CreatePageRequest), nameof(CreatePageRequest.Slug), ContentLimits.SlugLength },
        { typeof(UpdatePageRequest), nameof(UpdatePageRequest.Title), ContentLimits.TitleLength },
        { typeof(UpdatePageRequest), nameof(UpdatePageRequest.Slug), ContentLimits.SlugLength },
        { typeof(SaveUrlRedirectRequest), nameof(SaveUrlRedirectRequest.SourcePath), RedirectLimits.SourcePathLength },
        { typeof(SaveUrlRedirectRequest), nameof(SaveUrlRedirectRequest.TargetUrl), RedirectLimits.TargetUrlLength },
        { typeof(CreateUserRequest), nameof(CreateUserRequest.Username), ContentLimits.UsernameLength },
        { typeof(CreateUserRequest), nameof(CreateUserRequest.DisplayName), ContentLimits.DisplayNameLength },
        { typeof(LoginRequest), nameof(LoginRequest.Username), ContentLimits.UsernameLength },
        { typeof(SetupInitializeRequest), nameof(SetupInitializeRequest.Username), ContentLimits.UsernameLength },
        { typeof(SetupInitializeRequest), nameof(SetupInitializeRequest.DisplayName), ContentLimits.DisplayNameLength },
    };

    [Theory]
    [MemberData(nameof(BoundedFields))]
    public void BoundedField_DeclaresTheEnginesOwnLimit(Type type, string propertyName, int expected)
    {
        var attribute = Property(type, propertyName).GetCustomAttribute<StringLengthAttribute>();

        attribute.Should().NotBeNull(
            "{0}.{1} has a length ceiling in the schema that a client can only learn from a 400 without it",
            type.Name,
            propertyName);
        attribute!.MaximumLength.Should().Be(expected);
    }

    public static TheoryData<Type, string> RequiredFields() => new()
    {
        { typeof(CreateBlogPostRequest), nameof(CreateBlogPostRequest.Title) },
        { typeof(CreateBlogPostRequest), nameof(CreateBlogPostRequest.Summary) },
        { typeof(UpdateBlogPostRequest), nameof(UpdateBlogPostRequest.Title) },
        { typeof(UpdateBlogPostRequest), nameof(UpdateBlogPostRequest.Summary) },
        { typeof(CreatePageRequest), nameof(CreatePageRequest.Title) },
        { typeof(CreatePageRequest), nameof(CreatePageRequest.Slug) },
        { typeof(CreatePageRequest), nameof(CreatePageRequest.Content) },
        { typeof(UpdatePageRequest), nameof(UpdatePageRequest.Title) },
        { typeof(SaveUrlRedirectRequest), nameof(SaveUrlRedirectRequest.SourcePath) },
        { typeof(SaveUrlRedirectRequest), nameof(SaveUrlRedirectRequest.TargetUrl) },
        { typeof(CreateUserRequest), nameof(CreateUserRequest.Username) },
        { typeof(CreateUserRequest), nameof(CreateUserRequest.Password) },
        { typeof(LoginRequest), nameof(LoginRequest.Username) },
        { typeof(LoginRequest), nameof(LoginRequest.Password) },
        { typeof(ChangePasswordRequest), nameof(ChangePasswordRequest.CurrentPassword) },
        { typeof(ChangePasswordRequest), nameof(ChangePasswordRequest.NewPassword) },
    };

    [Theory]
    [MemberData(nameof(RequiredFields))]
    public void RequiredField_IsMarkedRequired(Type type, string propertyName) =>
        Property(type, propertyName)
            .GetCustomAttribute<RequiredAttribute>()
            .Should()
            .NotBeNull(
                "{0}.{1} is non-nullable on the server, which generated OpenAPI cannot infer on its own",
                type.Name,
                propertyName);

    /// <summary>
    /// The attributes have to land on the generated <b>properties</b>, not on the positional record
    /// parameters. Without a <c>[property:]</c> target C# attaches them to the parameter, where
    /// <see cref="Validator"/> and every OpenAPI generator ignore them — the record still compiles
    /// and the reflection above still passes if it reads parameters, so this exercises the real
    /// validator end to end instead.
    /// </summary>
    [Fact]
    public void Validator_RejectsAnOverLongTitle()
    {
        var request = new CreateBlogPostRequest(
            Title: new string('x', ContentLimits.TitleLength + 1),
            Summary: "summary",
            Content: null,
            SeoTitle: null,
            SeoDescription: null,
            SeoKeywords: null,
            OgImageUrl: null,
            TagNames: []);

        List<ValidationResult> results = [];
        var valid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        valid.Should().BeFalse();
        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(CreateBlogPostRequest.Title));
    }

    [Fact]
    public void Validator_AcceptsARequestWithinTheLimits()
    {
        var request = new CreateBlogPostRequest(
            Title: new string('x', ContentLimits.TitleLength),
            Summary: "summary",
            Content: null,
            SeoTitle: null,
            SeoDescription: null,
            SeoKeywords: null,
            OgImageUrl: null,
            TagNames: []);

        Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            [],
            validateAllProperties: true)
            .Should()
            .BeTrue();
    }

    /// <summary>
    /// Every length ceiling in the contracts assembly must be one of the constants the assembly
    /// itself declares. A literal would compile and read fine while quietly becoming a second copy
    /// of a number the schema and the server validator also hold — the exact drift this fix was
    /// weighed against.
    /// </summary>
    [Fact]
    public void EveryStringLength_ComesFromADeclaredLimitConstant()
    {
        var literals = ContractProperties()
            .Select(property => (
                Property: property,
                Attribute: property.GetCustomAttribute<StringLengthAttribute>()))
            .Where(pair => pair.Attribute is not null)
            .Where(pair => !DeclaredLimits.Contains(pair.Attribute!.MaximumLength))
            .Select(pair =>
                $"{pair.Property.DeclaringType!.Name}.{pair.Property.Name} = {pair.Attribute!.MaximumLength}")
            .ToArray();

        literals.Should().BeEmpty(
            "a length ceiling must reference ContentLimits, SeoLimits or RedirectLimits rather than "
            + "repeat the number, so the DTO cannot drift away from the column width and the "
            + "server-side check");
    }

    /// <summary>
    /// Encodes the other half of the decision: no attribute here may express a rule whose authority
    /// lives in the engine's validators. <c>MinLength</c> would restate the password policy,
    /// <c>Range</c> the settings bounds, <c>RegularExpression</c> the slug and URL character rules —
    /// none of which contracts can reference, so all three would be hand-copied numbers. If a rule
    /// genuinely belongs to both sides, move its constant into this assembly first and bound it with
    /// <c>StringLength</c>; then the test above permits it.
    /// </summary>
    [Fact]
    public void NoContractRestatesAServerSideValidatorRule()
    {
        var restated = ContractProperties()
            .SelectMany(property => property
                .GetCustomAttributes<ValidationAttribute>()
                .Where(attribute => attribute
                    is MinLengthAttribute or RangeAttribute or RegularExpressionAttribute)
                .Select(attribute =>
                    $"{property.DeclaringType!.Name}.{property.Name} [{attribute.GetType().Name}]"))
            .ToArray();

        restated.Should().BeEmpty(
            "the engine's PasswordPolicy, TextFieldValidator, AccountFieldValidator, "
            + "SiteSettingsValidator and UrlValidator are the authority for those rules, and "
            + "contracts cannot reference them without a circular dependency");
    }

    private static IEnumerable<PropertyInfo> ContractProperties() =>
        typeof(CreateBlogPostRequest).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == "BlogIt.Shared.DTOs")
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance));
}
