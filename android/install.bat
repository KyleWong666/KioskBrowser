@echo off
REM KioskBrowser Android 一键装机（用法: install.bat [设备IP:端口]）
REM 默认网络 ADB 5555；USB 直连时把 DEV 改为设备序列号
setlocal
set DEV=%1
if "%DEV%"=="" set DEV=192.168.0.104:5555

echo == 连接设备 %DEV% ==
adb connect %DEV%
adb -s %DEV% wait-for-device

echo == 安装 APK ==
adb -s %DEV% install -r "%~dp0app\build\outputs\apk\release\app-release.apk" || goto :fail

echo == 授权存储 ==
adb -s %DEV% shell pm grant com.ssdc.kiosk android.permission.WRITE_EXTERNAL_STORAGE
adb -s %DEV% shell pm grant com.ssdc.kiosk android.permission.READ_EXTERNAL_STORAGE
adb -s %DEV% shell pm grant com.ssdc.kiosk android.permission.RECORD_AUDIO
adb -s %DEV% shell mkdir -p /sdcard/kiosk
REM 语音模型（可选，160MB，从 PC 推送一次）:
REM   adb -s %DEV% shell mkdir -p /sdcard/kiosk/models/zh2025
REM   adb -s %DEV% push "%LOCALAPPDATA%\KioskBrowser\models\zh2025\" /sdcard/kiosk/models/zh2025/

echo == 设为默认输入法 ==
adb -s %DEV% shell ime enable com.ssdc.kiosk/.ime.KioskImeService
adb -s %DEV% shell ime set com.ssdc.kiosk/.ime.KioskImeService

echo == 授权 Device Owner（正规 Kiosk 管控；设备已有账号时会失败，需先清账号） ==
adb -s %DEV% shell dpm set-device-owner com.ssdc.kiosk/.admin.KioskAdminReceiver

echo == 可选：推配置（把本目录 config.json 推到 /sdcard/kiosk/）==
if exist "%~dp0config.json" adb -s %DEV% push "%~dp0config.json" /sdcard/kiosk/config.json

echo == 启动 ==
adb -s %DEV% shell am start -n com.ssdc.kiosk/.MainActivity
echo 完成。
goto :eof
:fail
echo 安装失败
exit /b 1
