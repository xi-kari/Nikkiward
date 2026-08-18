param(
    [int]$Iterations = 20,
    [int]$ClickDelayMs = 8,
    [int]$SettleMs = 1200
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$native = @'
using System;
using System.Runtime.InteropServices;
public static class NavigationMouse {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr info);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
}
'@
if (-not ('NavigationMouse' -as [type])) { Add-Type -TypeDefinition $native }

$exe = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Nikkiward.exe'))
$process = Get-Process -Name Nikkiward -ErrorAction SilentlyContinue |
    Where-Object {
        $_.MainWindowHandle -ne 0 -and
        [string]::Equals($_.Path, $exe, [StringComparison]::OrdinalIgnoreCase)
    } |
    Select-Object -First 1

if (-not $process) {
    if (-not (Test-Path -LiteralPath $exe)) { throw "not built: $exe" }
    $process = Start-Process -FilePath $exe -PassThru
    for ($i = 0; $i -lt 60 -and $process.MainWindowHandle -eq 0; $i++) {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
    }
}
if ($process.MainWindowHandle -eq 0) { throw 'no main window' }
Start-Sleep -Milliseconds 3500
$process.Refresh()
if ($process.HasExited -or $process.MainWindowHandle -eq 0) { throw 'launcher exited before automation was ready' }

$automationRoot = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
if (-not $automationRoot) { throw 'automation root unavailable' }
[NavigationMouse]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
[NavigationMouse]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 500
Write-Output "TARGET pid=$($process.Id) hwnd=$($process.MainWindowHandle) title=$($process.MainWindowTitle) responding=$($process.Responding)"
$visibleElements = $automationRoot.FindAll(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
$visibleAutomationIds = $visibleElements |
    ForEach-Object { $_.Current.AutomationId } | Where-Object { $_ }
Write-Output "AUTOMATION root=$($automationRoot.Current.Name) descendants=$($visibleElements.Count)"
Write-Output "AUTOMATION ids=$($visibleAutomationIds -join ',')"

$profileNavigation = $visibleElements |
    Where-Object {
        $_.Current.AutomationId -eq 'ProfilesNavigationItem' -or
        $_.Current.Name -eq 'Profile 与渠道'
    } |
    Select-Object -First 1
if ($profileNavigation) {
    Write-Output 'FAIL profile_navigation present=true'
    throw 'profile navigation must not be present'
}
Write-Output 'PASS profile_navigation present=false'

$navigationIds = @{
    '启动管理' = 'LauncherNavigationItem'
    '奇想手账' = 'LibraryNavigationItem'
    '相册' = 'GalleryNavigationItem'
    '收藏' = 'GalleryFavoritesNavigationItem'
    '心愿共鸣记录' = 'ResonanceNavigationItem'
}
$navigationPoints = @{}
$navigationElements = @{}
foreach ($name in $navigationIds.Keys) {
    $automationId = $navigationIds[$name]
    $element = $visibleElements |
        Where-Object { $_.Current.AutomationId -eq $automationId } |
        Select-Object -First 1
    if (-not $element) { throw "navigation item not found: $automationId" }
    $navigationElements[$name] = $element
    $rectangle = $element.Current.BoundingRectangle
    if ($rectangle.Width -le 0 -or $rectangle.Height -le 0) {
        throw "element is not visible: $name"
    }
    $navigationPoints[$name] = @(
        [int]($rectangle.X + $rectangle.Width / 2),
        [int]($rectangle.Y + $rectangle.Height / 2))
}

function Invoke-Navigation([string]$name) {
    $point = $navigationPoints[$name]
    if (-not $point) { throw "navigation point unavailable: $name" }
    $x = $point[0]
    $y = $point[1]
    [NavigationMouse]::SetCursorPos($x, $y) | Out-Null
    [NavigationMouse]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [NavigationMouse]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    if ($ClickDelayMs -gt 0) { Start-Sleep -Milliseconds $ClickDelayMs }
}

for ($iteration = 0; $iteration -lt $Iterations; $iteration++) {
    foreach ($name in @('奇想手账', '启动管理')) {
        Invoke-Navigation $name
    }
    foreach ($name in @('相册', '收藏', '启动管理')) {
        Invoke-Navigation $name
    }
}

Start-Sleep -Milliseconds $SettleMs
$currentElements = $automationRoot.FindAll(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
$selected = [System.Collections.Generic.List[string]]::new()
foreach ($name in $navigationIds.Keys) {
    $automationId = $navigationIds[$name]
    $element = $currentElements |
        Where-Object { $_.Current.AutomationId -eq $automationId } |
        Select-Object -First 1
    $pattern = $null
    if ($element -and $element.TryGetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern,
            [ref]$pattern) -and $pattern.Current.IsSelected) {
        $selected.Add($name)
    }
}
$failed = 0
$photoPluginElement = $currentElements |
    Where-Object { $_.Current.AutomationId -eq 'PhotoPluginNavigationItem' } |
    Select-Object -First 1
if ($photoPluginElement -and
    -not $photoPluginElement.Current.IsOffscreen -and
    $photoPluginElement.Current.BoundingRectangle.Width -gt 0 -and
    $photoPluginElement.Current.BoundingRectangle.Height -gt 0) {
    Write-Output 'FAIL photo_plugin_navigation visible=true'
    $failed++
}
else {
    Write-Output 'PASS photo_plugin_navigation visible=false'
}

if ($selected.Count -ne 1 -or $selected[0] -ne '启动管理') {
    Write-Output "FAIL selection selected_count=$($selected.Count) selected=$($selected -join ',')"
    $failed++
}
else {
    Write-Output "PASS selection selected_count=1 selected=启动管理"
}

$homeProfileVisible = $false
$hiddenPageCount = 0
foreach ($name in @('奇想手账', '相册', '收藏', '心愿共鸣记录', '启动管理')) {
    Invoke-Navigation $name
    Start-Sleep -Milliseconds 1200
    $pageElements = $automationRoot.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    $profileButton = $pageElements |
        Where-Object { $_.Current.AutomationId -eq 'ProfileButton' } |
        Select-Object -First 1
    $isHome = $name -eq '启动管理'
    $isVisible =
        $profileButton -and
        -not $profileButton.Current.IsOffscreen -and
        $profileButton.Current.BoundingRectangle.Width -gt 0 -and
        $profileButton.Current.BoundingRectangle.Height -gt 0
    if ($isVisible -ne $isHome) {
        Write-Output "FAIL profile icon page=$name visible=$isVisible expected=$isHome"
        $failed++
    }
    else {
        if ($isHome) {
            $homeProfileVisible = $true
        }
        else {
            $hiddenPageCount++
        }
    }
}

Invoke-Navigation '启动管理'
Start-Sleep -Milliseconds 1200
Write-Output "PASS profile_icon home_visible=$homeProfileVisible hidden_pages=$hiddenPageCount/4"
Write-Output "RESULT iterations=$Iterations failed=$failed"
if ($failed -gt 0) { exit 1 }
