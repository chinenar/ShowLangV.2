@echo on
echo Running AHK ShowLang script...
start "" "C:\My Script\ShowLang.ahk"

set result=%errorlevel%
if not %result%==0 (
    msg * "เกิดข้อผิดพลาดตอนเปิด ShowLang.ahk จ้า\n"
)
echo Done.

:: นับถอยหลัง
echo close terminal
timeout /t 2 >nul
exit