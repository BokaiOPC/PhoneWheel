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

## 步骤 1 · 把项目放到 GitHub（约 5 分钟，纯鼠标操作）

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

## 步骤 2 · 云端编译出 .ipa（约 10–15 分钟）

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
4. 点托盘 AltServer 图标 → **Install AltStore** → 选你的 iPhone → 输入 Apple ID 与密码（凭据只发给 Apple 用于签名；建议单独注册一个小号 Apple ID 专用于侧载）。
5. 手机上：
   - **设置 → 通用 → VPN 与设备管理 → 点你的 Apple ID → 信任**
   - iOS 16 及以上还需：**设置 → 隐私与安全性 → 开发者模式 → 打开并重启**

## 步骤 4 · 把 .ipa 装进手机（约 3 分钟）

1. 把 `PhoneWheel-unsigned.ipa` 传到 iPhone，最简单的方式：微信发给「文件传输助手」→ 手机上点开 → **存储到「文件」App**。
2. 打开手机上的 **AltStore** → **My Apps** → 左上角 **+** → 从「文件」里选这个 `.ipa`。
3. 等待签名安装完成，桌面出现 **PhoneWheel**。
   - 若提示未受信任，回到「设置 → 通用 → VPN 与设备管理」再信任一次。

> **更省事的备选**：装 **Sideloadly**（`winget install -e --id iOSGods.Sideloadly`，或 <https://sideloadly.io>）。数据线连手机 → 把 `.ipa` 拖进窗口 → 填 Apple ID → 点 Start 即可，不用在手机上装 AltStore。它是闭源软件，介意的话用 AltStore（开源）。

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
| 云端构建失败 | 展开日志看最后报错，把内容发我 |
| AltServer 看不到 iPhone | 装 Apple 官网版 iTunes + iCloud；`Win+R` → `services.msc` 重启 **Apple Mobile Device Service**；重插数据线并重新点「信任」 |
| 手机打开 App 提示「不受信任的开发者」 | 设置 → 通用 → VPN 与设备管理 → 信任你的 Apple ID |
| 插件托盘显示「离线」 | 确认手机 App 在前台、屏幕常亮、电脑端 iproxy 隧道已就绪；数据线换一条原装线试试 |
| 转向没反应 | 手机点一次「校准中位」；游戏内确认已绑定 Xbox 手柄预设 |
