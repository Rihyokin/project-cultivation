# .dsh/health.ps1 —— Unity 控制管线体检（只读，不改任何东西）
#
# 用法:
#   pwsh -File "D:\project：cultivation\.dsh\health.ps1"
#
# 检查四段链路：
#   1. Tuanjie/Unity 编辑器进程
#   2. 编辑器里的 codely TCP 桥（cn.tuanjie.codely.bridge）
#   3. MCP 服务器（stdio 由 harness 拉起；HTTP 8765 是可选的手动通道）
#   4. harness 侧的注册行（profiles/desktop/cordis.patch.yml）

$ErrorActionPreference = 'Continue'

$DshDir    = $PSScriptRoot
$Root      = Split-Path $DshDir -Parent
$UnityProj = Join-Path $Root 'cultivation'
$PortFile  = Join-Path $UnityProj 'Temp\.com-unity-codely.json'
$AliveFile = Join-Path $DshDir 'alive.txt'
$PatchFile = Join-Path $env:USERPROFILE '.dsh\profiles\desktop\cordis.patch.yml'
$CodelyExe = 'D:\Tuanjie Cowork\cli\bin\win32-x64\codely.exe'
$NodeExe   = 'D:\Tuanjie Cowork\cli\bin\win32-x64\node.exe'
$ProxyFile = Join-Path $DshDir 'unity-mcp-proxy.mjs'

function Test-TcpPort {
    param([string]$Target, [int]$Port, [int]$TimeoutMs = 1500)
    $c = New-Object System.Net.Sockets.TcpClient
    try {
        $iar = $c.BeginConnect($Target, $Port, $null, $null)
        if ($iar.AsyncWaitHandle.WaitOne($TimeoutMs) -and $c.Connected) { return $true }
        return $false
    } catch { return $false } finally { $c.Close() }
}

function Tag { param([bool]$Ok) if ($Ok) { '[ OK ]' } else { '[FAIL]' } }

Write-Output ''
Write-Output '================ Unity 控制管线体检 ================'
Write-Output ("项目: {0}" -f $UnityProj)
Write-Output ''

# ---- 1. 编辑器进程 ----
$editor = Get-Process -Name 'Tuanjie', 'tuanjie', 'Unity' -ErrorAction SilentlyContinue |
          Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$allEditor = Get-Process -Name 'Tuanjie', 'tuanjie', 'Unity' -ErrorAction SilentlyContinue
Write-Output ("{0} 1. 编辑器进程：{1} 个" -f (Tag ($null -ne $editor)), @($allEditor).Count)
if ($editor) {
    Write-Output ("        pid={0}  窗口='{1}'" -f $editor.Id, $editor.MainWindowTitle)
} else {
    Write-Output '        没找到带窗口的编辑器进程 —— 请先打开 Tuanjie/Unity 工程'
}

# ---- 2. codely TCP 桥 ----
$bridgeOk = $false
$port = 0
if (Test-Path $PortFile) {
    $age = (Get-Date) - (Get-Item $PortFile).LastWriteTime
    try {
        $j = Get-Content $PortFile -Raw | ConvertFrom-Json
        $port = [int]$j.unity_port
        $bridgeOk = Test-TcpPort -Target '127.0.0.1' -Port $port
        Write-Output ("{0} 2. codely TCP 桥：端口 {1}（{2} 分钟前写入，reason={3}）" -f (Tag $bridgeOk), $port, [int]$age.TotalMinutes, $j.reason)
        if (-not $bridgeOk) {
            Write-Output '        端口通了但拒绝连接 —— 编辑器可能正在重编译/重载域，稍等再试'
        }
    } catch {
        Write-Output ("{0} 2. codely TCP 桥：端口文件解析失败 {1}" -f (Tag $false), $_.Exception.Message)
    }
} else {
    Write-Output ("{0} 2. codely TCP 桥：找不到 {1}" -f (Tag $false), $PortFile)
    Write-Output '        说明编辑器从没启动过 codely 桥（或工程不是这个）'
}

# ---- 3. MCP 服务器通道 ----
Write-Output ''
Write-Output ('--- 3. MCP 服务器 ---')
$nodeOk = Test-Path $NodeExe
$proxyOk = Test-Path $ProxyFile
$stdioOk = $nodeOk -and $proxyOk
Write-Output ("{0} stdio 通道（harness 自动拉起）" -f (Tag $stdioOk))
Write-Output ("        node   : {0}" -f $(if ($nodeOk) { $NodeExe } else { "$NodeExe  <-- 缺失" }))
Write-Output ("        垫片   : {0}" -f $(if ($proxyOk) { $ProxyFile } else { "$ProxyFile  <-- 缺失" }))
if ($stdioOk) {
    Write-Output '        由 harness 按需 spawn；垫片把 codely 的 inputSchema 消毒成'
    Write-Output '        harness 受支持子集，否则 17 个工具会因「要么完整世代要么没有」全丢'
    Write-Output '        本地复验: node .dsh\unity-mcp-proxy.mjs --selftest <toolslist.json>'
} else {
    Write-Output '        Tuanjie Cowork CLI 或垫片缺失，主通道起不来'
}

$httpOk = Test-TcpPort -Target '127.0.0.1' -Port 8765
Write-Output ("{0} HTTP 通道（可选，手动排查用）：127.0.0.1:8765" -f (Tag $httpOk))
if ($httpOk) {
    Write-Output '        已有人在跑 `codely serve unity-mcp --http`，可用 .dsh\mcp.ps1 直接点工具'
} else {
    Write-Output '        没启动。需要时用 .dsh\serve-unity-mcp.ps1 起（不影响 stdio 通道）'
}

# ---- 4. harness 注册行 ----
Write-Output ''
Write-Output ('--- 4. harness 注册行 ---')
if (Test-Path $PatchFile) {
    $patch = Get-Content $PatchFile -Raw
    $hasRow = $patch -match 'id:\s*mcp-unity'
    $viaProxy = $patch -match 'unity-mcp-proxy\.mjs'
    Write-Output ("{0} {1}" -f (Tag ($hasRow -and $viaProxy)), $PatchFile)
    if ($hasRow -and $viaProxy) {
        Write-Output '        已指向垫片，工具会以 mcp__unity__<名字> 出现'
    } elseif ($hasRow) {
        Write-Output '        有 id: mcp-unity 但没指向垫片 —— codely 的 schema 会被 harness 拒绝'
    } else {
        Write-Output '        文件在，但没有 id: mcp-unity 这一行'
    }
} else {
    Write-Output ("{0} 找不到 {1}" -f (Tag $false), $PatchFile)
}

# ---- 5. 文件桥心跳（老通道，兜底用）----
Write-Output ''
Write-Output ('--- 5. 文件桥心跳（兜底通道）---')
if (Test-Path $AliveFile) {
    $hAge = (Get-Date) - (Get-Item $AliveFile).LastWriteTime
    $hOk = $hAge.TotalSeconds -lt 10
    Write-Output ("{0} alive.txt：{1} 秒前（>10 秒说明 DshBridge 没在跑）" -f (Tag $hOk), [int]$hAge.TotalSeconds)
} else {
    Write-Output ("{0} 没有 {1}" -f (Tag $false), $AliveFile)
}

# ---- 结论 ----
Write-Output ''
Write-Output '================ 结论 ================'
if ($editor -and $bridgeOk -and $stdioOk) {
    Write-Output '主通道（MCP / stdio）应当可用。'
    Write-Output '如果模型看不到 mcp__unity__* 工具，重启 DSH Desktop 让注册行生效。'
} elseif (-not $editor) {
    Write-Output '编辑器没开 —— 先把 Tuanjie/Unity 工程打开，其余链路会自动接上。'
} elseif (-not $bridgeOk) {
    Write-Output '编辑器开着但 TCP 桥不通 —— 看编辑器 Console 里 cn.tuanjie.codely.bridge 的报错。'
} else {
    Write-Output '链路基本正常，只有 stdio 可执行文件缺失，请检查 Tuanjie Cowork CLI 安装。'
}
Write-Output ''
