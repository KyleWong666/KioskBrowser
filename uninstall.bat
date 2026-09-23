@echo off
chcp 65001 >nul

net session >nul 2>&1
if errorlevel 1 (
    echo [错误] 请以管理员身份运行本脚本
    pause
    exit /b 1
)

schtasks /Delete /TN "KioskBrowserWatchdog" /F >nul 2>&1
echo [OK] 计划任务已删除

taskkill /F /IM KioskBrowser.exe >nul 2>&1
taskkill /F /IM KioskWatchdog.exe >nul 2>&1
echo [OK] 进程已停止

echo 卸载完成（配置文件保留在 %%LocalAppData%%\KioskBrowser，可手动删除）
pause
