# TYW Launcher · Android

TYW Launcher 的 Android 版，Kotlin + Jetpack Compose，UI 结构与配色对齐 Windows 版（同一个顶部渐变标题栏、
同样的卡片 / 行 / 按钮 / 15 套主题），服务端协议与 Windows / Linux 版完全一致。

只提供 **Java 版**，不提供基岩版。

## 功能

- **三通道**：正式版 / 快照版 / 内测版，三个 APK 独立构建（`-PtywChannel=release|snapshot|beta`），
  由服务端 `config.json` 的 `Android.Channels.<通道>.{VersionCode,File}` 下发版本码与包名；内测包需要
  设备码 + 内测码才能下载。
- **正版登录**：微软 OAuth 设备码流程，与 Windows / Linux 版共用同一个应用注册（`852d43f4-…`）。
  拿到授权码后**自动写入剪贴板**，登录对话框、「启动」页、设置页都保留「复制授权码」按钮。
- **设备码**：与 Windows 版格式相同（`XXXX-XXXX-XXXX-XXXX`，16 位十六进制大写），由
  `ANDROID_ID|厂商|型号|设备|指纹` 做 MD5 取前 16 位生成，同一台设备稳定不变。界面上可以一键复制，
  服务端按这个码发放内测码 / 封禁。
- **Java 自动补全**：先扫启动器自带的运行时目录与常见系统路径；没有则按服务端
  `Link.JavaDownloadsAndroid`（缺省回退 `java-android-<arch>.zip`）下载并解压 `.zip` / `.tar.gz`，
  自动 `chmod +x` 并把路径写回设置。
- **触控布局**：数据结构、默认键位与编辑方式参考 FCL（Fold Craft Launcher）。默认布局包含移动摇杆、
  视角区、攻击 / 使用、跳跃 / 潜行 / 疾跑、背包 / 聊天、丢弃 / 交换副手、切换视角 / 暂停，以及底部 9 格物品栏；
  坐标按屏幕百分比保存，可导出 JSON。

## 构建

```bash
cd TYWLauncher-Android
./gradlew assembleRelease -PtywChannel=release   # 正式版
./gradlew assembleRelease -PtywChannel=snapshot  # 快照版
./gradlew assembleRelease -PtywChannel=beta      # 内测版
```

需要 JDK 17 与 Android SDK 35。CI（`.github/workflows/build-android.yml`）会在 push / tag 时构建全部三个通道。

## 致谢

触控部分的设计参考 [Fold Craft Launcher (FCL)](https://github.com/FCL-Team/FoldCraftLauncher)（GPL-3.0）。
本项目未打包 FCL 的任何二进制，只按其布局与交互方式重新实现。
