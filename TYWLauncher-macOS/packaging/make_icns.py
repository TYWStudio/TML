#!/usr/bin/env python3
"""生成 macOS 的 AppIcon.icns。

源码是一张 256×256 的像素风图标（packaging/icons/tyw-256.png）：
像素画放大必须用 NEAREST，否则边缘会糊。放大到 1024 后套一个 macOS 惯例的
圆角方形遮罩（半径 = 1024 × 0.2237，和 Big Sur 之后的图标网格一致），
再按 16/32/64/128/256/512/1024 生成各档 PNG，最后拼成 .icns。

不依赖 macOS 的 iconutil，Windows / Linux 上也能跑，方便 CI 与本地出包。

用法：
    python packaging/make_icns.py            # 生成 packaging/icons/AppIcon.icns
"""
import struct
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
SRC = ROOT / "icons" / "tyw-256.png"
OUT = ROOT / "icons" / "AppIcon.icns"
MASTER = 1024

# icns 里 PNG 块的类型 → 边长（1x 与 2x 都给出，Retina 下更清晰）
CHUNKS = {
    "icp4": 16,
    "icp5": 32,
    "icp6": 64,
    "ic07": 128,
    "ic08": 256,
    "ic09": 512,
    "ic10": 1024,
}


def rounded(im: Image.Image, radius_ratio: float = 0.2237) -> Image.Image:
    size = im.size[0]
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        (0, 0, size - 1, size - 1), radius=int(size * radius_ratio), fill=255
    )
    out = im.convert("RGBA")
    out.putalpha(mask)
    return out


def build_master() -> Image.Image:
    src = Image.open(SRC).convert("RGBA")
    factor = MASTER // src.size[0]
    if factor < 1 or src.size[0] * factor != MASTER:
        raise SystemExit(f"源图必须是 {MASTER} 的整除数，当前 {src.size[0]}")
    big = src.resize((MASTER, MASTER), Image.NEAREST)
    return rounded(big)


def png_bytes(im: Image.Image) -> bytes:
    from io import BytesIO

    buf = BytesIO()
    im.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def write_icns(master: Image.Image, path: Path) -> None:
    body = b""
    for kind, size in CHUNKS.items():
        img = master if size == MASTER else master.resize((size, size), Image.LANCZOS)
        data = png_bytes(img)
        body += kind.encode("ascii") + struct.pack(">I", len(data) + 8) + data
    blob = b"icns" + struct.pack(">I", len(body) + 8) + body
    path.write_bytes(blob)


def main() -> int:
    if not SRC.exists():
        raise SystemExit(f"找不到源图：{SRC}")
    master = build_master()
    write_icns(master, OUT)
    # 同时导出一张 512 的 png，给 Avalonia 窗口图标 / 文档用
    master.resize((512, 512), Image.LANCZOS).save(ROOT / "icons" / "AppIcon-512.png")
    print(f"已生成 {OUT}（{OUT.stat().st_size // 1024} KB）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
