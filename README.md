# KioskBrowser — 公共场所 Web 展示浏览器（开源社区版）

[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

基于 WebView2 的 Windows Kiosk 浏览器，用于政务大厅 / 医院 / 展馆等无人值守终端。
全屏锁定、触摸软键盘（含中文拼音输入法与领域词库）、本地离线语音输入、模拟登录，单机永久免费可商用。

> 📺 演示视频：*待补充（30 秒：霸屏启动 → 触摸软键盘 → 语音输入）*

## 功能一览

| 功能 | 说明 |
|---|---|
| 霸屏锁定 | 全屏无边框置顶 + 拦截 WM_CLOSE + 低级键盘钩子（Win/Alt+Tab/F1-F12/Ctrl+W 等），可一键切普通窗口调试 |
| 自动访问 URL | 启动即导航目标页面；导航白名单（域名级，含子域） |
| 系统键盘压制 | TabTip.exe / 触摸键盘进程轮询终止，杜绝系统键盘抢占 |
| 自定义软键盘 | QWERTY/符号页/Shift/退格/回车/Tab，焦点自动弹出，字符 JS 注入不抢焦点 |
| 中文拼音输入法 | 3.6 万字 + 18.5 万词组 + 首字母联想 + jieba 词频排序 + 悬浮候选栏（翻页/滑动） |
| 领域词库管理 | 通用/政务/医疗/商场/工业 多选 + 自定义词库（候选置顶），引擎热加载 |
| 语音输入 | sherpa-onnx 本地离线识别，按住说话，AGC 增益；领域词库自动转识别热词 |
| 模拟登录 | 选择器轮询填充（React/Vue 兼容事件）、成功判定、3 次失败熔断 + Webhook 告警 |
| 凭据安全 | AES-256-GCM 加密，密钥绑定设备硬件指纹（CPU+主板+MachineGuid），配置拷走无法解密 |
| 设置入口 | 左上角 80×80 区域连击 8 次（超时/偏离/次数防误触，可再加 SHA256 密码门） |
| 守护进程 | 独立 KioskWatchdog.exe：心跳监控(3s×3)/内存阈值/崩溃临终日志/重启熔断/Webhook 告警 |
| 可靠性 | 双全局互斥量防多开、config.json 损坏自动 .bak 恢复、按天滚动日志保留 90 天 |

## 快速开始

### 构建

```bash
dotnet build -c Release
# 部署用自包含发布（目标机无需装 .NET，仅需 WebView2 Runtime）：
dotnet publish src/KioskBrowser -c Release -r win-x64 --self-contained true -o publish
dotnet publish src/KioskBrowser.Watchdog -c Release -r win-x64 --self-contained true -o publish
# 把 config_default.json / install.bat / uninstall.bat 拷入 publish/ 即可分发
```

### 部署

1. 以管理员身份运行 `install.bat`：检测 WebView2 → 写默认配置 → 注册计划任务（登录后 30s 启动守护进程）→ 立即启动。
2. 守护进程拉起主程序，全屏进入 Kiosk 模式。
3. **管理员在屏幕左上角 80×80 区域内连续点击 8 次**进入设置面板，配置目标 URL（需要时配登录凭据与选择器）→ 保存并重启。

### 开发调试

`%LocalAppData%\KioskBrowser\config.json` 中：
- `"browser.kioskMode": false` —— 普通窗口模式（右上角出现设置按钮，Ctrl+Alt+S 也可打开）
- `"devMode": true` —— 放行 F12/DevTools

测试页：`test/test-login.html`（任意静态服务器托管，凭据 admin/test123，可验证模拟登录/软键盘/输入法）。

## 项目结构

```
src/
├── KioskBrowser/             # 主程序 (KioskBrowser.exe, WinForms + WebView2)
├── KioskBrowser.Watchdog/    # 守护进程 (KioskWatchdog.exe)
├── KioskBrowser.Abstractions/# 专业版插件契约 (IProHost/IProPlugin) + 公共 UI 工具
└── KioskBrowser.Shared/      # 配置模型 / AES-256-GCM 凭据加密 / 日志 / 管道 IPC
android/                      # Android 版（com.ssdc.kiosk，见上节）
tools/                        # 研发测试工具（输入法引擎/候选栏/语音/基准等离屏验证）
test/                         # 测试页面
docs/                         # 使用说明书 / Android 部署指南
```

## 插件机制（专业版扩展点）

主程序启动时会尝试加载安装目录下的两个可选程序集，不存在则以社区版运行：

| 程序集 | 提供能力 | 缺失时行为 |
|---|---|---|
| `KioskBrowser.Sso.dll` | 票据 SSO 免登引擎（`auth.type=ticket` 配置生效） | SSO 配置自动降级为模拟登录 |
| `KioskBrowser.Pro.dll` | 远程管理客户端（注册/心跳/命令/截屏） | 远程管理不可用，其余功能不受影响 |

插件契约见 `src/KioskBrowser.Abstractions`（`IProHost` / `IProPlugin`），可自行实现同接口的扩展。

## Android 版（`android/`）

安卓一体机版（政务/医院查询机、排队机等 ARM 触摸屏设备），功能与 Windows 社区版对齐，
已在 RK3568 / Android 11 真机全项验证：

- 霸屏（沉浸 + **Device Owner LockTask** 官方 Kiosk 管控）+ 开机自启 + 导航白名单
- **自研系统输入法（IME）**：InputConnection 直写零卡顿，拼音输入法（18.5 万词）+ 领域/自定义词库
- 模拟登录（选择器注入 + 3 次熔断）、语音输入（sherpa-onnx 本地离线，按住说话）
- 左上角 8 连击设置面板（URL/霸屏/键盘模式/词库/模拟登录/语音/退出 Kiosk）

构建与部署（install.bat 一键装机，含默认输入法与 Device Owner 授权）：见 [docs/android-deploy.md](docs/android-deploy.md)。

## 专业版（KioskBrowser Pro）

开源社区版覆盖单终端全部本地能力。以下能力由专业版（商业授权）提供：

- **票据 SSO 免登**：对接平台方单点登录接口（声明式签名模板 + JS 脚本插件，支持 md5/sha1/sha256/hmac-sha1/hmac-sha256/RSA/AES/国密 SM2-SM4），失败自动降级模拟登录
- **远程管理后台**：终端注册/心跳/批量命令（改 URL/换词库/截屏/重启/关机/设置密码）/ 服务端授权激活（付费操作只在服务端）
- **信创适配**：麒麟 V10 ARM64 移植（技术验证已完成）

联系：*待补充*

## 运维速查

| 场景 | 操作 |
|---|---|
| 改配置 | 左上角连击 8 次 → 设置面板 |
| 紧急退出 | 设置面板 →「退出 Kiosk」（同时停止守护进程，确认弹窗） |
| 查日志 | `%LocalAppData%\KioskBrowser\logs\`（kiosk_* / watchdog_*） |
| 配置文件 | `%LocalAppData%\KioskBrowser\config.json`（损坏自动用 .bak 恢复） |
| 卸载 | 管理员运行 `uninstall.bat` |

## 已知限制

- **Ctrl+Alt+Del**：用户态无法拦截，需要时通过组策略禁用安全选项。
- **跨域 iframe**：页面若把内容放在跨域 iframe 中，iframe 内的左上角连击/焦点检测不生效（同源 iframe 正常）。
- Windows 11 的 `TextInputHost.exe` 触摸键盘策略可能与 TabTip 杀手互补，如有残留可加组策略关闭。

## 许可证

[Apache-2.0](LICENSE) © 2026 广州星空数创网络技术有限公司。第三方组件声明见 [NOTICE](NOTICE)。
