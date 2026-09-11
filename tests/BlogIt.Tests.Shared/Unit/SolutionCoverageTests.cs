using System.Xml.Linq;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// CI is split in two: the web job builds BlogIt.Web.slnx, and a path-filtered MAUI job builds
/// the client, which needs the maui-android workload. That split is only safe while the two
/// solutions between them still cover everything — a project added to BlogIt.slnx but forgotten
/// in BlogIt.Web.slnx would silently stop being built and tested, which is the kind of drift
/// nobody notices until a release.
/// </summary>
public class SolutionCoverageTests
{
    /// <summary>
    /// The projects deliberately absent from the web solution: the MAUI app (needs the workload),
    /// its Core library and its tests, all built by the MAUI job instead.
    /// </summary>
    private static readonly string[] MauiSideProjects =
    [
        "src/BlogIt.MauiAdmin/BlogIt.MauiAdmin.csproj",
        "src/BlogIt.MauiAdmin.Core/BlogIt.MauiAdmin.Core.csproj",
        "tests/BlogIt.Tests.MAUI/BlogIt.Tests.MAUI.csproj",
    ];

    [Fact]
    public void WebSolutionCoversEveryNonMauiProject()
    {
        var expected = ReadProjects("BlogIt.slnx").Except(MauiSideProjects).ToList();

        ReadProjects("BlogIt.Web.slnx").Should().BeEquivalentTo(expected,
            "every project outside the MAUI client must stay in the solution the web CI job builds — " +
            "add it to BlogIt.Web.slnx too, or CI will stop building it");
    }

    [Fact]
    public void WebSolutionExcludesTheMauiSide()
    {
        var web = ReadProjects("BlogIt.Web.slnx");

        foreach (var project in MauiSideProjects)
            web.Should().NotContain(project,
                "the MAUI side is built by its own path-filtered job so ordinary web changes " +
                "do not pay for the maui-android workload");
    }

    [Fact]
    public void FullSolutionContainsEverything()
    {
        var full = ReadProjects("BlogIt.slnx");

        foreach (var project in MauiSideProjects)
            full.Should().Contain(project, "local development builds the whole product from BlogIt.slnx");

        full.Should().Contain(ReadProjects("BlogIt.Web.slnx"),
            "the web solution must never hold a project the full solution has forgotten");
    }

    private static List<string> ReadProjects(string solutionFileName)
    {
        var path = RepoLayout.Combine(solutionFileName);
        File.Exists(path).Should().BeTrue($"{solutionFileName} should exist at the repository root");

        return [.. XDocument.Load(path)
            .Descendants("Project")
            .Select(p => (string?)p.Attribute("Path"))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Replace('\\', '/'))
            .Order()];
    }
}
