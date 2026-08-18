[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $PackageVersion,

    [Parameter(Mandatory)]
    [Alias("PackageArtifactPath")]
    [ValidateNotNullOrEmpty()]
    [string] $PackageFeed,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ConnectionString,

    [string] $OutputPath = (Join-Path $PSScriptRoot "artifacts"),

    [ValidateNotNullOrEmpty()]
    [string] $DependencySource = "https://api.nuget.org/v3/index.json"
)

$ErrorActionPreference = "Stop"

$smokeRoot = $PSScriptRoot
$hostProject = Join-Path $smokeRoot "InstalledHost\InstalledHost.csproj"
$azureProject = Join-Path $smokeRoot "AzureCompileConsumer\AzureCompileConsumer.csproj"
$feed = [IO.Path]::GetFullPath($PackageFeed)
$output = [IO.Path]::GetFullPath($OutputPath)
$hostPublish = Join-Path $output "host"
$mediaRoot = Join-Path $output "media"
$packagesPath = Join-Path $output "packages"
$nugetConfig = Join-Path $output "NuGet.Config"
$serverLog = Join-Path $output "server.log"
$serverErrorLog = Join-Path $output "server.err.log"

function Invoke-DotNet {
    $commandArguments = $args
    & dotnet @commandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($commandArguments -join ' ') failed with exit code $LASTEXITCODE."
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

function Get-PackageIdentity {
    param([Parameter(Mandatory)] [IO.FileInfo] $Package)

    $archive = [IO.Compression.ZipFile]::OpenRead($Package.FullName)
    try {
        $nuspec = $archive.Entries |
            Where-Object { $_.FullName.EndsWith(".nuspec", [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1
        if ($null -eq $nuspec) {
            throw "$($Package.Name) does not contain a nuspec."
        }

        $reader = [IO.StreamReader]::new($nuspec.Open())
        try {
            [xml]$document = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $id = $document.SelectSingleNode(
            "/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='id']")
        $version = $document.SelectSingleNode(
            "/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='version']")
        if ($null -eq $id -or $null -eq $version) {
            throw "$($Package.Name) has an invalid nuspec identity."
        }

        return [pscustomobject]@{
            File = $Package
            Id = $id.InnerText
            Version = $version.InnerText
        }
    }
    finally {
        $archive.Dispose()
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

function Invoke-SmokeRequest {
    param(
        [Parameter(Mandatory)] [Net.Http.HttpClient] $Client,
        [Parameter(Mandatory)] [string] $Method,
        [Parameter(Mandatory)] [string] $Uri,
        [Parameter(Mandatory)] [int] $ExpectedStatus,
        [Net.Http.HttpContent] $Content,
        [string] $Token
    )

    $request = [Net.Http.HttpRequestMessage]::new(
        [Net.Http.HttpMethod]::new($Method),
        $Uri)
    if ($null -ne $Content) {
        $request.Content = $Content
    }
    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        $request.Headers.Authorization =
            [Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $Token)
    }

    try {
        $response = $Client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            $text = [Text.Encoding]::UTF8.GetString($bytes)
            if ([int]$response.StatusCode -ne $ExpectedStatus) {
                throw "$Method $Uri returned $([int]$response.StatusCode), expected $ExpectedStatus. Response: $text"
            }

            return [pscustomobject]@{
                Bytes = $bytes
                Text = $text
                ContentType = $response.Content.Headers.ContentType?.MediaType
            }
        }
        finally {
            $response.Dispose()
        }
    }
    finally {
        $request.Dispose()
    }
}

function New-JsonContent {
    param([Parameter(Mandatory)] $Value)

    $json = $Value | ConvertTo-Json -Depth 10 -Compress
    return [Net.Http.StringContent]::new(
        $json,
        [Text.Encoding]::UTF8,
        "application/json")
}

function Get-ServerDiagnostics {
    $diagnostics = @()
    foreach ($logPath in @($serverLog, $serverErrorLog)) {
        if (Test-Path $logPath -PathType Leaf) {
            $diagnostics += "`n--- $logPath ---"
            $diagnostics += (Get-Content $logPath -Tail 100 | Out-String)
        }
    }
    return $diagnostics -join [Environment]::NewLine
}

if (-not (Test-Path $feed -PathType Container)) {
    throw "Package feed '$feed' does not exist or is not a directory."
}

$feedPrefix = $feed.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$outputPrefix = $output.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($feed -eq $output -or
    $feed.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    $output.StartsWith($feedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "PackageFeed and OutputPath must be separate, non-nested directories."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageFiles = @(
    Get-ChildItem $feed -File |
        Where-Object Extension -EQ ".nupkg"
)
$identities = @($packageFiles | ForEach-Object { Get-PackageIdentity $_ })
# All five release packages have to be present in the feed - the SQL smoke shares the feed
# with the layout harness - but only BlogIt and BlogIt.AzureStorage get their own consumer
# here. This suite exists to prove the engine works against a real SQL Server from an
# installed package; the AI and analytics satellites add no database behaviour, and their
# restore/compile/wiring is covered by build/package-layout-tests/verify.ps1's
# AiAnalyticsConsumer. Packing them and never referencing them would just add minutes.
# BlogIt.Contracts has no consumer of its own here either, but unlike the satellites it is a
# hard requirement of this feed: BlogIt takes an exact dependency on it, so restoring the
# engine below fails outright without it. verify.ps1's ContractsConsumer covers it directly.
Assert-SameSet `
    -Actual @($identities | ForEach-Object { "$($_.Id)/$($_.Version)" }) `
    -Expected @(
        "BlogIt/$PackageVersion",
        "BlogIt.Contracts/$PackageVersion",
        "BlogIt.AzureStorage/$PackageVersion",
        "BlogIt.OpenAi/$PackageVersion",
        "BlogIt.GoogleAnalytics/$PackageVersion") `
    -Description "Installed smoke package identities"

foreach ($project in @($hostProject, $azureProject)) {
    if ((Get-Content $project -Raw) -match "<ProjectReference") {
        throw "Installed package smoke project '$project' must not contain source ProjectReferences."
    }
}

Remove-Item $output -Recurse -Force -ErrorAction SilentlyContinue
foreach ($generatedPath in @(
    (Join-Path $smokeRoot "InstalledHost\bin"),
    (Join-Path $smokeRoot "InstalledHost\obj"),
    (Join-Path $smokeRoot "AzureCompileConsumer\bin"),
    (Join-Path $smokeRoot "AzureCompileConsumer\obj")
)) {
    Remove-Item $generatedPath -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item $output -ItemType Directory -Force | Out-Null
New-Item $mediaRoot -ItemType Directory -Force | Out-Null
New-Item $packagesPath -ItemType Directory -Force | Out-Null

$escapedFeed = [Security.SecurityElement]::Escape($feed)
$escapedDependencySource = [Security.SecurityElement]::Escape($DependencySource)
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="validated-artifacts" value="$escapedFeed" />
    <add key="dependencies" value="$escapedDependencySource" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="validated-artifacts">
      <package pattern="BlogIt" />
      <package pattern="BlogIt.AzureStorage" />
    </packageSource>
    <packageSource key="dependencies">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content $nugetConfig -Encoding utf8

$restoreProperties = @(
    "-p:BlogItPackageVersion=$PackageVersion",
    "-p:RestorePackagesPath=$packagesPath"
)
Invoke-DotNet restore $hostProject `
    --configfile $nugetConfig `
    --force `
    --no-cache `
    --nologo `
    @restoreProperties
Invoke-DotNet restore $azureProject `
    --configfile $nugetConfig `
    --force `
    --no-cache `
    --nologo `
    @restoreProperties
Invoke-DotNet publish $hostProject `
    -c Release `
    --no-restore `
    --nologo `
    -o $hostPublish `
    @restoreProperties
Invoke-DotNet build $azureProject `
    -c Release `
    --no-restore `
    --nologo `
    @restoreProperties

$hostAssets = Get-Content (Join-Path $smokeRoot "InstalledHost\obj\project.assets.json") -Raw |
    ConvertFrom-Json
$hostBlogItPackages = @(
    $hostAssets.libraries.PSObject.Properties.Name |
        Where-Object { $_ -Like "BlogIt/*" -or $_ -Like "BlogIt.AzureStorage/*" }
)
Assert-SameSet `
    -Actual $hostBlogItPackages `
    -Expected @("BlogIt/$PackageVersion") `
    -Description "Installed SQL/filesystem host BlogIt packages"

$azureAssets = Get-Content (Join-Path $smokeRoot "AzureCompileConsumer\obj\project.assets.json") -Raw |
    ConvertFrom-Json
$azureBlogItPackages = @(
    $azureAssets.libraries.PSObject.Properties.Name |
        Where-Object { $_ -Like "BlogIt/*" -or $_ -Like "BlogIt.AzureStorage/*" }
)
Assert-SameSet `
    -Actual $azureBlogItPackages `
    -Expected @("BlogIt/$PackageVersion", "BlogIt.AzureStorage/$PackageVersion") `
    -Description "Installed Azure consumer BlogIt packages"

$azureOutput = Join-Path $smokeRoot "AzureCompileConsumer\bin\Release\net10.0"
foreach ($assembly in @("BlogIt.dll", "BlogIt.Contracts.dll", "BlogIt.AzureStorage.dll")) {
    if (-not (Test-Path (Join-Path $azureOutput $assembly) -PathType Leaf)) {
        throw "Azure compile consumer output is missing $assembly."
    }
}

$hostDll = Join-Path $hostPublish "InstalledHost.dll"
if (-not (Test-Path $hostDll -PathType Leaf)) {
    throw "Installed host publish output is missing InstalledHost.dll."
}

$port = Get-FreePort
$baseUrl = "http://127.0.0.1:$port"
$oldUrls = $env:ASPNETCORE_URLS
$oldEnvironment = $env:ASPNETCORE_ENVIRONMENT
$oldConnectionString = $env:BLOGIT_SMOKE_CONNECTION_STRING
$oldMediaRoot = $env:BLOGIT_SMOKE_MEDIA_ROOT
$server = $null

try {
    try {
        $env:ASPNETCORE_URLS = $baseUrl
        $env:ASPNETCORE_ENVIRONMENT = "Production"
        $env:BLOGIT_SMOKE_CONNECTION_STRING = $ConnectionString
        $env:BLOGIT_SMOKE_MEDIA_ROOT = $mediaRoot
        $server = Start-Process dotnet `
            -ArgumentList "`"$hostDll`"" `
            -WorkingDirectory $hostPublish `
            -RedirectStandardOutput $serverLog `
            -RedirectStandardError $serverErrorLog `
            -PassThru
    }
    finally {
        $env:ASPNETCORE_URLS = $oldUrls
        $env:ASPNETCORE_ENVIRONMENT = $oldEnvironment
        $env:BLOGIT_SMOKE_CONNECTION_STRING = $oldConnectionString
        $env:BLOGIT_SMOKE_MEDIA_ROOT = $oldMediaRoot
    }

    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(30)
    try {
        $ready = $false
        for ($attempt = 0; $attempt -lt 120; $attempt++) {
            if ($server.HasExited) {
                throw "Installed host exited before becoming ready."
            }

            try {
                Invoke-SmokeRequest `
                    -Client $client `
                    -Method "GET" `
                    -Uri "$baseUrl/smoke/health" `
                    -ExpectedStatus 200 | Out-Null
                $ready = $true
                break
            }
            catch {
                Start-Sleep -Seconds 1
            }
        }
        if (-not $ready) {
            throw "Installed host did not become ready within 120 seconds."
        }

        $migrations = (
            Invoke-SmokeRequest $client "GET" "$baseUrl/smoke/migrations" 200
        ).Text | ConvertFrom-Json
        if (@($migrations.applied).Count -eq 0) {
            throw "No BlogIt migrations were applied to the fresh SQL Server database."
        }
        if (@($migrations.pending).Count -ne 0) {
            throw "BlogIt SQL Server database still has pending migrations: $($migrations.pending -join ', ')."
        }

        $statusBefore = (
            Invoke-SmokeRequest $client "GET" "$baseUrl/api/setup/status" 200
        ).Text | ConvertFrom-Json
        if ($statusBefore.isComplete) {
            throw "The SQL smoke database was not fresh; setup was already complete."
        }

        $setupRequest = @{
            username = "package-admin"
            displayName = "Package Admin"
            password = "Package_smoke_2026!"
            siteName = "BlogIt package smoke"
            siteUrl = $baseUrl
            siteDescription = "Installed package SQL smoke"
            defaultOgImage = $null
            aiProvider = "none"
            aiApiKey = ""
            aiBaseUrl = $null
            aiModel = $null
            aiExportModel = $null
            googleAnalyticsMeasurementId = $null
            googleAnalyticsPropertyId = $null
            googleAnalyticsCredentialsJson = $null
        }
        Invoke-SmokeRequest `
            -Client $client `
            -Method "POST" `
            -Uri "$baseUrl/api/setup/initialize" `
            -ExpectedStatus 200 `
            -Content (New-JsonContent $setupRequest) | Out-Null

        $statusAfter = (
            Invoke-SmokeRequest $client "GET" "$baseUrl/api/setup/status" 200
        ).Text | ConvertFrom-Json
        if (-not $statusAfter.isComplete) {
            throw "First-run setup did not persist to SQL Server."
        }

        Invoke-SmokeRequest `
            -Client $client `
            -Method "GET" `
            -Uri "$baseUrl/api/media/?page=1&pageSize=20" `
            -ExpectedStatus 401 | Out-Null

        $login = (
            Invoke-SmokeRequest `
                -Client $client `
                -Method "POST" `
                -Uri "$baseUrl/api/auth/login" `
                -ExpectedStatus 200 `
                -Content (New-JsonContent @{
                    username = "package-admin"
                    password = "Package_smoke_2026!"
                })
        ).Text | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace($login.token)) {
            throw "Login succeeded without returning a bearer token."
        }
        $token = [string]$login.token

        $protectedMedia = (
            Invoke-SmokeRequest `
                -Client $client `
                -Method "GET" `
                -Uri "$baseUrl/api/media/?page=1&pageSize=20" `
                -ExpectedStatus 200 `
                -Token $token
        ).Text | ConvertFrom-Json
        if ($protectedMedia.totalCount -ne 0) {
            throw "Fresh SQL smoke database unexpectedly contained media rows."
        }

        $shell = Invoke-SmokeRequest $client "GET" "$baseUrl/blogit/" 200
        if ($shell.Text -notmatch "<title>BlogIt Admin</title>" -or
            $shell.Text -notmatch '<base href="/blogit/" />') {
            throw "Installed package did not serve the BlogIt admin shell at /blogit/."
        }

        $framework = Invoke-SmokeRequest `
            $client `
            "GET" `
            "$baseUrl/blogit/_framework/blazor.webassembly.js" `
            200
        if ($framework.Bytes.Length -lt 1000) {
            throw "Installed package admin framework loader was unexpectedly small."
        }

        $deepLink = Invoke-SmokeRequest `
            $client `
            "GET" `
            "$baseUrl/blogit/posts/edit/installed-smoke" `
            200
        if ($deepLink.Text -notmatch "<title>BlogIt Admin</title>") {
            throw "Installed package admin deep link did not serve the admin shell."
        }

        $surface = (
            Invoke-SmokeRequest $client "GET" "$baseUrl/smoke/public-surface" 200
        ).Text | ConvertFrom-Json
        foreach ($publicType in @(
            "BlogIt.Components.Shared.SeoHead",
            "BlogIt.Services.IPublicContentService",
            "BlogIt.Shared.DTOs.BlogPostSummaryDto"
        )) {
            if (@($surface.types) -notcontains $publicType) {
                throw "Installed package public surface did not expose $publicType."
            }
        }

        $mediaText = "BlogIt installed package media lifecycle"
        $form = [Net.Http.MultipartFormDataContent]::new()
        $fileContent = [Net.Http.ByteArrayContent]::new(
            [Text.Encoding]::UTF8.GetBytes($mediaText))
        $fileContent.Headers.ContentType =
            [Net.Http.Headers.MediaTypeHeaderValue]::new("text/plain")
        $form.Add($fileContent, "file", "package-smoke.txt")
        $form.Add([Net.Http.StringContent]::new("Package smoke media"), "title")
        $uploaded = (
            Invoke-SmokeRequest `
                -Client $client `
                -Method "POST" `
                -Uri "$baseUrl/api/media/upload" `
                -ExpectedStatus 200 `
                -Content $form `
                -Token $token
        ).Text | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace($uploaded.id) -or
            [string]::IsNullOrWhiteSpace($uploaded.publicPath)) {
            throw "Media upload did not return an id and public path."
        }

        $storedFiles = @(Get-ChildItem $mediaRoot -File)
        if ($storedFiles.Count -ne 1) {
            throw "Filesystem storage contained $($storedFiles.Count) files after upload; expected one."
        }
        if ((Get-Content $storedFiles[0].FullName -Raw) -ne $mediaText) {
            throw "Filesystem storage content did not match the uploaded media."
        }

        $publicMedia = Invoke-SmokeRequest `
            $client `
            "GET" `
            "$baseUrl$($uploaded.publicPath)" `
            200
        if ($publicMedia.Text -ne $mediaText -or $publicMedia.ContentType -ne "text/plain") {
            throw "Public media route did not return the uploaded content and content type."
        }

        Invoke-SmokeRequest `
            -Client $client `
            -Method "DELETE" `
            -Uri "$baseUrl/api/media/$($uploaded.id)" `
            -ExpectedStatus 204 `
            -Token $token | Out-Null
        Invoke-SmokeRequest `
            -Client $client `
            -Method "GET" `
            -Uri "$baseUrl$($uploaded.publicPath)" `
            -ExpectedStatus 404 | Out-Null
        if (@(Get-ChildItem $mediaRoot -File).Count -ne 0) {
            throw "Filesystem media was not deleted after the API delete request."
        }

        Write-Host "PASS installed BlogIt ${PackageVersion}: SQL migrations, setup, login/auth, admin assets, public surface, and filesystem media lifecycle"
        Write-Host "PASS installed BlogIt.AzureStorage ${PackageVersion}: exact-feed restore and compile without cloud access"
    }
    finally {
        $client.Dispose()
        $handler.Dispose()
    }
}
catch {
    $diagnostics = Get-ServerDiagnostics
    if (-not [string]::IsNullOrWhiteSpace($diagnostics)) {
        Write-Host $diagnostics
    }
    throw
}
finally {
    if ($null -ne $server -and -not $server.HasExited) {
        Stop-Process -Id $server.Id
        $server.WaitForExit()
    }
}
