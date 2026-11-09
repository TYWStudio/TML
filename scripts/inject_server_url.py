# -*- coding: utf-8 -*-
"""把官方服务端地址在**编译期**注入 Linux / macOS 客户端的 ModServerUrl.vb。

仓库里的常量恒为空（开源/离线版）；真实地址来自：
    1) 环境变量 TYW_SERVER_URL（GitHub Actions Secrets 或本机 export）
    2) TYWLauncher-<平台>/packaging/server_url.txt（本机发版用，不入仓库）
两个都没有 → 保持空值，产物为纯离线版。

与 Windows 的 scripts/inject_secrets.py 是同一套思路，但目标文件与常量名不同：
    Windows : TYW Launcher/Modules/ModSecret.vb        → BuiltInServerAddress
    Linux   : TYWLauncher-Linux/src/TYW.Core/Base/ModServerUrl.vb → BuiltIn
    macOS   : TYWLauncher-macOS/src/TYW.Core/Base/ModServerUrl.vb → BuiltIn

重要：**地址只写进编译后的二进制，产物里不再附带 server.txt**。
所以本脚本必须在 dotnet publish / build-macos.sh 之前执行。

用法：
    python scripts/inject_server_url.py --platform linux apply
    python scripts/inject_server_url.py --platform linux restore
    python scripts/inject_server_url.py --platform linux status
不带 action 时默认 apply。
"""

import argparse
import io
import os
import re
import sys

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

TARGETS = {
    "linux": {
        "file": os.path.join(ROOT, "TYWLauncher-Linux", "src", "TYW.Core", "Base", "ModServerUrl.vb"),
        "local": os.path.join(ROOT, "TYWLauncher-Linux", "packaging", "server_url.txt"),
    },
    "macos": {
        "file": os.path.join(ROOT, "TYWLauncher-macOS", "src", "TYW.Core", "Base", "ModServerUrl.vb"),
        "local": os.path.join(ROOT, "TYWLauncher-macOS", "packaging", "server_url.txt"),
    },
}

CONST = "BuiltIn"
PATTERN = r'(Public Const ' + CONST + r' As String = )"[^"]*"'


def resolve(cfg):
    """返回 (地址, 来源)；地址保证以 / 结尾。"""
    value = (os.environ.get("TYW_SERVER_URL", "") or "").strip()
    source = "env:TYW_SERVER_URL"
    if not value and os.path.isfile(cfg["local"]):
        try:
            value = io.open(cfg["local"], encoding="utf-8-sig").read().strip()
            source = os.path.relpath(cfg["local"], ROOT).replace("\\", "/")
        except Exception:
            value = ""
    if value and not value.endswith("/"):
        value += "/"
    return value, source


def mask(value):
    if not value:
        return "（空）"
    return (value[:8] + "***(" + str(len(value)) + ")") if len(value) > 8 else value[:2] + "***"


def do_apply(cfg):
    value, source = resolve(cfg)
    text = io.open(cfg["file"], "rb").read().decode("utf-8-sig")
    new_text = re.sub(PATTERN, lambda m: m.group(1) + '"' + value.replace('"', '""') + '"',
                      text, count=1)
    if new_text == text and re.search(PATTERN, text) is None:
        raise SystemExit("注入失败：%s 里找不到 Public Const %s" % (cfg["file"], CONST))
    if new_text != text:
        io.open(cfg["file"], "wb").write(new_text.encode("utf-8-sig"))
    if value:
        print("  %-22s %s  ← %s" % (CONST, mask(value), source), flush=True)
    else:
        print("  %-22s 空（离线/开源默认值），产物不含任何服务器地址" % CONST, flush=True)
    return 0


def do_restore(cfg):
    text = io.open(cfg["file"], "rb").read().decode("utf-8-sig")
    new_text = re.sub(PATTERN, lambda m: m.group(1) + '""', text, count=1)
    if new_text != text:
        io.open(cfg["file"], "wb").write(new_text.encode("utf-8-sig"))
    print("已还原 %s（仓库里保持空值）" % os.path.relpath(cfg["file"], ROOT).replace("\\", "/"),
          flush=True)
    return 0


def do_status(cfg):
    text = io.open(cfg["file"], "rb").read().decode("utf-8-sig")
    m = re.search(r'Public Const ' + CONST + r' As String = "([^"]*)"', text)
    print("%-22s %s" % (CONST, mask(m.group(1) if m else "")), flush=True)
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(description="编译期注入 Linux/macOS 服务端地址（不产出 server.txt）")
    ap.add_argument("--platform", "-p", choices=sorted(TARGETS), default="linux")
    ap.add_argument("action", nargs="?", default="apply",
                    choices=["apply", "restore", "status"])
    a = ap.parse_args(argv)
    cfg = TARGETS[a.platform]
    if not os.path.isfile(cfg["file"]):
        raise SystemExit("找不到目标文件：" + cfg["file"])
    print("[%s] %s" % (a.platform, os.path.relpath(cfg["file"], ROOT).replace("\\", "/")), flush=True)
    return {"apply": do_apply, "restore": do_restore, "status": do_status}[a.action](cfg)


if __name__ == "__main__":
    sys.exit(main())
