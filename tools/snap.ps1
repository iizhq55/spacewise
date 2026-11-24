# Capture the Spacewise window to a PNG (for checking the UI without screen control).
param([string]$Out = "$PSScriptRoot\..\snap.png", [string]$Click = "")
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  public struct RECT { public int L, T, R, B; }
}
"@
[W]::SetProcessDPIAware() | Out-Null
$p = Get-Process -Name Spacewise -ErrorAction Stop | Select-Object -First 1
$h = $p.MainWindowHandle
if ($Click) {
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
  $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Click)
  $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
  if ($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); "clicked $Click" } else { "not found: $Click" }
  Start-Sleep -Milliseconds 400
}
$r = New-Object W+RECT
[W]::GetWindowRect($h, [ref]$r) | Out-Null
$bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc(); [W]::PrintWindow($h, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc)
$bmp.Save((Resolve-Path -LiteralPath (Split-Path $Out) ).Path + "\" + (Split-Path $Out -Leaf), [System.Drawing.Imaging.ImageFormat]::Png)
"saved $Out"
