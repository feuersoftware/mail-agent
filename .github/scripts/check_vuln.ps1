<#
  check_vuln.ps1
  Parameters:
    -ProjectPath <string> (relative path to csproj)
    -AllowlistPath <string> (path to allowlist file)
    -FailOnNonAllowlistedCritical <switch>
    -OutdatedJsonFile <string> (optional, for tests)
    -VulnOutputFile <string> (optional, for tests)

  Behavior:
    If OutdatedJsonFile is provided, reads JSON from file instead of calling dotnet list --outdated.
    If VulnOutputFile is provided, reads vuln output from file instead of calling dotnet list --vulnerable.
    Exits with 1 when non-allowlisted CRITICAL vulnerabilities are found and FailOnNonAllowlistedCritical is set.
    Otherwise exits with 0.
#>
param(
  [string]$ProjectPath = "MailAgent/MailAgent.csproj",
  [string]$AllowlistPath = "$PSScriptRoot/../allowlist.txt",
  [switch]$FailOnNonAllowlistedCritical,
  [string]$OutdatedJsonFile,
  [string]$VulnOutputFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-DotNetOutdatedJson {
  param($proj)
  $out = & dotnet list $proj package --outdated --include-transitive --format json 2>&1
  $exit = $LASTEXITCODE
  return @{ ExitCode = $exit; Output = $out -join "`n" }
}

function Invoke-DotNetVulnOutput {
  param($proj)
  $out = & dotnet list $proj package --vulnerable --include-transitive 2>&1
  $exit = $LASTEXITCODE
  return @{ ExitCode = $exit; OutputLines = $out }
}

Write-Host "check_vuln: ProjectPath=$ProjectPath, AllowlistPath=$AllowlistPath, FailOnNonAllowlistedCritical=$FailOnNonAllowlistedCritical"

# Get outdated JSON
if ($OutdatedJsonFile) {
  if (-Not (Test-Path $OutdatedJsonFile)) { Write-Error "OutdatedJsonFile not found: $OutdatedJsonFile"; exit 2 }
  $rawOutdated = Get-Content $OutdatedJsonFile -Raw
  $exitCode = 0
} else {
  $res = Invoke-DotNetOutdatedJson -proj $ProjectPath
  $exitCode = $res.ExitCode
  $rawOutdated = $res.Output
}

if ($exitCode -ne 0) {
  Write-Error "dotnet list --outdated returned exit code $exitCode"
  Write-Host $rawOutdated
  exit $exitCode
}

try {
  $parsed = $rawOutdated | ConvertFrom-Json
} catch {
  Write-Error "Failed to parse JSON from dotnet list --outdated."
  Write-Host $rawOutdated
  exit 3
}

$updatesAvailable = $false
if ($parsed -and $parsed.projects) {
  foreach ($proj in $parsed.projects) {
    foreach ($fw in $proj.frameworks) {
      # transitivePackages may be absent; check existence first
      $transitive = $null
      if ($fw.PSObject.Properties.Match('transitivePackages').Count -gt 0) { $transitive = $fw.transitivePackages }
      if ($transitive -and $transitive.Count -gt 0) {
        foreach ($pkg in $transitive) {
          if ($pkg.latestVersion -and $pkg.resolvedVersion -and ($pkg.latestVersion -ne $pkg.resolvedVersion)) {
            Write-Host "Outdated package found: $($pkg.id) $($pkg.resolvedVersion) -> $($pkg.latestVersion)"
            $updatesAvailable = $true
          }
        }
      }

      # topLevelPackages may also be absent
      $toplevel = $null
      if ($fw.PSObject.Properties.Match('topLevelPackages').Count -gt 0) { $toplevel = $fw.topLevelPackages }
      if ($toplevel -and $toplevel.Count -gt 0) {
        foreach ($pkg in $toplevel) {
          if ($pkg.latestVersion -and $pkg.resolvedVersion -and ($pkg.latestVersion -ne $pkg.resolvedVersion)) {
            Write-Host "Outdated package found: $($pkg.id) $($pkg.resolvedVersion) -> $($pkg.latestVersion)"
            $updatesAvailable = $true
          }
        }
      }
    }
  }
}

if (-not $updatesAvailable) {
  Write-Host "No package updates available. Skipping vulnerability fail-check and proceeding."
  exit 0
}

Write-Host "Package updates found — running vulnerability check (including transitive)..."

# Read allowlist
$allow = @()
if ($AllowlistPath -and (Test-Path $AllowlistPath)) {
  Write-Host "Reading allowlist: $AllowlistPath"
  $allow = Get-Content $AllowlistPath | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') }
  Write-Host "Allowlisted packages: $($allow -join ', ')"
} else {
  Write-Host "No allowlist file found at $AllowlistPath"
}

# Get vulnerability output lines
if ($VulnOutputFile) {
  if (-Not (Test-Path $VulnOutputFile)) { Write-Error "VulnOutputFile not found: $VulnOutputFile"; exit 4 }
  $vulnOutputLines = Get-Content $VulnOutputFile
  $vulnExit = 0
} else {
  $vres = Invoke-DotNetVulnOutput -proj $ProjectPath
  $vulnExit = $vres.ExitCode
  $vulnOutputLines = $vres.OutputLines
}

if ($vulnExit -ne 0) {
  Write-Error "dotnet list --vulnerable returned exit code $vulnExit"
  exit $vulnExit
}

# Parse vulnerability lines
$criticalFindings = @()
$highFindings = @()
foreach ($line in $vulnOutputLines) {
  $l = $line -replace '\t',' '
  $l = $l -replace '\s{2,}',' '
  if ($l -match '^[\s>]*([^\s]+)\s+([^\s]+)\s+(Critical|High|Moderate|Low)') {
    $pkgId = $matches[1]
    $ver = $matches[2]
    $sev = $matches[3]
    if ($sev -ieq 'Critical') { $criticalFindings += @{ id=$pkgId; version=$ver; severity=$sev } }
    if ($sev -ieq 'High')     { $highFindings += @{ id=$pkgId; version=$ver; severity=$sev } }
  }
}

foreach ($f in $criticalFindings) {
  if ($allow -contains $f.id) {
    Write-Host "CRITICAL but allowlisted: $($f.id) $($f.version)"
  } else {
    Write-Error "CRITICAL vulnerability found and NOT allowlisted: $($f.id) $($f.version)"
    if ($FailOnNonAllowlistedCritical) { exit 1 } else { Write-Host "FailOnNonAllowlistedCritical not set; reporting only." }
  }
}

if ($criticalFindings.Count -eq 0 -and $highFindings.Count -gt 0) {
  Write-Host "High severity vulnerabilities found (listed below) — they are reported but do not fail the release."
  foreach ($h in $highFindings) { Write-Host "HIGH: $($h.id) $($h.version)" }
}

Write-Host "Summary: Critical=$($criticalFindings.Count) High=$($highFindings.Count)"
Write-Host "Vulnerability check completed."
exit 0
