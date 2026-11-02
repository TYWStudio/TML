# TYW Launcher · Android

TYW Launcher 的 Android 版，Kotlin + Jetpack Compose，UI 结构与配色对齐 Windows 版（同一个顶部渐变标题栏、
同样的卡片 / 行 / 按钮 / 15 套主题），服务端协议与 Windows / Linux 版完全一致。

只提供 **Java 版**，不提供基岩版。

## 功能

- **三通道**：正式版 / 快照版 / 内测版，三通道各自是一个 buildType，产物分别落在
  `app/build/outputs/apk/release|snapshot|debug/`，由服务端 `config.json` 的
  `Android.Channels.<通道>.{VersionCode,File}` 下发版本码与包名；内测包需要设备码 + 内测码才能下载。
- **正版登录**：微软 OAuth 设备码流程，与 Windows / Linux 版共用同一个应用注册（`852d43f4-…`）。
  拿到授权码后**自动写入剪贴板**，登录对话框、「启动」页、设置页都保留「复制授权码」按钮。
- **设备码**：与 Windows 版格式相同（`XXXX-XXXX-XXXX-XXXX`，16 位十六进制大写），由
  `ANDROID_ID|厂商|型号|设备|指纹` 做 MD5 取前 16 位生成，同一台设备稳定不变。界面上可以一键复制，
  服务端按这个码发放内测码 / 封禁。
- **Java 管理（列表）**：`设置 → Java 运行环境 → 管理 Java` 是独立页面，列出 Java 8 / 11 / 16 / 17 / 21 / 25，
  每个版本都能单独下载或「导入安装包」（本地 zip / tar.gz），已装的可以设为默认或移除。
  补全时会先按当前游戏版本的要求挑运行时：优先读版本 JSON 的 `javaVersion.majorVersion`，
  没有就按版本号推（1.20.5+ → 21、1.18~1.20.4 → 17、1.17.x → 16、更早 → 8），
  已装版本低于要求时界面直接标红提示。
  下载**直连公网、不占服务端**：用与 FCL / Pojav 同源的 Android 原生运行时
  （`jre<大版本>-<架构>-<日期>-release.tar.xz`，Java 8 / 17 / 21 / 25），先查 GitHub Releases 取最新直链，
  查不到退回固定 tag 直链，直链不通再按顺序换公共镜像重试。
  注：Azul Zulu 只有 Linux（glibc）／Alpine（musl）／Windows／macOS 目标，**没有 Android（bionic）构建**，
  桌面 JDK 在手机上无法执行，所以不采用；Java 11 / 16 也没有对应构建，需要时用「导入安装包」。
- **触控布局**：数据结构、默认键位与编辑方式参考 FCL（Fold Craft Launcher）。默认布局包含移动摇杆、
  视角区、攻击 / 使用、跳跃 / 潜行 / 疾跑、背包 / 聊天、丢弃 / 交换副手、切换视角 / 暂停，以及底部 9 格物品栏；
  坐标按屏幕百分比保存，可导出 JSON。
- **动画**：页面切换是水平滑动 + 淡入，子页（Java 管理 / 触控编辑）从右侧推进，按钮按下有轻微缩放，
  导航选中态、进度条都做了过渡。

## 构建

```bash
cd TYWLauncher-Android
./gradlew assembleRelease    # 正式版 -> app/build/outputs/apk/release/
./gradlew assembleSnapshot   # 快照版 -> app/build/outputs/apk/snapshot/
./gradlew assembleDebug      # 内测版 -> app/build/outputs/apk/debug/
```

三个 buildType 共用同一个 `applicationId` 与 APK `versionCode`（避免跨通道切换时被系统判成降级），
通道自己的版本码由 `BuildConfig.VERSION_CODE_CN` 提供，只用于和服务端比对更新。

需要 JDK 17 与 Android SDK 35。CI（`.github/workflows/build-android.yml`）会在 push / tag 时构建全部三个通道。

## 致谢

触控部分的设计参考 [Fold Craft Launcher (FCL)](https://github.com/FCL-Team/FoldCraftLauncher)（GPL-3.0）。
本项目未打包 FCL 的任何二进制，只按其布局与交互方式重新实现。
