@echo off
chcp 65001 >nul
cd /d "%~dp0"

REM 需要管理员权限（注册计划任务 / 控制 TabTip）
net session >nul 2>&1
if errorlevel 1 (
    echo [错误] 请以管理员身份运行本脚本
    pause
    exit /b 1
)

REM 检测 WebView2 Runtime
reg query "HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" /v pv >nul 2>&1
if errorlevel 1 (
    reg query "HKLM\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" /v pv >nul 2>&1
)
if errorlevel 1 (
    echo [错误] 未检测到 WebView2 Runtime，请先安装 Evergreen Runtime：
    echo        https://developer.microsoft.com/microsoft-edge/webview2/
    pause
    exit /b 1
)
echo [OK] WebView2 Runtime 已安装

REM 首次运行生成默认配置
if not exist "%LocalAppData%\KioskBrowser\config.json" (
    if exist "config_default.json" (
        mkdir "%LocalAppData%\KioskBrowser" 2>nul
        copy /y "config_default.json" "%LocalAppData%\KioskBrowser\config.json" >nul
        echo [OK] 已写入默认配置
    )
)

REM 注册开机自启计划任务（登录后延迟 30 秒启动守护进程）
schtasks /Create /TN "KioskBrowserWatchdog" /SC ONLOGON /DELAY 0000:30 /TR "\"%~dp0KioskWatchdog.exe\"" /RL HIGHEST /F >nul
if errorlevel 1 (
    echo [错误] 注册计划任务失败
    pause
    exit /b 1
)
echo [OK] 计划任务 KioskBrowserWatchdog 已注册

REM 立即启动守护进程（它会拉起主程序）
tasklist /FI "IMAGENAME eq KioskWatchdog.exe" 2>nul | find /I "KioskWatchdog.exe" >nul
if errorlevel 1 (
    start "" "%~dp0KioskWatchdog.exe"
    echo [OK] 守护进程已启动
) else (
    echo [OK] 守护进程已在运行
)

echo.
echo 安装完成。首次进入后，请在屏幕左上角连续点击 8 次进入设置面板配置目标 URL。
pause
