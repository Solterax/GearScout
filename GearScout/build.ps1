param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'

if (-not $env:DALAMUD_HOME) {
    $hooksPath = Join-Path $env:APPDATA 'XIVLauncher\addon\Hooks'
    $defaultPath = Join-Path $hooksPath 'dev'
    if (Test-Path (Join-Path $defaultPath 'Dalamud.dll')) {
        $env:DALAMUD_HOME = $defaultPath
    } else {
        $installed = Get-ChildItem -LiteralPath $hooksPath -Directory |
            Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' -and (Test-Path (Join-Path $_.FullName 'Dalamud.dll')) } |
            Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
        if (-not $installed) { throw 'Dalamud assemblies were not found. Run FFXIV with XIVLauncher first, or set DALAMUD_HOME.' }
        $env:DALAMUD_HOME = $installed.FullName
    }
}

# The packager recreates this staging directory. Clear the Windows directory
# read-only attribute, which can otherwise prevent repeated Release builds.
$stagingPath = Join-Path $PSScriptRoot "bin\$Configuration\GearScout"
if (Test-Path -LiteralPath $stagingPath) {
    $stagingItem = Get-Item -LiteralPath $stagingPath
    $stagingItem.Attributes = $stagingItem.Attributes -band (-bnot [IO.FileAttributes]::ReadOnly)
}
& dotnet build (Join-Path $PSScriptRoot 'GearScout.csproj') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
Write-Host "Dev plugin DLL: $(Join-Path $PSScriptRoot "bin\$Configuration\GearScout.dll")"
