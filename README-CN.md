<p align="right">
  <a href="README.md">English</a> · <b>简体中文</b>
</p>

# TYW Launcher

**TYW Launcher** 是一款开源、免费的 **Minecraft：Java 版启动器**（面向 Windows），基于开源的
**Plain Craft Launcher 2（PCL2）** 代码库派生而来。玩家使用**自己的微软账户**登录，启动自己已经
拥有的游戏副本。无广告、无内购、无任何盈利。

> 非 Minecraft 官方产品。未经 Mojang 或 Microsoft 认可或关联。

本项目以 [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/) 许可发布。

> ## ⚠️ 第三方代码声明（必读）
>
> 本项目的 **Android 触控操作控件**（屏幕摇杆、攻击 / 跳跃 / 潜行 / 物品栏 / 视角区按钮、布局编辑器）
> **参考并复刻了 [Fold Craft Launcher（FCL）](https://github.com/FCL-Team/FoldCraftLauncher) 的 `ControlLayout` 设计**，
> FCL 以 **GPL-3.0** 发布。
>
> 具体落在这些文件，**文件头都写了同样的声明**：
>
> - `TYWLauncher-Android/app/src/main/java/cn/tyw/launcher/core/TouchLayout.kt` —— 布局数据模型、默认键位
> - `TYWLauncher-Android/app/src/main/java/cn/tyw/launcher/ui/TouchEditorPage.kt` —— 控件拖动 / 缩放 / 增删 / 导出
>
> 我们**没有复制 FCL 的任何 Java/Kotlin 源码**，只按相同的布局语义用 Kotlin 独立实现；FCL 的类名、包名、资源、图标
> 均未引入。逐文件的范围与许可证说明见 **[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)**。
> 本项目的 Windows 部分基于 **PCL2**（© LTCat / Meloong-Git），许可见 [`LICENCE`](LICENCE)。

- 源码（可审计）：https://github.com/TYWStudio/TML
- 问题反馈：https://github.com/TYWStudio/TML/issues
- 支持平台：
  - **Windows** x64 · VB.NET / WPF · .NET Framework 4.8
  - **Linux** x64 / arm64 · Avalonia 12（VB.NET 内核 + C# UI）
  - **macOS** 11+ · Apple Silicon（arm64）/ Intel（x64）· 与 Linux 同一套 Avalonia 代码，以签名 `.app`
    的形式打包进 **`.dmg`**（拖入“应用程序”）和 `.zip`
  - **Android** 8.0+ · Kotlin / Jetpack Compose · arm64-v8a

所有平台共享相同的 UI 结构、相同的 15 套预设主题引擎、相同的三种发布通道（Release / Snapshot / Beta），
以及相同的服务端协议。

**macOS 说明：** `.dmg` 内含应用与一个 `Applications` 快捷方式——打开后把图标拖过去即可。应用为
ad-hoc 签名（`codesign -s -`），未做公证，因此首次启动需右键 → 打开（或执行
`xattr -dr com.apple.quarantine "/Applications/TYW Launcher.app"`）。应用内更新与通道切换通过整体
替换 `.app`、清除隔离属性并重新 ad-hoc 签名实现；EasyTier 联机在 macOS 上暂不可用（创建 TUN 设备需要
root 权限），联机页面会就地说明这一点。

游戏文件**不**在此分发；它们会在运行时从官方 / 官方镜像 CDN 下载。启动器不会绕过验证或授权检查——
联机需要拥有 Minecraft：Java 版，每个会话都使用从用户自己账户获取的令牌。账户令牌仅保存在用户设备上。

> **这不是原版 PCL2。** 所有原始 PCL2 代码归 LTCat 与 PCL 社区所有——见下方 [原始项目](#原始项目)。

## 原始项目

- **PCL2 官方仓库（问题与发布）：** https://github.com/Hex-Dragon/PCL2
- **PCL2 开源代码（本仓库的基底）：** https://github.com/Meloong-Git/PCL
- 作者：**LTCat**（@Meloong-Git）。感谢这款出色的启动器！
- [下载原版 PCL2](https://meloong.com/pcl)

本仓库是对开源 PCL2 代码的 fork 式再分发，并附加了若干修改。服务端代码**不**包含在内，且**不开源**。

## 与原版 PCL2 的区别

- 品牌改为 **TYW Launcher**（新图标、关于 / 致谢页面、崩溃日志等），并在“关于”页保留“基于 PCL”的署名
- **三种发布通道**：Release / Snapshot / Beta（Beta 通道由服务端签发的、绑定设备 ID 的内测码控制）
- 每个版本的**内置更新日志**，保存在客户端、不依赖任何服务器
- 可选的自托管附加功能（见 [可选服务端功能](#可选服务端功能)）：公告、应用内更新、内测门禁、设备封禁、匿名错误上报

## 可选服务端功能

这些是**同一项目的运营附加功能**。它们**从不调用 Minecraft Game Service API**，也**不属于任何 API 访问申请**。

**开源构建完全不含服务端。** 本仓库任何地方都没有硬编码 URL，因此全新克隆（以及由它构建的每个产物）
都**完全离线**运行：没有公告、没有应用内更新检查、没有内测 / 封禁检查、没有匿名错误上报。一切都优雅降级——
连不上服务器从不被视为错误，也从不弹窗。

要使用依赖服务端的功能，你必须**自行运行后端**并把启动器指向它。管理端为**私有**（不属于本仓库）；客户端
只需要下列 HTTP 端点，任何实现它们的栈（PHP / Node / Go / .NET……）都可以工作。

按以下任一方式把启动器指向你的服务器（优先级从高到低）：

| 方式 | 位置 | 说明 |
|---|---|---|
| 启动器设置 | 设置 → **服务端（可选）** → 地址 | Windows / Linux / macOS / Android 均可 |
| 环境变量 | `TYW_SERVER=http://your-host:17403/` | 便于便携 / CI 使用 |
| `server.txt` | 可执行文件旁，或数据目录内 | 一行，仅 URL |
| 构建期 | 打包时设置 `TYW_SERVER_URL=...` | 写入二进制；见 [构建](#构建) |

地址为空 ⇒ **所有服务端功能关闭**，启动器独立运行。地址会被规范化为以 `/` 结尾；缺少协议头的值会自动补 `http://`。

客户端期望以下 HTTP 端点：

| 端点 | 方法 | 响应 |
|---|---|---|
| `Notice.cfg` | GET | 纯文本，以 `\|` 分隔：`Title\|SnapshotCode\|ReleaseCode\|ReleaseNotice\|SnapshotNotice\|BetaCode\|BetaNotice\|ReleaseChangelog\|SnapshotChangelog\|BetaChangelog`（文本内换行用 `\n`；末尾的更新日志段可省略） |
| `beta?ident={deviceId}&code={betaCode}` | GET | JSON `{"ok": bool, "msg": "...", "version_code": int}` |
| `ban?ident={deviceId}` | GET | JSON `{"banned": bool, "reason": "..."}` |
| `report` | POST | 接收纯文本错误上报（请求体） |
| `config.json` | GET | JSON `{"settings": {...}}` 云端配置（可选） |
| `TYW Launcher.exe` / `... Snapshot.exe` / `... Beta.exe` | GET | 用于更新的各通道启动器二进制 |

设备 ID 是机器派生的指纹，会显示在封禁对话框中，用户可把它发给你以进行（解）封禁。

## Android 应用

`TYWLauncher-Android/` 是基于 Windows XAML 布局重建的原生 Android 客户端（相同的顶部渐变栏、相同的卡片 /
行 / 按钮、相同的调色板引擎），并使用相同的服务端协议：

- **微软登录**：OAuth *设备代码* 流程。**授权码在签发的那一刻即复制到剪贴板**，也可从对话框、启动器页面或
  设置 → 关于 再次复制。密码从不进入应用。
- **设备代码**：与 Windows 相同的格式——`XXXX-XXXX-XXXX-XXXX`，由稳定的设备指纹派生
  （`ANDROID_ID|manufacturer|model|device|fingerprint` 的 MD5，取前 16 位十六进制并大写）。设备代码与
  授权码都有**一键复制**按钮，点击数值本身也会复制。
- **Java 管理（列表）**：设置 → Java 运行环境 → 管理 Java 会打开一个专用页面，列出 Java 8 / 11 / 16 / 17 / 21 / 25。
  每个版本都可从本地压缩包（`zip` / `tar.gz`）下载或安装；已安装的运行时可设为默认或移除。自动配置按钮会先检查
  所选游戏版本的要求——版本 JSON 中存在 `javaVersion.majorVersion` 时以其为准，否则根据版本号推导
  （1.20.5+ → 21，1.18–1.20.4 → 17，1.17.x → 16，更早 → 8）——仅在未安装合适版本时才下载。不匹配会在启动页
  以红色标出。下载**直接走公网，服务器上不托管任何内容**：启动器从 GitHub Releases 解析最新的
  `jre<major>-<arch>-<date>-release.tar.xz`（Java 8 / 17 / 21 / 25，按设备 ABI 选取）——与 FCL、PojavLauncher
  使用的是同一运行时家族——失败则回退到固定的发布标签，再不行就通过公共镜像重试。刻意不使用 Azul Zulu：它只提供
  Linux（glibc）/ Alpine（musl）/ Windows / macOS 目标，**没有 Android（bionic）构建**，桌面 JDK 无法在手机上执行。
  Java 11 / 16 也没有 Android 构建，只能导入。`config.json` 里的 `Link.JavaAndroid` 仍可作为可选附加源。
- **触控操作**：布局数据模型、默认键位与编辑行为遵循 **Fold Craft Launcher（FCL）**——移动摇杆、视角区、
  攻击 / 使用、跳跃 / 潜行 / 疾跑、物品栏、聊天、丢弃、副手交换、视角切换、暂停菜单以及 9 格快捷栏。位置以相对
  屏幕的百分比保存，可导出为 JSON。
- **通道**：三个分别构建的 APK，每个通道一个构建类型，输出到 `app/build/outputs/apk/release|snapshot|debug/`
  （`TYWLauncher-android.apk`、`-snapshot`、`-beta`），并由同一份 `config.json` →
  `Android.Channels.<channel>.{VersionCode,File}` 提供，Beta APK 在服务端受设备代码 + 内测码保护。
- **动画**：页面切换横向滑动并交叉淡入，子页面（Java 管理器、触控编辑器）从侧边推入，按钮按下时轻微缩放，
  导航胶囊与进度条的状态有动画过渡。

仅支持 Java 版——任何平台都不提供基岩版。

## 构建

**Windows**——要求：**Windows**、**Visual Studio 2022**（含 .NET 桌面开发工作负载）或 **.NET Framework 4.8 Developer Pack**。

```bash
msbuild "TYW Launcher.sln" /restore /p:Configuration=Release /m
```

- `Debug` → Beta 通道构建
- `Snapshot` → Snapshot 通道
- `Release` → 稳定通道

**Linux**——`dotnet publish TYWLauncher-Linux/src/TYW.Desktop/TYW.Desktop.csproj -c Release -r linux-x64 -p:TYWChannel=Release`

**macOS**——独立工程，双架构 × 三通道，产出 `.zip` **和 `.dmg`**：

```bash
cd TYWLauncher-macOS
./packaging/build-macos.sh 1.1.37          # arm64 + x64, Release / Snapshot / Beta
./packaging/build-macos.sh 1.1.37 arm64    # Apple Silicon only
```

输出在 `dist/`：`TYWLauncher-macos-<arch>[-snapshot|-beta].{zip,dmg}` 以及 `SHA256SUMS-macos.txt`。
脚本把发布输出打包成 `TYW Launcher.app`（`Info.plist` + `AppIcon.icns`），做 ad-hoc 签名
（`codesign --force --deep --sign -`），并**仅在 macOS 上**用 `hdiutil` 构建包含应用与 `/Applications` 符号链接的
`UDZO` 磁盘映像。在 Windows / Linux 上，`.dmg` 步骤会自动跳过（没有 `hdiutil`）。

**Android**——`./gradlew assembleRelease` / `assembleSnapshot` / `assembleDebug`（每个通道一个构建类型）

要把服务器地址写入二进制，请在构建时设置 `TYW_SERVER_URL`（离线开源构建请留空不设）：

```bash
TYW_SERVER_URL=http://your-host:17403/ ./packaging/build-macos.sh 1.1.37
TYW_SERVER_URL=http://your-host:17403/ ./gradlew assembleRelease
```

或者直接推送一个标签（`v*`）——**GitHub Actions 会自动构建所有平台，并把每个产物（Windows exe、Linux tar.gz ×3、
macOS zip + dmg ×3 ×2 架构、Android APK ×3）附加到 GitHub Release**。CI **不会**注入服务器地址，因此
GitHub 上发布的每个产物按设计都是仅离线的。

## 仓库结构

```
TYW Launcher/             Windows 启动器工程（VB.NET WPF，.NET Framework 4.8）
TYWLauncher-Linux/        Linux 启动器（Avalonia 12）
TYWLauncher-macOS/        macOS 启动器（同一套 Avalonia 代码；.app / .dmg 打包在 packaging/）
TYWLauncher-Android/      Android 启动器（Kotlin / Jetpack Compose）
PCLCS/                    PCL 核心支持代码（来自上游开源版本）
MeloongCore/              Git 子模块（Apache-2.0，https://github.com/Meloong-Git/MeloongCore）
```

## 许可与致谢

完整细节——包括涉及的确切文件以及复制 / 未复制了什么——见 **[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)**。摘要：

- 原始 PCL2 代码 © LTCat / Meloong-Git，按其自身许可发布——见 [`LICENCE`](LICENCE)（上游许可文件，保持未修改）。
- [`MeloongCore`](MeloongCore/LICENSE.txt) 由 Meloong/PCL 社区以 **Apache-2.0** 许可。
- **Android 触控操作** 参照 **[Fold Craft Launcher（FCL）](https://github.com/FCL-Team/FoldCraftLauncher)**（GPL-3.0）：
  控件布局数据模型、默认键位，以及控件的放置与编辑方式都遵循 FCL 的 `ControlLayout`。FCL 是独立项目，未打包于此——
  仅为本启动器重新实现了布局设计，位于 `TYWLauncher-Android/.../core/TouchLayout.kt` 与 `.../ui/TouchEditorPage.kt`。
  感谢 FCL 团队公开他们的工作。
- 本仓库的所有修改由 **TYW-MC** 完成。
- 服务端实现为专有，不属于本仓库。

## 免责声明

非 Minecraft 官方产品。未经 Mojang 或 Microsoft 认可或关联。

本启动器是社区修改版，与 Mojang 或 Microsoft 无隶属、认可或关联关系。Minecraft 是 Mojang Synergies AB 的商标。
“Minecraft”在此仅作描述性使用，以指明本启动器启动的是哪款游戏。

## 扩展 API（TMLExt / jar）

TYW Launcher 提供**进程隔离、按需启用**的扩展接口，四个平台（Windows / Linux / macOS / Android）共用：

- **Python 3**、**Node.js**、**C**、**Rust**、**Go** 扩展使用 `.tmlext` 包（zip + `manifest.json`），
  其中编译型语言直接携带原生可执行文件，由启动器直接拉起。
- **Java** 扩展使用 `.jar` 包，作为独立 JVM 进程启动，绝不加载进启动器进程。

扩展默认禁用，只有在用户审阅其权限列表后才可启用，会以 SHA-256 指纹标记（包内容变化会再次被禁用），
且只能调用七项白名单：`log`、`toast`、`host.info`、`instances.list`、`events`、`storage`、`settings`。
任意文件 / 进程 / 网络访问、凭据、令牌、设备代码、服务器地址、注册表、反射以及宿主内部**完全不暴露**，
也不会下载或安装任何运行时。

### 官方扩展模板

六种语言各一个仓库，克隆下来改一改就能用。每个仓库都含 SDK、可运行的示例、打包脚本和 GitHub Actions
构建，以及中英双语文档：

| 运行时 | 模板仓库 |
| --- | --- |
| Python 3 | <https://github.com/TYWStudio/template-tml-extension-python> |
| Node.js | <https://github.com/TYWStudio/template-tml-extension-node> |
| Java | <https://github.com/TYWStudio/template-tml-extension-java> |
| C | <https://github.com/TYWStudio/template-tml-extension-c> |
| Rust | <https://github.com/TYWStudio/template-tml-extension-rust> |
| Go | <https://github.com/TYWStudio/template-tml-extension-go> |

启动器里也有同一组入口：**设置 → 其他 → 拓展 → 开发拓展**。

完整协议、JSON 模式、SDK 与示例：[`extensions/README.md`](extensions/README.md)。
安全模型及其边界：[`extensions/SECURITY.md`](extensions/SECURITY.md)。
