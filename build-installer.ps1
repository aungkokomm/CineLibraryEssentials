# ============================================================================
# build-installer.ps1
# ----------------------------------------------------------------------------
# Convenience script: publish the Release build, then run Inno Setup compiler.
# Output: release\CineLibraryEssentials_Setup_<version>.exe
#
# Prereqs:
#   - .NET 10 SDK
#   - Inno Setup 6+ installed (default path used below; override with -ISCC)
# ============================================================================

param(
    [string]$ISCC = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot

try {
    if (-not $SkipPublish) {
        Write-Host "==> Publishing Release (self-contained, x64)..." -ForegroundColor Cyan
        dotnet publish -c Release `
            -p:Platform=x64 `
            -p:RuntimeIdentifier=win-x64 `
            -p:WindowsAppSDKSelfContained=true `
            --self-contained true
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    }

    if (-not (Test-Path $ISCC)) {
        throw "Inno Setup compiler not found at: $ISCC`nInstall from https://jrsoftware.org/isinfo.php or pass -ISCC <path>"
    }

    Write-Host "==> Compiling installer with Inno Setup..." -ForegroundColor Cyan
    & $ISCC "Setup.iss"
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed" }

    Write-Host ""
    Write-Host "==> Done. Installer is in 'release\'." -ForegroundColor Green
    Get-ChildItem "release\*.exe" | ForEach-Object {
        Write-Host "    $($_.FullName) ($([math]::Round($_.Length / 1MB, 1)) MB)" -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
