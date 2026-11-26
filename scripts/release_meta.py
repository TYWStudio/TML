#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""发布裁决：这次 CI 是「只构建」还是「发布新版本」，发布哪个通道，产物叫什么。

只看 tag 名，不依赖网页上勾选任何东西：

    v1.1.39                       正式版 -> 只构建 Release 通道，发布为正式 Release
    v1.1.39-snapshot              快照版 -> 只构建 Snapshot 通道，发布为 pre-release
    v1.1.39-beta / -preview / -rc / -alpha / -dev / -nightly
                                  同样算快照版（都是预览性质）
    push main / workflow_dispatch 只构建（两个通道都编译），不上传 artifact、不发布

手动触发时可以用 channel 输入（auto / release / snapshot / both）指定只构建哪个通道，
但手动触发没有 tag，所以永远只构建、不发布。

用法（workflow 的 plan job 里）：

    python3 scripts/release_meta.py --platform windows
    python3 scripts/release_meta.py --platform android --channel snapshot

输出到 $GITHUB_OUTPUT（也在 stdout 打一份摘要）：

    publish     true / false
    version     x.y.z，tag 优先，且已经去掉 -snapshot 之类的后缀
    channel     release / snapshot / both
    channels    空格分隔的构建通道名：Release、Snapshot、Release Snapshot
    prerelease  true / false（发布时是否标记为 pre-release）
    tag         触发用的 tag 名（不是 tag 时为空）
    matrix      Windows / Android 的动态构建矩阵 JSON
"""

import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# tag 后缀里出现这些词就当作预览/快照版
PREVIEW_HINTS = ("snapshot", "beta", "preview", "rc", "alpha", "dev", "nightly", "test")
CHANNEL_NAMES = {"release": "Release", "snapshot": "Snapshot"}

WINDOWS_MATRIX = {
    "Release": {
        "configuration": "Release",
        "binary": "TYW Launcher.exe",
        "artifact": "TYWLauncher-windows-x64",
    },
    "Snapshot": {
        "configuration": "Snapshot",
        "binary": "TYW Launcher Snapshot.exe",
        "artifact": "TYWLauncher-windows-x64-snapshot",
    },
}

ANDROID_MATRIX = {
    "Release": {
        "channel": "release",
        "task": "assembleRelease",
        "dir": "release",
        "file": "TYWLauncher-android.apk",
        "output": "TYWLauncher-android.apk",
    },
    "Snapshot": {
        "channel": "snapshot",
        "task": "assembleSnapshot",
        "dir": "snapshot",
        "file": "TYWLauncher-android-snapshot.apk",
        "output": "TYWLauncher-android-snapshot.apk",
    },
}


def read_source_version(platform):
    """没打 tag 时用各自源码里的版本号（发版时一律以 tag 为准）。"""
    if platform == "windows":
        path = os.path.join(ROOT, "TYW Launcher", "Modules", "Base", "ModBase.vb")
        pattern = r'VersionBaseName\s+As\s+String\s*=\s*"([0-9]+(?:\.[0-9]+)+)"'
    elif platform == "linux":
        path = os.path.join(ROOT, "TYWLauncher-Linux", "src", "TYW.Core", "Base", "ModApp.vb")
        pattern = r'Case Else\s*:\s*Return\s+"([0-9]+(?:\.[0-9]+)+)"'
    elif platform == "macos":
        path = os.path.join(ROOT, "TYWLauncher-macOS", "src", "TYW.Core", "Base", "ModApp.vb")
        pattern = r'Case Else\s*:\s*Return\s+"([0-9]+(?:\.[0-9]+)+)"'
    elif platform == "android":
        path = os.path.join(ROOT, "TYWLauncher-Android", "app", "build.gradle.kts")
        pattern = r'baseVersionName\s*=.*?\?\:\s*"([0-9]+(?:\.[0-9]+)+)"'
    else:
        return ""

    try:
        with open(path, "r", encoding="utf-8-sig", errors="replace") as fh:
            text = fh.read()
    except OSError:
        return ""
    m = re.search(pattern, text)
    return m.group(1) if m else ""


def parse_tag(tag_name):
    """v1.1.39 / v1.1.39-snapshot -> (version, is_preview)。认不出来就返回 ('', False)。"""
    m = re.match(r"^v?([0-9]+(?:\.[0-9]+)+)(?:-(.+))?$", tag_name or "")
    if not m:
        return "", False
    version, suffix = m.group(1), (m.group(2) or "").lower()
    preview = any(h in suffix for h in PREVIEW_HINTS) if suffix else False
    return version, preview


def build_matrix(platform, channel_keys):
    if platform == "windows":
        return [WINDOWS_MATRIX[k] for k in channel_keys if k in WINDOWS_MATRIX]
    if platform == "android":
        return [ANDROID_MATRIX[k] for k in channel_keys if k in ANDROID_MATRIX]
    return []


def main(argv):
    platform = "windows"
    requested = "auto"
    for i, arg in enumerate(argv):
        if arg == "--platform" and i + 1 < len(argv):
            platform = argv[i + 1].strip().lower()
        elif arg.startswith("--platform="):
            platform = arg.split("=", 1)[1].strip().lower()
        elif arg == "--channel" and i + 1 < len(argv):
            requested = argv[i + 1].strip().lower()
        elif arg.startswith("--channel="):
            requested = arg.split("=", 1)[1].strip().lower()

    if requested not in ("auto", "release", "snapshot", "both", ""):
        print("::warning::未知的 channel 输入 %r，按 auto 处理" % requested)
        requested = "auto"

    ref = os.environ.get("GITHUB_REF", "")
    tag_name = os.environ.get("GITHUB_REF_NAME", "") if ref.startswith("refs/tags/") else ""
    is_tag = bool(tag_name)

    source_version = read_source_version(platform)
    tag_version, tag_preview = parse_tag(tag_name) if is_tag else ("", False)

    if is_tag:
        # 发布：tag 决定通道，只构建该通道
        version = tag_version or source_version
        if not tag_version:
            print("::warning::tag %r 认不出版本号，改用源码版本 %s" % (tag_name, source_version))
        channel = "snapshot" if tag_preview else "release"
        publish = True
        prerelease = tag_preview
    else:
        # 只构建：不产出 artifact、不发布
        version = source_version
        publish = False
        prerelease = False
        if requested in ("release", "snapshot"):
            channel = requested
        else:
            channel = "both"

    if channel == "both":
        channel_keys = ["Release", "Snapshot"]
    else:
        channel_keys = [CHANNEL_NAMES[channel]]

    channels = " ".join(channel_keys)
    rows = build_matrix(platform, channel_keys)
    # GitHub 的 strategy.matrix 必须是对象，动态矩阵统一包成 {"include": [...]}
    matrix = {"include": rows} if rows else {"include": []}

    out = {
        "publish": "true" if publish else "false",
        "version": version,
        "channel": channel,
        "channels": channels,
        "prerelease": "true" if prerelease else "false",
        "tag": tag_name,
        "matrix": json.dumps(matrix, separators=(",", ":")),
    }

    print("平台 %s｜触发 %s" % (platform, tag_name or ref or "手动"))
    print("版本 %s｜通道 %s｜发布 %s｜pre-release %s"
          % (version or "(未读到)", channels, "是" if publish else "否（只构建）",
             "是" if prerelease else "否"))
    if matrix:
        print("矩阵 %s" % out["matrix"])

    gh_out = os.environ.get("GITHUB_OUTPUT")
    if gh_out:
        with open(gh_out, "a", encoding="utf-8") as fh:
            for k, v in out.items():
                fh.write("%s=%s\n" % (k, v))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
