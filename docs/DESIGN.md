# PhoneWheel — iPhone 有线方向盘插件 · 总体设计文档

> 目标：iPhone 通过 USB 数据线连接 Windows 电脑，在《神力科莎》(Assetto Corsa / Competizione) 与 F1 系列（Codemasters / EA，2016–24）游戏中充当方向盘：转向 + 油门/刹车 + 换挡/功能键。
> 本文是设计定稿；配套代码骨架见 `src/PhoneWheel.Plugin/`，帧协议见 `docs/PROTOCOL.md`。

---

## 1. 结论先行（关键工程决策）

1. **"重力感应"必须升级为 IMU 姿态融合。**
   绕重力轴（竖直轴）的旋转不改变重力矢量——纯加速度计/重力感应**测不出**方向盘转角。必须使用 CoreMotion 的 `deviceMotion` 融合姿态（陀螺仪积分 + 重力方向约束 + 磁力计校正）。对外仍可称"重力感应方向盘"，实现上是"基于重力感应的姿态解算"。
2. **浏览器方案在纯数据线场景不可行。**
   iOS 不允许 Safari 监听本地 TCP 端口，DeviceOrientation + WebSocket 只能走 WiFi。**纯有线必须有一个 iPhone 伴侣 App**（Swift，可 AltStore 侧载，无需上架 App Store）。
3. **有线通道走 usbmux，而非自研 USB 驱动。**
   iTunes 的 Apple Mobile Device Service（AMDS）监听 `127.0.0.1:27015`；用 libimobiledevice 的 `iproxy` 把本地 TCP 端口转发到 iPhone App 的监听端口。插件作为普通 TCP 客户端连接 `127.0.0.1:<本地端口>`——免逆向、免额外驱动、自动随设备插拔重连。
4. **游戏侧零改动：ViGEmBus 虚拟 Xbox 360 手柄。**
   AC / ACC / F1 系列原生支持 XInput，左摇杆 X = 转向、RT/LT = 油门/刹车、肩键/ABXY = 功能键。无需游戏 MOD、无需 Steam Input 中转。
5. **没有真实力反馈。**
   虚拟设备无法向游戏输出力矩；用 iPhone 线性马达（CoreHaptics）做触觉补偿（路肩/打滑/油门震动，二期，数据源可选 AC 共享内存或 F1 UDP 遥测）。

---

## 2. 系统总体架构

```
┌─────────────────────────── iPhone ───────────────────────────┐
│  PhoneWheel.iOS（Swift 伴侣 App）                              │
│   · CoreMotion deviceMotion @100Hz → 四元数 / 重力 / 角速度      │
│   · 触摸踏板（油门/刹车，支持同时按=循迹刹车）                   │
│   · 功能按钮（升/降挡、手刹、视角、重置车辆）                   │
│   · 校准交互（记录中位 / 最大锁止角）                          │
│   · CoreHaptics 触觉反馈（接收 PC 指令）                       │
│   · NWListener TCP :12345                                     │
└───────────────┬───────────────────────────────────────────────┘
                │ USB 数据线（同时充电）
      usbmux / Apple Mobile Device Service（127.0.0.1:27015）
                │  iproxy 本地端口转发：127.0.0.1:27183 ↔ iPhone:12345
┌───────────────┴──────── Windows · 电脑端插件 ─────────────────┐
│  Transport   IproxyLauncher（隧道进程监管）+ SensorClient      │
│              （帧编解码、断线重连、看门狗源）                   │
│  Core        SteeringEngine（四元数→转角、滤波、死区、校准）     │
│  InputMapper 轴/扳机/按钮映射                                  │
│  Virtual     ViGEmBridge → 虚拟 Xbox 360（XInput）             │
│  UI          托盘 + 状态窗口（转角条/踏板条/链路/日志）          │
└───────────────┬───────────────────────────────────────────────┘
                │ XInput 报告 @100Hz
        神力科莎 / ACC / F1 系列游戏
```

备用链路（同一编解码，便于无线上手联调）：WiFi UDP 广播/单播模式，配置 `transport.mode = "wifi"`。

---

## 3. iPhone 伴侣 App 设计（PhoneWheel.iOS）

### 3.1 职责

- 采集运动姿态并以 100Hz 推送二进制帧（协议见 `docs/PROTOCOL.md`）；
- 提供触摸踏板与功能按钮输入；
- 提供校准交互与握持姿态引导；
- 接收 PC 端触觉指令并播放振动。

### 3.2 采样与参考系

| 项 | 取值 | 理由 |
|----|------|------|
| API | `CMMotionManager.startDeviceMotionUpdates` | 融合加速度计+陀螺仪+磁力计，输出姿态四元数 |
| 频率 | `deviceMotionUpdateInterval = 0.01`（100Hz） | iPhone 支持上限；兼顾延迟与抖动 |
| 参考系 | `.xArbitraryZVertical` | Z 轴固定为重力反向，**不依赖磁力计**，抗车内磁场/金属干扰 |
| 姿态语义 | `attitude.quaternion`：设备坐标系 → 参考坐标系 | 与 PC 端解算约定一致 |

`NSMotionUsageDescription` 需在 Info.plist 声明（Xcode 15+ 自动生成 Info.plist 会覆盖手写的键，须设 `GENERATE_INFOPLIST_FILE=NO` + `INFOPLIST_FILE`）；App 需前台运行；`UIApplication.shared.isIdleTimerDisabled = true` 常亮屏（有线充电兜底）。原始 `deviceMotion` 流**不会**弹「运动与健身」授权框，不弹窗属正常；该键只是启动 CoreMotion 的必备声明。

### 3.3 交互

- **触摸踏板**：屏幕底部左右角触摸区，按下位置映射 0–255；支持双指同时按（循迹刹车）；有触觉确认。
- **功能按钮**：右拨片=升挡、左拨片=降挡、A=手刹、X/B=视角左右、Y=重置车辆、Start/Back=菜单/返回。
- **校准**：界面「校准中位」按钮向 PC 发送 `flags=0x10` 空载荷帧，PC 端用最近一帧姿态重置水平中位；引导界面提示"按驾驶握持姿势持机"。
- **锁止角**：以 PC 端 `steering.maxLockDeg` 为准（当前 roll 预设 **180°**，yaw/转盘握法用 `540`–`900`）；App 侧只发送中位校准请求，不改锁止角。

### 3.4 触觉反馈（二期）

PC 端下发指令帧（`flags=0x01`）：短震 / 持续震动（强度+时长）/ 停止。数据源：AC 共享内存 `acpmf_physics`（路肩/打滑）或 F1 系列 UDP 遥测（端口 20777）。

### 3.5 可靠性与保活

- 帧带 `seq`，客户端以最新为准、不重传（实时流语义）；
- 500ms 无运动帧则补发 Keepalive；
- 断线由 PC 端监管（见 4.3），App 侧只负责发送。

---

## 4. 传输层设计

### 4.1 usbmux / iproxy 隧道

- 进程模型：插件启动时拉起 `tools/iproxy/iproxy.exe <本地端口>:<设备端口>`（默认 `27183:12345`）。注意本机所用 `jrjr/libimobiledevice-windows` 新版为**冒号单参数**格式，旧版才是两个空格分隔数字，传错会直接打印 usage 退出；
- 依赖 AMDS（`127.0.0.1:27015`），iTunes 安装后自带；
- 监管策略：探测本地端口可连即视为隧道就绪；进程退出（设备拔出/AMDS 重启）按 0.5s→5s 退避重拉；稳定运行 > 30s 后重置退避计数，使下一次重拉立即发生；
- **静默看门狗（关键）**：重装 iPhone App、Sideloadly 重签名、数据线重插或手机锁屏会让 usbmux 会话重枚举，此时 `iproxy` 进程**仍存活、本地端口仍可 connect**，但设备侧连接已僵死，`SensorClient` 的 `ReadAsync` 会一直阻塞而不触发 TCP 重连。因此 `TrayApplicationContext.RecoverStaleTunnel()` 覆盖两种卡死：
  - **A. 曾有帧后静默**：连续 4s 无新帧即调用 `IproxyLauncher.RequestRestart()` 杀掉进程触发重拉（两次间隔 ≥ 15s；连续失败 ≥3 次降频到 30s，避免锁屏时刷屏）；
  - **B. 冷启动零帧**：插件启动时手机 `NWListener` 尚未就绪，`SensorClient` 连上**半开连接**、从未收到任何帧，`ReadAsync` 永久阻塞，也不会触发 TCP 重连。旧版只在「曾收到过帧」时才判定，这种场景会永久卡死、手机一直显示「等待电脑」。新版在启动 10s 仍零帧时同样重拉隧道，重拉会打断半开连接进而触发 `SensorClient` 的 2s 退避重连；
  - 托盘菜单同时提供 **「重连 USB 隧道」** 手动入口。
- **单实例保护（`Program.cs`）**：命名 `Mutex(@"Local\PhoneWheel.Plugin.SingleInstance")`。重复双击启动器会产生两个 `dotnet` + 两个 `iproxy` 抢同一个 27183 与同一条 USB 隧道，表现正是「手机一直等待电脑 / 链路时通时断」。第二实例弹窗提示后直接退出。
- 实测（2026-11-12）：Sideloadly 重装横屏版 App 后旧隧道进入半死状态（4s 读 0 字节）；重启插件后 `127.0.0.1:27183` 4s 收到 25,935B / 约 399 帧，帧头 `50575231`。修复冷启动看门狗后，4s / 19s 两次采样均为 19,5xxB ≈ 300 帧（100Hz）。因此「App 重开无需重启插件」仅在 usbmux 会话未重枚举时成立，不能作为通用假设。

### 4.2 帧协议（PWR1）

小端二进制；头部 20 字节 + 载荷。手机→PC 为运动帧（45B 载荷，100Hz ≈ 6.5KB/s），PC→手机为触觉指令帧。字段表、按钮位定义、错误码、握手与心跳见 `docs/PROTOCOL.md`。

### 4.3 可靠性与看门狗

| 机制 | 参数 | 行为 |
|------|------|------|
| TCP 重连 | 失败 2s 退避 | 自动恢复 |
| 帧看门狗 | 200ms 无任何帧 | 判定离线：**方向盘回中 + 松油门/刹车 + 清按钮**，托盘提示 |
| 进程监管 | iproxy 退出即重拉 | 插拔数据线 5s 内恢复 |
| 隧道静默看门狗 | 曾有帧 + 连续 4s 无帧，间隔 ≥ 15s（≥3 次后 30s） | 主动杀掉 iproxy 触发重拉，修复「进程活着但设备侧已死」 |
| 冷启动零帧看门狗 | 启动 10s 仍一帧未收 | 重拉 iproxy 打断半开连接，修复手机「等待电脑」永久卡死 |
| 单实例保护 | 命名 Mutex | 阻止多份插件抢 27183 与同一条 USB 隧道 |
| 版本协商 | Hello 帧 | protoVersion 不匹配 → 错误码 1 并关闭连接 |

> 安全设计：看门狗触发即"熄火"，避免手机断开后游戏卡油门——这是方向盘外设的底线行为。

---

## 5. Windows 插件设计（电脑端主体）

### 5.1 模块划分

| 模块 | 文件 | 职责 |
|------|------|------|
| Transport | `Transport/IproxyLauncher.cs` | iproxy 进程拉起、端口探测、退避重拉 |
| Transport | `Transport/SensorClient.cs` | TCP 连接、帧头校验、载荷解码、事件回调 |
| Core | `Core/Quat.cs` | 四元数/向量运算（乘、共轭、旋转、水平投影） |
| Core | `Core/SteeringEngine.cs` | 姿态→转角解算、自适应滤波、死区、归一化、校准、看门狗 |
| Virtual | `Virtual/ViGEmBridge.cs` | 虚拟 Xbox 360 创建/提交/释放 |
| UI | `App/TrayApplicationContext.cs`、`App/StatusForm.cs` | 托盘菜单、状态窗口、10ms 轮询提交 |
| Config | `appsettings.json` | 锁止角、死区、Expo、灵敏度、换向限速、反向、前向轴、看门狗、端口；支持热重载 |

主循环：8–10ms 定时器 → `SteeringEngine.TickWatchdog()` → `ViGEmBridge.Submit(轴, 油门, 刹车, 按钮)` → 刷新状态窗口。

### 5.2 姿态解算：两种互补的动作模型

**这是最容易踩的坑**：「绕世界竖直轴转（yaw）」和「像方向盘一样绕视线轴滚转（roll）」是两个完全不同的物理动作，用错算法会表现为「转 90° 才有反应 / 只有一边能转 / 极不灵敏」。由 `steering.mode` 选择，状态窗口显示信号质量并提示握法。

**roll（默认，方向盘握法）** — 手机竖握/斜握、屏幕朝向自己：

```
g  = CMDeviceMotion.gravity（设备系重力向量，单位 g）
a  = atan2(g.x, g.y)                 重力在屏幕平面(XY)内的方向角
θ  = -wrap180( a - a₀ )              a₀ = 校准中位时的 a；负号使右打为正
```

绕设备 +Z（屏幕法线/视线轴）滚转时 `a` 线性变化，±180° 满量程、左右天然对称。可靠性由 `|g.xy|` 判定：竖握 ≈1，平放（g≈(0,0,−1)）≈0 → 此时不可靠，保持上一帧并提示用户把手机竖起来。

**yaw（平放转盘握法）** — 手机平放、像转盘一样绕竖直轴转：

```
f₀ = 校准中位时手机"12 点方向"轴在参考系水平面的单位向量
f  = Horizontal( q ⊗ a_fwd ⊗ q* )    q：姿态四元数（设备系→参考系）
                                      a_fwd：配置前向轴（默认 +Y）
                                      Horizontal()：去掉参考系 Z 分量后归一化
θ  = atan2( (f × f₀)·ẑ , f·f₀ )    绕世界上轴
```

可靠性由 `|f.xy|` 判定：平放 ≈1，竖握 ≈0。两种模式的适用条件正好互补。

**实测教训（2026-11-12）**：用户竖握手机转方向盘，旧版只有 yaw 算法。数值模拟显示 `|f.xy| = |sin θ|`，±30° 内 < 0.5 被直接丢弃、要转到 ±90° 才满，中位附近投影由噪声主导容易只剩单边 —— 精确复现了「非常不灵敏 / 只有右转没有左转 / 要完全旋转 90° 才有反应」。改用 roll 后 −90°..+90° 全程线性对称。横屏物理朝向可能让符号相反，用 `invert` 校正（状态窗口看「当前角 / 轴值」符号即可确认）。

### 5.3 滤波与手感

- **自适应 EMA**：阈值按半锁止角的比例划分（`<5%` 用 α=0.30，`5–15%` 用 α=0.60，`>15%` 用 α=1.0）。早期用固定 25°/8°，把锁止角从 540 调到 270 后阈值相对变大、小动作会明显变钝，故改为比例制；
- **slew 限幅**：默认 1400°/s（`@100Hz ≈ 14°/帧`，由 `maxStepDegPerSec` 配置），抑制姿态解算异常跳变；
- **死区**：默认 ±2°，消除手部微抖与支架旷量；
- **中位柔化曲线**：`x = clamp(θ / (maxLock/2) × sensitivity, ±1)`，`axis = [(1−e)·x + e·x³] × 32767`，`e` 默认 0.35。端点仍为 ±1，**不损失满打行程**，只压低中位附近增益。手机陀螺仪抖动比实体摇杆大，纯线性在 F1 2017 直路上会摆头；
- **热重载**：`TrayApplicationContext` 每 500ms 比较 `appsettings.json` 的 `LastWriteTimeUtc`，变化即调用 `SteeringEngine.ApplyConfig()` 原地替换标量参数（并夹到合法范围，防止 0 锁止角除零），**不重置中位校准、不打断链路**；状态窗口显示当前生效参数，配置标签做了文本去抖避免 100Hz 重绘闪烁。

F1 2017 调参过程中的线性→曲线对比（270° / 2° / expo 0.35），摇杆打满 = 100%：

| 手机转角 | 5° | 30° | 90° | 135° |
|---|---|---|---|---|
| 旧 540° 线性 | 1.5% | 10.7% | 33.0% | 49.6% |
| 新 270° + Expo 0.35 | 1.4% | 13.8% | 52.1% | 97.5% |

小角度几乎不变（直路依旧稳），大角度能真正打满（弯里够用）。旧配置 540° 下人手最多转 90°、摇杆仅到 1/3，叠加 F1 2017 自身的高速转向衰减，体感即「转不动、推头」。

> **当前实际预设是 roll 模式 180°**（见 5.7）：上表是 540°→270° 的调参对比，用来说明 Expo 曲线「不损失满打行程、只压低中位增益」的性质。roll 模式下 `maxLockDeg=180` 意味着手腕转 ±90° 即打满，游戏内若有 Steering Lock / Wheel Rotation 也应设为 **180** 与之对齐。

### 5.4 输入映射

| 手机输入 | 虚拟 Xbox 360 | 游戏默认用途 |
|----------|---------------|--------------|
| 方向盘角 θ | 左摇杆 X（-32768..32767） | 转向轴 |
| 右下触摸区 | 右扳机 RT（0..255） | 油门 |
| 左下触摸区 | 左扳机 LT（0..255） | 刹车 |
| 右拨片 | RB | 升挡 |
| 左拨片 | LB | 降挡 |
| A / X / B / Y | A / X / B / Y | 手刹 / 视角左 / 视角右 / 重置车辆 |
| Start / Back | Start / Back | 菜单 / 返回 |

映射表可在 InputMapper 层扩展（如线性踏板曲线、转向灵敏度曲线）。

### 5.5 虚拟手柄（ViGEmBridge）

- NuGet `Nefarius.ViGEm.Client` **1.21.256**（该包最终版本，无 3.x）：`ViGEmClient` → `CreateXbox360Controller()` → `Connect()`；每帧用 `SetAxisValue(Xbox360Axis.LeftThumbX, …)` / `SetSliderValue(Xbox360Slider.*, …)` / `SetButtonState(Xbox360Button.*, …)` 写入，再 `SubmitReport()`（100Hz）。
- API 坑：`Xbox360Axis / Xbox360Slider / Xbox360Button` 是带静态字段的强类型类（**不是枚举**），位于 `Nefarius.ViGEm.Client.Targets.Xbox360`；`IXbox360Controller` 在 `.Targets` 命名空间；`Connect/SubmitReport/AutoSubmitReport` 继承自 `IVirtualGamepad`；此版本没有 `IViGEmClient`。`AutoSubmitReport = false`，避免每个值写入都触发一次提交。
- 驱动缺失时捕获异常并提示安装 ViGEmBus；
- 离线/退出时 `Release()`：回中、清扳机，再断开。

### 5.6 托盘与状态窗口

- 托盘图标：菜单 = 状态窗口 / 校准中位 / 退出；
- 状态窗口：转向条（-100..100）、油门/刹车条、链路与校准状态、当前角度、日志框；
- 可选全局热键校准（Ctrl+Alt+C）。

### 5.7 配置（appsettings.json）

以下是当前随插件分发的 **roll / F1 预设**（`mode` 与 `maxLockDeg` 由 5.2 节的动作模型决定；`vigemEnabled` 是顶层布尔，不是嵌套对象）：

```json
{
  "transport": {
    "mode": "usb",
    "localPort": 27183,
    "devicePort": 12345
  },
  "steering": {
    "mode": "roll",
    "maxLockDeg": 180,
    "deadzoneDeg": 2.0,
    "sensitivity": 1.0,
    "invert": false,
    "forwardAxis": "0,1,0",
    "expo": 0.35,
    "maxStepDegPerSec": 1400
  },
  "vigemEnabled": true
}
```

`mode=yaw` 时把 `maxLockDeg` 调回 `540`–`900`（平放转盘握法），其余参数不变。改完存盘 0.5s 内热重载生效，不重启、不重置中位校准（见 5.3）。

---

## 6. 游戏适配

### 6.1 神力科莎 / Assetto Corsa Competizione

- 直接识别虚拟 XInput 手柄；Options → Controls 选择 Xbox 预设，或在转向设置里手动绑定左摇杆 X；
- 转向设置内调 `Steering scale` 与最大转角：改用 `steering.mode="yaw"` 后把 `maxLockDeg` 调回 `540`–`900` 并让游戏内最大转角与之对齐；若沿用默认 `roll` 预设（180°），游戏内保持 180 即可；
- Content Manager 可进一步微调死区与线性度。

### 6.2 F1 系列（2016–24）

- 2016 起原生支持 XInput；F1 23/24 有 "Analog Steering" 选项，建议开启获得渐进转向；
- RT/LT 默认油门/刹车；LB/RB 可绑换挡。

### 6.3 与真实方向盘的差距

- 游戏不会把虚拟手柄列入"方向盘"设备，方向盘专属 UI/FFB 不可用；
- 虚拟手柄摇杆分辨率 16bit，远超需求；
- 补偿：二期 CoreHaptics 触觉反馈（路肩/打滑/油门震感）。

---

## 7. 延迟预算

| 环节 | 典型耗时 |
|------|----------|
| CoreMotion 采样间隔（100Hz） | 0–10 ms |
| App 编码 + 发送 | < 1 ms |
| iproxy / TCP 本地回环 | 1–2 ms |
| 插件解码 + 滤波 + 提交 | < 1 ms |
| 游戏 XInput 轮询 | 1–16 ms |
| **端到端** | **≈ 12–25 ms** |

与入门级力反馈方向盘（8–15ms）同档；本地回环 + 100Hz 是关键。USB 有线模式相比 WiFi（20–60ms 抖动）是本方案的核心收益。

---

## 8. 部署与依赖

**用户侧（Windows）**

- .NET 运行时（或发布 self-contained）；
- ViGEmBus 驱动（安装一次，需管理员）；
- iTunes 或独立 "Apple Mobile Device Support"（提供 AMDS）；
- Apple 设备已配对并信任本机；
- `iproxy.exe` 随插件分发（libimobiledevice Windows 构建）。

**用户侧（iPhone）**

- 伴侣 App：Xcode 真机部署 / AltStore 侧载 / 开发者账号；
- iOS 15+；`NSMotionUsageDescription` 已声明（原始 deviceMotion 流无需用户授权，不会弹权限框）；iOS 16+ 侧载还需开启「开发者模式」并信任开发者证书。

**运行权限**：插件本体免管理员；仅驱动安装需要。

---

## 9. 测试计划

| 层 | 用例 |
|----|------|
| 单元 | Quat 运算（已知旋转→期望向量）；SteeringEngine（给定四元数序列→期望轴值）；协议编解码往返 |
| 回放 | 录制手机帧 → 注入 SensorClient → 断言虚拟手柄输出序列 |
| 实机 | AC 练习赛：转向/踏板/换挡/视角；F1 24 计时赛 |
| 稳定性 | 插拔数据线 50 次无崩溃，隧道恢复 < 5s；看门狗熄火正确 |
| 延迟 | 手机帧内打点 vs PC 端 XInput 提交时刻，统计 P50/P95 |

---

## 10. 路线图

| 里程碑 | 内容 |
|--------|------|
| **M1（MVP）** | 转向 + 触摸踏板 + 校准 + 托盘 + 虚拟手柄（本文代码骨架即此范围） |
| M2 | 功能按钮/拨片、状态窗口完善、WiFi UDP 备用模式、配置文件热重载 |
| M3 | 触觉反馈：AC 共享内存 / F1 UDP 遥测驱动 CoreHaptics |
| M4 | 拟真力反馈（遥测→振动强度/自动回中辅助）、多设备、Android 复用（SensorManager + 同协议） |

---

## 11. 风险与限制

1. **iOS 侧载有效期**：免费 Apple ID 7 天需刷新（AltStore）；长期使用建议开发者账号（$99/年）或 TestFlight。
2. **无 FFB**：虚拟设备物理上无法输出力矩，只能触觉补偿（见 6.3）。
3. **磁干扰**：车内金属/扬声器干扰磁力计——参考系选 `.xArbitraryZVertical` 已规避；陀螺仪零偏缓慢漂移由重力方向持续约束。
4. **XInput 与真方向盘的差距**：游戏内方向盘专属选项不可用；功能无差，体验有差。
5. **误触**：触摸踏板需防误触（触摸锁定、区域可调）。
6. **发热与耗电**：常亮屏 + 100Hz；有线充电兜底，建议原装线（数据线质量影响 usbmux 稳定性）。
7. **反作弊**：赛车游戏基本无内核级反作弊，但线上比赛使用外设级虚拟手柄请自行确认赛事规则。

---

## 12. 交付物清单

- `docs/DESIGN.md`（本文）、`docs/PROTOCOL.md`
- `src/PhoneWheel.Plugin/**`：可编译的 C# 插件骨架（net8.0-windows + WinForms + Nefarius.ViGEm.Client）
- `ios/PhoneWheel/MotionSender.swift`：iPhone 伴侣 App 核心骨架
- `README.md`：快速开始、构建、校准、排障
