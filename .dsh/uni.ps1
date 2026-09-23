# Drive Unity through the file bridge (replaces the dead MCP bridge).
#
#   . ([scriptblock]::Create((Get-Content "D:\project：cultivation\.dsh\uni.ps1" -Raw)))
#   Uni "menu:Cultivation/Build Main Menu Scene"
#   Uni @("refresh","console:get:20")
#
# ASCII only on purpose: this file gets read back as GBK sometimes and
# non-ASCII would break string literals.

$script:DshDir     = "D:\project：cultivation\.dsh"
$script:CmdPath    = Join-Path $script:DshDir "cmd.txt"
$script:ResultPath = Join-Path $script:DshDir "result.txt"

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class UniWin {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
}
"@ -ErrorAction SilentlyContinue

function Focus-Unity {
    $p = Get-Process -Name "tuanjie","Tuanjie","Unity" -ErrorAction SilentlyContinue |
         Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($null -eq $p) { return $false }
    $h = $p.MainWindowHandle
    if ([UniWin]::IsIconic($h)) { [UniWin]::ShowWindow($h, 9) | Out-Null }
    [UniWin]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 250
    return $true
}

function Uni {
    param(
        [Parameter(Mandatory = $true)]$Command,
        [int]$TimeoutSec = 180
    )
    $cmds = @($Command)
    $null = Focus-Unity
    Remove-Item $script:ResultPath -ErrorAction SilentlyContinue
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($script:CmdPath, ($cmds -join "`n"), $utf8)

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 400
        if (Test-Path $script:ResultPath) {
            Start-Sleep -Milliseconds 150
            $r = [System.IO.File]::ReadAllText($script:ResultPath, [System.Text.Encoding]::UTF8)
            Remove-Item $script:ResultPath -ErrorAction SilentlyContinue
            return $r
        }
        # keep nudging Unity so its editor update loop keeps ticking
        if (((Get-Date) -lt $deadline) -and ((Get-Random -Maximum 6) -eq 0)) { $null = Focus-Unity }
    }
    return "!! TIMEOUT after $TimeoutSec s - Unity not running or not focused"
}
