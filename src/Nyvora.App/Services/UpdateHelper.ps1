# Runs in a hidden Windows PowerShell after Nyvora has exited. Embedded in the app and also exercised by
# tests/update-helper/Test-UpdateHelper.ps1, so what ships is what is tested.
#
# Inputs (environment variables): NYVORA_PID, NYVORA_STAGE, NYVORA_DEST, NYVORA_EXE, NYVORA_TARGET,
# NYVORA_BACKUP, NYVORA_LOG; optional NYVORA_EXE_ARGS and NYVORA_HEALTH_SECONDS (default 8).
#
# Order of events: back up the install folder, copy the new files over it, start the new version, and give it a
# few seconds to prove it stays up. Any failure puts the backup back, so a bad update never leaves a broken install.
# robocopy exit codes below 8 mean success.
$ErrorActionPreference = 'Stop'
$dest = $env:NYVORA_DEST
$stage = $env:NYVORA_STAGE
$backup = $env:NYVORA_BACKUP
$exe = $env:NYVORA_EXE
$target = $env:NYVORA_TARGET
$health = 8
if ($env:NYVORA_HEALTH_SECONDS) { $health = [int]$env:NYVORA_HEALTH_SECONDS }

function Log([string]$m) {
  try { Add-Content -LiteralPath $env:NYVORA_LOG -Value ((Get-Date).ToString('o') + ' ' + $m) } catch { }
}

function Copy-Tree([string]$from, [string]$to, [string]$flags) {
  $a = @($from, $to) + ($flags -split ' ') + @('/R:5', '/W:1', '/NFL', '/NDL', '/NP', '/NJH', '/NJS')
  $out = & robocopy @a
  $code = $LASTEXITCODE
  if ($code -ge 8) {
    # Keep the reason (a locked file, access denied...) so a failed update can be diagnosed from update.log.
    @($out | Where-Object { $_ -match 'ERROR|denied|being used|cannot' } | Select-Object -First 8) | ForEach-Object { Log ('robocopy: ' + ([string]$_).Trim()) }
  }
  return $code
}

function Start-App {
  if ($env:NYVORA_EXE_ARGS) { return Start-Process -FilePath $exe -ArgumentList $env:NYVORA_EXE_ARGS -PassThru }
  return Start-Process -FilePath $exe -PassThru
}

function Restore-Backup {
  if (-not (Test-Path -LiteralPath (Join-Path $backup 'Nyvora.exe'))) {
    Log 'no usable backup; cannot roll back'
    return $false
  }
  # /MIR makes the install folder identical to the backup again (including removing files the update added).
  $c = Copy-Tree $backup $dest '/MIR'
  if ($c -ge 8) {
    Log ('ROLLBACK FAILED (robocopy ' + $c + ')')
    return $false
  }
  Log 'rolled back to the previous version'
  return $true
}

$backedUp = $false
$copied = $false
try {
  if ($env:NYVORA_PID) { Wait-Process -Id ([int]$env:NYVORA_PID) -Timeout 30 -ErrorAction SilentlyContinue }

  if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
  $c = Copy-Tree $dest $backup '/E /IS /IT'
  if ($c -ge 8) { throw ('backup failed (robocopy ' + $c + '); nothing was changed') }
  $backedUp = $true

  $c = Copy-Tree $stage $dest '/E /IS /IT'
  if ($c -ge 8) { throw ('copy failed (robocopy ' + $c + ')') }
  $copied = $true
} catch {
  Log ('update failed: ' + $_.Exception.Message)
  if ($backedUp) { [void](Restore-Backup) }
}

$p = $null
try { $p = Start-App } catch { Log ('could not start the app: ' + $_.Exception.Message) }

if ($copied -and $null -eq $p) {
  Log 'new version did not start'
  if (Restore-Backup) { try { [void](Start-App) } catch { Log ('could not start the old version: ' + $_.Exception.Message) } }
}
elseif ($copied) {
  Start-Sleep -Seconds $health
  if ($p.HasExited) {
    Log ('new version exited within ' + $health + 's (code ' + $p.ExitCode + ')')
    if (Restore-Backup) { try { [void](Start-App) } catch { Log ('could not start the old version: ' + $_.Exception.Message) } }
  }
  else {
    Log ('updated to ' + $target)
    Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue
  }
}
