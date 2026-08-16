<#
.SYNOPSIS
    Builds local NuGet packages for BlogIt with an auto-incrementing pre-release
    version, so a consuming project always picks up the latest local build
    without manually bumping the version or clearing the NuGet cache.

.DESCRIPTION
    Version scheme: <BaseVersion>-local.<counter>, e.g. 0.1.0-local.7
    The counter is stored in nupkg-local/.local-version-counter (gitignored)
    and incremented on every run. This is for LOCAL testing only - it is
    never used for the real NuGet.org release, which is driven by git tags
    (see .github/workflows/release.yml).

.PARAMETER BaseVersion
    The version prefix to use. Defaults to 0.1.0.

.PARAMETER OutputPath
    Where to write the .nupkg/.snupkg files. Defaults to ./nupkg-local.

.PARAMETER AddSource
    If set, registers (or refreshes) a "BlogItLocal" NuGet source pointing at
    OutputPath, so `dotnet add package` can find it immediately.

.EXAMPLE
    ./scripts/pack-local.ps1
    ./scripts/pack-local.ps1 -AddSource
    ./scripts/pack-local.ps1 -BaseVersion 0.2.0
#>
param(
    [string]$BaseVersion = "0.1.0",
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\nupkg-local"),
    [switch]$AddSource
)

$ErrorActionPreference = "Stop"

$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

$counterFile = Join-Path $OutputPath ".local-version-counter"
$counter = 0
if (Test-Path $counterFile) {
    $counter = [int](Get-Content $counterFile -Raw)
}
$counter++
Set-Content -Path $counterFile -Value $counter -NoNewline

$packageVersion = "$BaseVersion-local.$counter"
Write-Host "Packing BlogIt as $packageVersion ..." -ForegroundColor Cyan

$projects = @(
    (Join-Path $PSScriptRoot "..\src\BlogIt\BlogIt.csproj"),
    (Join-Path $PSScriptRoot "..\src\BlogIt.AzureStorage\BlogIt.AzureStorage.csproj"),
    (Join-Path $PSScriptRoot "..\src\BlogIt.OpenAi\BlogIt.OpenAi.csproj"),
    (Join-Path $PSScriptRoot "..\src\BlogIt.GoogleAnalytics\BlogIt.GoogleAnalytics.csproj")
)

foreach ($project in $projects) {
    & dotnet pack $project -c Release -o $OutputPath -p:PackageVersion=$packageVersion
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed for $project"
    }
}

if ($AddSource) {
    & dotnet nuget remove source BlogItLocal 2>$null | Out-Null
    & dotnet nuget add source $OutputPath --name BlogItLocal
}

Write-Host ""
Write-Host "Built version $packageVersion in $OutputPath" -ForegroundColor Green
Write-Host "In the consuming project, run:"
Write-Host "  dotnet add package BlogIt --version $packageVersion --source $OutputPath"
