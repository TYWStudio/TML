# -*- coding: utf-8 -*-
"""把官方服务端地址 / 拓展商店密钥在**编译期**注入 Linux / macOS 客户端的 ModServerUrl.vb。

仓库里的常量恒为空（开源/离线版）；真实值来自：
    1) 环境变量 TYW_SERVER_URL / TYW_STORE_API_KEY（GitHub Actions Secrets 或本机 export）
    2) TYWLauncher-<平台>/packaging/server_url.txt（本机发版用，不入仓库）
两个都没有 -> 保持空值，产物为纯离线版。

与 Windows 的 scripts/inject_secrets.py 是同一套思路，但目标文件与常量名不同：
    Windows : TYW Launcher/Modules/ModSecret.vb        -> BuiltInServerAddress / BuiltInStoreApiKey
    Linux   : TYWLauncher-Linux/src/TYW.Core/Base/ModServerUrl.vb -> BuiltIn / BuiltInStoreApiKey
    macOS   : TYWLauncher-macOS/src/TYW.Core/Base/ModServerUrl.vb -> BuiltIn / BuiltInStoreApiKey

重要：**地址/密钥只写进编译后的二进制，产物里不再附带 server.txt**。
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
CONST_KEY = "BuiltInStoreApiKey"
PATTERN = r'(Public Const ' + CONST + r' As String = )"[^"]*"'
PATTERN_KEY = r'(Public Const ' + CONST_KEY + r' As String = )"[^"]*"'


def resolve_url(cfg):
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


def resolve_key():
    return (os.environ.get("TYW_STORE_API_KEY", "") or "").strip()


def mask(value):
    if not value:
        return "（空）"
    return (value[:8] + "***(" + str(len(value)) + ")") if len(value) > 8 else value[:2] + "***"


def do_apply(cfg):
    url_val, url_src = resolve_url(cfg)
    key_val = resolve_key()
    path = cfg["file"]
    if not os.path.isfile(path):
        raise SystemExit("找不到目标文件：" + path)
    text = io.open(path, "rb").read().decode("utf-8-sig")
    if re.search(PATTERN, text) is None:
        raise SystemExit("注入失败：%s 里找不到 Public Const %s" % (path, CONST))
    text = re.sub(PATTERN, lambda m: m.group(1) + '"' + url_val.replace('"', '""') + '"',
                  text, count=1)
    # 确保商店密钥常量存在，再注入
    if re.search(PATTERN_KEY, text) is None:
        text = re.sub(r'(Public Const BuiltIn As String = "[^"]*")',
                      r'\1\n\n    Public Const BuiltInStoreApiKey As String = ""', text, count=1)
    text = re.sub(PATTERN_KEY, lambda m: m.group(1) + '"' + key_val.replace('"', '""') + '"',
                  text, count=1)
    io.open(path, "wb").write(text.encode("utf-8-sig"))
    print("  %-22s %s  <- %s" % (CONST, mask(url_val), url_src))
    print("  %-22s %s  <- %s" % (CONST_KEY, mask(key_val),
          "env:TYW_STORE_API_KEY" if key_val else "(空)"))
    return 0


def do_restore(cfg):
    path = cfg["file"]
    if not os.path.isfile(path):
        return 0
    text = io.open(path, "rb").read().decode("utf-8-sig")
    new = text
    new = re.sub(PATTERN, lambda m: m.group(1) + '""', new, count=1)
    new = re.sub(PATTERN_KEY, lambda m: m.group(1) + '""', new, count=1)
    if new != text:
        io.open(path, "wb").write(new.encode("utf-8-sig"))
    print("已还原 %s（仓库里保持空值）" % os.path.relpath(path, ROOT).replace("\\", "/"),
          flush=True)
    return 0


def do_status(cfg):
    path = cfg["file"]
    if not os.path.isfile(path):
        raise SystemExit("找不到目标文件：" + path)
    text = io.open(path, "rb").read().decode("utf-8-sig")
    m = re.search(r'Public Const ' + CONST + r' As String = "([^"]*)"', text)
    mk = re.search(r'Public Const ' + CONST_KEY + r' As String = "([^"]*)"', text)
    print("%-22s %s" % (CONST, mask(m.group(1) if m else "")), flush=True)
    print("%-22s %s" % (CONST_KEY, mask(mk.group(1) if mk else "")), flush=True)
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(description="编译期注入 Linux/macOS 服务端地址与商店密钥（不产出 server.txt）")
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
