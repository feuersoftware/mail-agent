<#
Local simulation of the Release workflow.
Runs on macOS / Linux where possible; skips Windows-only steps (Defender, signtool).

Usage: pwsh -NoProfile -ExecutionPolicy Bypass -File .github/scripts/local_release_simulate.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Resolve-Path .
Write-Host "Repo: $repo"

# Clean
$dirs = @('publish_temp','publish_win_x86','publish_win_x64','artifact_win-x86','artifact_win-x64')
foreach ($d in $dirs) { if (Test-Path $d) { Remove-Item -Recurse -Force $d } }
if (Test-Path 'release') { Get-ChildItem -Path 'release' -Filter '*.zip' -File | Remove-Item -Force -ErrorAction SilentlyContinue } else { New-Item -ItemType Directory -Path 'release' | Out-Null }
if (Test-Path '.tools') { Remove-Item -Recurse -Force .tools }

# 1) Build Release (no RID) to read assembly version
Write-Host "1) dotnet build (Release)"
dotnet build MailAgent/MailAgent.csproj -c Release

$asmPath = 'MailAgent/bin/Release/net9.0/FeuerSoftware.MailAgent.dll'
if (-Not (Test-Path $asmPath)) { Write-Error "Assembly not found at $asmPath"; exit 2 }
$version = [System.Reflection.AssemblyName]::GetAssemblyName($asmPath).Version.ToString()
Write-Host "Detected assembly version: $version"

# 2) Publish single-file for both RIDs with matching PlatformTarget
$rids = @('win-x86','win-x64')
foreach ($rid in $rids) {
  $pt = if ($rid -like '*x86*') { 'x86' } else { 'x64' }
  $out = "publish_$($rid)"
  if (Test-Path $out) { Remove-Item -Recurse -Force $out }
  Write-Host "Publishing for $rid (PlatformTarget=$pt) -> $out"
  try {
    dotnet publish MailAgent/MailAgent.csproj -c Release -r $rid /p:PublishSingleFile=true /p:PublishTrimmed=false /p:SelfContained=true /p:PlatformTarget=$pt -o $out
    Write-Host "Publish succeeded for $rid"
  } catch {
    $err = ($_ | Out-String).Trim()
    Write-Host ("Publish FAILED for {0}: {1}" -f $rid, $err)
  }
}

# 3) Collect licenses via dotnet-project-licenses
Write-Host "Collecting licenses with dotnet-project-licenses"
New-Item -ItemType Directory -Path '.tools' | Out-Null
try {
  dotnet tool install --tool-path .tools dotnet-project-licenses --prerelease | Out-Null
} catch {
  dotnet tool update --tool-path .tools dotnet-project-licenses --prerelease | Out-Null
}
$toolExe = Join-Path (Resolve-Path .tools).Path 'dotnet-project-licenses.exe'
if (-Not (Test-Path $toolExe)) { $toolExe = Join-Path (Resolve-Path .tools).Path 'dotnet-project-licenses' }
if (-Not (Test-Path $toolExe)) { Write-Host "dotnet-project-licenses not found as exe; attempting to run via dotnet"; $useDotnetTool = $true } else { $useDotnetTool = $false }
if ($useDotnetTool) {
  dotnet ./.tools/dotnet-project-licenses.dll -p MailAgent/MailAgent.csproj -f text -o .tools/licenses.txt
} else {
  & $toolExe -p MailAgent/MailAgent.csproj -f text -o .tools/licenses.txt
}
Write-Host "Licenses written to .tools/licenses.txt"

# 4) Prepare artifacts and ZIP
foreach ($rid in $rids) {
  $out = "publish_$rid"
  $artifact = "artifact_$rid"
  if (Test-Path $artifact) { Remove-Item -Recurse -Force $artifact }
  New-Item -ItemType Directory -Path $artifact | Out-Null

  if (Test-Path $out) {
    Write-Host "Copying published files from $out to $artifact"
    Copy-Item -Path (Join-Path $out '*') -Destination $artifact -Recurse -Force
  } else {
    Write-Host "No publish output found for $rid (skipping copy of binaries)."
  }

  # extras
  $extras = @('MailAgent/readme.md','MailAgent/install.bat','MailAgent/uninstall.bat')
  foreach ($e in $extras) {
    if (Test-Path $e) { Copy-Item -Path $e -Destination $artifact -Force }
  }

  if (Test-Path '.tools/licenses.txt') { Copy-Item -Path '.tools/licenses.txt' -Destination (Join-Path $artifact 'licenses.txt') -Force }

  $zipName = "release/mail-agent-$version-$rid.zip"
  if (Test-Path $zipName) { Remove-Item $zipName -Force }
  Write-Host "Creating ZIP $zipName"
  Compress-Archive -Path (Join-Path $artifact '*') -DestinationPath $zipName -Force
}

# 5) List release folder and zip contents
Write-Host "Release folder contents:"
# only show zips that match the generated version pattern
$zipPattern = "mail-agent-$version-*.zip"
$zips = Get-ChildItem -Path release -Filter $zipPattern -File -ErrorAction SilentlyContinue
if (-not $zips -or $zips.Count -eq 0) {
  Write-Host "No release zips found matching pattern $zipPattern"
} else {
  foreach ($f in $zips) {
    Write-Host "Contents of $($f.Name):"
    try {
      $tmp = Join-Path $env:TEMP ("tmp_unzip_$($f.BaseName)")
      if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
      Expand-Archive -LiteralPath $f.FullName -DestinationPath $tmp -Force
      Get-ChildItem -Path $tmp | ForEach-Object { Write-Host "  - $($_.Name)" }
    } catch {
      Write-Host "  (failed to list contents of $($f.Name): $($_ | Out-String))"
    }
  }
}

Write-Host "Local release simulation complete."
