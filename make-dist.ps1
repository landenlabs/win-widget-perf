# Copyright (c) 2026 LanDen Labs - Dennis Lang
#
# Build unsigned Microsoft Store MSIX packages (x64 + arm64) locally.
# Identity values must match Partner Center > Product management > Product identity.
#
#   pwsh ./make-dist.ps1                    # version read from the latest git tag
#   pwsh ./make-dist.ps1 -Version 6.11.20
#
# Output: dist-store\WinWidgetPerf-<version>-<arch>-store.msix

param(
    [string] $Version = '',
    [string] $IdentityName = 'LanDenLabs.WinWidgetPerf',
    [string] $Publisher = 'CN=6814ED37-752E-4806-BC75-6CDF85ABDA72',
    [string] $PublisherDisplayName = 'LanDen Labs',
    [string] $OutDir = 'dist-store'
)

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not $Version) {
        $Version = (git describe --tags --abbrev=0 2>$null)
        if (-not $Version) { throw 'No -Version given and no git tag found.' }
    }
    $Version = $Version.TrimStart('v')
    Write-Host "Building Store packages for version $Version"

    foreach ($arch in 'x64', 'arm64') {
        & "$PSScriptRoot\.github\scripts\build-msix.ps1" -Arch $arch -Version $Version -Store `
            -IdentityName $IdentityName -Publisher $Publisher `
            -PublisherDisplayName $PublisherDisplayName -OutDir $OutDir
        if ($LASTEXITCODE) { throw "build-msix.ps1 failed for $arch ($LASTEXITCODE)" }
    }

    Write-Host "`nDone. Upload these to Partner Center:"
    Get-ChildItem $OutDir -Filter '*-store.msix' | ForEach-Object { Write-Host "  $($_.FullName)" }
}
finally { Pop-Location }
