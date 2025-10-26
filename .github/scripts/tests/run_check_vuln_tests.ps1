<# Test runner for check_vuln.ps1
Runs a set of scenarios and prints results.
#>
param()

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
# repo root is three levels up from .github/scripts/tests
$repoRoot = (Get-Item $scriptDir).Parent.Parent.Parent.FullName
$checkScript = Join-Path $repoRoot '.github/scripts/check_vuln.ps1'

$fixtures = Join-Path $scriptDir 'fixtures'

$tests = @(
  @{ name = 'No updates -> skip vuln'; outdated = Join-Path $fixtures 'outdated_no_updates.json'; vuln = $null; expect = 0 },
  @{ name = 'Updates + High only -> report but continue'; outdated = Join-Path $fixtures 'outdated_with_updates.json'; vuln = Join-Path $fixtures 'vuln_high.txt'; expect = 0 },
  @{ name = 'Updates + Critical non-allowlisted -> fail'; outdated = Join-Path $fixtures 'outdated_with_updates.json'; vuln = Join-Path $fixtures 'vuln_critical_nonallow.txt'; expect = 1 },
  @{ name = 'Updates + Critical allowlisted -> allow and continue'; outdated = Join-Path $fixtures 'outdated_with_updates.json'; vuln = Join-Path $fixtures 'vuln_critical.txt'; expect = 0 }
)

Write-Host "Running tests against: $checkScript`n"

foreach ($t in $tests) {
  Write-Host "=== Test: $($t.name) ==="
  $args = @(
    '-ProjectPath', 'MailAgent/MailAgent.csproj',
    '-AllowlistPath', (Join-Path $repoRoot '.github/allowlist.txt'),
    '-FailOnNonAllowlistedCritical'
  )
  if ($t.outdated) { $args += ('-OutdatedJsonFile', $t.outdated) }
  if ($t.vuln)    { $args += ('-VulnOutputFile', $t.vuln) }

  $stdoutFile = Join-Path $scriptDir 'last_stdout.txt'
  $stderrFile = Join-Path $scriptDir 'last_stderr.txt'
  if (Test-Path $stdoutFile) { Remove-Item $stdoutFile -Force }
  if (Test-Path $stderrFile) { Remove-Item $stderrFile -Force }

  $argList = @('-File', $checkScript) + $args
  Write-Host "Invoking: pwsh $($argList -join ' ')"
  $proc = Start-Process -FilePath pwsh -ArgumentList $argList -NoNewWindow -PassThru -Wait -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
  $exit = $proc.ExitCode

  Write-Host "Exit code: $exit"
  Write-Host "--- STDOUT ---"
  if (Test-Path $stdoutFile) { Get-Content $stdoutFile | ForEach-Object { Write-Host "  $_" } } else { Write-Host "  (no stdout)" }
  Write-Host "--- STDERR ---"
  if (Test-Path $stderrFile) { Get-Content $stderrFile | ForEach-Object { Write-Host "  $_" } } else { Write-Host "  (no stderr)" }

  if ($exit -eq $t.expect) { Write-Host "RESULT: PASS (expected $($t.expect))`n" } else { Write-Host "RESULT: FAIL (expected $($t.expect))`n" }
}

Write-Host "All tests done."
