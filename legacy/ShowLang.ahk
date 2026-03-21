#Persistent
#NoTrayIcon
#SingleInstance force
SetTimer, CheckLang, 100

prevLang := ""

CheckLang:
    lang := GetKeyboardLayout()
    if (lang != prevLang) {
        prevLang := lang

        ; --- Prefer showing at text caret (typing cursor) ---
        x := A_CaretX
        y := A_CaretY

        ; Some apps don't expose caret position (A_CaretX/Y can be blank or -1)
        if (x = "" || y = "" || x = -1 || y = -1) {
            MouseGetPos, x, y
        }

        ; Small offset so it doesn't cover the caret
        x += 14
        y -= 23

        ToolTip, %lang%, %x%, %y%
        SetTimer, HideTooltip, -1000
    }
Return

HideTooltip:
    ToolTip
Return

GetKeyboardLayout() {
    threadID := DllCall("GetWindowThreadProcessId", "UInt", WinActive("A"), "UInt", 0)
    layoutID := DllCall("GetKeyboardLayout", "UInt", threadID)
    langID := layoutID & 0xFFFF
    if (langID = 0x041E)
        return "TH"
    else if (langID = 0x0409)
        return "EN"
    else if (langID = 0x0411)
        return "JP"
    else
        return Format("{:X}", langID)
}
