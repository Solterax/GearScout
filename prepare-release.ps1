param(
    [string]$Repository = 'Solterax/GearScout',
    [string]$Tag = ''
)
$ErrorActionPreference = 'Stop'
if ($Repository -notmatch '^[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$') { throw 'Repository must be owner/name.' }

& (Join-Path $PSScriptRoot 'GearScout\build.ps1')
& dotnet run --project (Join-Path $PSScriptRoot 'GearScout\Tests\GearScout.Tests.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Importer and matching checks failed.' }
& dotnet run --project (Join-Path $PSScriptRoot 'NativeTextChecks\NativeTextChecks.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Native name-formatting checks failed.' }

$buildPath = Join-Path $PSScriptRoot 'GearScout\bin\Release'
$manifest = Get-Content -Raw -LiteralPath (Join-Path $buildPath 'GearScout.json') | ConvertFrom-Json
if (-not $Tag) {
    [xml]$project = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'GearScout\GearScout.csproj')
    $Tag = 'v' + $project.Project.PropertyGroup.Version
}
if ($Tag -notmatch '^v\d+\.\d+\.\d+(\.\d+)?$') { throw 'Tag must be v followed by a three- or four-part version.' }
$tagParts = $Tag.Substring(1).Split('.')
while ($tagParts.Count -lt 4) { $tagParts += '0' }
if ([version]($tagParts -join '.') -ne [version]$manifest.AssemblyVersion) {
    throw 'Tag must match the compiled assembly version.'
}

$artifactPath = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifactPath | Out-Null
$packagePath = Join-Path $artifactPath 'GearScout-plugin.zip'
Copy-Item -LiteralPath (Join-Path $buildPath 'GearScout\latest.zip') -Destination $packagePath -Force

$downloadUrl = "https://github.com/$Repository/releases/download/$Tag/GearScout-plugin.zip"
$manifest | Add-Member -Force -NotePropertyName RepoUrl -NotePropertyValue "https://github.com/$Repository"
$manifest | Add-Member -Force -NotePropertyName IsHide -NotePropertyValue $false
$manifest | Add-Member -Force -NotePropertyName IsTestingExclusive -NotePropertyValue $false
$manifest | Add-Member -Force -NotePropertyName DownloadLinkInstall -NotePropertyValue $downloadUrl
$manifest | Add-Member -Force -NotePropertyName DownloadLinkUpdate -NotePropertyValue $downloadUrl
$manifest | Add-Member -Force -NotePropertyName LastUpdate -NotePropertyValue ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString())
ConvertTo-Json -InputObject @($manifest) -Depth 10 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'pluginmaster.json') -Encoding utf8
Write-Host "Release asset: $packagePath"
Write-Host "Tag: $Tag"
Write-Host "Dalamud repository: https://raw.githubusercontent.com/$Repository/main/pluginmaster.json"
