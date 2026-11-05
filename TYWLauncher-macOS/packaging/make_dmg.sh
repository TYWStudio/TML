#!/usr/bin/env bash
# 把打好包的 .app 目录做成 DMG（macOS 上才有的交付格式）。
#
# 用法：
#   ./packaging/make_dmg.sh <含 .app 的目录> <输出 dmg> <包名> <卷名>
# 例：
#   ./packaging/make_dmg.sh stage/arm64-Release dist/TYWLauncher-macos-arm64.dmg "TYW Launcher.app" "TYW Launcher"
#
# 说明：
#   * 只有 macOS 自带 hdiutil，Windows / Linux 上调用会直接返回 2（调用方据此跳过）。
#   * 卷里除了 .app 还放一个 /Applications 符号链接，用户打开 dmg 直接把图标拖进去即可，
#     不用自己找「应用程序」文件夹。
#   * UDZO = zlib 压缩的只读镜像，签名过的 .app 原样写入，不会破坏 ad-hoc 签名。
#   * 不做 Finder 窗口美化（图标排布 / 背景图）：那需要 AppleScript 驱动 Finder，
#     CI 的无头 runner 上经常卡住或失败，收益不值得这份不确定性。
set -uo pipefail

if [ "$#" -ne 4 ]; then
    echo "用法: $0 <含 .app 的目录> <输出 dmg> <包名> <卷名>" >&2
    exit 2
fi

SRC_DIR="$1"
OUT="$2"
APPNAME="$3"
VOLNAME="$4"

if ! command -v hdiutil >/dev/null 2>&1; then
    echo "当前平台没有 hdiutil（非 macOS），跳过 dmg" >&2
    exit 2
fi

if [ ! -d "$SRC_DIR/$APPNAME" ]; then
    echo "找不到 .app 目录：$SRC_DIR/$APPNAME" >&2
    exit 1
fi

WORK="$(mktemp -d "${TMPDIR:-/tmp}/tyw-dmg.XXXXXX")" || exit 1
trap 'rm -rf "$WORK"' EXIT

# 复制时保留权限位与符号链接（.app 里的框架靠链接组织，-R 不能跟 -L）
cp -a "$SRC_DIR/$APPNAME" "$WORK/$APPNAME" || exit 1
ln -s /Applications "$WORK/Applications"

mkdir -p "$(dirname "$OUT")"
rm -f "$OUT"

# -fs HFS+ 显式指定文件系统：默认在某些系统上会挑 APFS，APFS 卷在 10.13 以下打不开
# CI 上偶发「Resource busy」（刚卸载的上一个镜像还没释放），重试几次即可
for attempt in 1 2 3 4; do
  if hdiutil create \
      -volname "$VOLNAME" \
      -srcfolder "$WORK" \
      -fs HFS+ \
      -format UDZO \
      -ov \
      "$OUT" >/dev/null 2>&1; then
    echo "已生成 $OUT（第 $attempt 次）"
    exit 0
  fi
  echo "hdiutil create 失败（第 $attempt 次），2 秒后重试" >&2
  rm -f "$OUT"
  sleep 2
done

echo "hdiutil create 连续失败，放弃：$OUT" >&2
exit 1
