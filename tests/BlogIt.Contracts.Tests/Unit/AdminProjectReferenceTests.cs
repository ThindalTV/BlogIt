using System.Xml.Linq;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// BlogIt.Core references BlogIt.Admin only to build and bundle its published wwwroot as
/// BlogItAdminAssets. The admin is an executable, so without Private="false" the SDK also hands its
/// BlogIt.Admin.runtimeconfig.json to every referencing project, and a host that references BlogIt
/// from source ended up with two *.runtimeconfig.json files in its publish root. Azure App Service on
/// Linux refuses to pick a startup DLL in that case and runs its placeholder app instead.
/// </summary>
/// <remarks>
/// A fast guard on the metadata only. tests/PackageLayout/verify.ps1 proves the behaviour itself by
/// publishing a source-referencing host and asserting exactly one runtimeconfig in its output.
/// </remarks>
public class AdminProjectReferenceTests
{
    [Fact]
    public void CoreReferencesTheAdminWithoutFlowingItsRuntimeArtifacts()
    {
        var path = RepoLayout.Combine("src", "BlogIt.Core", "BlogIt.Core.csproj");
        File.Exists(path).Should().BeTrue("BlogIt.Core.csproj should exist under src/BlogIt.Core");

        var adminReferences = XDocument.Load(path)
            .Descendants("ProjectReference")
            .Where(r => ((string?)r.Attribute("Include"))?.Replace('\\', '/')
                .EndsWith("admin/BlogIt.Admin/BlogIt.Admin.csproj", StringComparison.Ordinal) == true)
            .ToList();

        adminReferences.Should().ContainSingle("BlogIt.Core references the admin exactly once, to bundle its assets");
        var reference = adminReferences[0];
        ((string?)reference.Attribute("ReferenceOutputAssembly")).Should().Be("false",
            "the engine does not compile against the WebAssembly admin");
        ((string?)reference.Attribute("Private")).Should().Be("false",
            "without it the admin's runtimeconfig lands in every source-referencing host's output root, " +
            "next to the host's own, and Azure App Service on Linux cannot pick a startup DLL");
    }
}
