# TYW Launcher

**TYW Launcher** is an open-source, free-of-charge **launcher for Minecraft: Java Edition** for Windows, derived from
the open-source **Plain Craft Launcher 2 (PCL2)** codebase. Players sign in with **their own Microsoft account** to
launch the copy of the game they already own. No ads, no in-app purchases, no monetization.

> NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.

> ## ⚠️ Third-party code / 第三方代码声明（必读）
>
> 本项目的 **Android 触控操作控件**（屏幕摇杆、攻击 / 跳跃 / 潜行 / 物品栏 / 视角区按钮、布局编辑器）
> **参考并复刻了 [Fold Craft Launcher (FCL)](https://github.com/FCL-Team/FoldCraftLauncher) 的 `ControlLayout` 设计**，
> FCL 以 **GPL-3.0** 发布。
>
> 具体落在这些文件，**文件头都写了同样的声明**：
>
> - `TYWLauncher-Android/app/src/main/java/cn/tyw/launcher/core/TouchLayout.kt` — 布局数据模型、默认键位
> - `TYWLauncher-Android/app/src/main/java/cn/tyw/launcher/ui/TouchEditorPage.kt` — 控件拖动 / 缩放 / 增删 / 导出
>
> 我们**没有复制 FCL 的任何 Java/Kotlin 源码**，只按相同的布局语义用 Kotlin 独立实现；FCL 的类名、包名、资源、图标
> 均未引入。逐文件的范围与许可证说明见 **[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)**。
> 本项目的 Windows 部分基于 **PCL2**（© LTCat / Meloong-Git），许可见 [`LICENCE`](LICENCE)。

- Source code (auditable): https://github.com/TYWStudio/TML
- Issue tracker: https://github.com/TYWStudio/TML/issues
- Platforms:
  - **Windows** x64 · VB.NET / WPF · .NET Framework 4.8
  - **Linux** x64 / arm64 · Avalonia 12 (VB.NET core + C# UI)
  - **macOS** 11+ · Apple Silicon (arm64) / Intel (x64) · same Avalonia codebase as Linux, shipped as a signed `.app`
    inside **`.dmg`** (drag-to-Applications) and `.zip`
  - **Android** 8.0+ · Kotlin / Jetpack Compose · arm64-v8a

All platforms share the same UI structure, the same 15-preset theme engine, the same three release channels
(Release / Snapshot / Beta) and the same server protocol.

**macOS notes:** the `.dmg` contains the app plus an `Applications` shortcut — open it, drag the icon over, done.
The app is ad-hoc signed (`codesign -s -`), not notarized, so the first launch needs right-click → Open (or
`xattr -dr com.apple.quarantine "/Applications/TYW Launcher.app"`). In-app updates and channel switching work by
swapping the whole `.app` bundle, clearing the quarantine attribute and re-signing ad-hoc; EasyTier multiplayer is
not available on macOS yet (creating a TUN device needs root), and the multiplayer page says so in-place.

Game files are **not** redistributed here; they are downloaded at runtime from official / official-mirror CDNs.
The launcher does not bypass authentication or entitlement checks — online play requires owning Minecraft: Java
Edition, and every session uses the token obtained from the user's own account. Account tokens are stored only on the
user's device.

> **This is NOT the original PCL2.** All original PCL2 code belongs to LTCat and the PCL community — see
> [Original Project](#original-project) below.

## Original Project

- **PCL2 official repo (issues & releases):** https://github.com/Hex-Dragon/PCL2
- **PCL2 open-source code (this repo's base):** https://github.com/Meloong-Git/PCL
- Author: **LTCat** (@Meloong-Git). Thanks for the amazing launcher!
- [Download the original PCL2](https://meloong.com/pcl)

This repository is a fork-style redistribution of the open-source PCL2 code with additional modifications. The server-side code is **not** included and is **not** open source.

## What's Different from Upstream PCL2

- Rebranded to **TYW Launcher** (new icon, About/Credits pages, crash logs, etc.), with a "Based on PCL" attribution kept in the About page
- **Three release channels**: Release / Snapshot / Beta (the Beta channel is gated by server-issued beta codes bound to a device ID)
- **Built-in changelog** for each version, kept in the client and independent of any server
- Optional self-hosted extras (see [Server Features](#optional-server-features)): announcements, in-app updates, beta gating, device banning, anonymous error reports

## Optional Server Features

These are **operational extras of the same project**. They **never call the Minecraft Game Service API** and are
**not part of any API access request**.

**The open-source build ships with no server at all.** There is no hard-coded URL anywhere in this repository, so a
fresh clone (and every artefact built from it) runs **fully offline**: no announcements, no in-app update checks, no
beta/ban checks, no anonymous error reports. Everything degrades gracefully — a missing server is never treated as an
error and never shows a dialog.

To use the server-dependent features you must **run your own backend** and point the launcher at it. The management
server is **private** (not part of this repository); the client only needs the HTTP endpoints listed below, so any
stack (PHP / Node / Go / .NET …) that implements them will work.

Point the launcher at your server in any of these ways (highest priority first):

| How | Where | Notes |
|---|---|---|
| Launcher setting | Settings → **服务端（可选）** → address | Works on Windows / Linux / macOS / Android |
| Environment variable | `TYW_SERVER=http://your-host:17403/` | Handy for portable/CI use |
| `server.txt` | next to the executable, or in the data directory | One line, just the URL |
| Build-time | `TYW_SERVER_URL=... ` when packaging | Baked into the binary; see [Building](#building) |

Empty address ⇒ **all server features are off** and the launcher works standalone. The address is normalised to end
with `/`; a scheme-less value gets `http://` prepended.

The client expects these HTTP endpoints:

| Endpoint | Method | Response |
|---|---|---|
| `Notice.cfg` | GET | Plain text, `\|`-separated: `Title\|SnapshotCode\|ReleaseCode\|ReleaseNotice\|SnapshotNotice\|BetaCode\|BetaNotice\|ReleaseChangelog\|SnapshotChangelog\|BetaChangelog` (use `\n` for newlines inside text; trailing changelog segments are optional) |
| `beta?ident={deviceId}&code={betaCode}` | GET | JSON `{"ok": bool, "msg": "...", "version_code": int}` |
| `ban?ident={deviceId}` | GET | JSON `{"banned": bool, "reason": "..."}` |
| `report` | POST | Accepts plain-text error reports (body) |
| `config.json` | GET | JSON `{"settings": {...}}` cloud configuration (optional) |
| `TYW Launcher.exe` / `... Snapshot.exe` / `... Beta.exe` | GET | Per-channel launcher binaries for updates |

The device ID is a machine-derived fingerprint shown in the ban dialog, so users can send it to you for (un)banning.

## Android App

`TYWLauncher-Android/` is a native Android client rebuilt from the Windows XAML layouts (same top gradient bar,
same cards / rows / buttons, same palette engine), with the same server protocol:

- **Microsoft sign-in**: OAuth *device code* flow. The **authorisation code is copied to the clipboard the moment it
  is issued**, and can be copied again from the dialog, the launcher page or Settings → About. No password ever
  touches the app.
- **Device code**: identical format to Windows — `XXXX-XXXX-XXXX-XXXX`, derived from a stable device fingerprint
  (MD5 of `ANDROID_ID|manufacturer|model|device|fingerprint`, first 16 hex chars, uppercased). Both the device code
  and the authorisation code have a one-tap **Copy** button, and tapping the value copies it too.
- **Java management (list)**: Settings → Java 运行环境 → 管理 Java opens a dedicated page listing Java 8 / 11 / 16 /
  17 / 21 / 25. Every version can be downloaded or installed from a local archive (`zip` / `tar.gz`); installed
  runtimes can be set as default or removed. The auto-provisioning button first checks the requirement of the
  selected game version — `javaVersion.majorVersion` from the version JSON when present, otherwise derived from the
  version number (1.20.5+ → 21, 1.18–1.20.4 → 17, 1.17.x → 16, older → 8) — and only downloads when nothing suitable
  is installed. A mismatch is called out in red on the launch page. Downloads go **straight to the public internet,
  nothing is hosted on our server**: the launcher resolves the newest `jre<major>-<arch>-<date>-release.tar.xz`
  (Java 8 / 17 / 21 / 25, picked by the device ABI) from GitHub Releases — the same runtime family FCL and
  PojavLauncher use — falls back to a pinned release tag, then retries through public mirrors if GitHub is unreachable.
  Azul Zulu is deliberately not used: it ships Linux (glibc) / Alpine (musl) / Windows / macOS targets only and has
  **no Android (bionic) build**, so a desktop JDK cannot execute on a phone. Java 11 / 16 have no Android build either
  and are import-only. `Link.JavaAndroid` in `config.json` still works as an optional extra source.
- **Touch controls**: layout data model, default keymap and editing behaviour follow **Fold Craft Launcher (FCL)** —
  movement joystick, view area, attack / use, jump / sneak / sprint, inventory, chat, drop, swap-offhand,
  perspective toggle, pause menu and the 9-slot hotbar. Positions are stored as screen-relative percentages and can
  be exported as JSON.
- **Channels**: three separately built APKs, one build type per channel, landing in
  `app/build/outputs/apk/release|snapshot|debug/` (`TYWLauncher-android.apk`, `-snapshot`, `-beta`) and served from the
  same `config.json` → `Android.Channels.<channel>.{VersionCode,File}`, with the beta APK protected by device code +
  beta key on the server.
- **Animations**: page switches slide horizontally with a cross-fade, sub-pages (Java manager, touch editor) push in
  from the side, buttons scale slightly while pressed, nav pills and progress bars animate their state.

Java Edition only — no Bedrock edition is offered on any platform.

## Building

**Windows** — requirements: **Windows**, **Visual Studio 2022** (with the .NET desktop development workload) or the **.NET Framework 4.8 Developer Pack**.

```bash
msbuild "TYW Launcher.sln" /restore /p:Configuration=Release /m
```

- `Debug` → Beta-channel build
- `Snapshot` → Snapshot channel
- `Release` → Stable channel

**Linux** — `dotnet publish TYWLauncher-Linux/src/TYW.Desktop/TYW.Desktop.csproj -c Release -r linux-x64 -p:TYWChannel=Release`

**macOS** — dedicated project, both architectures × three channels, produces `.zip` **and `.dmg`**:

```bash
cd TYWLauncher-macOS
./packaging/build-macos.sh 1.1.37          # arm64 + x64, Release / Snapshot / Beta
./packaging/build-macos.sh 1.1.37 arm64    # Apple Silicon only
```

Output in `dist/`: `TYWLauncher-macos-<arch>[-snapshot|-beta].{zip,dmg}` plus `SHA256SUMS-macos.txt`.
The script wraps the publish output into `TYW Launcher.app` (`Info.plist` + `AppIcon.icns`), ad-hoc signs it
(`codesign --force --deep --sign -`) and, **on macOS only**, builds a `UDZO` disk image with `hdiutil` containing the
app plus an `/Applications` symlink. On Windows / Linux the `.dmg` step is skipped automatically (no `hdiutil`).

**Android** — `./gradlew assembleRelease` / `assembleSnapshot` / `assembleDebug` (one build type per channel)

To bake a server address into a binary, set `TYW_SERVER_URL` for the build (leave it unset for the offline open-source
build):

```bash
TYW_SERVER_URL=http://your-host:17403/ ./packaging/build-macos.sh 1.1.37
TYW_SERVER_URL=http://your-host:17403/ ./gradlew assembleRelease
```

Or just push a tag (`v*`) — **GitHub Actions builds all platforms automatically and attaches every artefact
(Windows exe, Linux tar.gz ×3, macOS zip + dmg ×3 ×2 arch, Android APK ×3) to a GitHub Release**. CI does **not**
inject a server address, so every artefact published on GitHub is offline-only by design.

## Repository Layout

```
TYW Launcher/             Windows launcher project (VB.NET WPF, .NET Framework 4.8)
TYWLauncher-Linux/        Linux launcher (Avalonia 12)
TYWLauncher-macOS/        macOS launcher (same Avalonia codebase; .app / .dmg packaging in packaging/)
TYWLauncher-Android/      Android launcher (Kotlin / Jetpack Compose)
PCLCS/                    PCL core support code (from the upstream open-source release)
MeloongCore/              Git submodule (Apache-2.0, https://github.com/Meloong-Git/MeloongCore)
```

## License & Credits

Full details — including the exact files involved and what was / was not copied — are in
**[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)**. Summary:

- The original PCL2 code is © LTCat / Meloong-Git, released under its own license — see [`LICENCE`](LICENCE) (the upstream license file, kept unmodified).
- [`MeloongCore`](MeloongCore/LICENSE.txt) is licensed under **Apache-2.0** by the Meloong/PCL community.
- **Android touch controls** are modelled on **[Fold Craft Launcher (FCL)](https://github.com/FCL-Team/FoldCraftLauncher)**
  (GPL-3.0): the control layout data model, the default keymap, and the way controls are placed and edited all follow
  FCL's `ControlLayout`. FCL is a separate project and is not bundled here — only the layout design is re-implemented
  for this launcher, in `TYWLauncher-Android/.../core/TouchLayout.kt` and `.../ui/TouchEditorPage.kt`.
  Thanks to the FCL team for publishing their work.
- All modifications in this repository are by **TYW-MC**.
- The server-side implementation is proprietary and not part of this repository.

## Disclaimer

NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.

This launcher is a community modification and is not affiliated with, endorsed by, or associated with Mojang or
Microsoft. Minecraft is a trademark of Mojang Synergies AB. "Minecraft" is used here only descriptively, to indicate
which game this launcher starts.
