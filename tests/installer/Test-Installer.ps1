# Silent install into a throwaway folder, run the installed MCP server, then silent uninstall.
param([Parameter(Mandatory)][string]$Setup, [string]$ExpectedVersion = '')
$ErrorActionPreference = 'Stop'
$dir = Join-Path ([IO.Path]::GetTempPath()) ("nyvora-install-" + [guid]::NewGuid().ToString('N'))
$log = Join-Path ([IO.Path]::GetTempPath()) 'nyvora-setup.log'
$failures = 0
function Check($name, $ok, $detail = '') {
    if ($ok) { "  ok    $name" } else { "  FAIL  $name $detail"; $script:failures++ }
}

if ($ExpectedVersion) {
    Check 'the installer file name carries the version' ((Split-Path $Setup -Leaf) -eq "Nyvora-Setup-v$ExpectedVersion-x64.exe") (Split-Path $Setup -Leaf)
}

"Install"
$p = Start-Process -FilePath $Setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$dir`"", "/LOG=`"$log`"") -Wait -PassThru
Check 'setup exits with 0' ($p.ExitCode -eq 0) "exit code $($p.ExitCode)"
Check 'Nyvora.exe is installed' (Test-Path (Join-Path $dir 'Nyvora.exe'))
Check 'nyvora-mcp.exe is installed' (Test-Path (Join-Path $dir 'nyvora-mcp.exe'))
Check 'the .NET runtime is installed with it' (Test-Path (Join-Path $dir 'coreclr.dll'))
Check 'an uninstaller is installed' (Test-Path (Join-Path $dir 'unins000.exe'))
if ($ExpectedVersion) {
    $reg = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8E3C1A52-6B1D-4F57-9C0E-2A7D5B94F3C8}_is1' -ErrorAction SilentlyContinue
    Check 'Windows lists the installed version' ($null -ne $reg -and $reg.DisplayVersion -eq $ExpectedVersion) ("DisplayVersion=" + $reg.DisplayVersion)
}
$startLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'Nyvora.lnk'
Check 'the Start menu entry exists' (Test-Path $startLink)

"Installed MCP server"
& "$PSScriptRoot\..\mcp\Test-Mcp.ps1" -Exe (Join-Path $dir 'nyvora-mcp.exe')

"Uninstall"
if (Test-Path (Join-Path $dir 'unins000.exe')) {
    Start-Process -FilePath (Join-Path $dir 'unins000.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait | Out-Null
    # The uninstaller hands off to a temporary copy of itself, so wait for the folder to disappear.
    for ($i = 0; $i -lt 60 -and (Test-Path $dir); $i++) { Start-Sleep -Milliseconds 500 }
}
Check 'the program folder is removed' (-not (Test-Path $dir))
Check 'the Start menu entry is removed' (-not (Test-Path $startLink))

if (Test-Path $log) { "--- setup log (tail) ---"; Get-Content $log -Tail 12 }
if ($failures -gt 0) { throw "$failures installer check(s) failed" }
"Installer checks passed"
