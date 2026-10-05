<#
.SYNOPSIS
  Builds the tool in Release and produces two distributables:
    package\out\Oliver4.IconLibrary.<version>.nupkg   - for the XrmToolBox Tool Library
    package\out\Oliver4.IconLibrary-<version>.zip     - manual install (copy the DLL into the XrmToolBox Plugins folder)

.NOTES
  Run from a Developer PowerShell / a shell where dotnet and nuget are on the path.
  nuget.exe: https://www.nuget.org/downloads (needed only for the .nupkg)
#>
param(
    [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$proj = Join-Path $root "src\Oliver4.IconLibrary\Oliver4.IconLibrary.csproj"
$bin  = Join-Path $root "src\Oliver4.IconLibrary\bin\Release\net48"
$out  = Join-Path $root "package\out"

if (-not $SkipBuild) {
    Write-Host "Building icon catalogue..."
    python (Join-Path $root "build\build_catalogue.py")
    if ($LASTEXITCODE -ne 0) { throw "Catalogue build failed" }
    Write-Host "Building tool (Release)..."
    dotnet build $proj -c Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
}

$dll = Join-Path $bin "Oliver4.IconLibrary.dll"
$version = (Get-Item $dll).VersionInfo.ProductVersion
if ($version -match '^(\d+\.\d+\.\d+)') { $version = $Matches[1] }
Write-Host "Version $version"

New-Item -ItemType Directory -Force -Path $out | Out-Null

# --- manual install zip: just the DLL plus licence files ---
$stage = Join-Path $out "stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item $dll $stage
Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.txt") $stage
Copy-Item (Join-Path $root "LICENSE") (Join-Path $stage "LICENSE.txt")
Copy-Item (Join-Path $root "package\INSTALL.txt") $stage
$zip = Join-Path $out "Oliver4.IconLibrary-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Write-Host "Wrote $zip"

# --- NuGet package for the XrmToolBox Tool Library ---
$nuget = Get-Command nuget -ErrorAction SilentlyContinue
if ($nuget) {
    $nuspec = Join-Path $root "package\Oliver4.IconLibrary.nuspec"
    & nuget pack $nuspec -Version $version -OutputDirectory $out -NoPackageAnalysis
    Write-Host "Wrote nupkg to $out"
} else {
    Write-Warning "nuget.exe not found on the path - .nupkg not created. Install it and rerun with -SkipBuild."
}
