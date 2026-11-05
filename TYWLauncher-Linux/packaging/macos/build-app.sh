#!/bin/sh
# 把 dotnet publish 的产物打成 macOS 的 .app 包，再压成 zip（给服务端 www 分发用）。
#
# 用法（必须在 macOS 上跑，需要 codesign / ditto）：
#   ./packaging/macos/build-app.sh <publish目录> <通道> <架构> [输出目录]
#   ./packaging/macos/build-app.sh ../out/ch-Release Release arm64
#
# 通道：Release / Snapshot / Beta —— 对应三套独立 bundle id，可以同时装在「应用程序」里。
# 架构：arm64（Apple Silicon）/ x64（Intel）—— 必须和 dotnet publish 的 -r 一致：
#        osx-arm64 → arm64，osx-x64 → x64。
set -eu

PUBLISH_DIR="${1:?用法: build-app.sh <publish目录> <通道> <架构> [输出目录]}"
CHANNEL="${2:?缺少通道 Release/Snapshot/Beta}"
ARCH="${3:?缺少架构 arm64/x64}"
OUT_DIR="${4:-../dist}"

HERE="$(cd "$(dirname "$0")" && pwd)"
ICNS="$HERE/TYWLauncher.icns"
VERSION="1.1.36"
MIN_MACOS="11.0"

case "$CHANNEL" in
  Release)  APP_NAME="TYW Launcher";          BUNDLE_ID="cn.tywmc.launcher";           BUNDLE_VER="1" ;;
  Snapshot) APP_NAME="TYW Launcher Snapshot"; BUNDLE_ID="cn.tywmc.launcher.snapshot";  BUNDLE_VER="2" ;;
  Beta)     APP_NAME="TYW Launcher Beta";     BUNDLE_ID="cn.tywmc.launcher.beta";      BUNDLE_VER="3" ;;
  *) echo "未知通道：$CHANNEL" >&2; exit 1 ;;
esac

case "$CHANNEL" in
  Release)  FILE_SUFFIX="" ;;
  Snapshot) FILE_SUFFIX="-snapshot" ;;
  Beta)     FILE_SUFFIX="-beta" ;;
esac

[ -f "$PUBLISH_DIR/TYWLauncher" ] || { echo "publish 目录里没有 TYWLauncher：$PUBLISH_DIR" >&2; exit 1; }
[ -f "$ICNS" ] || { echo "缺少图标 ${ICNS}（先跑 make_icns.py）" >&2; exit 1; }

mkdir -p "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"
STAGE="$(mktemp -d "${TMPDIR:-/tmp}/tyw-mac.XXXXXX")"
trap 'rm -rf "$STAGE"' EXIT

APP="$STAGE/$APP_NAME.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# 1) 程序本体
cp -R "$PUBLISH_DIR/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/TYWLauncher"

# 2) 图标 + PkgInfo
cp "$ICNS" "$APP/Contents/Resources/tyw-launcher.icns"
printf 'APPL????' > "$APP/Contents/PkgInfo"

# 3) Info.plist
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleName</key>
	<string>$APP_NAME</string>
	<key>CFBundleDisplayName</key>
	<string>$APP_NAME</string>
	<key>CFBundleIdentifier</key>
	<string>$BUNDLE_ID</string>
	<key>CFBundleExecutable</key>
	<string>TYWLauncher</string>
	<key>CFBundleIconFile</key>
	<string>tyw-launcher</string>
	<key>CFBundlePackageType</key>
	<string>APPL</string>
	<key>CFBundleShortVersionString</key>
	<string>$VERSION</string>
	<key>CFBundleVersion</key>
	<string>$BUNDLE_VER</string>
	<key>CFBundleInfoDictionaryVersion</key>
	<string>6.0</string>
	<key>LSMinimumSystemVersion</key>
	<string>$MIN_MACOS</string>
	<key>LSApplicationCategoryType</key>
	<string>public.app-category.games</string>
	<key>NSHighResolutionCapable</key>
	<true/>
	<key>NSSupportsAutomaticGraphicsSwitching</key>
	<true/>
	<key>NSRequiresAquaSystemAppearance</key>
	<false/>
</dict>
</plist>
PLIST

# 4) 临时签名：Apple Silicon 上未签名的可执行文件会被内核直接杀掉，
#    必须至少做 ad-hoc 签名（--sign -）。正式公证需要开发者证书，这里不做。
codesign --force --deep --sign - "$APP" 2>/dev/null || {
  echo "警告：codesign 失败（ad-hoc 签名没成功），arm64 上可能无法启动" >&2
}

# 5) 打包：外层目录里放 .app + 安装说明，ditto 才能保留符号链接与权限
PKG_NAME="TYWLauncher-macos-$ARCH$FILE_SUFFIX"
PKG_ROOT="$STAGE/$PKG_NAME"
mkdir -p "$PKG_ROOT"
cp -R "$APP" "$PKG_ROOT/"

cat > "$PKG_ROOT/安装说明.txt" <<'README'
TYW Launcher（macOS）
=====================

1. 把 TYW Launcher（或 TYW Launcher Snapshot / Beta）拖进「应用程序」文件夹。
2. 第一次打开如果提示「无法打开，因为无法验证开发者」：
   在图标上右键 →「打开」→ 再点「打开」；
   或者打开终端执行：xattr -dr com.apple.quarantine "/Applications/TYW Launcher.app"
3. 启动器需要 Java 才能启动游戏：可从 https://adoptium.net 装 Temurin（Minecraft 1.20.5+ 需要 Java 21）。
   Java 必须与启动器架构一致（Apple Silicon 用 aarch64 版，Intel 用 x64 版）。
4. 游戏目录默认是 ~/Library/Application Support/minecraft，与官方启动器共用。

本包为 ad-hoc 签名（未做 Gatekeeper 公证），所以第一次需要手动放行。
macOS 版暂不支持启动器内自动更新与联机（详见启动器内说明）。
README

( cd "$STAGE" && ditto -c -k --sequesterRsrc --keepParent "$PKG_NAME" "$OUT_DIR/$PKG_NAME.zip" )

echo "已生成：$OUT_DIR/$PKG_NAME.zip"
ls -lh "$OUT_DIR/$PKG_NAME.zip"
