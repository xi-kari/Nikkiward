# Verifies the window drag strip, page-owned controls, and caption buttons.
param([int]$SettleMs = 1500)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$native = @'
using System;
using System.Runtime.InteropServices;
public static class Hit {
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
}
'@
if (-not ('Hit' -as [type])) { Add-Type -TypeDefinition $native }

$proc = Get-Process -Name Nikkiward -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { throw 'launcher not running; start it first' }
$hwnd = $proc.MainWindowHandle
Start-Sleep -Milliseconds $SettleMs

$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$fail = 0

$names = @{ 1 = 'HTCLIENT'; 2 = 'HTCAPTION'; 8 = 'HTMINBUTTON'; 9 = 'HTMAXBUTTON'; 20 = 'HTCLOSE' }

function Test-Point([string]$label, [int]$x, [int]$y, [int[]]$expected) {
    # WM_NCHITTEST: 1 = HTCLIENT (input reaches the page); 2/8/9/20 are
    # non-client, meaning the window chrome consumed the point.
    $packed = [IntPtr](($y -shl 16) -bor ($x -band 0xFFFF))
    $code = [Hit]::SendMessage($hwnd, 0x0084, [IntPtr]::Zero, $packed).ToInt32()
    $ok = $expected -contains $code
    $name = if ($names.ContainsKey($code)) { $names[$code] } else { "code=$code" }
    Write-Output ("{0} {1} at ({2},{3}) -> {4}" -f
        $(if ($ok) { 'PASS' } else { 'FAIL' }), $label, $x, $y, $name)
    if (-not $ok) { $script:fail++ }
}

# Locate the gallery header buttons through automation so the probe follows the
# real layout instead of hardcoded pixels.
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)
$buttons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)

$wr = $root.Current.BoundingRectangle

# Classify by automation name rather than by x, because the reserved caption
# width is in logical units while these rectangles are in physical pixels.
$captionCodes = @{ Minimize = 8; Maximize = 9; Close = 20 }

$inStrip = @()
foreach ($b in $buttons) {
    $r = $b.Current.BoundingRectangle
    if ($r.Height -le 0) { continue }
    if ($captionCodes.ContainsKey($b.Current.Name)) { continue }
    $rel = $r.Y - $wr.Y
    if ($rel -ge 0 -and $rel -lt 48) {
        $inStrip += , @($b.Current.Name, [int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    }
}

Write-Output "found $($inStrip.Count) header control(s) inside the 48px drag strip"
if ($inStrip.Count -eq 0) { Write-Output 'INFO caption-only drag strip' }

foreach ($entry in $inStrip) {
    $label = if ($entry[0]) { $entry[0] } else { '(unnamed)' }
    Test-Point "header '$label'" $entry[1] $entry[2] @(1)
}

Test-Point "drag strip" `
    ([int]($wr.X + $wr.Width / 2)) ([int]($wr.Y + 24)) @(2)

# The caption buttons must stay non-client, or the window becomes unclosable.
# Located by automation and matched by expected hit-test code, so a layout shift
# cannot make this pass by probing empty space.
$captionSeen = 0
foreach ($b in $buttons) {
    $name = $b.Current.Name
    if (-not $captionCodes.ContainsKey($name)) { continue }
    $r = $b.Current.BoundingRectangle
    if ($r.Height -le 0) { continue }
    $captionSeen++
    Test-Point "caption $name" `
        ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2)) @($captionCodes[$name])
}

if ($captionSeen -lt 3) {
    Write-Output "FAIL expected 3 caption buttons, found $captionSeen"
    $fail++
}

Write-Output "RESULT failed=$fail"
if ($fail -gt 0) { exit 1 }
