[CmdletBinding()]
param(
    [AllowEmptyString()]
    [string] $Version,

    [AllowEmptyString()]
    [string] $DefaultVersion
)

$ErrorActionPreference = "Stop"

$candidate = $Version.Trim()
if ([string]::IsNullOrWhiteSpace($candidate)) {
    $candidate = $DefaultVersion.Trim()
}
if ([string]::IsNullOrWhiteSpace($candidate)) {
    $runNumber = if ($env:GITHUB_RUN_NUMBER) { $env:GITHUB_RUN_NUMBER } else { "local" }
    $runAttempt = if ($env:GITHUB_RUN_ATTEMPT) { $env:GITHUB_RUN_ATTEMPT } else { "1" }
    $candidate = "0.0.0-smoke.$runNumber.$runAttempt"
}

if ($candidate.StartsWith("refs/tags/", [StringComparison]::OrdinalIgnoreCase)) {
    $candidate = $candidate.Substring("refs/tags/".Length)
}
if ($candidate.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
    $candidate = $candidate.Substring(1)
}

$match = [regex]::Match(
    $candidate,
    "^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
    [Text.RegularExpressions.RegexOptions]::CultureInvariant)
if (-not $match.Success) {
    throw "Package version '$candidate' is invalid. Use vMAJOR.MINOR.PATCH or MAJOR.MINOR.PATCH with an optional SemVer prerelease suffix; build metadata is not supported."
}

$prerelease = $match.Groups[4].Value
foreach ($identifier in @($prerelease.Split(".", [StringSplitOptions]::RemoveEmptyEntries))) {
    if ($identifier -match "^[0-9]+$" -and
        $identifier.Length -gt 1 -and
        $identifier.StartsWith("0", [StringComparison]::Ordinal)) {
        throw "Package version '$candidate' has a numeric prerelease identifier with a leading zero."
    }
}

$normalized = "$($match.Groups[1].Value).$($match.Groups[2].Value).$($match.Groups[3].Value)"
if (-not [string]::IsNullOrEmpty($prerelease)) {
    $normalized += "-$prerelease"
}

if ($normalized.Length -gt 128) {
    throw "Package version '$normalized' is too long."
}

Write-Output $normalized
