# .dsh/serve-unity-mcp.ps1 —— 手动启动 Unity MCP 服务器的 HTTP 通道
#
# 这是【可选】通道。主通道是 stdio：harness 按 profiles/desktop/cordis.patch.yml
# 里的 mcp-unity 行自己拉起 `codely serve unity-mcp --stdio`，不需要本脚本。
#
# 什么时候用它：
#   - 想在 harness 之外直接点 Unity 工具（配合 .dsh\mcp.ps1 发 tools/call）
#   - 排查 MCP 服务器本身的问题（stdio 模式看不到它的启动日志）
#
# 用法:
#   pwsh -File "D:\project：cultivation\.dsh\serve-unity-mcp.ps1"              # 前台
#   pwsh -File "D:\project：cultivation\.dsh\serve-unity-mcp.ps1" -Background  # 后台
#   pwsh -File "D:\project：cultivation\.dsh\serve-unity-mcp.ps1" -Port 8766

param(
    [int]$Port = 8765,
    [string]$ProjectPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'cultivation'),
    [switch]$Background
)

$ErrorActionPreference = 'Stop'
$CodelyExe = 'D:\Tuanjie Cowork\cli\bin\win32-x64\codely.exe'

if (-not (Test-Path $CodelyExe)) { throw "找不到 codely.exe: $CodelyExe" }
if (-not (Test-Path (Join-Path $ProjectPath 'Assets'))) { throw "不像 Unity 工程（没有 Assets/）: $ProjectPath" }

# 端口文件是服务器发现编辑器端口的唯一依据，先确认它存在
$PortFile = Join-Path $ProjectPath 'Temp\.com-unity-codely.json'
if (-not (Test-Path $PortFile)) {
    Write-Warning "没有 $PortFile —— 编辑器可能没开，或 codely 桥没启动。服务器会一直重试连接。"
} else {
    $j = Get-Content $PortFile -Raw | ConvertFrom-Json
    Write-Output ("编辑器桥端口: {0}  ({1})" -f $j.unity_port, $PortFile)
}

$cliArgs = @('serve', 'unity-mcp', '--http', '--http-port', "$Port", '--unity-project-path', $ProjectPath)

Write-Output ("启动: codely {0}" -f ($cliArgs -join ' '))
Write-Output ("MCP 端点: http://127.0.0.1:{0}/mcp" -f $Port)
Write-Output ''

if ($Background) {
    Start-Process -FilePath $CodelyExe -ArgumentList $cliArgs -WindowStyle Hidden
    Write-Output '已在后台启动。用 .dsh\health.ps1 确认端口，用 .dsh\mcp.ps1 发工具调用。'
    Write-Output '停止: Get-Process codely | Stop-Process'
} else {
    Write-Output '前台运行，Ctrl+C 停止。'
    & $CodelyExe @cliArgs
}
