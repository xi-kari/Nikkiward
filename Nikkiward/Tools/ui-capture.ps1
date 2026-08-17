# Captures the launcher window, optionally after clicking a navigation item.
#
# Uses PrintWindow rather than a screen grab: the window does not need to be
# foreground, so a capture cannot silently photograph whatever else is on top.
#
#   ui-capture.ps1 -Out shot.png
#   ui-capture.ps1 -Nav 相册 -Out shot.png
param(
    [string]$Nav = '',
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$SettleMs = 2500
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

$native = @'
using System;
using System.Runtime.InteropServices;
public static class Native {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
'@
if (-not ('Native' -as [type])) { Add-Type -TypeDefinition $native }

$exe = Join-Path $PSScriptRoot '..\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Nikkiward.exe'
$exe = [System.IO.Path]::GetFullPath($exe)

$proc = Get-Process -Name Nikkiward -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) {
    if (-not (Test-Path $exe)) { throw "not built: $exe" }
    $proc = Start-Process -FilePath $exe -PassThru
    for ($i = 0; $i -lt 60 -and $proc.MainWindowHandle -eq 0; $i++) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
    }
    Start-Sleep -Milliseconds 3500
}
if ($proc.MainWindowHandle -eq 0) { throw 'no main window' }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
if (-not $root) { throw 'automation root unavailable' }

if ($Nav) {
    # Nav items carry no accessible name of their own, so match the text node
    # and walk up to the nearest invokable ancestor.
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Nav)
    $hit = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $hit) { throw "nav item not found: $Nav" }

    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $node = $hit
    $clicked = $false
    while ($node -and -not $clicked) {
        $pattern = $null
        if ($node.TryGetCurrentPattern(
                [System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
            $pattern.Invoke()
            $clicked = $true
            break
        }
        if ($node.TryGetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
            $pattern.Select()
            $clicked = $true
            break
        }
        $node = $walker.GetParent($node)
    }
    if (-not $clicked) { throw "no invokable ancestor for: $Nav" }
    Start-Sleep -Milliseconds $SettleMs
}

$rect = New-Object Native+RECT
if (-not [Native]::GetClientRect($proc.MainWindowHandle, [ref]$rect)) { throw 'GetClientRect failed' }
$w = $rect.R - $rect.L
$h = $rect.B - $rect.T
if ($w -le 0 -or $h -le 0) { throw "bad client size ${w}x${h}" }

$bmp = New-Object System.Drawing.Bitmap $w, $h
$gfx = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $gfx.GetHdc()
try {
    # 2 = PW_RENDERFULLCONTENT, required for composited WinUI surfaces.
    if (-not [Native]::PrintWindow($proc.MainWindowHandle, $hdc, 2)) { throw 'PrintWindow failed' }
}
finally {
    $gfx.ReleaseHdc($hdc)
    $gfx.Dispose()
}

$outPath = if ([System.IO.Path]::IsPathRooted($Out)) {
    [System.IO.Path]::GetFullPath($Out)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Out))
}
$bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "captured ${w}x${h} -> $outPath"
