# PhoneWheel — iPhone 有线方向盘插件

让 iPhone 通过 **USB 数据线**连接 Windows 电脑，基于机身运动传感器（陀螺仪 + 重力融合姿态）在
《神力科莎》(Assetto Corsa / Competizione) 与 **F1 系列**游戏中充当方向盘：转向、油门/刹车（屏幕触摸踏板）、换挡与功能键。

游戏侧零改动 —— 插件通过 ViGEmBus 创建"虚拟 Xbox 360 手柄"，上述游戏原生按 XInput 识别。

## 工作原理（一句话）

iPhone 伴侣 App 以 100Hz 采集 CoreMotion 姿态四元数 → 经 usbmux（iproxy 端口转发）通过数据线送达 PC
→ 插件解算出方向盘转角、滤波与校准 → 写入虚拟 Xbox 360 手柄 → 游戏读取为标准手柄输入。

## 前置依赖

| 依赖 | 说明 |
|------|------|
| iTunes 或"Apple Mobile Device Support" | 提供 Apple Mobile Device Service（AMDS，监听 127.0.0.1:27015），usbmux 隧道的基础 |
| ViGEmBus 驱动 | https://github.com/nefarius/ViGEmBus ，安装一次（需管理员），用于创建虚拟手柄 |
| .NET 8 SDK | 构建 PC 插件（发布为 self-contained 可免用户装运行时） |
| iPhone 伴侣 App | `ios/` 完整 SwiftUI 工程（iOS 15+）；有 Mac 用 Xcode 真机运行，没有 Mac 见 `docs/SIDELOAD.md`（云端构建 + AltStore 侧载） |

> 首次连接需在 iPhone 上"信任此电脑"。数据线同时为手机充电，可放心常亮屏使用。

**本机环境状态（2026-11-12 已完成）**：Apple Mobile Device Service 运行中；ViGEmBus 1.22.0 已安装（服务 Running）；.NET SDK 8.0.428 已安装（用户级，位于 `C:\Users\94787\.dotnet`）；libimobiledevice 工具链（iproxy.exe + 依赖 DLL）已放在 `tools/iproxy/`；插件已通过 `dotnet build -c Release` 编译成功（0 警告 0 错误）。iPhone 伴侣 App 也已在 GitHub Actions 云端编译成功，未签名包位于 **`dist/PhoneWheel-unsigned.ipa`**。**目前只剩把 .ipa 签名装进 iPhone 与实机联调。**

## 目录结构

```
PhoneWheel/
├─ README.md                     本文件
├─ docs/
│  ├─ DESIGN.md                  总体设计文档（架构 / 算法 / 延迟 / 测试 / 路线图）
│  ├─ PROTOCOL.md                PWR1 二进制帧协议
│  └─ SIDELOAD.md                没有 Mac 时的侧载完整指南
├─ src/PhoneWheel.Plugin/        Windows 插件（C# .NET 8, WinForms 托盘程序）
│  ├─ Program.cs                 入口
│  ├─ Core/Quat.cs               四元数 / 向量数学
│  ├─ Core/SteeringEngine.cs     姿态→转角解算、滤波、死区、校准、看门狗
│  ├─ Transport/IproxyLauncher.cs  usbmux 隧道进程管理（自动重拉）
│  ├─ Transport/SensorClient.cs   TCP 帧编解码与重连
│  ├─ Virtual/ViGEmBridge.cs      虚拟 Xbox 360 手柄封装
│  └─ App/                       托盘 + 状态窗口
├─ ios/                          iPhone 伴侣 App（完整 SwiftUI + UIKit 工程）
│  ├─ project.yml                XcodeGen 工程定义（云端 CI 用它生成 .xcodeproj）
│  └─ PhoneWheel/                App 入口 / 界面 / 多点触控踏板 / 姿态发送 / 触觉
├─ .github/workflows/            云端 macOS 构建未签名 .ipa 的 Actions
└─ tools/iproxy/                 libimobiledevice Windows 工具链（exe + 全部 DLL）
```

## 构建与运行

```bash
# 1) 构建插件（若提示找不到 dotnet，重开一个终端窗口；或用绝对路径
#    C:\Users\94787\.dotnet\dotnet.exe）
dotnet build src/PhoneWheel.Plugin/PhoneWheel.Plugin.csproj -c Release

# 2) iproxy 已就位：tools/iproxy/ 内已放入 libimobiledevice Windows 预编译版
#    （jrjr/libimobiledevice-windows v20261011，含 iproxy.exe + 全部 DLL，
#      构建时会自动复制到输出目录；另附 idevice_id.exe 供诊断：
#      tools/iproxy/idevice_id.exe -l 可列出已连接设备）

# 3) 运行（先插好数据线并在手机上信任电脑）
src/PhoneWheel.Plugin/bin/Release/net8.0-windows/PhoneWheel.Plugin.exe
```

托盘图标右键 → "状态窗口" 可查看实时转角、踏板、链路状态。程序启动后**没有主窗口**，只有右下角托盘图标。

> **本机启动方式（重要）**：.NET 8 SDK 是**用户级**安装在 `C:\Users\94787\.dotnet`。从资源管理器直接双击 `PhoneWheel.Plugin.exe` 会弹「You must install .NET Desktop Runtime」—— 因为 Explorer 会话没继承 `DOTNET_ROOT`，apphost 找不到 `hostfxr.dll`（Windows 事件日志 `.NET Runtime` 里可见 `Failed to resolve hostfxr.dll`）。
> 请双击桌面上的 **`PhoneWheel-Start.bat`**：它用绝对路径的 `C:\Users\94787\.dotnet\dotnet.exe` 直接加载 `PhoneWheel.Plugin.dll`，绕过 apphost 查找。重新登录 / 重启 Windows 后，直接双击 exe 也可以。
> 启动器内容等价命令：
> ```bat
> cd /d "...\src\PhoneWheel.Plugin\bin\Release\net8.0-windows"
> start "PhoneWheel" /MIN "C:\Users\94787\.dotnet\dotnet.exe" "PhoneWheel.Plugin.dll"
> ```
> 另可发布 self-contained 单文件版本（`dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`）彻底免除本机运行时依赖。

## 校准（每次开玩 10 秒）

1. 手机进入伴侣 App，按驾驶握持姿势拿好（或装上车载支架）；
2. 保持当前姿势，点手机 App 上的 **「校准中位」**（会通知电脑端重置水平中位；也可点电脑托盘菜单同名项）；
3. 直接进游戏即可 —— 左右转角即方向盘锁止角内线性映射（默认 540°，可配置）。

## 没有 Mac：把 App 装到 iPhone（云端编译已完成）

iOS App 只能在 macOS 上编译，本项目走「**云端编译 + 本地签名**」。**云端部分已经做完**：

- Public 仓库：<https://github.com/BokaiOPC/PhoneWheel>（`main`，Actions 已跑通）
- 成功构建：<https://github.com/BokaiOPC/PhoneWheel/actions/runs/43135835911>
- 产物已下载到本机：**`dist/PhoneWheel-unsigned.ipa`**（未签名，49 KB，内含 `Payload/PhoneWheel.app/PhoneWheel`）
- CI runner 用 `macos-15`（macOS 14 镜像已于 2026-11-02 从 GitHub Actions 退役，继续写 `macos-14` 会让任务 0 秒被取消）

你只剩两步：

1. Windows 上装 **Sideloadly** 或 **AltServer**（AMDS 已在本机运行）；
2. 数据线连手机 → 用免费 Apple ID 给 `dist/PhoneWheel-unsigned.ipa` 签名安装。

完整逐步截图级说明、Sideloadly / AltStore 两条路线、7 天刷新规则与排障见 **`docs/SIDELOAD.md`**。

> 如果觉得 GitHub 麻烦，**借一台 Mac 用 30 分钟**用 Xcode 真机运行一次其实更省事，之后所有刷新都在这台 Windows 上完成。

## 游戏内设置

- **神力科莎 / ACC**：Options → Controls，选手柄（Xbox）预设或手动把转向绑到左摇杆 X；在转向设置里调 Steering scale 与最大转角。
- **F1 系列（2016–24）**：默认识别 XInput 手柄；F1 23/24 建议开启 "Analog Steering"；RT/LT 即油门/刹车。

## 常见问题

- **双击弹出「You must install .NET Desktop Runtime」**：.NET 8 是用户级安装，改用桌面 `PhoneWheel-Start.bat` 启动；或重新登录 / 重启 Windows 后再双击 exe。
- **双击后完全没反应**：先看 exe 同目录有没有 `crash.log`（新版会记录异常并弹窗）。没有 crash.log 就说明已正常启动 —— 托盘程序本来就没有主窗口，到右下角托盘（可能在 `^` 里）右键 → **状态窗口**。
- **iproxy 启动失败/隧道不通**：确认 iTunes（或 Apple Mobile Device Support）已安装、Apple Mobile Device Service 正在运行；手机已解锁并信任电脑；重插数据线。本机 `iproxy` 新版参数是 `27183:12345`（冒号单参数）。
- **虚拟手柄不被识别**：检查 ViGEmBus 是否安装；以管理员运行一次安装程序；设备管理器中应出现"Virtual Xbox 360 Controller"。
- **没反应**：看状态窗口"链路"是否在线、校准是否完成；iPhone 需允许"动作与健身"权限（NSMotionUsageDescription）。
- **延迟/抖动**：调 `appsettings.json` 中 `steering.deadzoneDeg` / `sensitivity`；100Hz 帧 + 本地回环，端到端通常 12–25ms。

## 说明与边界

- 虚拟手柄**没有力反馈（FFB）**；触觉补偿（路肩/打滑震动）为二期路线图，见 `docs/DESIGN.md` 第 10 节。
- iOS 免费账号侧载的 App 有效期为 7 天，到期需用 AltStore 刷新；长期使用建议开发者账号。
- 本项目为完整设计 + 可编译的电脑端插件 + 完整 iOS 工程源码；协议与算法已定稿，按 README 与 `docs/SIDELOAD.md` 即可联调。
