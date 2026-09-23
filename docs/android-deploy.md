# KioskBrowser Android 版部署指南

适用：Android 7+（RK3568 等安卓一体机，已验证开发环境 Android 11）。

## 构建

```bash
# 需要 JDK 11（AGP 4.0.1 与老环境对齐，系统 Java 17 会炸 Groovy）：
cd android
# 首次：local.properties 写入本机 SDK 路径（注意 .properties 转义，两个反斜杠）：
#   sdk.dir=D:\\AndroidSDK
# 首次生成签名 keystore（不入库）：
mkdir keystore
keytool -genkeypair -keystore keystore/kiosk.jks -alias kiosk -keyalg RSA \
  -keysize 2048 -validity 10950 -storepass kiosk123 -keypass kiosk123 -dname "CN=KioskBrowser"
JAVA_HOME=/path/to/jdk-11 ./gradlew assembleRelease
# 产物: app/build/outputs/apk/release/app-release.apk
```

## 部署到设备

```bash
adb connect <设备IP>:5555          # 或 USB 直连
adb -s <设备> install -r app/build/outputs/apk/release/app-release.apk

# 授权存储权限（或首启弹窗授权）
adb -s <设备> shell pm grant com.ssdc.kiosk android.permission.WRITE_EXTERNAL_STORAGE

# 写配置（可提前推送，首启也会自动生成默认配置）
adb -s <设备> shell mkdir -p /sdcard/kiosk
adb -s <设备> push config.json /sdcard/kiosk/config.json
```

## 开启 Device Owner（正规 Kiosk 管控）

前置：设备**没有登录任何账号**（设置→账户里清空），有 root 更省事。

```bash
adb -s <设备> shell dpm set-device-owner com.ssdc.kiosk/.admin.KioskAdminReceiver
```

授权后下次启动自动进入 **LockTask**：禁用状态栏/多任务/Home/通知栏。
同时建议把 KioskBrowser 设为默认桌面（它声明了 HOME category，按 Home 会询问）。

解除管控（调试用）：
```bash
adb -s <设备> shell dpm remove-active-admin --user current com.ssdc.kiosk/.admin.KioskAdminReceiver
```

## 配置（/sdcard/kiosk/config.json）

与 Windows 版同模型：

```jsonc
{
  "version": "1.0",
  "browser": {
    "homeUrl": "http://192.168.0.108:19800",
    "kioskMode": true,               // false = 普通窗口调试
    "navigationWhitelist": []        // 域名白名单，空=不限
  },
  "settingsEntry": { "enabled": true, "zoneSize": 80, "tapCount": 8 },
  "devMode": false                   // true = 放行 chrome://inspect 调试
}
```

## 运维

| 场景 | 操作 |
|---|---|
| 进设置 | 屏幕左上角 80×80 连击 8 次（Phase2 出面板，当前为 Toast 占位） |
| 退出 LockTask | adb 解除管控（见上） |
| 清 WebView 登录态 | `adb shell pm clear com.ssdc.kiosk`（会连配置数据一起清，谨慎） |
| 查日志 | `adb logcat -s KioskBrowser:I` |

## 已知事项

- 已默认允许明文 HTTP（`usesCleartextTraffic=true`，内网平台必需；Android 9+ 默认禁止）。
- 如设备状态栏/导航栏有厂商残留，可再补一刀（root）：`settings put global policy_control immersive.full=com.ssdc.kiosk`
- 电池优化白名单：长期运行设备请在 设置→电池 里把 KioskBrowser 设为不优化。
- WebView 内核版本随设备系统/厂商 ROM，建议保持 Android System WebView 更新。
- 一期范围：霸屏骨架已完成（沉浸+LockTask+自启+白名单+连击入口）；
  软键盘/拼音输入法（Phase2）、模拟登录（Phase3，移植 ids WebAutoLogin）、语音输入（Phase4，sherpa-onnx AAR）迭代中。
