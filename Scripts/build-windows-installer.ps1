<#
.SYNOPSIS
  Builds the Windows packages of a Lumerist release, on Windows.

.DESCRIPTION
  For x64 and arm64: a self-contained publish of the app (no .NET needed on
  the user's PC), a portable zip and a per-user installer (Inno Setup 6).
  Output: build\release\<version>\windows\, with BUILD-COMMIT (the commit
  they were built from) and a .sha256 next to each package. Copy that
  folder to build/release/<version>/windows/ on the release Mac:
  Scripts/release.sh publishes the Mac and Windows packages together and
  refuses when one is missing or was built from another commit.

  Needs: the .NET 10 SDK, Inno Setup 6, git, a clean working tree.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Scripts\build-windows-installer.ps1
#>
[CmdletBinding()]
param(
    [string[]] $Architectures = @('x64', 'arm64'),
    [string] $Iscc,
    # For a test build from uncommitted changes (BUILD-COMMIT then says so,
    # and release.sh refuses the packages); never for a release.
    [switch] $AllowDirty
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
$out = Join-Path $root "build\release\$version\windows"

function Step($message) { Write-Host "==> $message" -ForegroundColor Cyan }

if (-not $Iscc) {
    $Iscc = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Iscc) { throw 'Inno Setup 6 not found: install it, or pass -Iscc <path to ISCC.exe>.' }

# git's own messages must not stop the script (Windows PowerShell turns them into errors).
$ErrorActionPreference = 'Continue'
$commit = git -C $root rev-parse --verify --quiet HEAD 2>$null
$headFound = $LASTEXITCODE -eq 0 -and $commit
$dirty = git -C $root status --porcelain 2>$null
$ErrorActionPreference = 'Stop'
if (-not $headFound) {
    if (-not $AllowDirty) { throw 'No commit to build from (not a git repository, or no commit yet).' }
    $commit = 'uncommitted'
}
$commit = "$commit".Trim()
if ($dirty -and -not $AllowDirty) { throw 'Uncommitted changes: release packages are built from a commit.' }

Step "Lumerist $version, Windows packages from $commit"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

foreach ($arch in $Architectures) {
    $app = Join-Path $out "$arch\Lumerist"
    Step "Publishing win-$arch"
    dotnet publish (Join-Path $root 'Windows\Lumerist.App') -c Release -r "win-$arch" --self-contained true -o $app
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $arch." }
    if (-not (Test-Path (Join-Path $app 'Lumerist.lrplugin\Info.lua'))) { throw "The plug-in is missing from the $arch build." }

    # The packages are not code-signed (README and SECURITY.md explain why).
    # Signing goes here: Lumerist.exe before it is zipped and packed, the
    # installer after ISCC.

    $zip = Join-Path $out "Lumerist-$version-windows-$arch.zip"
    Step "Portable zip: $(Split-Path -Leaf $zip)"
    Compress-Archive -Path $app -DestinationPath $zip

    Step "Installer for $arch"
    & $Iscc "/DVersion=$version" "/DArch=$arch" "/DSourceDir=$app" "/DOutputDir=$out" (Join-Path $root 'Windows\Installer\Lumerist.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed for $arch." }
}

Step 'Checksums'
Get-ChildItem $out -File | Where-Object { $_.Extension -in '.zip', '.exe' } | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    # Same format as shasum -a 256, so the Mac checks them the same way.
    Set-Content -Path "$($_.FullName).sha256" -Value "$hash  $($_.Name)" -NoNewline -Encoding ascii
}
Set-Content -Path (Join-Path $out 'BUILD-COMMIT') -Value $commit -NoNewline -Encoding ascii

Write-Host ''
Write-Host "Packages in ${out}:"
Get-ChildItem $out -File | Format-Table Name, Length -AutoSize
