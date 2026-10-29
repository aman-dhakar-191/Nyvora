# Drives the published nyvora-mcp.exe over stdio exactly as an AI agent would, against a throwaway data folder.
param([Parameter(Mandatory)][string]$Exe)
$ErrorActionPreference = 'Stop'

$data = Join-Path ([IO.Path]::GetTempPath()) ("nyvora-mcp-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $data | Out-Null
$script:failures = 0

function Check($name, $ok, $detail = '') {
    if ($ok) { "  ok    $name" } else { "  FAIL  $name $detail"; $script:failures++ }
}

function Start-Mcp([string]$extra) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Exe
    $psi.Arguments = "--data-dir `"$data`" $extra"
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.StandardInputEncoding = New-Object System.Text.UTF8Encoding($false)
    return [System.Diagnostics.Process]::Start($psi)
}

function Send($p, [string]$json) { $p.StandardInput.WriteLine($json); $p.StandardInput.Flush() }

function Receive($p) {
    $t = $p.StandardOutput.ReadLineAsync()
    if (-not $t.Wait(15000)) { throw 'no reply from nyvora-mcp within 15 s' }
    return $t.Result | ConvertFrom-Json
}

function Rpc($p, [int]$id, [string]$method, $params = $null) {
    $msg = @{ jsonrpc = '2.0'; id = $id; method = $method }
    if ($null -ne $params) { $msg.params = $params }
    Send $p ($msg | ConvertTo-Json -Depth 10 -Compress)
    return Receive $p
}

function Tool($p, [int]$id, [string]$name, $arguments = @{}) {
    return Rpc $p $id 'tools/call' @{ name = $name; arguments = $arguments }
}

try {
    "Read-only session"
    $p = Start-Mcp ''
    $r = Rpc $p 1 'initialize' @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'ci'; version = '1' } }
    Check 'initialize answers with the server name' ($r.result.serverInfo.name -eq 'nyvora')
    Send $p '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    $r = Rpc $p 2 'tools/list'
    $names = @($r.result.tools | ForEach-Object { $_.name })
    Check 'lists the read tools' ($names -contains 'list_tasks')
    Check 'hides the write tools' (-not ($names -contains 'add_task'))
    $r = Tool $p 3 'add_task' @{ title = 'should not exist' }
    Check 'refuses to add without --allow-write' ($r.result.isError -eq $true)
    Check 'wrote nothing' (-not (Test-Path (Join-Path $data 'tasks.json')))
    $p.StandardInput.Close()
    Check 'exits when stdin closes' ($p.WaitForExit(10000))

    "Read-write session"
    $p = Start-Mcp '--allow-write'
    $null = Rpc $p 1 'initialize' @{ protocolVersion = '2025-06-18' }
    $r = Tool $p 2 'add_task' @{ title = 'Written by an agent'; remindAt = '2030-01-01T09:00:00+00:00' }
    $task = $r.result.content[0].text | ConvertFrom-Json
    Check 'add_task succeeds' ($r.result.isError -eq $false -and $task.title -eq 'Written by an agent')
    $r = Tool $p 3 'list_tasks'
    $list = $r.result.content[0].text | ConvertFrom-Json
    Check 'list_tasks returns it' ($list.total -eq 1 -and $list.tasks[0].id -eq $task.id)
    $r = Tool $p 4 'set_status' @{ id = $task.id; status = 'In progress' }
    Check 'set_status moves it' (($r.result.content[0].text | ConvertFrom-Json).status -eq 'In progress')
    $r = Tool $p 5 'complete_task' @{ id = $task.id }
    Check 'complete_task finishes it' (($r.result.content[0].text | ConvertFrom-Json).done -eq $true)
    $r = Tool $p 6 'delete_task' @{ id = $task.id }
    Check 'delete_task is refused without --allow-delete' ($r.result.isError -eq $true)
    $p.StandardInput.Close()
    $null = $p.WaitForExit(10000)

    $file = Get-Content (Join-Path $data 'tasks.json') -Raw | ConvertFrom-Json
    $saved = @($file)
    Check 'the change is in tasks.json' ($saved.Count -eq 1 -and $saved[0].Done -eq $true)
}
finally {
    Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
}

if ($script:failures -gt 0) { throw "$script:failures MCP check(s) failed" }
"MCP checks passed"
