#!/usr/bin/env python3
"""把 .app 目录压成 zip，并保住 Unix 权限位。

为什么不用现成的 zip 命令：Windows 上没有 zip/ditto，而 macOS 上解压后会按
zip 里记录的外部属性决定可执行位。这里手动写 external_attr，保证
Contents/MacOS/TYWLauncher（apphost）和 *.dylib 解压出来就是可执行的，
否则用户双击时会被系统拒绝（"无法打开，因为无法验证开发者"或直接闪退）。

用法：
    python packaging/zip_app.py <父目录> <输出 zip> <包名>
例：
    python packaging/zip_app.py stage/arm64-Release dist/TYWLauncher-macos-arm64.zip "TYW Launcher.app"
"""
import os
import sys
import time
import zipfile
from pathlib import Path

EXEC_SUFFIX = (".dylib", ".so", ".jnilib")


def mode_for(rel: Path, name: str) -> int:
    if rel.parts and rel.parts[0] == "Contents" and len(rel.parts) > 1 and rel.parts[1] == "MacOS":
        if name == "TYWLauncher" or name.endswith(EXEC_SUFFIX) or "." not in name:
            return 0o755
    return 0o644


def main() -> int:
    if len(sys.argv) != 4:
        print(__doc__)
        return 2
    root = Path(sys.argv[1])
    zip_path = Path(sys.argv[2])
    top = sys.argv[3]
    base = root / top
    if not base.is_dir():
        raise SystemExit(f"找不到 .app 目录：{base}")

    zip_path.parent.mkdir(parents=True, exist_ok=True)
    if zip_path.exists():
        zip_path.unlink()

    count = 0
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames.sort()
            for fn in sorted(filenames):
                full = Path(dirpath) / fn
                rel = full.relative_to(base)
                # zip 内路径要带 .app 这一层，macOS 解压后才会得到完整的包
                arc = Path(top) / rel
                info = zipfile.ZipInfo(str(arc).replace(os.sep, "/"))
                info.date_time = time.localtime(full.stat().st_mtime)[:6]
                info.create_system = 3  # unix，解压方才会读外部属性里的权限位
                info.external_attr = (0o100000 | mode_for(rel, fn)) << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                with open(full, "rb") as f:
                    z.writestr(info, f.read())
                count += 1

    size = zip_path.stat().st_size / 1024 / 1024
    print(f"  ✓ {zip_path.name}  {count} 个文件  {size:.1f} MB")
    return 0


if __name__ == "__main__":
    sys.exit(main())
