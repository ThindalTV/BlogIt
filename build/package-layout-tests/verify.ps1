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
$packageProject = Join-Path $repo "src\BlogIt\BlogIt.csproj"
$azurePackageProject = Join-Path $repo "src\BlogIt.AzureStorage\BlogIt.AzureStorage.csproj"
$consumerOutput = Join-Path $artifacts "consumer-publish"
$version = $PackageVersion
$packageName = "BlogIt.$version.nupkg"
$azurePackageName = "BlogIt.AzureStorage.$version.nupkg"
$symbolPackageName = "BlogIt.$version.snupkg"
$azureSymbolPackageName = "BlogIt.AzureStorage.$version.snupkg"
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

        $namespace = [Xml.XmlNamespaceManager]::new($nuspec.NameTable)
        $namespace.AddNamespace(
            "n",
            "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd")
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

        return [pscustomobject]@{
            Id = [string]$nuspec.package.metadata.id
            Version = [string]$nuspec.package.metadata.version
            EntryNames = @($entries.FullName)
            Dependencies = $dependencies
            FrameworkReferences = $frameworkReferences
        }
    }
    finally {
        $archive.Dispose()
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
            throw "$Name did not load BlogIt.Contracts from the BlogIt package."
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
    (Join-Path $repo "src\BlogIt.AzureStorage\BlogIt.AzureStorage.csproj")
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
    (Join-Path $testRoot "AzureConsumer\obj")
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
    Invoke-DotNet pack $azurePackageProject `
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
    -Expected @($packageName, $azurePackageName) `
    -Description "Produced nupkgs"
$producedSymbolPackages = @(
    Get-ChildItem $feed -File |
        Where-Object Extension -EQ ".snupkg"
)
Assert-SameSet `
    -Actual @($producedSymbolPackages.Name) `
    -Expected @($symbolPackageName, $azureSymbolPackageName) `
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
$azurePackage = Get-Item (Join-Path $feed $azurePackageName)
# The ceiling is deliberately close to the real size (~11 MB) so that re-introducing the
# precompressed admin variants - which alone added 18.4 MB of already-compressed, and
# therefore incompressible, payload - trips this instead of passing unnoticed.
if ($package.Length -gt 15MB) {
    throw "BlogIt package size is $([math]::Round($package.Length / 1MB, 2)) MB; expected at most 15 MB."
}
if ($azurePackage.Length -gt 1MB) {
    throw "BlogIt.AzureStorage package size is $([math]::Round($azurePackage.Length / 1KB, 2)) KB; expected at most 1 MB."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$mainInspection = Get-PackageInspection $package
$azureInspection = Get-PackageInspection $azurePackage

foreach ($symbolExpectation in @(
    @{
        Package = Get-Item (Join-Path $feed $symbolPackageName)
        Pdb = "lib/net10.0/BlogIt.pdb"
    },
    @{
        Package = Get-Item (Join-Path $feed $azureSymbolPackageName)
        Pdb = "lib/net10.0/BlogIt.AzureStorage.pdb"
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
    "lib/net10.0/BlogIt.Contracts.dll",
    "buildTransitive/BlogIt.targets",
    "${adminAssetPrefix}index.html",
    "${adminAssetPrefix}_framework/blazor.webassembly.js"
)) {
    if ($mainInspection.EntryNames -notcontains $requiredEntry) {
        throw "BlogIt package is missing $requiredEntry."
    }
}
Assert-SameSet `
    -Actual @($mainInspection.EntryNames | Where-Object { $_ -Like "lib/*" }) `
    -Expected @(
        "lib/net10.0/BlogIt.dll",
        "lib/net10.0/BlogIt.Contracts.dll",
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
    "Google.Analytics.Data.V1Beta" = "2.0.0-beta10"
    "Markdig" = "1.3.2"
    "Microsoft.AspNetCore.Authentication.JwtBearer" = "10.0.11"
    "Microsoft.EntityFrameworkCore" = "10.0.11"
    "Microsoft.EntityFrameworkCore.SqlServer" = "10.0.11"
    "OpenAI" = "2.12.0"
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

if ($azureInspection.Id -ne "BlogIt.AzureStorage" -or
    $azureInspection.Version -ne $version) {
    throw "Azure package identity is '$($azureInspection.Id) $($azureInspection.Version)', expected 'BlogIt.AzureStorage $version'."
}
if ($azureInspection.EntryNames -notcontains "README.md") {
    throw "BlogIt.AzureStorage package is missing README.md."
}
Assert-SameSet `
    -Actual @($azureInspection.EntryNames | Where-Object { $_ -Like "lib/*" }) `
    -Expected @("lib/net10.0/BlogIt.AzureStorage.dll") `
    -Description "BlogIt.AzureStorage library assets"
if (@($azureInspection.EntryNames | Where-Object {
    $_ -Like "staticwebassets/*" -or $_ -Like "buildTransitive/*"
}).Count -ne 0) {
    throw "BlogIt.AzureStorage duplicates BlogIt admin or build-transitive assets."
}
$azureDependencies = [ordered]@{
    "BlogIt" = $version
    "Azure.Storage.Blobs" = "12.29.1"
}
Assert-PackageDependencies `
    -Inspection $azureInspection `
    -Expected $azureDependencies `
    -Description "BlogIt.AzureStorage"
Assert-SameSet `
    -Actual @($azureInspection.FrameworkReferences) `
    -Expected @() `
    -Description "BlogIt.AzureStorage framework references"

$consumerProjectText = Get-Content $consumer -Raw
$azureConsumerProjectText = Get-Content $azureConsumer -Raw
foreach ($projectText in @($consumerProjectText, $azureConsumerProjectText)) {
    if ($projectText -match "<ProjectReference") {
        throw "Clean package consumers must not contain source ProjectReferences."
    }
}
if (($consumerProjectText -notmatch 'PackageReference Include="BlogIt"') -or
    ($consumerProjectText -match 'PackageReference Include="BlogIt\.(Contracts|Admin|AzureStorage)"')) {
    throw "The filesystem consumer must reference only the BlogIt production package."
}
if (($azureConsumerProjectText -notmatch 'PackageReference Include="BlogIt\.AzureStorage"') -or
    ($azureConsumerProjectText -match 'PackageReference Include="BlogIt"')) {
    throw "The Azure consumer must reference only BlogIt.AzureStorage and receive BlogIt transitively."
}

$restoreProperties = @(
    "-p:BlogItPackageVersion=$version",
    "-p:RestorePackagesPath=$packagesPath"
)
foreach ($consumerProject in @($consumer, $azureConsumer)) {
    Invoke-DotNet restore $consumerProject `
        "-p:RestoreAdditionalProjectSources=$feed" `
        @restoreProperties `
        --force `
        --no-cache `
        --nologo
}
Invoke-DotNet build $consumer -c Release --no-restore --nologo @restoreProperties
Invoke-DotNet build $azureConsumer -c Release --no-restore --nologo @restoreProperties
Invoke-DotNet publish $consumer `
    -c Release `
    --no-restore `
    --nologo `
    -o $consumerOutput `
    @restoreProperties

$consumerAssets = Get-Content (Join-Path $testRoot "Consumer\obj\project.assets.json") -Raw |
    ConvertFrom-Json
$consumerLibraries = @($consumerAssets.libraries.PSObject.Properties.Name)
Assert-SameSet `
    -Actual @($consumerLibraries | Where-Object { $_ -Like "BlogIt/*" }) `
    -Expected @("BlogIt/$version") `
    -Description "Filesystem consumer BlogIt packages"

$azureConsumerAssets = Get-Content (Join-Path $testRoot "AzureConsumer\obj\project.assets.json") -Raw |
    ConvertFrom-Json
$azureConsumerLibraries = @($azureConsumerAssets.libraries.PSObject.Properties.Name)
Assert-SameSet `
    -Actual @($azureConsumerLibraries | Where-Object {
        $_ -Like "BlogIt/*" -or $_ -Like "BlogIt.AzureStorage/*"
    }) `
    -Expected @("BlogIt/$version", "BlogIt.AzureStorage/$version") `
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
$azureSizeKb = [math]::Round($azurePackage.Length / 1KB, 2)
Write-Host "PASS packages produced: $packageName, $azurePackageName"
Write-Host "PASS ${packageName}: $mainSizeMb MB, $($mainInspection.EntryNames.Count) files, $($adminAssets.Count) admin assets"
Write-Host "PASS ${azurePackageName}: $azureSizeKb KB, one library asset, exact BlogIt $version dependency"
Write-Host "PASS package dependency boundaries and forbidden browser dependencies: 0"
Write-Host "PASS clean consumers: filesystem/public Razor surface and Azure startup/transitive BlogIt"
Write-Host "PASS published consumer: $consumerOutput"
