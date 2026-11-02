# TYW Launcher

**TYW Launcher** is an open-source, free-of-charge **launcher for Minecraft: Java Edition** for Windows, derived from
the open-source **Plain Craft Launcher 2 (PCL2)** codebase. Players sign in with **their own Microsoft account** to
launch the copy of the game they already own. No ads, no in-app purchases, no monetization.

> NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.

- Source code (auditable): https://github.com/TYW-MC/TYW-Minecraft-Launcher
- Issue tracker: https://github.com/TYW-MC/TYW-Minecraft-Launcher/issues
- Platforms:
  - **Windows** x64 · VB.NET / WPF · .NET Framework 4.8
  - **Linux** x64 · Avalonia 12 (VB.NET core + C# UI) · amd64
  - **Android** 8.0+ · Kotlin / Jetpack Compose · arm64-v8a

All three platforms share the same UI structure, the same 15-preset theme engine, the same three release channels
(Release / Snapshot / Beta) and the same server protocol.

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
**not part of any API access request**. With no server URL configured, all of them are disabled and the launcher
works standalone.

The client is fully open source, but the management server is **private**. To use the server-dependent features, you must implement your own backend and point the launcher to it.

Set the server URL in [`TYW Launcher/Modules/ModSecret.vb`](TYW%20Launcher/Modules/ModSecret.vb):

```vb
Public Const ServerAddress As String = ""  ' e.g. "http://your-server.com:8080/"
```

If `ServerAddress` is empty, **all server features are disabled** and the launcher works standalone (no announcements, no update checks, no beta/ban checks, no error reporting — everything degrades gracefully).

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
- **Java auto-provisioning**: scans the launcher's own runtime directory and common system paths; if nothing is
  found it downloads the runtime listed as `Link.JavaDownloadsAndroid` in `config.json` (falling back to
  `java-android-<arch>.zip` on the server), extracts `.zip` / `.tar.gz`, marks `bin/java` executable and stores the
  path. One button — *自动补全 Java*.
- **Touch controls**: layout data model, default keymap and editing behaviour follow **Fold Craft Launcher (FCL)** —
  movement joystick, view area, attack / use, jump / sneak / sprint, inventory, chat, drop, swap-offhand,
  perspective toggle, pause menu and the 9-slot hotbar. Positions are stored as screen-relative percentages and can
  be exported as JSON.
- **Channels**: three separately built APKs (`TYWLauncher-android.apk`, `-snapshot`, `-beta`) served from the same
  `config.json` → `Android.Channels.<channel>.{VersionCode,File}`, with the beta APK protected by device code + beta
  key on the server.

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

**Android** — `./gradlew assembleRelease -PtywChannel=release|snapshot|beta`

Or just push a tag (`v*`) — **GitHub Actions builds all three platforms automatically and attaches every artefact
(Windows exe, Linux tar.gz ×3, Android APK ×3) to a GitHub Release**.

## Repository Layout

```
TYW Launcher/             Windows launcher project (VB.NET WPF, .NET Framework 4.8)
TYWLauncher-Linux/        Linux launcher (Avalonia 12)
TYWLauncher-Android/      Android launcher (Kotlin / Jetpack Compose)
PCLCS/                    PCL core support code (from the upstream open-source release)
MeloongCore/              Git submodule (Apache-2.0, https://github.com/Meloong-Git/MeloongCore)
```

## License & Credits

- The original PCL2 code is © LTCat / Meloong-Git, released under its own license — see [`LICENCE`](LICENCE) (the upstream license file, kept unmodified).
- [`MeloongCore`](MeloongCore/LICENSE.txt) is licensed under **Apache-2.0** by the Meloong/PCL community.
- **Android touch controls** are modelled on **[Fold Craft Launcher (FCL)](https://github.com/FCL-Team/FoldCraftLauncher)**
  (GPL-3.0): the control layout data model, the default keymap, and the way controls are placed and edited all follow
  FCL's `ControlLayout`. FCL is a separate project and is not bundled here — only the layout design is re-implemented
  for this launcher. Thanks to the FCL team for publishing their work.
- All modifications in this repository are by **TYW-MC**.
- The server-side implementation is proprietary and not part of this repository.

## Disclaimer

NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.

This launcher is a community modification and is not affiliated with, endorsed by, or associated with Mojang or
Microsoft. Minecraft is a trademark of Mojang Synergies AB. "Minecraft" is used here only descriptively, to indicate
which game this launcher starts.
