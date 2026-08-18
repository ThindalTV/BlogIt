[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string] $PackageVersion = "0.0.1-package-proof",

    [string] $PackageFeed,

    [switch] $SkipPack
)

$ErrorActionPreference = "Stop"

$testRoot = $PSScriptRoot
$repo = Split-Path (Split-Path $testRoot -Parent) -Parent
$artifacts = Join-Path $testRoot "artifacts"
$feed = if ([string]::IsNullOrWhiteSpace($PackageFeed)) {
    Join-Path $artifacts "feed"
}
else {
    [IO.Path]::GetFullPath($PackageFeed)
}
$packagesPath = Join-Path $artifacts "packages"
$consumer = Join-Path $testRoot "Consumer\Consumer.csproj"
$azureConsumer = Join-Path $testRoot "AzureConsumer\AzureConsumer.csproj"
$aiAnalyticsConsumer = Join-Path $testRoot "AiAnalyticsConsumer\AiAnalyticsConsumer.csproj"
$contractsConsumer = Join-Path $testRoot "ContractsConsumer\ContractsConsumer.csproj"
$packageProject = Join-Path $repo "src\BlogIt\BlogIt.csproj"
$contractsPackageProject = Join-Path $repo "src\BlogIt.Contracts\BlogIt.Contracts.csproj"
$azurePackageProject = Join-Path $repo "src\BlogIt.AzureStorage\BlogIt.AzureStorage.csproj"
$openAiPackageProject = Join-Path $repo "src\BlogIt.OpenAi\BlogIt.OpenAi.csproj"
$analyticsPackageProject = Join-Path $repo "src\BlogIt.GoogleAnalytics\BlogIt.GoogleAnalytics.csproj"
$consumerOutput = Join-Path $artifacts "consumer-publish"
$version = $PackageVersion
$packageName = "BlogIt.$version.nupkg"
$contractsPackageName = "BlogIt.Contracts.$version.nupkg"
$azurePackageName = "BlogIt.AzureStorage.$version.nupkg"
$openAiPackageName = "BlogIt.OpenAi.$version.nupkg"
$analyticsPackageName = "BlogIt.GoogleAnalytics.$version.nupkg"
$symbolPackageName = "BlogIt.$version.snupkg"
$contractsSymbolPackageName = "BlogIt.Contracts.$version.snupkg"
$azureSymbolPackageName = "BlogIt.AzureStorage.$version.snupkg"
$openAiSymbolPackageName = "BlogIt.OpenAi.$version.snupkg"
$analyticsSymbolPackageName = "BlogIt.GoogleAnalytics.$version.snupkg"
$adminAssetPrefix = "staticwebassets/BlogItAdminAssets/"
$adminPublishTree = Join-Path $repo "src\BlogIt\obj\admin-publish\Release\wwwroot\blogit"

function Invoke-DotNet {
    $commandArguments = $args

    & dotnet @commandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($commandArguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try {
        return ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Assert-Status {
    param(
        [Parameter(Mandatory)] $Response,
        [Parameter(Mandatory)] [int] $Expected,
        [Parameter(Mandatory)] [string] $Description
    )

    if ([int]$Response.StatusCode -ne $Expected) {
        throw "$Description returned $([int]$Response.StatusCode), expected $Expected."
    }
}

function Assert-SameSet {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Actual,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Expected,
        [Parameter(Mandatory)] [string] $Description
    )

    $actualValues = @($Actual | ForEach-Object { [string]$_ })
    $expectedValues = @($Expected | ForEach-Object { [string]$_ })
    $missing = @($expectedValues | Where-Object { $actualValues -notcontains $_ })
    $unexpected = @($actualValues | Where-Object { $expectedValues -notcontains $_ })
    if ($missing.Count -ne 0 -or $unexpected.Count -ne 0) {
        throw "$Description mismatch. Missing: [$($missing -join ', ')]. Unexpected: [$($unexpected -join ', ')]."
    }
}

function Get-PackageInspection {
    param(
        [Parameter(Mandatory)] [IO.FileInfo] $Package
    )

    $archive = [IO.Compression.ZipFile]::OpenRead($Package.FullName)
    try {
        $entries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
        $nuspecEntry = $entries |
            Where-Object FullName -Like "*.nuspec" |
            Select-Object -First 1
        if ($null -eq $nuspecEntry) {
            throw "$($Package.Name) does not contain a nuspec."
        }

        $reader = [IO.StreamReader]::new($nuspecEntry.Open())
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        # Read the namespace off the document instead of hardcoding one. NuGet picks the nuspec
        # schema version per package from the metadata it actually emits: packages with dependency
        # or framework-reference content get .../2013/05/nuspec.xsd, while a leaf package with
        # neither gets .../2012/06/nuspec.xsd. This was pinned to the 2013/05 URI, which meant every
        # SelectNodes below silently returned nothing for a 2012/06 package - the license assertion
        # failed outright, but the dependency and framework-reference assertions would have passed
        # for the wrong reason, reporting an empty set for a package that declared plenty. Found when
        # BlogIt.Contracts became its own dependency-free package and hit the 2012/06 path.
        $namespace = [Xml.XmlNamespaceManager]::new($nuspec.NameTable)
        $namespace.AddNamespace("n", $nuspec.DocumentElement.NamespaceURI)
        $dependencies = @(
            $nuspec.SelectNodes("//n:dependency", $namespace) |
                ForEach-Object {
                    [pscustomobject]@{
                        Id = [string]$_.id
                        Version = [string]$_.version
                    }
                }
        )
        $frameworkReferences = @(
            $nuspec.SelectNodes("//n:frameworkReference", $namespace) |
                ForEach-Object { [string]$_.name }
        )
        $licenseNode = $nuspec.SelectSingleNode("//n:license", $namespace)

        return [pscustomobject]@{
            Id = [string]$nuspec.package.metadata.id
            Version = [string]$nuspec.package.metadata.version
            EntryNames = @($entries.FullName)
            Dependencies = $dependencies
            FrameworkReferences = $frameworkReferences
            LicenseType = if ($null -eq $licenseNode) { $null } else { [string]$licenseNode.type }
            LicenseValue = if ($null -eq $licenseNode) { $null } else { [string]$licenseNode.InnerText }
        }
    }
    finally {
        $archive.Dispose()
    }
}

# NuGet.org renders "License not specified" for any package whose nuspec carries no <license>
# node, which is indistinguishable from proprietary to anyone evaluating the package. The repo
# ships LICENSE.txt (MIT), so every package produced from it must advertise that as an SPDX
# expression. Asserted as an expression rather than a packed license file so the metadata is
# machine-readable for consumers running license audits.
function Assert-PackageLicense {
    param(
        [Parameter(Mandatory)] $Inspection,
        [Parameter(Mandatory)] [string] $Expected
    )

    if ($Inspection.LicenseType -ne "expression" -or $Inspection.LicenseValue -ne $Expected) {
        throw "$($Inspection.Id) declares license type '$($Inspection.LicenseType)' value '$($Inspection.LicenseValue)'; expected the SPDX expression '$Expected'."
    }
}

# PackageVersion is what CI and this harness pass on the pack command line, and NuGet's default
# wiring only flows the other way (Version -> PackageVersion), so without explicit plumbing every
# release shipped assemblies stamped 1.0.0.0 - leaving customer stack traces and crash dumps
# unable to tell one build from another. AssemblyVersion/FileVersion take the four-part numeric
# core (a prerelease label is not legal in either), and InformationalVersion carries the full
# package version; SourceLink appends "+<commit sha>" to it, so this is a prefix match.
function Assert-PackedAssemblyVersion {
    param(
        [Parameter(Mandatory)] [IO.FileInfo] $Package,
        [Parameter(Mandatory)] [string] $Entry,
        [Parameter(Mandatory)] [string] $PackageVersion
    )

    $numericCore = [regex]::Match($PackageVersion, '^[0-9]+(\.[0-9]+)*').Value
    $expectedAssemblyVersion = [regex]::Match("$numericCore.0.0.0", '^[0-9]+(\.[0-9]+){3}').Value

    # Copied out through streams rather than ZipFileExtensions::ExtractToFile: that is an extension
    # method, and PowerShell fails to bind its three-argument overload here. The assembly has to
    # land on disk either way, because AssemblyName and FileVersionInfo both need a file path -
    # and reading metadata from a file is what keeps this from loading customer code into the
    # verifier process.
    $extracted = Join-Path ([IO.Path]::GetTempPath()) ("blogit-verify-" + [Guid]::NewGuid().ToString("N") + ".dll")
    $archive = [IO.Compression.ZipFile]::OpenRead($Package.FullName)
    try {
        $packedEntry = $archive.Entries | Where-Object FullName -EQ $Entry | Select-Object -First 1
        if ($null -eq $packedEntry) {
            throw "$($Package.Name) does not contain $Entry."
        }

        $source = $packedEntry.Open()
        try {
            $destination = [IO.File]::Create($extracted)
            try {
                $source.CopyTo($destination)
            }
            finally {
                $destination.Dispose()
            }
        }
        finally {
            $source.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    try {
        $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($extracted).Version.ToString()
        $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($extracted)
        if ($assemblyVersion -ne $expectedAssemblyVersion) {
            throw "$Entry in $($Package.Name) has AssemblyVersion '$assemblyVersion'; expected '$expectedAssemblyVersion' for package version '$PackageVersion'."
        }
        if ($versionInfo.FileVersion -ne $expectedAssemblyVersion) {
            throw "$Entry in $($Package.Name) has FileVersion '$($versionInfo.FileVersion)'; expected '$expectedAssemblyVersion'."
        }
        if (-not $versionInfo.ProductVersion.StartsWith($PackageVersion, [StringComparison]::Ordinal)) {
            throw "$Entry in $($Package.Name) has InformationalVersion '$($versionInfo.ProductVersion)'; expected it to start with '$PackageVersion'."
        }
    }
    finally {
        Remove-Item $extracted -Force -ErrorAction SilentlyContinue
    }
}

function Assert-PackageDependencies {
    param(
        [Parameter(Mandatory)] $Inspection,
        [Parameter(Mandatory)] [Collections.IDictionary] $Expected,
        [Parameter(Mandatory)] [string] $Description
    )

    Assert-SameSet `
        -Actual @($Inspection.Dependencies.Id) `
        -Expected @($Expected.Keys) `
        -Description "$Description dependency IDs"

    foreach ($id in $Expected.Keys) {
        $dependency = @($Inspection.Dependencies | Where-Object Id -EQ $id)
        if ($dependency.Count -ne 1 -or $dependency[0].Version -ne $Expected[$id]) {
            throw "$Description dependency '$id' has version '$($dependency.Version -join ', ')'; expected '$($Expected[$id])'."
        }
    }
}

function Invoke-ConsumerScenario {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $AdminPath,
        [Parameter(Mandatory)] [string] $ApiPath,
        [Parameter(Mandatory)] [string] $AdminWasmPath
    )

    $port = Get-FreePort
    $baseUrl = "http://127.0.0.1:$port"
    $serverLog = Join-Path $artifacts "$Name.log"
    $serverErrorLog = Join-Path $artifacts "$Name.err.log"
    $oldUrls = $env:ASPNETCORE_URLS
    $oldAdminPath = $env:AdminPath
    $oldApiPath = $env:ApiPath
    $server = $null

    try {
        $env:ASPNETCORE_URLS = $baseUrl
        $env:AdminPath = $AdminPath
        $env:ApiPath = $ApiPath
        $server = Start-Process dotnet `
            -ArgumentList "`"$consumerOutput\Consumer.dll`"" `
            -WorkingDirectory $consumerOutput `
            -RedirectStandardOutput $serverLog `
            -RedirectStandardError $serverErrorLog `
            -PassThru
    }
    finally {
        $env:ASPNETCORE_URLS = $oldUrls
        $env:AdminPath = $oldAdminPath
        $env:ApiPath = $oldApiPath
    }

    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)

    try {
        $shell = $null
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            if ($server.HasExited) {
                throw "$Name consumer exited before becoming ready. See $serverErrorLog."
            }

            try {
                $shell = $client.GetAsync("$baseUrl$AdminPath/").GetAwaiter().GetResult()
                if ($shell.IsSuccessStatusCode) {
                    break
                }
            }
            catch {
                Start-Sleep -Milliseconds 250
            }
        }

        if ($null -eq $shell -or -not $shell.IsSuccessStatusCode) {
            throw "$Name consumer did not become ready at $baseUrl."
        }

        $shellHtml = $shell.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (($shellHtml -notmatch "<title>BlogIt Admin</title>") -or
            ($shellHtml -notmatch "<base href=`"$([regex]::Escape($AdminPath))/`" />")) {
            throw "$Name shell did not contain the expected dynamic base path."
        }

        $redirect = $client.GetAsync("$baseUrl$AdminPath").GetAwaiter().GetResult()
        Assert-Status $redirect 302 "$Name exact admin path"
        if ($redirect.Headers.Location.OriginalString -ne "$AdminPath/") {
            throw "$Name redirect location was '$($redirect.Headers.Location)', expected '$AdminPath/'."
        }

        $config = $client.GetAsync(
            "$baseUrl$AdminPath/_blogit/config").GetAwaiter().GetResult()
        Assert-Status $config 200 "$Name bootstrap config"
        $configJson = $config.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        if ($configJson.apiPath -ne $ApiPath) {
            throw "$Name bootstrap returned API path '$($configJson.apiPath)', expected '$ApiPath'."
        }

        $framework = $client.GetAsync(
            "$baseUrl$AdminPath/_framework/blazor.webassembly.js").GetAwaiter().GetResult()
        Assert-Status $framework 200 "$Name framework loader"
        if (($framework.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()).Length -lt 1000) {
            throw "$Name framework loader was unexpectedly small."
        }

        $adminWasm = $client.GetAsync(
            "$baseUrl$AdminPath/$AdminWasmPath").GetAwaiter().GetResult()
        Assert-Status $adminWasm 200 "$Name admin WebAssembly"
        if ($adminWasm.Content.Headers.ContentType.MediaType -ne "application/wasm") {
            throw "$Name admin assembly had content type '$($adminWasm.Content.Headers.ContentType)'."
        }
        if (-not $adminWasm.Headers.CacheControl.ToString().Contains(
                "immutable",
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "$Name fingerprinted admin assembly was not served with immutable caching."
        }

        $deepLink = $client.GetAsync(
            "$baseUrl$AdminPath/posts/edit/123").GetAwaiter().GetResult()
        Assert-Status $deepLink 200 "$Name deep link"
        $deepHtml = $deepLink.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if ($deepHtml -notmatch "<title>BlogIt Admin</title>") {
            throw "$Name deep link did not serve the admin shell."
        }

        $setup = $client.GetAsync(
            "$baseUrl$ApiPath/setup/status").GetAwaiter().GetResult()
        Assert-Status $setup 200 "$Name configured API path"

        $contractAssembly = $client.GetAsync(
            "$baseUrl/contract-assembly").GetAwaiter().GetResult()
        Assert-Status $contractAssembly 200 "$Name contracts assembly"
        if ($contractAssembly.Content.ReadAsStringAsync().GetAwaiter().GetResult().Trim() -ne
            "BlogIt.Contracts") {
            throw "$Name did not load BlogIt.Contracts as a transitive dependency of the BlogIt package."
        }

        $packageSurface = $client.GetAsync(
            "$baseUrl/package-surface").GetAwaiter().GetResult()
        Assert-Status $packageSurface 200 "$Name public package surface"
        $surfaceJson = $packageSurface.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        foreach ($publicType in @(
            "BlogIt.Components.Shared.SeoHead",
            "BlogIt.Services.IPublicContentService",
            "BlogIt.Shared.DTOs.BlogPostSummaryDto"
        )) {
            if ($surfaceJson -notmatch [regex]::Escape($publicType)) {
                throw "$Name public package surface did not expose $publicType."
            }
        }

        $internal = $client.GetAsync(
            "$baseUrl/BlogItAdminAssets/index.html").GetAwaiter().GetResult()
        Assert-Status $internal 404 "$Name internal asset path"

        if ($AdminPath -ne "/blogit") {
            $oldAdmin = $client.GetAsync("$baseUrl/blogit/").GetAwaiter().GetResult()
            Assert-Status $oldAdmin 404 "$Name old admin path"
            $oldApi = $client.GetAsync("$baseUrl/api/setup/status").GetAwaiter().GetResult()
            Assert-Status $oldApi 404 "$Name old API path"
        }

        Write-Host "PASS ${Name}: $AdminPath/, $ApiPath, framework, WebAssembly, deep link"
    }
    finally {
        $client.Dispose()
        $handler.Dispose()
        if ($null -ne $server -and -not $server.HasExited) {
            Stop-Process -Id $server.Id
            $server.WaitForExit()
        }
    }
}

# A floating version is re-resolved on every restore, so the same BlogIt source can pack
# against different dependency builds - which silently contradicts <Deterministic>true</Deterministic>
# and, for a preview wildcard, drags every consuming application onto preview dependencies.
# Every project whose output reaches a consumer must pin exact versions: BlogIt and
# BlogIt.AzureStorage because their nuspecs become the consumer's dependency floor, and
# BlogIt.Admin and BlogIt.Contracts because their published output is packed verbatim.
# Test, sample and consumer-fixture projects are deliberately not covered - nothing they
# restore reaches a consumer. Checked before packing so this fails in seconds, not minutes.
foreach ($shippedProject in @(
    (Join-Path $repo "src\BlogIt\BlogIt.csproj"),
    (Join-Path $repo "src\BlogIt.Admin\BlogIt.Admin.csproj"),
    (Join-Path $repo "src\BlogIt.Contracts\BlogIt.Contracts.csproj"),
    (Join-Path $repo "src\BlogIt.AzureStorage\BlogIt.AzureStorage.csproj"),
    (Join-Path $repo "src\BlogIt.OpenAi\BlogIt.OpenAi.csproj"),
    (Join-Path $repo "src\BlogIt.GoogleAnalytics\BlogIt.GoogleAnalytics.csproj")
)) {
    $floatingVersions = @(
        [regex]::Matches(
            (Get-Content $shippedProject -Raw),
            '<PackageReference\s[^>]*?Version="(?<version>[^"]*\*[^"]*)"') |
            ForEach-Object { $_.Groups["version"].Value }
    )
    if ($floatingVersions.Count -ne 0) {
        throw "$([IO.Path]::GetFileName($shippedProject)) declares floating package versions [$($floatingVersions -join ', ')]; shipped projects must pin exact versions."
    }
}

foreach ($generatedPath in @(
    (Join-Path $testRoot "Consumer\bin"),
    (Join-Path $testRoot "Consumer\obj"),
    (Join-Path $testRoot "AzureConsumer\bin"),
    (Join-Path $testRoot "AzureConsumer\obj"),
    (Join-Path $testRoot "AiAnalyticsConsumer\bin"),
    (Join-Path $testRoot "AiAnalyticsConsumer\obj"),
    (Join-Path $testRoot "ContractsConsumer\bin"),
    (Join-Path $testRoot "ContractsConsumer\obj")
)) {
    Remove-Item $generatedPath -Recurse -Force -ErrorAction SilentlyContinue
}

if ($SkipPack) {
    $defaultFeed = [IO.Path]::GetFullPath((Join-Path $artifacts "feed"))
    if ([IO.Path]::GetFullPath($feed) -eq $defaultFeed) {
        Get-ChildItem $artifacts -Force -ErrorAction SilentlyContinue |
            Where-Object { [IO.Path]::GetFullPath($_.FullName) -ne $defaultFeed } |
            Remove-Item -Recurse -Force
    }
    else {
        Remove-Item $artifacts -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item $artifacts -ItemType Directory -Force | Out-Null
}
else {
    Remove-Item $artifacts -Recurse -Force -ErrorAction SilentlyContinue
    if (-not [string]::IsNullOrWhiteSpace($PackageFeed)) {
        Remove-Item $feed -Recurse -Force -ErrorAction SilentlyContinue
    }
}

New-Item $feed -ItemType Directory -Force | Out-Null
New-Item $packagesPath -ItemType Directory -Force | Out-Null

if (-not $SkipPack) {
    Invoke-DotNet pack $packageProject `
        -c Release `
        -o $feed `
        --nologo `
        "-p:PackageVersion=$version"
    # Packed in its own right, not carried inside BlogIt: a client that only speaks to the API over
    # HTTP has to be able to take the DTOs without EF Core, SqlClient and BCrypt coming with them.
    Invoke-DotNet pack $contractsPackageProject `
        -c Release `
        -o $feed `
        --nologo `
        "-p:PackageVersion=$version"
    Invoke-DotNet pack $azurePackageProject `
        -c Release `
        -o $feed `
        --nologo `
        "-p:PackageVersion=$version"
    Invoke-DotNet pack $openAiPackageProject `
        -c Release `
        -o $feed `
        --nologo `
        "-p:PackageVersion=$version"
    Invoke-DotNet pack $analyticsPackageProject `
        -c Release `
        -o $feed `
        --nologo `
        "-p:PackageVersion=$version"
}

$producedPackages = @(
    Get-ChildItem $feed -File |
        Where-Object Extension -EQ ".nupkg"
)
Assert-SameSet `
    -Actual @($producedPackages.Name) `
    -Expected @(
        $packageName,
        $contractsPackageName,
        $azurePackageName,
        $openAiPackageName,
        $analyticsPackageName) `
    -Description "Produced nupkgs"
$producedSymbolPackages = @(
    Get-ChildItem $feed -File |
        Where-Object Extension -EQ ".snupkg"
)
Assert-SameSet `
    -Actual @($producedSymbolPackages.Name) `
    -Expected @(
        $symbolPackageName,
        $contractsSymbolPackageName,
        $azureSymbolPackageName,
        $openAiSymbolPackageName,
        $analyticsSymbolPackageName) `
    -Description "Produced snupkgs"
foreach ($symbolPackage in $producedSymbolPackages) {
    $correspondingPackage = Join-Path $feed (
        $symbolPackage.Name.Substring(0, $symbolPackage.Name.Length - ".snupkg".Length) +
        ".nupkg")
    if (-not (Test-Path $correspondingPackage -PathType Leaf)) {
        throw "Symbol package '$($symbolPackage.Name)' has no corresponding nupkg."
    }
}

$package = Get-Item (Join-Path $feed $packageName)
$contractsPackage = Get-Item (Join-Path $feed $contractsPackageName)
$azurePackage = Get-Item (Join-Path $feed $azurePackageName)
$openAiPackage = Get-Item (Join-Path $feed $openAiPackageName)
$analyticsPackage = Get-Item (Join-Path $feed $analyticsPackageName)
# The ceiling is deliberately close to the real size (~11 MB) so that re-introducing the
# precompressed admin variants - which alone added 18.4 MB of already-compressed, and
# therefore incompressible, payload - trips this instead of passing unnoticed.
if ($package.Length -gt 15MB) {
    throw "BlogIt package size is $([math]::Round($package.Length / 1MB, 2)) MB; expected at most 15 MB."
}
# Every satellite is a handful of source files over a provider abstraction; anything approaching a
# megabyte means it has started duplicating the engine's assets rather than depending on them.
foreach ($satellite in @($azurePackage, $openAiPackage, $analyticsPackage)) {
    if ($satellite.Length -gt 1MB) {
        throw "$($satellite.Name) size is $([math]::Round($satellite.Length / 1KB, 2)) KB; expected at most 1 MB."
    }
}
# Contracts is a few dozen records and four constant classes over no dependencies. A tighter ceiling
# than the satellites get because the whole claim of this package is that it is cheap to take: if it
# ever grows past this, something with real weight has been moved into it and a client is paying for
# the engine again by another route.
if ($contractsPackage.Length -gt 200KB) {
    throw "$($contractsPackage.Name) size is $([math]::Round($contractsPackage.Length / 1KB, 2)) KB; expected at most 200 KB."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$mainInspection = Get-PackageInspection $package
$contractsInspection = Get-PackageInspection $contractsPackage
$azureInspection = Get-PackageInspection $azurePackage
$openAiInspection = Get-PackageInspection $openAiPackage
$analyticsInspection = Get-PackageInspection $analyticsPackage

foreach ($symbolExpectation in @(
    @{
        Package = Get-Item (Join-Path $feed $symbolPackageName)
        Pdb = "lib/net10.0/BlogIt.pdb"
    },
    @{
        Package = Get-Item (Join-Path $feed $contractsSymbolPackageName)
        Pdb = "lib/net10.0/BlogIt.Contracts.pdb"
    },
    @{
        Package = Get-Item (Join-Path $feed $azureSymbolPackageName)
        Pdb = "lib/net10.0/BlogIt.AzureStorage.pdb"
    },
    @{
        Package = Get-Item (Join-Path $feed $openAiSymbolPackageName)
        Pdb = "lib/net10.0/BlogIt.OpenAi.pdb"
    },
    @{
        Package = Get-Item (Join-Path $feed $analyticsSymbolPackageName)
        Pdb = "lib/net10.0/BlogIt.GoogleAnalytics.pdb"
    }
)) {
    $symbolArchive = [IO.Compression.ZipFile]::OpenRead($symbolExpectation.Package.FullName)
    try {
        $symbolEntries = @(
            $symbolArchive.Entries |
                Where-Object { -not [string]::IsNullOrEmpty($_.Name) } |
                ForEach-Object FullName
        )
        if ($symbolEntries -notcontains $symbolExpectation.Pdb) {
            throw "$($symbolExpectation.Package.Name) is missing $($symbolExpectation.Pdb)."
        }
    }
    finally {
        $symbolArchive.Dispose()
    }
}

if ($mainInspection.Id -ne "BlogIt" -or $mainInspection.Version -ne $version) {
    throw "Main package identity is '$($mainInspection.Id) $($mainInspection.Version)', expected 'BlogIt $version'."
}
foreach ($requiredEntry in @(
    "README.md",
    "lib/net10.0/BlogIt.dll",
    "buildTransitive/BlogIt.targets",
    "${adminAssetPrefix}index.html",
    "${adminAssetPrefix}_framework/blazor.webassembly.js"
)) {
    if ($mainInspection.EntryNames -notcontains $requiredEntry) {
        throw "BlogIt package is missing $requiredEntry."
    }
}
# BlogIt.Contracts.dll is deliberately absent from this set. It used to be injected here by an
# IncludeBlogItContractsInPackage target while its own project was IsPackable=false, which meant the
# assembly shipped with no independent version and no way to take it on its own: writing a separate
# client meant referencing all of BlogIt and restoring EF Core, SqlClient and BCrypt for a handful of
# records. It is now its own package and an ordinary nuspec dependency, asserted below. Smuggling it
# back in would give a consumer two copies of the same assembly from two sources, so this set is
# exact rather than a contains-check.
Assert-SameSet `
    -Actual @($mainInspection.EntryNames | Where-Object { $_ -Like "lib/*" }) `
    -Expected @(
        "lib/net10.0/BlogIt.dll",
        "lib/net10.0/BlogIt.runtimeconfig.json"
    ) `
    -Description "BlogIt library assets"

if (-not (Test-Path $adminPublishTree -PathType Container)) {
    throw "Admin publish tree was not produced at $adminPublishTree."
}
$publishedAdminSourceAssets = @(
    Get-ChildItem $adminPublishTree -File -Recurse |
        ForEach-Object {
            [IO.Path]::GetRelativePath($adminPublishTree, $_.FullName).Replace('\', '/')
        }
)
$adminAssets = @(
    $mainInspection.EntryNames |
        Where-Object { $_.StartsWith($adminAssetPrefix, [StringComparison]::Ordinal) }
)
$adminAssetRelativePaths = @(
    $adminAssets | ForEach-Object { $_.Substring($adminAssetPrefix.Length) }
)
Assert-SameSet `
    -Actual $adminAssetRelativePaths `
    -Expected $publishedAdminSourceAssets `
    -Description "Complete packaged admin publish tree"
if ($adminAssets.Count -lt 100) {
    throw "BlogIt package contains only $($adminAssets.Count) admin assets."
}

# The admin tree is served from a private PhysicalFileProvider through a plain
# UseStaticFiles pipeline (AdminAssetMiddlewareContributor), which performs no
# Accept-Encoding negotiation. Any .br/.gz variant in here is therefore unservable weight
# that still lands in every consuming project's bin/ and publish/. Serving them properly
# was considered and rejected: it needs hand-rolled negotiation (q-values, Content-Encoding,
# Vary, inner-extension content type, per-variant validators) for a saving hosts already
# get from response compression or the proxy/CDN in front of them.
$precompressedAdminAssets = @(
    $adminAssetRelativePaths | Where-Object { $_ -match '\.(br|gz)$' }
)
if ($precompressedAdminAssets.Count -ne 0) {
    throw "BlogIt package contains $($precompressedAdminAssets.Count) unservable precompressed admin assets, for example '$($precompressedAdminAssets[0])'."
}

$adminWasmEntry = $adminAssets |
    Where-Object { $_ -Match '/_framework/BlogIt\.Admin\.[^/]+\.wasm$' } |
    Select-Object -First 1
$contractsWasmEntry = $adminAssets |
    Where-Object { $_ -Match '/_framework/BlogIt\.Contracts\.[^/]+\.wasm$' } |
    Select-Object -First 1
if ($null -eq $adminWasmEntry -or $null -eq $contractsWasmEntry) {
    throw "BlogIt package is missing the BlogIt.Admin or BlogIt.Contracts browser assembly."
}

$forbidden = @(
    "Microsoft.EntityFrameworkCore",
    "Microsoft.Data.SqlClient",
    "Azure.",
    "OpenAI.",
    "Google.Analytics"
)
foreach ($dependency in $forbidden) {
    if (@($adminAssets | Where-Object {
        $_ -Match [regex]::Escape($dependency)
    }).Count -ne 0) {
        throw "Browser assets unexpectedly contain $dependency."
    }
}

# These are the exact versions the nuspec must advertise as the consumer's dependency
# floor. They are asserted literally, not as a range, so bumping a pin is a deliberate edit
# here as well as in the csproj - and so a floating version that happens to resolve to a
# stable build today cannot pass while still being floating tomorrow.
$mainDependencies = [ordered]@{
    "BCrypt.Net-Next" = "4.2.0"
    # Exact, and equal to this package's own version: the DTOs are the wire format both halves
    # compile against, so an engine paired with a different contracts build is a silent
    # serialisation mismatch. Falls out of the ProjectReference rather than being hand-written, but
    # asserted here so a stray PrivateAssets="all" cannot quietly turn it back into a smuggled DLL.
    "BlogIt.Contracts" = $version
    "Markdig" = "1.3.2"
    "Microsoft.AspNetCore.Authentication.JwtBearer" = "10.0.11"
    "Microsoft.EntityFrameworkCore" = "10.0.11"
    "Microsoft.EntityFrameworkCore.SqlServer" = "10.0.11"
    "System.IdentityModel.Tokens.Jwt" = "8.22.0"
}
Assert-PackageDependencies `
    -Inspection $mainInspection `
    -Expected $mainDependencies `
    -Description "BlogIt"
Assert-SameSet `
    -Actual @($mainInspection.FrameworkReferences) `
    -Expected @("Microsoft.AspNetCore.App") `
    -Description "BlogIt framework references"

# Named separately from the exact-set assertion above so a regression reports the reason rather
# than just "unexpected dependency". These two SDKs are the reason the core package could not be
# published as a stable 1.0.0: Google.Analytics.Data.V1Beta has no stable release, so a stable
# core version depending on it raises NU5104, and feeds configured to reject prerelease
# transitives refuse the package outright. They also drag the Gax/gRPC/Protobuf tree into every
# consumer that never touches AI or analytics, which is what the satellite split exists to stop.
foreach ($satelliteOnlyDependency in @("OpenAI", "Google.Analytics.Data.V1Beta")) {
    if (@($mainInspection.Dependencies.Id) -contains $satelliteOnlyDependency) {
        throw "BlogIt depends on '$satelliteOnlyDependency'; that SDK belongs in its satellite package so hosts that do not use it never restore it."
    }
}

# Every satellite is held to the same shape: one library asset, its own README, an exact-version
# dependency on the matching BlogIt so a consumer can never end up with a mismatched pair, exactly
# one SDK of its own, no framework reference, and none of the engine's admin or build-transitive
# assets duplicated. Asserted as one table so adding a satellite means adding a row, not a block.
$satelliteExpectations = @(
    @{
        Inspection = $azureInspection
        Package = $azurePackage
        Id = "BlogIt.AzureStorage"
        Assembly = "BlogIt.AzureStorage.dll"
        Dependencies = [ordered]@{
            "BlogIt" = $version
            "Azure.Storage.Blobs" = "12.29.1"
        }
    },
    @{
        Inspection = $openAiInspection
        Package = $openAiPackage
        Id = "BlogIt.OpenAi"
        Assembly = "BlogIt.OpenAi.dll"
        Dependencies = [ordered]@{
            "BlogIt" = $version
            "OpenAI" = "2.12.0"
        }
    },
    @{
        Inspection = $analyticsInspection
        Package = $analyticsPackage
        Id = "BlogIt.GoogleAnalytics"
        Assembly = "BlogIt.GoogleAnalytics.dll"
        # The one prerelease dependency left in the repo, and the reason this satellite exists:
        # Google publishes no stable Analytics Data client, so keeping it here lets BlogIt itself
        # release stable. This package ships prerelease until Google ships a stable V1 client.
        Dependencies = [ordered]@{
            "BlogIt" = $version
            "Google.Analytics.Data.V1Beta" = "2.0.0-beta10"
        }
    }
)
foreach ($satellite in $satelliteExpectations) {
    $inspection = $satellite.Inspection
    if ($inspection.Id -ne $satellite.Id -or $inspection.Version -ne $version) {
        throw "Satellite package identity is '$($inspection.Id) $($inspection.Version)', expected '$($satellite.Id) $version'."
    }
    if ($inspection.EntryNames -notcontains "README.md") {
        throw "$($satellite.Id) package is missing README.md."
    }
    Assert-SameSet `
        -Actual @($inspection.EntryNames | Where-Object { $_ -Like "lib/*" }) `
        -Expected @("lib/net10.0/$($satellite.Assembly)") `
        -Description "$($satellite.Id) library assets"
    if (@($inspection.EntryNames | Where-Object {
        $_ -Like "staticwebassets/*" -or $_ -Like "buildTransitive/*"
    }).Count -ne 0) {
        throw "$($satellite.Id) duplicates BlogIt admin or build-transitive assets."
    }
    Assert-PackageDependencies `
        -Inspection $inspection `
        -Expected $satellite.Dependencies `
        -Description $satellite.Id
    Assert-SameSet `
        -Actual @($inspection.FrameworkReferences) `
        -Expected @() `
        -Description "$($satellite.Id) framework references"
}

# Contracts is held to a shape of its own rather than added to the satellite table above: a
# satellite depends on the engine, and this deliberately does the opposite - the engine depends on
# it. The assertions that matter are the empty dependency set and the empty framework reference set,
# because those two together are the whole finding. A client that has to restore EF Core, SqlClient,
# BCrypt or take a Microsoft.AspNetCore.App framework reference to deserialise a BlogPostDetailDto is
# back where it started, and either of those creeping in would otherwise be invisible until someone
# tried to consume the package from a console app or a MAUI target.
if ($contractsInspection.Id -ne "BlogIt.Contracts" -or $contractsInspection.Version -ne $version) {
    throw "Contracts package identity is '$($contractsInspection.Id) $($contractsInspection.Version)', expected 'BlogIt.Contracts $version'."
}
if ($contractsInspection.EntryNames -notcontains "README.md") {
    throw "BlogIt.Contracts package is missing README.md."
}
Assert-SameSet `
    -Actual @($contractsInspection.EntryNames | Where-Object { $_ -Like "lib/*" }) `
    -Expected @("lib/net10.0/BlogIt.Contracts.dll") `
    -Description "BlogIt.Contracts library assets"
# Asserted with Assert-SameSet rather than Assert-PackageDependencies because the expectation is the
# empty set, and that helper takes a dictionary of id-to-version whose Keys collection does not
# survive being empty. The empty set is the whole point, so it gets its own line.
Assert-SameSet `
    -Actual @($contractsInspection.Dependencies | ForEach-Object Id) `
    -Expected @() `
    -Description "BlogIt.Contracts dependencies"
Assert-SameSet `
    -Actual @($contractsInspection.FrameworkReferences) `
    -Expected @() `
    -Description "BlogIt.Contracts framework references"
if (@($contractsInspection.EntryNames | Where-Object {
    $_ -Like "staticwebassets/*" -or $_ -Like "buildTransitive/*"
}).Count -ne 0) {
    throw "BlogIt.Contracts duplicates BlogIt admin or build-transitive assets."
}

foreach ($licensedPackage in @(
    $mainInspection,
    $contractsInspection,
    $azureInspection,
    $openAiInspection,
    $analyticsInspection
)) {
    Assert-PackageLicense -Inspection $licensedPackage -Expected "MIT"
}

# BlogIt.Contracts.dll is stamped from its own package now that it has one. It carried the stamp
# while it was smuggled inside BlogIt for the same reason - a stack trace crossing it has to identify
# the build - and the requirement only gets stronger now that a client can restore it independently.
foreach ($stampedAssembly in @(
    @{ Package = $package; Entry = "lib/net10.0/BlogIt.dll" },
    @{ Package = $contractsPackage; Entry = "lib/net10.0/BlogIt.Contracts.dll" },
    @{ Package = $azurePackage; Entry = "lib/net10.0/BlogIt.AzureStorage.dll" },
    @{ Package = $openAiPackage; Entry = "lib/net10.0/BlogIt.OpenAi.dll" },
    @{ Package = $analyticsPackage; Entry = "lib/net10.0/BlogIt.GoogleAnalytics.dll" }
)) {
    Assert-PackedAssemblyVersion `
        -Package $stampedAssembly.Package `
        -Entry $stampedAssembly.Entry `
        -PackageVersion $version
}

$consumerProjectText = Get-Content $consumer -Raw
$azureConsumerProjectText = Get-Content $azureConsumer -Raw
$aiAnalyticsConsumerProjectText = Get-Content $aiAnalyticsConsumer -Raw
$contractsConsumerProjectText = Get-Content $contractsConsumer -Raw
foreach ($projectText in @(
    $consumerProjectText,
    $azureConsumerProjectText,
    $aiAnalyticsConsumerProjectText,
    $contractsConsumerProjectText
)) {
    if ($projectText -match "<ProjectReference") {
        throw "Clean package consumers must not contain source ProjectReferences."
    }
}
if (($consumerProjectText -notmatch 'PackageReference Include="BlogIt"') -or
    ($consumerProjectText -match 'PackageReference Include="BlogIt\.(Contracts|Admin|AzureStorage|OpenAi|GoogleAnalytics)"')) {
    throw "The filesystem consumer must reference only the BlogIt production package."
}
if (($azureConsumerProjectText -notmatch 'PackageReference Include="BlogIt\.AzureStorage"') -or
    ($azureConsumerProjectText -match 'PackageReference Include="BlogIt"')) {
    throw "The Azure consumer must reference only BlogIt.AzureStorage and receive BlogIt transitively."
}
if (($aiAnalyticsConsumerProjectText -notmatch 'PackageReference Include="BlogIt\.OpenAi"') -or
    ($aiAnalyticsConsumerProjectText -notmatch 'PackageReference Include="BlogIt\.GoogleAnalytics"') -or
    ($aiAnalyticsConsumerProjectText -match 'PackageReference Include="BlogIt"')) {
    throw "The AI/analytics consumer must reference only the two satellites and receive BlogIt transitively."
}
# The fixture that proves the finding is fixed. Its one PackageReference must be BlogIt.Contracts and
# it must not name BlogIt or any satellite, because "a third-party client can reference contracts
# alone" is exactly the claim, and a fixture that quietly acquired a BlogIt reference would still
# compile while proving nothing.
if (($contractsConsumerProjectText -notmatch 'PackageReference Include="BlogIt\.Contracts"') -or
    ($contractsConsumerProjectText -match 'PackageReference Include="BlogIt"') -or
    ($contractsConsumerProjectText -match 'PackageReference Include="BlogIt\.(Admin|AzureStorage|OpenAi|GoogleAnalytics)"')) {
    throw "The contracts consumer must reference only the BlogIt.Contracts package."
}
# Not the web SDK either: a console or MAUI client is the case the contracts package exists for, and
# Microsoft.NET.Sdk.Web would silently supply the ASP.NET Core framework reference that the empty
# framework-reference assertion above is trying to prove the package does not need.
if ($contractsConsumerProjectText -notmatch 'Sdk="Microsoft\.NET\.Sdk"') {
    throw "The contracts consumer must build on the plain Microsoft.NET.Sdk, not the web SDK."
}

$restoreProperties = @(
    "-p:BlogItPackageVersion=$version",
    "-p:RestorePackagesPath=$packagesPath"
)
foreach ($consumerProject in @(
    $consumer,
    $azureConsumer,
    $aiAnalyticsConsumer,
    $contractsConsumer
)) {
    Invoke-DotNet restore $consumerProject `
        "-p:RestoreAdditionalProjectSources=$feed" `
        @restoreProperties `
        --force `
        --no-cache `
        --nologo
}
Invoke-DotNet build $consumer -c Release --no-restore --nologo @restoreProperties
Invoke-DotNet build $azureConsumer -c Release --no-restore --nologo @restoreProperties
Invoke-DotNet build $aiAnalyticsConsumer -c Release --no-restore --nologo @restoreProperties
# Compiling is the assertion here: ClientUsage.cs names a DTO, a request record, the concurrency
# stamp, BlogUrlHelper, SettingKeys, the bootstrap config, the length constants and the
# DataAnnotations validator, all resolved from the contracts package alone. The fixture sets
# TreatWarningsAsErrors so a type going missing or changing shape fails here.
Invoke-DotNet build $contractsConsumer -c Release --no-restore --nologo @restoreProperties
Invoke-DotNet publish $consumer `
    -c Release `
    --no-restore `
    --nologo `
    -o $consumerOutput `
    @restoreProperties

$consumerAssets = Get-Content (Join-Path $testRoot "Consumer\obj\project.assets.json") -Raw |
    ConvertFrom-Json
$consumerLibraries = @($consumerAssets.libraries.PSObject.Properties.Name)
# BlogIt.Contracts arrives here transitively, which is the other half of the packaging split: a host
# that installs the engine alone must still get the DTOs without naming them, exactly as it did when
# the assembly was smuggled into BlogIt's lib folder. Matched on BlogIt*/* rather than BlogIt/* so an
# extra BlogIt.* package sneaking into this graph is a failure rather than an invisible pass.
Assert-SameSet `
    -Actual @($consumerLibraries | Where-Object { $_ -Like "BlogIt*/*" }) `
    -Expected @("BlogIt/$version", "BlogIt.Contracts/$version") `
    -Description "Filesystem consumer BlogIt packages"

# The whole point of the satellite split, measured on the real restore graph rather than inferred
# from the nuspec: a host that installs BlogIt alone must not pull the OpenAI client or the
# Gax/gRPC/Protobuf tree that Google.Analytics.Data.V1Beta sits on top of. Asserted by prefix over
# every restored library because these arrive transitively - Grpc.Net.Client, Grpc.Core.Api,
# Google.Api.Gax.Grpc, Google.Apis.Auth and Google.Protobuf are never named in any csproj.
$satelliteOnlyLibraryPrefixes = @(
    "OpenAI/",
    "Google.Analytics.",
    "Google.Api.Gax",
    "Google.Apis.",
    "Google.Protobuf/",
    "Grpc."
)
$leakedLibraries = @(
    $consumerLibraries | Where-Object {
        $library = $_
        @($satelliteOnlyLibraryPrefixes | Where-Object {
            $library.StartsWith($_, [StringComparison]::OrdinalIgnoreCase)
        }).Count -ne 0
    }
)
if ($leakedLibraries.Count -ne 0) {
    throw "A BlogIt-only consumer restored $($leakedLibraries.Count) AI/analytics SDK libraries it cannot use: $($leakedLibraries -join ', ')."
}

# The other half of the same claim: opting in has to actually deliver the SDKs, or the assertion
# above would also pass for a satellite that forgot to depend on anything. Also proves BlogIt
# arrives transitively from each satellite, so a host never pins the engine version itself.
$aiAnalyticsConsumerAssets = Get-Content (
    Join-Path $testRoot "AiAnalyticsConsumer\obj\project.assets.json") -Raw |
    ConvertFrom-Json
$aiAnalyticsConsumerLibraries = @(
    $aiAnalyticsConsumerAssets.libraries.PSObject.Properties.Name)
Assert-SameSet `
    -Actual @($aiAnalyticsConsumerLibraries | Where-Object { $_ -Like "BlogIt*/*" }) `
    -Expected @(
        "BlogIt/$version",
        "BlogIt.Contracts/$version",
        "BlogIt.OpenAi/$version",
        "BlogIt.GoogleAnalytics/$version") `
    -Description "AI/analytics consumer BlogIt packages"
foreach ($expectedLibrary in @("OpenAI/2.12.0", "Google.Analytics.Data.V1Beta/2.0.0-beta10")) {
    if ($aiAnalyticsConsumerLibraries -notcontains $expectedLibrary) {
        throw "The AI/analytics consumer did not restore $expectedLibrary from its satellite package."
    }
}
$aiAnalyticsConsumerOutput = Join-Path $testRoot "AiAnalyticsConsumer\bin\Release\net10.0"
foreach ($assembly in @(
    "BlogIt.dll",
    "BlogIt.Contracts.dll",
    "BlogIt.OpenAi.dll",
    "BlogIt.GoogleAnalytics.dll"
)) {
    if (-not (Test-Path (Join-Path $aiAnalyticsConsumerOutput $assembly) -PathType Leaf)) {
        throw "AI/analytics package consumer output is missing $assembly."
    }
}

# Finding #21, measured on the real restore graph rather than inferred from the nuspec: a client that
# wants the DTOs gets the DTOs. Before the split BlogIt.Contracts.dll had IsPackable=false and was
# injected into BlogIt's own lib folder, so this fixture could not have existed - the only way to
# reach BlogPostDetailDto was a PackageReference to BlogIt, which drags in EF Core, SqlClient and
# BCrypt. The exact-set assertion below is what proves that is over; the per-assembly output check
# further down names the worst offenders so a regression reports which one came back.
$contractsConsumerAssets = Get-Content (
    Join-Path $testRoot "ContractsConsumer\obj\project.assets.json") -Raw |
    ConvertFrom-Json
$contractsConsumerLibraries = @($contractsConsumerAssets.libraries.PSObject.Properties.Name)
Assert-SameSet `
    -Actual @($contractsConsumerLibraries | Where-Object { $_ -Like "BlogIt*/*" }) `
    -Expected @("BlogIt.Contracts/$version") `
    -Description "Contracts consumer BlogIt packages"
# The stronger form of the same claim: the contracts package has no dependencies at all, so this
# restore graph should be exactly one library. Stated as an exact set rather than a "does not
# contain EF Core" check, because the failure this guards against is any dependency being added to
# contracts, not specifically a database one.
Assert-SameSet `
    -Actual $contractsConsumerLibraries `
    -Expected @("BlogIt.Contracts/$version") `
    -Description "Contracts consumer complete restore graph"
$contractsConsumerOutput = Join-Path $testRoot "ContractsConsumer\bin\Release\net10.0"
if (-not (Test-Path (Join-Path $contractsConsumerOutput "BlogIt.Contracts.dll") -PathType Leaf)) {
    throw "Contracts package consumer output is missing BlogIt.Contracts.dll."
}
# The engine must not follow the DTOs into a client's output directory. This is what "smuggled" cost
# a client in practice: 11 MB of engine and admin assets on disk to deserialise a few records.
foreach ($unwantedAssembly in @(
    "BlogIt.dll",
    "Microsoft.EntityFrameworkCore.dll",
    "Microsoft.Data.SqlClient.dll",
    "BCrypt.Net-Next.dll"
)) {
    if (Test-Path (Join-Path $contractsConsumerOutput $unwantedAssembly) -PathType Leaf) {
        throw "Contracts package consumer output contains $unwantedAssembly; the contracts package must not drag the engine into a client."
    }
}
if (Test-Path (Join-Path $contractsConsumerOutput "BlogItAdminAssets")) {
    throw "Contracts package consumer output contains the admin asset tree."
}

$azureConsumerAssets = Get-Content (Join-Path $testRoot "AzureConsumer\obj\project.assets.json") -Raw |
    ConvertFrom-Json
$azureConsumerLibraries = @($azureConsumerAssets.libraries.PSObject.Properties.Name)
Assert-SameSet `
    -Actual @($azureConsumerLibraries | Where-Object { $_ -Like "BlogIt*/*" }) `
    -Expected @(
        "BlogIt/$version",
        "BlogIt.Contracts/$version",
        "BlogIt.AzureStorage/$version") `
    -Description "Azure consumer BlogIt packages"

$azureConsumerOutput = Join-Path $testRoot "AzureConsumer\bin\Release\net10.0"
foreach ($assembly in @("BlogIt.dll", "BlogIt.Contracts.dll", "BlogIt.AzureStorage.dll")) {
    if (-not (Test-Path (Join-Path $azureConsumerOutput $assembly) -PathType Leaf)) {
        throw "Azure package consumer output is missing $assembly."
    }
}
$azureConsumerAdminAssets = Join-Path $azureConsumerOutput "BlogItAdminAssets"
$azureConsumerAdminAssetPaths = @(
    Get-ChildItem $azureConsumerAdminAssets -File -Recurse |
        ForEach-Object {
            [IO.Path]::GetRelativePath(
                $azureConsumerAdminAssets,
                $_.FullName).Replace('\', '/')
        }
)
Assert-SameSet `
    -Actual $azureConsumerAdminAssetPaths `
    -Expected $adminAssetRelativePaths `
    -Description "Transitive BlogIt admin tree in Azure consumer"

$publishedAssets = Join-Path $consumerOutput "BlogItAdminAssets"
if ((-not (Test-Path (Join-Path $publishedAssets "index.html"))) -or
    (-not (Test-Path (Join-Path $publishedAssets "_framework\blazor.webassembly.js")))) {
    throw "The build-transitive target did not copy the complete private admin tree."
}
$publishedConsumerAdminAssets = @(
    Get-ChildItem $publishedAssets -File -Recurse |
        ForEach-Object {
            [IO.Path]::GetRelativePath($publishedAssets, $_.FullName).Replace('\', '/')
        }
)
Assert-SameSet `
    -Actual $publishedConsumerAdminAssets `
    -Expected $adminAssetRelativePaths `
    -Description "Build-transitive consumer admin tree"
if (Test-Path (Join-Path $consumerOutput "wwwroot\blogit")) {
    throw "Admin assets were copied to the fixed public wwwroot/blogit path."
}

$adminWasmPath = $adminWasmEntry.Substring($adminAssetPrefix.Length)
Invoke-ConsumerScenario `
    -Name "default" `
    -AdminPath "/blogit" `
    -ApiPath "/api" `
    -AdminWasmPath $adminWasmPath
Invoke-ConsumerScenario `
    -Name "custom" `
    -AdminPath "/control-panel" `
    -ApiPath "/backend/v2" `
    -AdminWasmPath $adminWasmPath

$mainSizeMb = [math]::Round($package.Length / 1MB, 2)
Write-Host "PASS packages produced: $(@($producedPackages.Name) -join ', ')"
Write-Host "PASS ${packageName}: $mainSizeMb MB, $($mainInspection.EntryNames.Count) files, $($adminAssets.Count) admin assets, $($mainInspection.Dependencies.Count) dependencies, MIT"
foreach ($satellite in $satelliteExpectations) {
    $satelliteSizeKb = [math]::Round($satellite.Package.Length / 1KB, 2)
    Write-Host "PASS $($satellite.Package.Name): $satelliteSizeKb KB, one library asset, exact BlogIt $version dependency, MIT"
}
$contractsSizeKb = [math]::Round($contractsPackage.Length / 1KB, 2)
Write-Host "PASS ${contractsPackageName}: $contractsSizeKb KB, one library asset, 0 dependencies, 0 framework references, MIT"
Write-Host "PASS contracts-only client: plain Microsoft.NET.Sdk fixture compiled against BlogIt.Contracts alone, restore graph of exactly 1 library"
Write-Host "PASS release stamping: AssemblyVersion/FileVersion/InformationalVersion track $version in 5 shipped assemblies"
Write-Host "PASS package dependency boundaries and forbidden browser dependencies: 0"
Write-Host "PASS BlogIt-only consumer restored 0 AI/analytics SDK libraries; satellite consumer restored both"
Write-Host "PASS clean consumers: filesystem/public Razor surface, Azure and AI/analytics startup/transitive BlogIt"
Write-Host "PASS published consumer: $consumerOutput"
