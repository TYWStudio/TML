# -*- coding: utf-8 -*-
"""生成 macOS 图标 TYWLauncher.icns（从 Windows 版 icon.ico 的各个尺寸取图）。

用法：py packaging/macos/make_icns.py
依赖：Pillow（pip install pillow）

像素画风图标放大用 NEAREST，不会糊；16/32/64/128/256 直接取 ICO 里的原始帧，
512/1024 由 256 帧最近邻放大（Retina 下边缘依然是硬边）。
"""
import os
import struct

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_ICO = os.path.abspath(os.path.join(HERE, "..", "..", "..", "TYW Launcher", "Images", "icon.ico"))
OUT = os.path.join(HERE, "TYWLauncher.icns")

# icns 里每种类型对应的边长
TYPES = [
    (b"icp4", 16),
    (b"icp5", 32),
    (b"icp6", 64),
    (b"ic07", 128),
    (b"ic08", 256),
    (b"ic09", 512),
    (b"ic10", 1024),
]


def load_frames(path):
    """把 ICO 里的各尺寸帧抽成 {边长: PIL.Image}。"""
    frames = {}
    im = Image.open(path)
    sizes = sorted(im.info.get("sizes", [im.size]))
    for size in sizes:
        im.size = size
        frames[size[0]] = im.convert("RGBA").copy()
    im.size = max(sizes)
    return frames


def pick(frames, want):
    """取最接近的目标尺寸帧：有原图就用原图，没有就最近邻放大。"""
    if want in frames:
        return frames[want]
    base = max((s for s in frames if s <= want), default=min(frames))
    return frames[base].resize((want, want), Image.NEAREST)


def main():
    if not os.path.exists(SRC_ICO):
        raise SystemExit("找不到源图标：" + SRC_ICO)
    frames = load_frames(SRC_ICO)
    print("ICO 里的尺寸：", sorted(frames))

    chunks = []
    for tag, size in TYPES:
        img = pick(frames, size)
        tmp = os.path.join(HERE, "_tmp_%d.png" % size)
        img.save(tmp, "PNG", optimize=True)
        with open(tmp, "rb") as f:
            data = f.read()
        os.remove(tmp)
        chunks.append(tag + struct.pack(">I", len(data) + 8) + data)
        print("  %-6s %dpx  %d 字节" % (tag.decode(), size, len(data)))

    body = b"".join(chunks)
    with open(OUT, "wb") as f:
        f.write(b"icns" + struct.pack(">I", len(body) + 8) + body)
    print("已生成：%s（%d 字节）" % (OUT, os.path.getsize(OUT)))


if __name__ == "__main__":
    main()
