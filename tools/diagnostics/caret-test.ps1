Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class LayoutTestNative {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    public static extern IntPtr LoadKeyboardLayout(string id, uint flags);
    [DllImport("user32.dll")]
    public static extern IntPtr ActivateKeyboardLayout(IntPtr layout, uint flags);
}
'@

$form = New-Object System.Windows.Forms.Form
$form.Text = 'ShowLang automated caret test'
$form.StartPosition = 'CenterScreen'
$form.TopMost = $true
$form.Width = 620
$form.Height = 180
$text = New-Object System.Windows.Forms.TextBox
$text.Multiline = $true
$text.Font = New-Object System.Drawing.Font('Segoe UI', 16)
$text.Text = 'Caret test | เคอร์เซอร์ทดสอบ'
$text.Select($text.TextLength, 0)
$text.Dock = 'Fill'
$form.Controls.Add($text)

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 700
$step = 0
$timer.Add_Tick({
    $script:step++
    switch ($script:step) {
        1 {
            $form.Activate()
            $text.Focus()
        }
        2 {
            $thai = [LayoutTestNative]::LoadKeyboardLayout('0000041E', 1)
            [void][LayoutTestNative]::ActivateKeyboardLayout($thai, 0)
        }
        3 {
            $english = [LayoutTestNative]::LoadKeyboardLayout('00000409', 1)
            [void][LayoutTestNative]::ActivateKeyboardLayout($english, 0)
        }
        5 {
            $timer.Stop()
            $form.Close()
        }
    }
})
$form.Add_Shown({
    $text.Focus()
    $timer.Start()
})
[System.Windows.Forms.Application]::Run($form)
