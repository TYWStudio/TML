# Third-Party Notices / 第三方代码与设计声明

TYW Launcher 基于多个开源项目。本文件列出**每一个**我们用到其代码或设计的上游项目、
对应的许可证、涉及范围，以及我们改了什么。README 里只做摘要，细节以本文件为准。

如果你发现本文件遗漏了某个上游项目，请在 Issues 里指出，我们会补上。

---

## 1. Plain Craft Launcher 2 (PCL2) — 代码基础

| | |
|---|---|
| 上游 | https://github.com/Meloong-Git/PCL |
| 作者 | © LTCat / 龙腾猫跃，及 PCL 社区贡献者 |
| 许可证 | 见仓库根 [`LICENCE`](LICENCE)（上游许可文件，原样保留，未做任何修改） |
| 涉及范围 | **Windows 版**的绝大部分：`Modules/`、`Pages/`、`Controls/`、`Styles/`、`Images/`、启动器核心逻辑（版本解析、下载、启动、账户、设置） |

我们做了什么：

- 品牌改名（图标、标题、关于页、崩溃日志路径），并在「关于」页保留 "Based on PCL" 署名。
- 新增/重写：三通道发布体系、服务端更新与公告、内测码鉴权、Java 管理页、更新日志、若干页面。
- 删除了 PCL 的捐赠入口与统计上报。

**所有 `TYW Launcher/` 目录下的代码都受上游许可证约束**，不是我们的原创作品。

---

## 2. MeloongCore — 子模块

| | |
|---|---|
| 上游 | https://github.com/Meloong-Git/MeloongCore |
| 许可证 | Apache-2.0（见 [`MeloongCore/LICENSE.txt`](MeloongCore/LICENSE.txt)） |
| 涉及范围 | Windows 版的底层工具库（以 git submodule 引入，未修改） |

---

## 3. Fold Craft Launcher (FCL) — 触控操作控件

这一项是**设计参考**，不是代码复制，单独说明清楚。

| | |
|---|---|
| 上游 | https://github.com/FCL-Team/FoldCraftLauncher |
| 许可证 | GPL-3.0 |
| 涉及范围 | **Android 版的触控操作控件**，即屏幕上的摇杆、按键、物品栏、视角区 |

参考了 FCL 的哪些东西：

- **数据模型**：FCL `ControlLayout` 的语义——每个控件由「类型（摇杆 / 按键 / 物品栏 / 视角区）+ 相对屏幕的百分比位置 + 相对短边的尺寸 + 显示文字 + 对应的 Minecraft 按键名（`key.forward` / `key.jump` / `key.hotbar.1` …）」描述。我们的 `TouchLayout.kt` 用的是同一套字段语义。
- **默认键位**：左下移动摇杆，右下攻击 / 使用 / 跳跃 / 潜行，左上背包 / 聊天，右上视角切换 / 暂停菜单，底部 9 格物品栏，外加疾跑、丢弃、交换副手等 FCL 默认布局里也有的按键。
- **编辑交互**：进入编辑模式后拖动摆放、缩放、增删控件、导出 JSON 的操作流。

**没有**参考自 FCL 的东西：

- 没有复制、翻译或改写 FCL 的任何 Java / Kotlin 源文件；`TouchLayout.kt` 与 `ui/TouchEditorPage.kt` 是对上述设计语义的独立 Kotlin 实现。
- 没有把 FCL 的类名、包名、资源文件、图标带进来。
- 没有使用 FCL 的渲染层（游戏内绘制由本项目自行实现）。

对应文件：

- `TYWLauncher-Android/app/src/main/java/cn/tyw/launcher/core/TouchLayout.kt`
- `TYWLauncher-Android/app/src/main/java/cn/tyw/launcher/ui/TouchEditorPage.kt`

> 注：FCL 采用 GPL-3.0。若将来引入 FCL 的实际源代码（而不是仅参考设计），本项目相应部分必须改为
> GPL-3.0 兼容，届时会在本文件更新许可说明。

---

## 4. 运行时与构建依赖

这些是作为依赖库使用的，未修改其源码：

| 项目 | 许可证 | 用在哪 |
|---|---|---|
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | MIT | Linux / macOS 版 UI 框架 |
| [.NET](https://github.com/dotnet/runtime) | MIT | 三个桌面平台（Windows 用 .NET Framework 4.8） |
| [AndroidX / Jetpack Compose](https://github.com/androidx/androidx) | Apache-2.0 | Android 版 UI |
| [org.tukaani:xz](https://tukaani.org/xz/java.html) | Public Domain | Android 版解压 `tar.xz` 运行库 |
| [EasyTier](https://github.com/EasyTier/EasyTier) | LGPL-3.0 | 联机功能的虚拟组网内核（运行时下载，未随仓库分发） |
| [EclipseJRE / PojavLauncher 运行时](https://github.com/SouhardyaMaybe/EclipseJRE) | 见上游 | Android 版 Java 运行时（运行时下载，未随仓库分发） |

界面控件的来源：Linux / macOS 版的 `PclButton`、`PclCard`、`PclSkin` 等控件是
**照本仓库 Windows 版的 PCL 控件重新实现**的 Avalonia 版本（不引用 Windows 版二进制），
因此同样归属第 1 条 PCL2 的许可证范围。

---

## 5. 不属于本仓库的部分

- 服务端实现（`TYW Server.exe`）为专有软件，未包含在本仓库中，也不在本文件的声明范围内。
- 游戏本体、Java 运行时、模组均不随仓库分发，由用户在运行时从各自的官方渠道获取。
