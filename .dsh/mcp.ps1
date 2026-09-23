# Minimal Unity MCP client for the Codely/Tuanjie bridge.
# Usage: pwsh -File mcp.ps1 -Tool unity_scene -Json '{"action":"get_active"}'
#        pwsh -File mcp.ps1 -Tool exec_editor_script -Json '{"script":"return 1+1;","summary":"t"}'
param(
    [Parameter(Mandatory = $true)][string]$Tool,
    [string]$Json = '{}',
    [string]$Endpoint = 'http://127.0.0.1:8765/mcp',
    [switch]$Raw
)

$ErrorActionPreference = 'Stop'
$headers = @{ 'Accept' = 'application/json, text/event-stream'; 'Content-Type' = 'application/json' }

function Send-Mcp($body, $sessionId) {
    $h = @{} + $headers
    if ($sessionId) { $h['mcp-session-id'] = $sessionId }
    $r = Invoke-WebRequest -Uri $Endpoint -Method Post -Body $body -Headers $h -TimeoutSec 900 -UseBasicParsing
    $line = ($r.Content -split "`n" | Where-Object { $_ -match '^\s*\{' } | Select-Object -Last 1)
    return @{ Json = ($line | ConvertFrom-Json); Session = $r.Headers['mcp-session-id'] }
}

$init = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"dsh","version":"1.0"}}}'
$s = Send-Mcp $init $null
$sid = $s.Session
if ($sid -is [array]) { $sid = $sid[0] }

Send-Mcp '{"jsonrpc":"2.0","method":"notifications/initialized"}' $sid | Out-Null

$argObj = $Json | ConvertFrom-Json
$payload = @{
    jsonrpc = '2.0'
    id      = 2
    method  = 'tools/call'
    params  = @{ name = $Tool; arguments = $argObj }
} | ConvertTo-Json -Depth 20 -Compress

$res = Send-Mcp $payload $sid
$j = $res.Json

if ($Raw) { $j | ConvertTo-Json -Depth 20; exit 0 }

$texts = @($j.result.content | Where-Object { $_.type -eq 'text' } | ForEach-Object { $_.text })
if ($texts.Count -gt 0) { $texts -join "`n" }
else { $j | ConvertTo-Json -Depth 20 }
