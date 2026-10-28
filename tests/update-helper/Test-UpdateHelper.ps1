# Exercises src/Nyvora.App/Services/UpdateHelper.ps1 the way the app runs it (Windows PowerShell, -EncodedCommand)
# against throwaway folders: a good update, a locked file during the copy, a locked file during the backup,
# and a new version that crashes on start.
$ErrorActionPreference = 'Stop'
$helper = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\..\src\Nyvora.App\Services\UpdateHelper.ps1') -Raw
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($helper))
$root = Join-Path ($(if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { $env:TEMP })) ('nyvora-helper-' + [guid]::NewGuid().ToString('N'))
$script:failures = 0

function Check([bool]$ok, [string]$what) {
  if ($ok) { Write-Host ('  ok    ' + $what) } else { Write-Host ('  FAIL  ' + $what); $script:failures++ }
}

function New-Case([string]$name) {
  $c = Join-Path $root $name
  $dest = Join-Path $c 'install'; $stage = Join-Path $c 'stage'
  New-Item -ItemType Directory -Force -Path $dest, $stage | Out-Null
  Set-Content -LiteralPath (Join-Path $dest 'Nyvora.exe') -Value 'old-exe'
  Set-Content -LiteralPath (Join-Path $dest 'a.txt') -Value 'old-a'
  Set-Content -LiteralPath (Join-Path $stage 'Nyvora.exe') -Value 'new-exe'
  Set-Content -LiteralPath (Join-Path $stage 'a.txt') -Value 'new-a'
  Set-Content -LiteralPath (Join-Path $stage 'added.txt') -Value 'only-in-new'
  return [pscustomobject]@{ Dir = $c; Dest = $dest; Stage = $stage; Backup = (Join-Path $c 'backup'); Log = (Join-Path $c 'update.log') }
}

function Run-Helper($case, [string]$exe, [string]$exeArgs, [int]$health) {
  $env:NYVORA_PID = ''
  $env:NYVORA_STAGE = $case.Stage; $env:NYVORA_DEST = $case.Dest; $env:NYVORA_BACKUP = $case.Backup
  $env:NYVORA_LOG = $case.Log; $env:NYVORA_TARGET = 'v9.9.9'
  $env:NYVORA_EXE = $exe; $env:NYVORA_EXE_ARGS = $exeArgs; $env:NYVORA_HEALTH_SECONDS = "$health"
  & powershell.exe -NoProfile -NonInteractive -EncodedCommand $encoded | Out-Null
}

function Read-Log($case) { if (Test-Path -LiteralPath $case.Log) { Get-Content -LiteralPath $case.Log -Raw } else { '' } }
function Text($path) { (Get-Content -LiteralPath $path -Raw).Trim() }
function Stop-Pings { Get-Process -Name ping -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }

$ping = Join-Path $env:SystemRoot 'System32\PING.EXE'
$cmd = Join-Path $env:SystemRoot 'System32\cmd.exe'

try {
  Write-Host 'Case 1: good update'
  $c = New-Case 'good'
  Run-Helper $c $ping '-n 40 127.0.0.1' 3
  $log = Read-Log $c
  Check ((Text (Join-Path $c.Dest 'Nyvora.exe')) -eq 'new-exe') 'new files are installed'
  Check (Test-Path (Join-Path $c.Dest 'added.txt')) 'files added by the update are present'
  Check ($log -match 'updated to v9\.9\.9') 'log says it updated'
  Check (-not (Test-Path $c.Backup)) 'backup is removed once the new version is healthy'
  Stop-Pings

  Write-Host 'Case 2: a file is locked while copying -> roll back'
  $c = New-Case 'locked-copy'
  $lock = [IO.File]::Open((Join-Path $c.Dest 'a.txt'), 'Open', 'Read', 'Read')   # readable (so the backup works) but not writable
  try { Run-Helper $c $ping '-n 40 127.0.0.1' 2 } finally { $lock.Dispose() }
  $log = Read-Log $c
  Check ($log -match 'copy failed') 'log says the copy failed'
  Check ($log -match 'rolled back') 'log says it rolled back'
  Check ((Text (Join-Path $c.Dest 'Nyvora.exe')) -eq 'old-exe') 'old executable is back'
  Check ((Text (Join-Path $c.Dest 'a.txt')) -eq 'old-a') 'old files are intact'
  Check (-not (Test-Path (Join-Path $c.Dest 'added.txt'))) 'files added by the failed update are gone'
  Stop-Pings

  Write-Host 'Case 3: a file is locked while backing up -> nothing is touched'
  $c = New-Case 'locked-backup'
  $lock = [IO.File]::Open((Join-Path $c.Dest 'a.txt'), 'Open', 'Read', 'None')   # not even readable
  try { Run-Helper $c $ping '-n 40 127.0.0.1' 2 } finally { $lock.Dispose() }
  $log = Read-Log $c
  Check ($log -match 'backup failed') 'log says the backup failed'
  Check ((Text (Join-Path $c.Dest 'Nyvora.exe')) -eq 'old-exe') 'install folder was not modified'
  Check (-not (Test-Path (Join-Path $c.Dest 'added.txt'))) 'nothing from the update was copied'
  Stop-Pings

  Write-Host 'Case 4: the new version crashes on start -> roll back'
  $c = New-Case 'crash'
  Run-Helper $c $cmd '/c exit 1' 3
  $log = Read-Log $c
  Check ($log -match 'exited within') 'log says the new version exited'
  Check ($log -match 'rolled back') 'log says it rolled back'
  Check ((Text (Join-Path $c.Dest 'Nyvora.exe')) -eq 'old-exe') 'old executable is back'
  Check (-not (Test-Path (Join-Path $c.Dest 'added.txt'))) 'files added by the bad update are gone'
}
finally {
  Stop-Pings
  Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

if ($script:failures -gt 0) { Write-Host ("`n$script:failures check(s) failed"); exit 1 }
Write-Host "`nAll update-helper checks passed"
