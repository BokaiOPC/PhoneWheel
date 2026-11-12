# 没有 Mac 时，如何把伴侣 App 装到 iPhone

> 适用：只有 Windows 电脑 + iPhone，没有 Mac，也不想付 $99/年开发者账号。

## 为什么需要这一步

iOS App 只能用 macOS 上的 Xcode 编译成 `.ipa`。没有 Mac 就没法生成这个文件，而 AltStore / Sideloadly 侧载必须先有 `.ipa`。

解决办法：**用 GitHub 免费提供的 macOS 云主机构建未签名 `.ipa`**，再在 Windows 上用你自己的免费 Apple ID 签名装进手机。全程不需要 Mac、不需要付费账号、不需要敲命令行。

```
[本项目源码] → GitHub 云端 macOS 编译 → PhoneWheel-unsigned.ipa
                                          ↓
                        Windows 上 AltStore / Sideloadly 签名
                                          ↓
                              数据线装进 iPhone → 开玩
```

依赖已在本机就绪（.NET SDK、ViGEmBus、AMDS、iproxy），下面只处理 iPhone 端。

---

## ✅ 当前进度（2026-11-12，步骤 1–2 已由助手完成）

| 项目 | 状态 |
|------|------|
| Public 仓库 | <https://github.com/BokaiOPC/PhoneWheel>（账号 `BokaiOPC`，默认分支 `main`，24 个源码文件） |
| 云端编译 | 已成功：<https://github.com/BokaiOPC/PhoneWheel/actions/runs/43135835911>（`macos-15`，Xcode 16.4，无签名 archive） |
| `.ipa` 产物 | **已下载到本机 `dist/PhoneWheel-unsigned.ipa`**（49,990 字节，含 `Payload/PhoneWheel.app/PhoneWheel` + `Info.plist` + `PkgInfo`） |
| 手机连接 | `idevice_id -l` 已检测到 UDID `00008030-000E15562240802E`，Apple Mobile Device Service Running |
| 待你操作 | **只剩「步骤 3 装侧载工具」→「步骤 4 用 Apple ID 签名安装」→「步骤 5 联调」** |

> 已经拿到 `.ipa` 了，下面的步骤 1、2 只是留作以后重新构建时的说明，**现在可以直接跳到步骤 3**。

> **CI 注意**：GitHub 已于 2026-11-02 退役所有 macOS 14 runner。workflow 的 `runs-on` 必须是 `macos-15`（或 `macos-26` / `macos-15-intel`），写 `macos-14` 会让任务 0 秒被取消，注解为 `The configuration 'macos-14-arm64-us-default' is not supported`。

---

## 步骤 1 · 把项目放到 GitHub（约 5 分钟，纯鼠标操作｜已完成）

1. 打开 <https://github.com> 注册并登录（免费账号即可）。
2. 点右上角 **+** → **New repository**：
   - Repository name：`PhoneWheel`
   - **务必选 `Public`（公开）**——公开仓库的 macOS 构建机才免费
   - 勾选 `Add a README file` 可留空，其余默认 → 点 **Create repository**
3. 上传代码，二选一：
   - **推荐（全图形界面）**：安装 [GitHub Desktop](https://desktop.github.com)（也可在 PowerShell 里执行 `winget install -e --id GitHub.GitHubDesktop`），登录后在软件里 `Clone` 刚建的仓库，把本工作区**全部文件**复制进克隆出来的文件夹，点 `Commit` → `Push`。
   - 或网页上传：仓库页点 **Add file → Upload files**，把文件拖进去。隐藏目录 `.github/workflows/` 也要一起包含，否则不会出现构建按钮。
4. 确认仓库里能看到这几个路径：
   - `ios/PhoneWheel/*.swift`、`ios/PhoneWheel/Info.plist`、`ios/project.yml`
   - `.github/workflows/build-unsigned-ipa.yml`

> 注意：国内访问 GitHub 有时不稳定，若网页打不开或上传失败，需要自行准备可用的网络环境。

## 步骤 2 · 云端编译出 .ipa（约 2 分钟｜已完成）

1. 仓库页点 **Actions** 标签 → 左侧选 **Build unsigned iPhone IPA**。
2. 点右侧 **Run workflow** → 再点绿色的 **Run workflow**。
3. 等任务变绿（首次约 10–15 分钟，macOS 构建机可能排队几分钟）。
4. 拉到页面底部 **Artifacts** → 下载 `PhoneWheel-unsigned-ipa`（一个 zip）→ 解压得到 **`PhoneWheel-unsigned.ipa`**。
   - 产物保留 30 天，请及时下载。
   - 若任务失败，把日志最后 30 行发给我，我来修工程配置。

## 步骤 3 · Windows 装 AltServer，手机装 AltStore（约 10 分钟）

1. **装 Apple 官网版 iTunes 与 iCloud**（不要用 Microsoft Store 版，AltServer 依赖其后台服务）：
   - PowerShell 执行 `winget install -e --id Apple.iTunes`（已在本机 winget 源验证），或到 <https://www.apple.com/itunes/> 下载；
   - iCloud 到 <https://www.apple.com/icloud/> 下载。你机器上已有 Apple Mobile Device Support，这一步主要是补齐 Bonjour / iCloud 组件。
2. 安装 **AltServer for Windows**（二选一）：
   - PowerShell 执行 `winget install -e --id RileyTestut.AltServer`（已在本机 winget 源验证）；
   - 或到 <https://altstore.io> 下载安装包。
   安装后运行，托盘出现菱形图标；防火墙提示时允许专用网络。
3. iPhone **用数据线连接电脑 → 解锁 → 弹窗点「信任此电脑」**。
4. 点托盘 AltServer 图标 → **Install AltStore** → 选你的 iPhone → 输入 **Apple ID 与账号密码**（凭据只发给 Apple 用于签名；建议单独注册一个小号 Apple ID 专用于侧载）。同样不要用 App 专用密码，见下方 Sideloadly 注意事项。
5. 手机上：
   - **设置 → 通用 → VPN 与设备管理 → 点你的 Apple ID → 信任**
   - iOS 16 及以上还需：**设置 → 隐私与安全性 → 开发者模式 → 打开并重启**

## 步骤 4 · 把 .ipa 装进手机（约 3 分钟）

1. 取本机 **`dist/PhoneWheel-unsigned.ipa`**。若走 AltStore 路线，把它传到 iPhone（最简单：微信发给「文件传输助手」→ 手机上点开 → **存储到「文件」App**）；若走 Sideloadly 路线，直接在电脑上拖进窗口即可，不用传文件。
2. 打开手机上的 **AltStore** → **My Apps** → 左上角 **+** → 从「文件」里选这个 `.ipa`。
3. 等待签名安装完成，桌面出现 **PhoneWheel**。
   - 若提示未受信任，回到「设置 → 通用 → VPN 与设备管理」再信任一次。

> **更省事的备选（当前推荐）**：装 **Sideloadly**，从 <https://sideloadly.io> 下载安装包（winget 的 0.60.0 清单哈希已过期，会报「安装程序哈希不匹配」，别用 winget）。数据线连手机 → 把 `.ipa` 拖进窗口 → 填 Apple ID → 点 Start，不用在手机上装 AltStore。它是闭源软件，介意的话用 AltStore（开源）。

> ⚠️ **免费 Apple ID 千万不要填 App 专用密码**。Sideloadly 官方 FAQ 原文：*App-specific password can only work if you are using a paid Apple Developer ID with anisette option disabled*。免费账号必须填 **Apple ID 账号本身的登录密码**，点 Start 后在弹窗里输入手机/邮箱收到的 **6 位双重认证验证码**。填专用密码会持续登录失败，连续重试又会触发 Apple 限流，表现就是「网络不稳定」或「请 10 分钟后再试」。

## 步骤 5 · 开玩

1. 数据线保持连接，电脑上启动 `PhoneWheel.Plugin.exe`（托盘图标出现）。
2. 手机上打开 **PhoneWheel**，右上角状态变成绿色「电脑已连接」。
3. **按驾驶握持姿势拿好手机，点「校准中位」**（会把中位姿态同步给电脑端）。
4. 进游戏：神力科莎 / ACC 在控制里选 Xbox 手柄预设；F1 系列默认识别 XInput，23/24 建议开启 Analog Steering。
5. 左下角按住 = 刹车，右下角按住 = 油门，上下滑动控制深度，两指可同时按；上排功能键按住生效。

## 每 7 天要做的维护（免费 Apple ID 的限制）

- 免费账号签名的 App **7 天过期**。AltServer 在电脑上运行、且电脑与 iPhone 在同一 WiFi 时，AltStore 会自动刷新；也可插线后打开 AltStore → My Apps → **Refresh All**。
- 免费账号同时最多 **3 个侧载 App**（AltStore 本身也算一个）；每周最多注册约 10 个 App ID。
- 过期后重新装一次即可，校准参数在电脑端 `appsettings.json`，不会丢。
- 想彻底免维护：付费开发者账号（$99/年，签名有效期 1 年），或借一台 Mac 用 Xcode 直接签名。

## 其它可行路线（如果觉得 GitHub 太麻烦）

| 路线 | 代价 |
|------|------|
| **借一台 Mac 用 30 分钟** | Xcode 打开 `ios/` 工程 → 数据线真机运行一次。之后所有刷新都在这台 Windows 上完成。**这一步其实比 CI 更简单**，只要能借到 Mac |
| 云 Mac 租用（MacinCloud 等） | 按小时付费，同样是一次性编译 |
| 欧盟地区 + iOS 17.4+ | 可从 altstore.io 直接装 AltStore PAL，无需电脑，但中国大陆不适用 |

## 排障速查

| 现象 | 处理 |
|------|------|
| Actions 页没有 workflow / 没有 Run 按钮 | 检查 `.github/workflows/build-unsigned-ipa.yml` 是否上传、仓库是否 Public、默认分支是否 `main`；也可直接在 Actions 页手动触发 |
| 运行 0 秒就被 `cancelled`、没有任何步骤 | `runs-on` 仍是已退役的 `macos-14`；改成 `macos-15`。看 check-run 注解可确认 `configuration 'macos-14-arm64-us-default' is not supported` |
| 云端构建失败（Archive / Swift 报错） | 展开日志看 `error:` 行；常见坑：屏幕常亮要用 `UIApplication.shared.isIdleTimerDisabled`（不是 `UIDevice`），`startDeviceMotionUpdates(using:to:)` 要 `OperationQueue`（不是 `DispatchQueue`） |
| 云端构建失败 | 展开日志看最后报错，把内容发我 |
| AltServer 看不到 iPhone | 装 Apple 官网版 iTunes + iCloud；`Win+R` → `services.msc` 重启 **Apple Mobile Device Service**；重插数据线并重新点「信任」 |
| **Sideloadly 报「网络不稳定」/「请 10 分钟后再试」/ `-36607`** | ① 立刻停手，等 10–15 分钟再试（每次点击都会刷新 Apple 限流计时）；② 别用 App 专用密码，改填**账号密码 + 2FA 验证码**；③ 浏览器登录 <https://developer.apple.com/account> 接受开发者条款、并在 <https://appleid.apple.com> 确认账号未被锁定 |
| **Sideloadly 报 `Failed to obtain anisette` / 500 / Guru Meditation** | 远程 anisette 服务器临时故障：Advanced Settings 里把 **Remote Anisette 切到 Local Anisette**（依赖本机 Apple 官网版 iTunes，非 Microsoft Store 版）；或稍后再试 |
| **Sideloadly 报 `-22406` / 账号密码错误** | 确认 Apple ID 与密码是登录 appleid.apple.com 用的那一组；国内手机号账号用登录时的完整格式；不要用 App 专用密码 |
| **Sideloadly 报 `np_client_new failed: NP_E_CONN_FAILED` / `LOCKDOWN_E_INVALID_HOST_ID`** | 确认装的是 Apple 官网版 iTunes，打开 iTunes 连一次手机同步；手机上重插线并重新点「信任此电脑」 |
| **提示 `maximum App ID limit ... 10 App IDs every 7 days`** | 免费账号 7 天最多注册 10 个 App ID，反复重试容易撞限；等几天或换一个 Apple ID |
| 手机打开 App 提示「不受信任的开发者」 | 设置 → 通用 → VPN 与设备管理 → 信任你的 Apple ID |
| 插件托盘显示「离线」 | 确认手机 App 在前台、屏幕常亮、电脑端 iproxy 隧道已就绪；数据线换一条原装线试试 |
| 转向没反应 | 手机点一次「校准中位」；游戏内确认已绑定 Xbox 手柄预设 |
