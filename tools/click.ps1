# Click (UI Automation "Invoke") the first Spacewise button whose name contains the given text. For testing only.
param([Parameter(Mandatory)][string]$Text)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$p = Get-Process -Name Spacewise -ErrorAction Stop | Select-Object -First 1
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
$buttons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
foreach ($b in $buttons) {
  if ($b.Current.Name -like "*$Text*") { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); "clicked: $($b.Current.Name)"; Start-Sleep -Milliseconds 400; return }
}
"not found: $Text"
