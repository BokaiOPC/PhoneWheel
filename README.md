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

**本机环境状态（2026-11-12 已完成）**：Apple Mobile Device Service 运行中；ViGEmBus 1.22.0 已安装（服务 Running）；.NET SDK 8.0.428 已安装（用户级，位于 `C:\Users\94787\.dotnet`）；libimobiledevice 工具链（iproxy.exe + 依赖 DLL）已放在 `tools/iproxy/`；插件已通过 `dotnet build -c Release` 编译成功（0 警告 0 错误）。iPhone 伴侣 App 也已在 GitHub Actions 云端编译成功，**横屏新版**未签名包位于 **`dist/PhoneWheel-unsigned.ipa`**（53,307 字节）。**目前只剩把横屏新版 .ipa 重装进 iPhone 与实机联调。**

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
│  ├─ Transport/IproxyLauncher.cs  usbmux 隧道进程管理（自动重拉 + 静默看门狗）
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

托盘图标右键 → "状态窗口" 可查看实时转角、踏板、链路状态。程序启动后**没有主窗口**，只有右下角托盘图标。托盘菜单共四项：**状态窗口 / 校准中位 / 重连 USB 隧道 / 退出**。

> **USB 隧道会自动恢复**：重装手机 App、重新插拔数据线或手机锁屏后，usbmux 会话可能被重枚举，此时 `iproxy` 进程仍存活、端口仍能连上，但设备侧连接已僵死（表现为端口可连、`PWR1` 帧为 0 字节）。插件在连续 4 秒收不到帧时会自动杀掉并重拉 `iproxy`（两次至少间隔 15 秒），`SensorClient` 随后 2s 退避重连；也可以随时点托盘 **「重连 USB 隧道」** 手动触发。
> 2026-11-12 实测：Sideloadly 重装横屏版 App 后，老隧道确实进入这种半死状态；重启插件后 `127.0.0.1:27183` 4 秒内收到 25,935 字节 / 约 399 个 PWR1 帧（100Hz），帧头 `50575231` 正确。

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
3. 直接进游戏即可 —— 左右转角在锁止角内映射（F1 预设 270°，带中位柔化曲线，见下节）。

## 没有 Mac：把 App 装到 iPhone（云端编译已完成）

iOS App 只能在 macOS 上编译，本项目走「**云端编译 + 本地签名**」。**云端部分已经做完**：

- Public 仓库：<https://github.com/BokaiOPC/PhoneWheel>（`main`，Actions 已跑通）
- 成功构建：**横屏新版** <https://github.com/BokaiOPC/PhoneWheel/actions/runs/43147881853>（初版 <.../43135835911> 为竖屏包，已废弃）
- 产物已下载到本机：**`dist/PhoneWheel-unsigned.ipa`**（横屏版，53,307 字节，未签名；旧竖屏包备份在 `dist/PhoneWheel-unsigned-portrait-OLD.ipa.bak`）
- 已验证新包 `Payload/PhoneWheel.app/Info.plist` 含 `UISupportedInterfaceOrientations = [LandscapeLeft, LandscapeRight]`、`UIStatusBarHidden = true`、`NSMotionUsageDescription`；二进制含强制横屏 AppDelegate
- CI runner 用 `macos-15`（macOS 14 镜像已于 2026-11-02 从 GitHub Actions 退役，继续写 `macos-14` 会让任务 0 秒被取消）

> ⚠️ 手机上若已装初版（竖屏）App，**请先用 Sideloadly 重装这个横屏新版**：旧包缺 `NSMotionUsageDescription`，实测 USB 隧道读不到 PWR1 姿态帧。

你只剩两步：

1. Windows 上装 **Sideloadly** 或 **AltServer**（AMDS 已在本机运行）；
2. 数据线连手机 → 用免费 Apple ID 给 `dist/PhoneWheel-unsigned.ipa` 签名安装。

完整逐步截图级说明、Sideloadly / AltStore 两条路线、7 天刷新规则与排障见 **`docs/SIDELOAD.md`**。

> 如果觉得 GitHub 麻烦，**借一台 Mac 用 30 分钟**用 Xcode 真机运行一次其实更省事，之后所有刷新都在这台 Windows 上完成。

## 手感调参（改完立即生效，不用重启）

参数在插件同目录 `appsettings.json`，即 `src/PhoneWheel.Plugin/bin/Release/net8.0-windows/appsettings.json`。**存盘 0.5 秒后热重载**：不重启插件、不重新校准中位；状态窗口实时显示当前参数，日志会打印一条「配置已热重载」。

| 参数 | F1 2017 预设 | 作用 / 怎么调 |
|---|---|---|
| `maxLockDeg` | `270` | 打满方向盘需要手机转过的总角度。F1 真车 lock-to-lock 约 240–270°；**调大更迟钝、调小更灵敏**。回神力科莎 / ACC 改 `540`–`900` |
| `deadzoneDeg` | `2.0` | 中位死区。直线仍轻微摆头 → 加到 `3`–`4`；觉得起步有迟滞 → 降到 `1` |
| `expo` | `0.35` | 中位柔化。`0` = 纯线性；直线发飘 → `0.5`；想要更贼 → `0.15` |
| `sensitivity` | `1.0` | 整体增益，等效缩放满打点 |
| `maxStepDegPerSec` | `1400` | 换向速度上限。快速左右打跟不上 → `1800`；姿态偶发跳变 → `900` |
| `invert` | `false` | 左右反了就改 `true` |

曲线 `curve(x) = (1−e)·x + e·x³`（x 为归一化转角），端点仍是 ±1，**不会损失满打行程**，只压低中位附近增益。实测对照（摇杆打满 = 100%）：

| 手机转角 | 旧 540° 线性 | 新 270° + Expo 0.35 |
|---|---|---|
| 5° | 1.5% | 1.4% |
| 30° | 10.7% | 13.8% |
| 90° | 33.0% | 52.1% |
| 135° | 49.6% | 97.5% |

也就是**直路依旧稳、弯里能真正打满**。旧配置 540° 时人手最多转 90°、摇杆才到 1/3，再叠加 F1 2017 自身的高速转向衰减，体感就是「转不动、推头」。

## 游戏内设置

- **神力科莎 / ACC**：Options → Controls，选手柄（Xbox）预设或手动把转向绑到左摇杆 X；把 `maxLockDeg` 改回 `540`–`900`，游戏内 Steering scale 设 1.0。
- **F1 系列（2016–24）**：默认识别 XInput 手柄；RT/LT 即油门/刹车，RB/LB 升降挡；F1 23/24 建议开启 "Analog Steering"。

### F1 2017 具体设置

1. 主菜单按 **F2**（Game Options）→ **Settings** → **Customise Controls** → **Control Schemes**，选 **Gamepad / Xbox 360** 预设。Windows 版原生支持 XInput，ViGEm 虚拟手柄会被认成标准 Xbox 360 手柄（Feral 手册里「必须切 DirectInput」那条只针对 Mac 移植版，Windows 不用管）。
2. 同页确认或手动绑定：

   | 游戏动作 | 绑到 |
   |---|---|
   | Steering | 左摇杆 X（Left Stick X） |
   | Accelerate / Brake | RT / LT |
   | Gear Up / Gear Down | RB / LB |
   | DRS（建议） | X —— 手机端「视角左」，开车时用不上左右看 |
   | Pause / Menu | Start |

3. 进 **Controller Settings / Calibration**（转向校准页）按表调：

   | 游戏内项 | 建议值 | 原因 |
   |---|---|---|
   | Steering Deadzone | **0**（直线飘才给 3–5） | 插件已有 2° 死区，别叠加 |
   | Steering Saturation | **0** | 只在摇杆打不满 100% 时给 4–6；我们端点就是 ±1 |
   | Steering Linearity | **0–10**（仍发飘给 15–25） | 插件已做 Expo 曲线，游戏里再叠会过度发钝 |
   | Speed Sensitivity / Steering Rate（若该版本有） | 尽量调低 | F1 2017 高速会主动削弱摇杆转向，是推头主因之一 |
   | Steering Lock / Wheel Rotation（若有） | **270** | 与 `maxLockDeg` 对齐 |

4. 关闭 **Steering Assist（转向辅助）**，否则会和手机输入打架；牵引力控制按喜好留中等即可。
5. 先去 **Time Trial / Practice** 跑两圈，别直接开生涯：直线松手看车会不会自己跑偏（跑偏 → 手机重新点「校准中位」），再试 90° 弯能不能一把打满。

## 常见问题

- **双击弹出「You must install .NET Desktop Runtime」**：.NET 8 是用户级安装，改用桌面 `PhoneWheel-Start.bat` 启动；或重新登录 / 重启 Windows 后再双击 exe。
- **双击后完全没反应**：先看 exe 同目录有没有 `crash.log`（新版会记录异常并弹窗）。没有 crash.log 就说明已正常启动 —— 托盘程序本来就没有主窗口，到右下角托盘（可能在 `^` 里）右键 → **状态窗口**。
- **iproxy 启动失败/隧道不通**：确认 iTunes（或 Apple Mobile Device Support）已安装、Apple Mobile Device Service 正在运行；手机已解锁并信任电脑；重插数据线。本机 `iproxy` 新版参数是 `27183:12345`（冒号单参数）。
- **端口能连但读不到任何 PWR1 帧（链路显示离线）**：多半是上述「隧道僵死」，点托盘 **「重连 USB 隧道」** 或重启插件即可；新版也会自动重拉。注意 Sideloadly 每次重装 App 都会触发一次。
- **日志反复「自动重拉 iproxy」却始终无数据**：这次多半不是隧道问题，而是 **手机 App 被 iOS 挂起**（锁屏、切到别的应用、手机放下）。把 PhoneWheel 切回前台并解锁即可；App 内已禁用自动锁屏，但手动锁屏仍会让网络监听停掉。`idevice_id -l` 能看到 UDID 只证明数据线连着，不代表 App 在监听 TCP 12345（独立隧道会报 `Error connecting to device`）。
- **虚拟手柄不被识别**：检查 ViGEmBus 是否安装；以管理员运行一次安装程序；设备管理器中应出现"Virtual Xbox 360 Controller"。
- **手机转了但游戏没反应**：看状态窗口"链路"是否在线、校准是否完成、转向条是否随手机左右变化。CoreMotion 的原始 device-motion 流**不会**弹「运动与健身」授权框，不弹窗是正常的；`NSMotionUsageDescription` 只是 Info.plist 必备键，无需手动去设置里开权限。
- **延迟/抖动**：状态窗口能实时看到 `锁止 / 死区 / Expo / 灵敏度 / 限速`；边跑边改同目录 `appsettings.json`，0.5 秒热生效，不用重启也不用重新校准。100Hz 帧 + 本地回环，端到端通常 12–25ms。

## 说明与边界

- 虚拟手柄**没有力反馈（FFB）**；触觉补偿（路肩/打滑震动）为二期路线图，见 `docs/DESIGN.md` 第 10 节。
- iOS 免费账号侧载的 App 有效期为 7 天，到期需用 AltStore 刷新；长期使用建议开发者账号。
- 本项目为完整设计 + 可编译的电脑端插件 + 完整 iOS 工程源码；协议与算法已定稿，按 README 与 `docs/SIDELOAD.md` 即可联调。
