param([string]$Repository = 'Solterax/DalamudPlugins', [string]$GitHubCliPath = '')
$ErrorActionPreference = 'Stop'
if ($Repository -notmatch '^[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$') { throw 'Repository must be owner/name.' }
if (-not $GitHubCliPath) {
    $command = Get-Command gh -ErrorAction SilentlyContinue
    if ($command) { $GitHubCliPath = $command.Source }
    else { $GitHubCliPath = Join-Path $PSScriptRoot 'work/tools/gh/bin/gh.exe' }
}
if (-not (Test-Path -LiteralPath $GitHubCliPath)) { throw 'Install and sign in to GitHub CLI, or supply -GitHubCliPath.' }
& $GitHubCliPath workflow run refresh.yml --repo $Repository --ref main
if ($LASTEXITCODE -ne 0) { throw 'Could not start the shared catalogue refresh.' }
Write-Output "Shared catalogue refresh started: https://github.com/$Repository/actions/workflows/refresh.yml"
