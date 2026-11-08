#!/usr/bin/env bash
# TYW Launcher Linux 打包脚本
#
# 一次生成常用发行版格式：
#   tar.gz        通用免安装（任意发行版解压即用）
#   deb           Debian / Ubuntu / Deepin / UOS / 麒麟
#   rpm           Fedora / RHEL / CentOS / openSUSE
#   pkg.tar.zst   Arch / Manjaro / EndeavourOS
#   AppImage      通用单文件（不需要安装）
#
# 用法：
#   ./packaging/build.sh [版本号] [架构...]
#   ./packaging/build.sh 1.0.0                 # 自动探测 out/ 下已发布的架构
#   ./packaging/build.sh 1.0.0 x64 arm64       # 指定架构
#
# 环境变量（CI / 多通道时用，本机单通道构建留空即可）：
#   TYW_CHANNEL   Release | Snapshot | Beta
#                 指定后 tar.gz 用服务端认的固定名（TYWLauncher-linux-<arch>[-snapshot|-beta].tar.gz），
#                 deb / rpm / pkg.tar.zst / AppImage 的版本号带上通道标记，避免三个通道互相覆盖。
#   TYW_SRC_DIR   发布产物目录（默认 out/linux-<arch>）。CI 里三通道分别发布到 out/ch-<通道>。
#
# 依赖（缺失的格式会自动跳过，不会中断）：
#   dpkg-deb      deb        apt install dpkg
#   rpmbuild      rpm        apt install rpm        / dnf install rpm-build
#   zstd          pkg.tar.zst apt install zstd
#   mksquashfs    AppImage 的备用打包路径
#   appimagetool  AppImage  脚本会自动下载到 .tools/（也可自行放到 PATH），按目标架构下载
#
# 依赖（缺失的格式会自动跳过，不会中断）：
#   dpkg-deb      deb        apt install dpkg
#   rpmbuild      rpm        apt install rpm        / dnf install rpm-build
#   zstd          pkg.tar.zst apt install zstd
#   mksquashfs    AppImage 的备用打包路径
#   appimagetool  AppImage  脚本会自动下载到 .tools/（也可自行放到 PATH）
set -uo pipefail

VER="${1:-1.0.0}"
shift || true
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="tyw-launcher"
APPNAME="TYW Launcher"
OUT="$ROOT/dist"
ICONS="$ROOT/packaging/icons"
TOOLS="$ROOT/.tools"
WORK="$OUT/.work"

say()  { printf '\033[36m==>\033[0m %s\n' "$*"; }
ok()   { printf '    \033[32m✓\033[0m %s\n' "$*"; }
skip() { printf '    \033[33m-\033[0m 跳过 %s：%s\n' "$1" "$2"; }
die()  { printf '\033[31m错误：%s\033[0m\n' "$*" >&2; exit 1; }

# ---------------------------------------------------------------- 通道
# 留空 = 本机单通道构建，文件名与历史版本完全一致。
CHANNEL="${TYW_CHANNEL:-}"
case "$CHANNEL" in
  Release|"") CH_SUF=""        ;;   # 正式版
  Snapshot)   CH_SUF="-snapshot" ;;
  Beta)       CH_SUF="-beta"     ;;
  *) die "未知通道：$CHANNEL（应为 Release / Snapshot / Beta）" ;;
esac
# 各包格式的版本号标记。deb 用 ~ 让它排在正式版之前；rpm 的 Release / Arch 的 pkgver
# 不允许 ~，改用 . 与 _ 分隔。
case "$CHANNEL" in
  Snapshot) DEB_CH="~snapshot"; RPM_REL="1.snapshot"; PAC_CH="_snapshot" ;;
  Beta)     DEB_CH="~beta";     RPM_REL="1.beta";     PAC_CH="_beta"     ;;
  *)        DEB_CH="";          RPM_REL="1";          PAC_CH=""         ;;
esac

# 发布产物目录：默认 out/linux-<arch>，CI 里由 TYW_SRC_DIR 指向 out/ch-<通道>
src_dir() { # $1=arch
  if [ -n "${TYW_SRC_DIR:-}" ]; then
    printf '%s' "$TYW_SRC_DIR"
  else
    printf '%s' "$ROOT/out/linux-$1"
  fi
}

# 官方发行包注入服务端地址（开源仓库默认不带任何地址）。
# TYW_SERVER_URL 优先，其次 packaging/server_url.txt（本机发版用，不入仓库）。
SRV_URL="${TYW_SERVER_URL:-}"
if [ -z "$SRV_URL" ] && [ -f "$ROOT/packaging/server_url.txt" ]; then
  SRV_URL="$(head -n 1 "$ROOT/packaging/server_url.txt" | tr -d '\r\n')"
fi

mkdir -p "$OUT"
rm -rf "$WORK"
mkdir -p "$WORK"

# ---------------------------------------------------------------- 架构映射
# 目录名 -> (deb 架构, rpm 架构, arch 架构, appimage 架构)
deb_arch()     { case "$1" in x64) echo amd64 ;; arm64) echo arm64 ;; *) echo amd64 ;; esac; }
rpm_arch()     { case "$1" in x64) echo x86_64 ;; arm64) echo aarch64 ;; *) echo x86_64 ;; esac; }
pac_arch()     { case "$1" in x64) echo x86_64 ;; arm64) echo aarch64 ;; *) echo x86_64 ;; esac; }
appimg_arch()  { case "$1" in x64) echo x86_64 ;; arm64) echo aarch64 ;; *) echo x86_64 ;; esac; }

probe_arches() {
  local found=()
  for d in "$ROOT"/out/linux-*; do
    [ -d "$d" ] || continue
    [ -f "$d/TYWLauncher" ] || continue
    found+=("${d##*/linux-}")
  done
  [ ${#found[@]} -eq 0 ] && return 1
  printf '%s\n' "${found[@]}"
}

if [ $# -gt 0 ]; then
  ARCHES=("$@")
else
  mapfile -t ARCHES < <(probe_arches) || die "out/ 下没有已发布的产物，先执行 dotnet publish"
fi

printf '版本：%s    架构：%s    通道：%s\n\n' "$VER" "${ARCHES[*]}" "${CHANNEL:-单通道}"

# ---------------------------------------------------------------- 公共文件
desktop_file() {
  cat <<EOF
[Desktop Entry]
Type=Application
Name=$APPNAME
Name[zh_CN]=TYW 启动器
GenericName=Minecraft Launcher
GenericName[zh_CN]=Minecraft 启动器
Comment=Launch Minecraft with TYW Launcher
Comment[zh_CN]=使用 TYW 启动器启动 Minecraft
Exec=/usr/bin/$APP %U
TryExec=/usr/bin/$APP
Icon=$APP
Terminal=false
Categories=Game;
Keywords=minecraft;launcher;tyw;
StartupWMClass=TYWLauncher
StartupNotify=true
EOF
}

# 按 FHS 摆放：程序在 /opt，命令与图标做链接/安装
stage_fhs() { # $1=arch $2=目标目录
  local arch="$1" pub stage
  pub="$(src_dir "$1")"; stage="$2"
  rm -rf "$stage"
  mkdir -p "$stage/opt/$APP" \
           "$stage/usr/bin" \
           "$stage/usr/share/applications" \
           "$stage/usr/share/doc/$APP"
  cp -a "$pub/." "$stage/opt/$APP/"
  chmod +x "$stage/opt/$APP/TYWLauncher"
  ln -sf "/opt/$APP/TYWLauncher" "$stage/usr/bin/$APP"
  desktop_file > "$stage/usr/share/applications/$APP.desktop"
  local size
  for size in 32 48 64 128 256; do
    [ -f "$ICONS/tyw-$size.png" ] || continue
    install -Dm644 "$ICONS/tyw-$size.png" \
      "$stage/usr/share/icons/hicolor/${size}x${size}/apps/$APP.png"
  done
  cat > "$stage/usr/share/doc/$APP/copyright" <<EOF
$APPNAME $VER
https://tywmc.cn
EOF
}

# ================================================================ tar.gz
build_tarball() { # $1=arch
  local arch="$1" pub stage
  pub="$(src_dir "$1")"
  stage="$WORK/tar-$arch/$APP-$VER"
  mkdir -p "$stage"
  cp -a "$pub/." "$stage/"
  cp "$ICONS/tyw-256.png" "$stage/$APP.png"
  desktop_file | sed "s|/usr/bin/$APP|$APP|" > "$stage/$APP.desktop"
  cat > "$stage/$APP" <<'SH'
#!/bin/sh
# 免安装启动入口
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/TYWLauncher" "$@"
SH
  chmod +x "$stage/$APP" "$stage/TYWLauncher"
  local out
  if [ -n "$CHANNEL" ]; then
    # 服务端 config.json 的 Linux.Channels.*.File / FileArm64 认这套固定名，别改
    out="$OUT/TYWLauncher-linux-${arch}${CH_SUF}.tar.gz"
  else
    out="$OUT/${APP}-${VER}-linux-${arch}.tar.gz"
  fi
  tar -C "$WORK/tar-$arch" -czf "$out" "$APP-$VER"
  ok "$(basename "$out")"
}

# ================================================================ deb
build_deb() { # $1=arch
  local arch="$1" darch; darch="$(deb_arch "$arch")"
  local stage="$WORK/deb-$arch" pkg="$WORK/debroot-$arch"
  command -v dpkg-deb >/dev/null || { skip deb "没有 dpkg-deb"; return 0; }
  stage_fhs "$arch" "$stage"
  rm -rf "$pkg"; mkdir -p "$pkg/DEBIAN"
  cp -a "$stage/." "$pkg/"
  local installed size
  installed=$(du -sk "$pkg" | cut -f1)
  size=$(du -sb "$pkg/opt" | cut -f1)
  cat > "$pkg/DEBIAN/control" <<EOF
Package: $APP
Version: $VER$DEB_CH
Section: games
Priority: optional
Architecture: $darch
Installed-Size: $installed
Depends: libicu76 | libicu74 | libicu72 | libicu70 | libicu67, libssl3t64 | libssl3 | libssl1.1, libx11-6, libice6, libsm6, libfontconfig1, libgl1, libxrandr2, libxi6, libxcursor1
Recommends: openjdk-21-jre | openjdk-17-jre | default-jre
Maintainer: TYW <support@tywmc.cn>
Homepage: https://tywmc.cn
Description: TYW Launcher for Linux
 TYW Minecraft 启动器的 Linux 版本，界面与 Windows 版一致。
 支持原版游戏下载安装、离线与微软账户登录、启动游戏。
EOF
  # 让 dpkg 知道要建菜单缓存
  mkdir -p "$pkg/DEBIAN"
  local out="$OUT/${APP}_${VER}${DEB_CH}_${darch}.deb"
  dpkg-deb --build --root-owner-group "$pkg" "$out" >/dev/null || { skip deb "dpkg-deb 打包失败"; return 0; }
  ok "$(basename "$out")  $(du -h "$out" | cut -f1)"
}

# ================================================================ rpm
build_rpm() { # $1=arch
  local arch="$1" rarch; rarch="$(rpm_arch "$arch")"
  command -v rpmbuild >/dev/null || { skip rpm "没有 rpmbuild"; return 0; }
  local stage="$WORK/rpm-$arch-root" top="$WORK/rpm-$arch"
  stage_fhs "$arch" "$stage"
  rm -rf "$top"; mkdir -p "$top"/{BUILD,RPMS,SOURCES,SPECS,SRPMS}
  cat > "$top/SPECS/$APP.spec" <<EOF
Name:           $APP
Version:        $VER
Release:        $RPM_REL
Summary:        TYW Launcher for Linux
License:        Proprietary
URL:            https://tywmc.cn
BuildArch:      $rarch
AutoReqProv:    no
Requires:       libicu, openssl-libs, libX11, libICE, libSM, fontconfig, mesa-libGL, libXrandr, libXcursor
Recommends:     java-21-openjdk-headless

%description
TYW Launcher 的 Linux 版本，界面与 Windows 版一致。
支持原版游戏下载安装、离线与微软账户登录、启动游戏。

%install
rm -rf %{buildroot}
mkdir -p %{buildroot}
cp -a $stage/. %{buildroot}/

%files
/opt/$APP
/usr/bin/$APP
/usr/share/applications/$APP.desktop
/usr/share/icons/hicolor
/usr/share/doc/$APP

%changelog
* $(LC_ALL=C date '+%a %b %d %Y') TYW <support@tywmc.cn> - $VER-$RPM_REL
- Release $VER${CH_SUF}
EOF
  # 交叉构建：rpmbuild 默认只允许给本机架构打包，
  # 在 rpmrc 里补一条 buildarch_compat 才能产出 aarch64 包。
  local rc_args=()
  local host_arch rc_file
  host_arch=$(rpm --eval '%{_arch}' 2>/dev/null | head -1)
  rc_file="$(rpm --eval '%{_rpmconfigdir}' 2>/dev/null)/rpmrc"
  if [ -n "$host_arch" ] && [ "$host_arch" != "$rarch" ] && [ -f "$rc_file" ]; then
    cp "$rc_file" "$WORK/rpmrc-$arch"
    echo "buildarch_compat: $host_arch: $rarch" >> "$WORK/rpmrc-$arch"
    rc_args=(--rcfile "$WORK/rpmrc-$arch")
  fi
  rpmbuild -bb --target "$rarch" "${rc_args[@]}" \
    --define "_topdir $top" \
    --define "_rpmdir $top/RPMS" \
    --define "__os_install_post %{nil}" \
    --define "debug_package %{nil}" \
    --define "_binary_payload w6.xzdio" \
    --define "_build_id_links none" \
    "$top/SPECS/$APP.spec" >"$WORK/rpm-$arch.log" 2>&1 || {
      skip rpm "rpmbuild 失败，日志：$WORK/rpm-$arch.log"; return 0; }
  local built
  built=$(find "$top/RPMS" -name "*.rpm" | head -1)
  [ -n "$built" ] || { skip rpm "没有产出 rpm"; return 0; }
  local out="$OUT/${APP}-${VER}-${RPM_REL}.${rarch}.rpm"
  cp "$built" "$out"
  ok "$(basename "$out")  $(du -h "$out" | cut -f1)"
}

# ================================================================ Arch (pkg.tar.zst)
build_pacman() { # $1=arch
  local arch="$1" parch; parch="$(pac_arch "$arch")"
  command -v zstd >/dev/null || { skip "pkg.tar.zst" "没有 zstd"; return 0; }
  local stage="$WORK/pac-$arch"
  stage_fhs "$arch" "$stage"
  local size
  size=$(du -sb "$stage" | cut -f1)
  {
    echo "pkgname = $APP"
    echo "pkgbase = $APP"
    echo "pkgver = $VER$PAC_CH-1"
    echo "pkgdesc = TYW Launcher for Linux"
    echo "url = https://tywmc.cn"
    echo "builddate = $(date +%s)"
    echo "packager = TYW <support@tywmc.cn>"
    echo "size = $size"
    echo "arch = $parch"
    echo "license = custom"
    echo "depend = icu"
    echo "depend = openssl"
    echo "depend = libx11"
    echo "depend = libice"
    echo "depend = libsm"
    echo "depend = fontconfig"
    echo "depend = mesa"
    echo "depend = libxrandr"
    echo "depend = libxcursor"
  } > "$stage/.PKGINFO"
  local out="$OUT/${APP}-${VER}${PAC_CH}-1-${parch}.pkg.tar.zst"
  ( cd "$stage" && tar --owner=0 --group=0 --numeric-owner --zstd -cf "$out" .PKGINFO opt usr ) \
    || { skip "pkg.tar.zst" "tar 打包失败"; return 0; }
  ok "$(basename "$out")  $(du -h "$out" | cut -f1)"
}

# ================================================================ AppImage
# AppImage = type2-runtime（约 900KB 的 ELF）+ 追加的 squashfs。
# 用 appimagetool 生成；runtime 从 type2-runtime 仓库取（国内走加速镜像）。
RT_BASE="https://github.com/AppImage/type2-runtime/releases/download/continuous"
AIT_BASE="https://github.com/AppImage/appimagetool/releases/download/continuous"

# 通用下载：curl 优先（带重试、跟随跳转），没有 curl 才用 wget。
# GitHub 的 release 资源会 302 到 objects.githubusercontent.com，走 wget 偶尔会
# 在 CI 里直接失败，所以这里多轮重试 + 镜像轮换，并且按大小校验。
#   $1=url  $2=输出文件  $3=最小字节数
fetch_file() {
  local url="$1" out="$2" minsize="$3" i p size
  local mirrors=("" "https://ghproxy.net/" "https://gh-proxy.com/" "https://ghfast.top/")
  mkdir -p "$(dirname "$out")"
  for i in 1 2 3; do
    for p in "${mirrors[@]}"; do
      rm -f "$out.part"
      if command -v curl >/dev/null 2>&1; then
        curl -fsSL --retry 2 --retry-delay 2 --connect-timeout 20 --max-time 240 \
             -o "$out.part" "$p$url" >/dev/null 2>&1
      elif command -v wget >/dev/null 2>&1; then
        wget -q -T 90 -t 2 -O "$out.part" "$p$url" >/dev/null 2>&1
      else
        return 1
      fi
      size=$(stat -c%s "$out.part" 2>/dev/null || echo 0)
      if [ "$size" -ge "$minsize" ]; then
        mv "$out.part" "$out"; echo "$size"; return 0
      fi
    done
    [ "$i" -lt 3 ] && sleep 3
  done
  rm -f "$out.part"
  return 1
}

fetch_runtime() { # $1=x86_64|aarch64 -> 打印 runtime 路径
  local iarch="$1" dst="$TOOLS/runtime-$iarch"
  if [ -s "$dst" ] && [ "$(stat -c%s "$dst")" -gt 100000 ]; then echo "$dst"; return 0; fi
  # runtime 约 900KB
  fetch_file "$RT_BASE/runtime-$iarch" "$dst" 100000 >/dev/null || return 1
  chmod +x "$dst"
  echo "$dst"
}

ensure_appimagetool() { # $1=x86_64|aarch64 -> 打印 appimagetool 入口
  command -v appimagetool >/dev/null && { command -v appimagetool; return 0; }
  local iarch="$1" home ai
  home="$TOOLS/appimagetool-$iarch"
  [ -x "$home/AppRun" ] && { echo "$home/AppRun"; return 0; }
  ai="$TOOLS/ai-$iarch.AppImage"
  # 按目标架构取工具：arm64 上跑 x86_64 的 AppImage 需要 binfmt，直接取 aarch64 版
  # appimagetool 约 15MB，低于 1MB 视为失败
  fetch_file "$AIT_BASE/appimagetool-${iarch}.AppImage" "$ai" 1000000 >/dev/null || { rm -f "$ai"; return 1; }
  chmod +x "$ai"
  rm -rf "$TOOLS/squashfs-root"
  if ( cd "$TOOLS" && "./ai-$iarch.AppImage" --appimage-extract >/dev/null 2>&1 ) \
       && [ -x "$TOOLS/squashfs-root/AppRun" ]; then
    rm -rf "$home"
    mv "$TOOLS/squashfs-root" "$home"
    rm -f "$ai"
    echo "$home/AppRun"; return 0
  fi
  rm -f "$ai"
  return 1
}

make_appdir() { # $1=arch -> 打印 AppDir 路径
  local arch="$1" appdir="$WORK/AppDir-$arch"
  rm -rf "$appdir"
  mkdir -p "$appdir/usr/bin" "$appdir/usr/share/icons/hicolor/256x256/apps"
  cp -a "$(src_dir "$arch")/." "$appdir/usr/bin/"
  chmod +x "$appdir/usr/bin/TYWLauncher"
  install -Dm644 "$ICONS/tyw-256.png" "$appdir/usr/share/icons/hicolor/256x256/apps/$APP.png"
  cp "$ICONS/tyw-256.png" "$appdir/$APP.png"
  cp "$ICONS/tyw-256.png" "$appdir/.DirIcon"
  desktop_file | sed "s|/usr/bin/$APP|$APP|" > "$appdir/$APP.desktop"
  cat > "$appdir/AppRun" <<'SHX'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
export TYW_APPIMAGE=1
exec "$HERE/usr/bin/TYWLauncher" "$@"
SHX
  chmod +x "$appdir/AppRun"
  echo "$appdir"
}

build_appimage() { # $1=arch
  local arch="$1" iarch; iarch="$(appimg_arch "$arch")"
  local tool rt
  tool="$(ensure_appimagetool "$iarch")" || { skip AppImage "没有 ${iarch} 版 appimagetool 且下载失败"; return 0; }
  rt="$(fetch_runtime "$iarch")" || { skip AppImage "type2 runtime 下载失败（离线？）"; return 0; }
  local appdir out
  appdir="$(make_appdir "$arch")"
  out="$OUT/${APPNAME// /_}-${VER}${CH_SUF}-${iarch}.AppImage"
  # appimagetool 自带联网下载 runtime，国内基本不通，这里显式指定
  if ARCH="$iarch" "$tool" --no-appstream --runtime-file "$rt" "$appdir" "$out" \
       >"$WORK/appimage-$arch.log" 2>&1 && [ -s "$out" ]; then
    chmod +x "$out"
    ok "$(basename "$out")  $(du -h "$out" | cut -f1)"
  else
    rm -f "$out"
    skip AppImage "appimagetool 失败，日志：$WORK/appimage-$arch.log"
  fi
  rm -rf "$appdir"
}

# ================================================================ 主流程
for arch in "${ARCHES[@]}"; do
  SRC="$(src_dir "$arch")"
  [ -f "$SRC/TYWLauncher" ] || die "缺少 ${SRC}，先执行 dotnet publish -r linux-$arch"
  echo "[linux-$arch]"
  # 地址只写进包里的 server.txt，源码里查不到；5 种格式都会带上它
  if [ -n "$SRV_URL" ]; then
    printf '%s\n' "$SRV_URL" > "$SRC/server.txt"
    echo "已写入 server.txt：$SRV_URL"
  fi
  build_tarball "$arch"
  build_deb     "$arch"
  build_rpm     "$arch"
  build_pacman  "$arch"
  build_appimage "$arch"
  echo
done

# 校验和
( cd "$OUT" && rm -f SHA256SUMS && \
  find . -maxdepth 1 -type f ! -name 'SHA256SUMS*' -printf '%f\n' | sort | \
  xargs -r sha256sum > SHA256SUMS 2>/dev/null )
ok "SHA256SUMS"

rm -rf "$WORK"
echo
say "产物目录：$OUT"
ls -lh "$OUT" | grep -v '^total'
