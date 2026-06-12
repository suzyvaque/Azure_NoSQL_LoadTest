<#
.SYNOPSIS
  Validates every scenario JSON in this folder against the BMT scenario contract.

.DESCRIPTION
  Dependency-free checks (no external modules) mirroring Bmt.Contracts.ScenarioConfig.Validate():
    - JSON is well-formed
    - required keys present
    - idRange is [min, max] with 1 <= min <= max
    - 0 <= thinkMsMin <= thinkMsMax
    - workers >= 1 and poolSize >= 1
    - rampToJobsPerHour (if present) >= jobsPerHour
    - distribution in {uniform, zipfian}; arrival == poisson; writeConcern == w1

.EXAMPLE
  ./validate.ps1
#>

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$required = @(
  'name', 'durationMinutes', 'jobsPerHour', 'tasksPerJob', 'arrival',
  'workers', 'poolSize', 'distribution', 'idRange', 'thinkMsMin', 'thinkMsMax', 'writeConcern'
)

$failures = 0
Get-ChildItem -Path $here -Filter '*.json' |
  Where-Object { $_.Name -ne 'schema.json' } |
  Sort-Object Name |
  ForEach-Object {
    $file = $_.Name
    $problems = @()
    try {
      $s = Get-Content -Raw -Path $_.FullName | ConvertFrom-Json
    }
    catch {
      Write-Host "FAIL  $file : invalid JSON - $($_.Exception.Message)" -ForegroundColor Red
      $script:failures++
      return
    }

    foreach ($key in $required) {
      if ($null -eq $s.PSObject.Properties[$key]) { $problems += "missing '$key'" }
    }

    if ($s.idRange -isnot [System.Array] -or $s.idRange.Count -ne 2 -or
        $s.idRange[0] -lt 1 -or $s.idRange[1] -lt $s.idRange[0]) {
      $problems += "idRange must be [min, max] with 1 <= min <= max"
    }
    if ($s.thinkMsMin -lt 0 -or $s.thinkMsMax -lt $s.thinkMsMin) {
      $problems += "think time must satisfy 0 <= thinkMsMin <= thinkMsMax"
    }
    if ($s.workers -lt 1 -or $s.poolSize -lt 1) {
      $problems += "workers and poolSize must be >= 1"
    }
    if ($null -ne $s.PSObject.Properties['rampToJobsPerHour'] -and
        $s.rampToJobsPerHour -lt $s.jobsPerHour) {
      $problems += "rampToJobsPerHour must be >= jobsPerHour"
    }
    if ($s.distribution -notin @('uniform', 'zipfian')) {
      $problems += "distribution must be uniform or zipfian"
    }
    if ($s.arrival -ne 'poisson') { $problems += "arrival must be poisson" }
    if ($s.writeConcern -ne 'w1') { $problems += "writeConcern must be w1" }

    if ($problems.Count -gt 0) {
      Write-Host "FAIL  $file : $($problems -join '; ')" -ForegroundColor Red
      $script:failures++
    }
    else {
      Write-Host "PASS  $file" -ForegroundColor Green
    }
  }

if ($failures -gt 0) {
  Write-Host "`n$failures scenario file(s) failed validation." -ForegroundColor Red
  exit 1
}
Write-Host "`nAll scenario files valid." -ForegroundColor Green
