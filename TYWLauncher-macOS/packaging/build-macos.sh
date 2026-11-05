#!/usr/bin/env bash
# TYW Launcher macOS 打包脚本
#
# 产物（dist/）：
#   TYWLauncher-macos-arm64.zip          Apple Silicon 正式版
#   TYWLauncher-macos-arm64-snapshot.zip 快照版
#   TYWLauncher-macos-arm64-beta.zip     内测版
#   TYWLauncher-macos-x64*.zip           Intel 版（同上三通道）
#   TYWLauncher-macos-*.dmg              同上六种，仅 macOS 上生成（hdiutil）
#   SHA256SUMS-macos.txt
#
# zip 里是一个 "TYW Launcher.app"，解压后拖进「应用程序」即可。
# dmg 里是 .app + Applications 快捷方式，双击挂载后直接把图标拖进去。
# Windows / Linux 上交叉编译时没有 hdiutil，只出 zip，dmg 会自动跳过。
#
# 用法：
#   ./packaging/build-macos.sh                 # 版本号取默认，双架构三通道全出
#   ./packaging/build-macos.sh 1.1.37          # 指定版本号
#   ./packaging/build-macos.sh 1.1.37 arm64    # 只出 Apple Silicon
#
# 说明：
#   * 可以在 Windows / Linux 上交叉编译（dotnet publish -r osx-*），
#     但 codesign 只有 macOS 上有，其他平台会跳过签名，产物仍是 ad-hoc 未签名包。
#   * 用户首次打开未签名/未公证的包时，macOS 会拦一下：
#     右键「打开」或系统设置里允许即可；用启动器自带更新下载的包会由脚本自动
#     去隔离属性并 ad-hoc 重签名，不会再拦。
set -uo pipefail

VER="${1:-1.1.37}"
if [ "$#" -gt 1 ]; then shift; ARCHES=("$@"); else ARCHES=(arm64 x64); fi

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APPNAME="TYW Launcher"
BUNDLE_BASE="cn.tywmc.launcher"
DIST="$ROOT/dist"
STAGE="$ROOT/stage"
ICNS="$ROOT/packaging/icons/AppIcon.icns"
PLIST_TPL="$ROOT/packaging/Info.plist.in"

PYTHON="${PYTHON:-}"
if [ -z "$PYTHON" ]; then
  if command -v python3 >/dev/null 2>&1; then PYTHON=python3
  elif command -v python >/dev/null 2>&1; then PYTHON=python
  else echo "需要 python3 来生成 icns / zip" >&2; exit 1; fi
fi

say() { printf '\033[36m==>\033[0m %s\n' "$*"; }
ok()  { printf '    \033[32m✓\033[0m %s\n' "$*"; }
warn(){ printf '    \033[33m!\033[0m %s\n' "$*"; }

[ -f "$ICNS" ] || "$PYTHON" "$ROOT/packaging/make_icns.py"

# 官方发行包可以在这里注入服务端地址（开源仓库默认不带任何地址）：
#   TYW_SERVER_URL=http://your-host:17403/ ./packaging/build-macos.sh 1.1.37
# 没设环境变量时读 packaging/server_url.txt（本机发版用，不入仓库）；
# 两者都没有 → 离线版。只在构建期间修改源码，脚本退出时（含失败）自动还原。
SERVER_URL="${TYW_SERVER_URL:-}"
if [ -z "$SERVER_URL" ] && [ -f "$ROOT/packaging/server_url.txt" ]; then
  SERVER_URL="$(head -n 1 "$ROOT/packaging/server_url.txt" | tr -d '\r\n')"
fi
SRV_FILE="$ROOT/src/TYW.Core/Base/ModServerUrl.vb"
if [ -n "$SERVER_URL" ]; then
  SRV_BACKUP="$(mktemp "${TMPDIR:-/tmp}/tyw-srv.XXXXXX")"
  cp "$SRV_FILE" "$SRV_BACKUP"
  trap 'cp "$SRV_BACKUP" "$SRV_FILE" 2>/dev/null; rm -f "$SRV_BACKUP"' EXIT
  "$PYTHON" - "$SRV_FILE" "$SERVER_URL" <<'PY'
import io, re, sys
path, url = sys.argv[1], sys.argv[2].strip()
if not url.endswith("/"):
    url += "/"
s = io.open(path, encoding="utf-8-sig", newline="").read()
s2 = re.sub(r'(Public Const BuiltIn As String = )"[^"]*"', lambda m: m.group(1) + '"' + url + '"', s, count=1)
if s2 == s:
    raise SystemExit("找不到 BuiltIn 常量，注入失败")
io.open(path, "w", encoding="utf-8-sig", newline="").write(s2)
PY
  [ $? -eq 0 ] || { echo "注入服务端地址失败" >&2; exit 1; }
  ok "已注入服务端地址：$SERVER_URL"
else
  warn "未设置 TYW_SERVER_URL / server_url.txt，产物为离线版（不带任何服务器地址）"
fi

mkdir -p "$DIST" "$STAGE"

build_id() { case "$1" in Snapshot) echo "2" ;; Beta) echo "3" ;; *) echo "1" ;; esac; }
suffix_of() { case "$1" in Snapshot) echo "-snapshot" ;; Beta) echo "-beta" ;; *) echo "" ;; esac; }
display_of() {
  case "$1" in
    Snapshot) echo "TYW Launcher 快照版" ;;
    Beta)     echo "TYW Launcher 内测版" ;;
    *)        echo "TYW Launcher" ;;
  esac
}

for arch in "${ARCHES[@]}"; do
  case "$arch" in
    arm64) rid="osx-arm64" ;;
    x64)   rid="osx-x64" ;;
    *) echo "未知架构：$arch（只支持 arm64 / x64）" >&2; exit 1 ;;
  esac

  for ch in Release Snapshot Beta; do
    say "$rid $ch"
    out="$ROOT/out/macos-$arch-$ch"
    rm -rf "$out"

    dotnet publish "$ROOT/src/TYW.Desktop/TYW.Desktop.csproj" \
      -c Release -r "$rid" --self-contained true \
      -p:TYWChannel="$ch" \
      -p:Version="$VER" \
      -o "$out" >/dev/null || { echo "编译失败：$rid $ch" >&2; exit 1; }
    ok "已编译 $out"

    app="$STAGE/$arch-$ch/$APPNAME.app"
    rm -rf "$STAGE/$arch-$ch"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

    cp -a "$out/." "$app/Contents/MacOS/"
    rm -f "$app/Contents/MacOS/"*.pdb
    cp "$ICNS" "$app/Contents/Resources/AppIcon.icns"
    printf 'APPL????' > "$app/Contents/PkgInfo"

    sed -e "s|@VERSION@|$VER|g" \
        -e "s|@BUILD@|$VER.$(( $(build_id "$ch") + 100 ))|g" \
        -e "s|@BUNDLEID@|$BUNDLE_BASE$( [ "$ch" = Release ] || echo ".$(echo "$ch" | tr '[:upper:]' '[:lower:]')" )|g" \
        -e "s|@DISPLAY@|$(display_of "$ch")|g" \
        "$PLIST_TPL" > "$app/Contents/Info.plist"

    chmod +x "$app/Contents/MacOS/TYWLauncher" 2>/dev/null || true
    chmod +x "$app/Contents/MacOS/"*.dylib 2>/dev/null || true

    # 有 codesign 就做一次 ad-hoc 签名：Apple Silicon 上完全没签名的包会被直接拒绝
    if command -v codesign >/dev/null 2>&1; then
      xattr -cr "$app" 2>/dev/null || true
      if codesign --force --deep --sign - "$app" >/dev/null 2>&1; then
        ok "已 ad-hoc 签名"
      else
        warn "签名失败，产物仍可运行但需要右键打开"
      fi
    else
      warn "非 macOS 环境，跳过签名"
    fi

    name="TYWLauncher-macos-$arch$(suffix_of "$ch").zip"
    "$PYTHON" "$ROOT/packaging/zip_app.py" "$STAGE/$arch-$ch" "$DIST/$name" "$APPNAME.app" || exit 1

    # dmg：只有 macOS 有 hdiutil，其它平台 make_dmg.sh 返回 2，静默跳过
    dmgname="TYWLauncher-macos-$arch$(suffix_of "$ch").dmg"
    bash "$ROOT/packaging/make_dmg.sh" "$STAGE/$arch-$ch" "$DIST/$dmgname" \
        "$APPNAME.app" "$(display_of "$ch")"
    dmg_rc=$?
    if [ "$dmg_rc" -eq 0 ]; then
      ok "已生成 $dmgname"
    elif [ "$dmg_rc" -ne 2 ]; then
      echo "dmg 生成失败：$dmgname（返回 $dmg_rc）" >&2
      exit 1
    fi

    rm -rf "$STAGE/$arch-$ch"
  done
done

say "校验和"
# 非 macOS 上不会生成 dmg，用 ls 过滤一遍，避免把未展开的通配符当文件名
LIST="$( cd "$DIST" && ls TYWLauncher-macos-*.zip TYWLauncher-macos-*.dmg 2>/dev/null | sort )"
if command -v sha256sum >/dev/null 2>&1; then
  ( cd "$DIST" && sha256sum $LIST > SHA256SUMS-macos.txt )
else
  ( cd "$DIST" && shasum -a 256 $LIST > SHA256SUMS-macos.txt )
fi
ok "$DIST/SHA256SUMS-macos.txt"
ls -la "$DIST"/TYWLauncher-macos-*
