# TYW Launcher · macOS 版

TYW Launcher 的 macOS 客户端。界面与业务逻辑与 Windows 版（WPF / VB.NET）一一对应，
只是在 macOS 上用 Avalonia 重新实现了一遍：**XAML 结构、配色、动画节奏、文案都照搬 Windows 版**
（`Pages/*.xaml` → `Views/*.axaml` / `Views/*.cs`），Core 层则是同一套逻辑的 .NET 8 移植。

> Windows 版用的是 WPF，WPF 只能在 Windows 上跑，所以 macOS 这边不可能直接编译同一份 XAML，
> 只能按 Windows 版的界面结构与交互逐页复刻。对照关系见文末「与 Windows 版的对应关系」。

## 目录

```
TYWLauncher-macOS/
├── src/
│   ├── TYW.Core/        VB.NET，业务核心（下载 / 启动 / Java / 联机 / 服务端 / 自更新）
│   └── TYW.Desktop/     C# + Avalonia，界面层（窗口、页面、控件、主题）
├── packaging/
│   ├── build-macos.sh   一键出 .app 与 zip（三通道 × 双架构）
│   ├── make_icns.py     生成 AppIcon.icns
│   ├── zip_app.py       打包 zip 并保住 Unix 权限位
│   ├── Info.plist.in    Info.plist 模板
│   └── icons/           图标源图
└── dist/                构建产物（不入库）
```

## 构建

环境：.NET 8 SDK。macOS 原生构建最省事，Windows / Linux 上交叉编译也可以。

```bash
# 双架构 × 三通道，全部产物进 dist/
./packaging/build-macos.sh 1.1.37

# 只出 Apple Silicon
./packaging/build-macos.sh 1.1.37 arm64
```

单通道手动构建（调试用）：

```bash
dotnet publish src/TYW.Desktop/TYW.Desktop.csproj \
  -c Release -r osx-arm64 --self-contained true \
  -p:TYWChannel=Release -o out/macos-arm64
```

产物：

| 文件 | 通道 |
| --- | --- |
| `dist/TYWLauncher-macos-arm64.zip` | 正式版 |
| `dist/TYWLauncher-macos-arm64-snapshot.zip` | 快照版 |
| `dist/TYWLauncher-macos-arm64-beta.zip` | 内测版 |
| `dist/TYWLauncher-macos-x64*.zip` | Intel 版（同上三通道） |

zip 里是一个 `TYW Launcher.app`，解压后拖进「应用程序」即可。

## 签名与首次打开

- 构建脚本在 macOS 上会自动做 **ad-hoc 签名**（`codesign --sign -`）。Apple Silicon 上完全不签名的
  可执行文件会被系统直接拒绝启动，这一步不能省。
- 没有 Apple Developer 账号（99 美元/年）就**没法公证**，用户首次打开会看到「无法验证开发者」：
  右键 →「打开」，或「系统设置 → 隐私与安全性 → 仍要打开」。
- 启动器自带更新下载的包，替换脚本会自动 `xattr -cr` + 重新 ad-hoc 签名，不会再被拦。

## 和 Linux 版相比，macOS 上这些地方不一样

| 项目 | 位置 |
| --- | --- |
| 数据目录 | `~/Library/Application Support/TYWLauncher` |
| 游戏目录 | `~/Library/Application Support/minecraft`（与原版启动器一致） |
| Java 搜索 | `/Library/Java/JavaVirtualMachines`、`~/Library/Java/JavaVirtualMachines`、PATH；跳过 `/usr/bin/java` 占位壳 |
| 原生库 | `natives-macos` / `natives-macos-arm64` 分类，只解压 `.dylib` / `.jnilib` |
| 启动参数 | MC 1.19+ 追加 `-XstartOnFirstThread`（GLFW 必须在主线程初始化） |
| 自更新 | 下载 zip → 整体替换 `.app` → 去隔离属性 → 重签名 → `open -n` 重启 |
| 窗口 | 无边框自绘标题栏（与 Windows 版一致），四边四角补了手动缩放热区 |
| 字体 | 苹方（PingFang SC）替代微软雅黑 |
| 图标 | `AppIcon.icns`，从 256 像素原图 NEAREST 放大到 1024 后套 macOS 圆角遮罩 |

服务端 `config.json` 的 macOS 段：

```json
"MacOS": {
  "Channels": {
    "Release":  { "VersionCode": 1, "File": "TYWLauncher-macos-arm64.zip",  "FileX64": "TYWLauncher-macos-x64.zip" },
    "Snapshot": { "VersionCode": 1, "File": "TYWLauncher-macos-arm64-snapshot.zip", "FileX64": "TYWLauncher-macos-x64-snapshot.zip" },
    "Beta":     { "VersionCode": 1, "File": "TYWLauncher-macos-arm64-beta.zip",     "FileX64": "TYWLauncher-macos-x64-beta.zip" }
  }
}
```

## 已知限制

- **没有公证**：首次打开需手动放行（见上）。
- **联机（EasyTier）暂未开放**：macOS 建 TUN 需要 root，首版先关掉，界面会给说明。
- **Java 需要用户自备**：与 Linux 版一致，只做检测与版本匹配，不内置下载。
- **只支持 macOS 11 及以上**，且启动器架构要与 Java 架构一致（Apple Silicon 上装 x64 JDK 会走 Rosetta，容易出原生库问题）。

## 与 Windows 版的对应关系

| Windows 版 | macOS 版 |
| --- | --- |
| `FormMain.xaml` | `Views/MainWindow.axaml` + `.axaml.cs` |
| `Pages/PageLaunch/*` | `Views/PageLaunchLeft.axaml`、`PageLaunchRight.axaml` |
| `Pages/PageDownload`、`PageInstance`、`PageLink`、`PageOther`、`PageSetup` | `Views/StaticPage.cs`（按页索引分发）、`InstanceWindow.cs`、`LinkPage.cs`、`SettingsPage.cs`、`OtherPage.cs`、`ResPage.cs` |
| `Controls/Pcl*.xaml` | `Controls/Pcl*.cs`（纯代码构建，动画与配色同源） |
| `Modules/ModBase.vb`、`ModDownload.vb`、`ModLaunch.vb`、`ModJava.vb`、`ModNet.vb` … | `src/TYW.Core/**` 同名模块 |
| `Modules/ModUpdate.vb`（exe 替换） | `Server/ModUpdate.vb`（目录 / .app 替换） |
