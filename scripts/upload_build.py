#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 CI 构建产物推到下载站（POST /api/upload.php）。

由四个平台的构建工作流直接调用（只在正式发版 tag 上执行，编译/打包失败不会走到这里）：

    .github/workflows/build.yml          -> --platform windows --root dist
    .github/workflows/build-linux.yml    -> --platform linux   --root TYWLauncher-Linux/dist
    .github/workflows/build-macos.yml    -> --platform macos   --root TYWLauncher-macOS/dist
    .github/workflows/build-android.yml  -> --platform android --root dist

也可以本地手工跑：

    TYW_UPLOAD_KEY=uk_xxx python3 scripts/upload_build.py \
        --platform linux --root dist --version 1.1.38

约定：
  * 密钥只从环境变量 TYW_UPLOAD_KEY（或 --key）读取，不写进仓库、不打印到日志；
    没有密钥（例如 fork）时打印警告并跳过，不让构建失败；
  * 接口地址默认 https://downloads.tywmc.cn，可用 TYW_UPLOAD_URL 覆盖；
  * 构建直接挂在应用下（node_id=0），不塞进任何分支项目/分类；
  * 平台 / 架构 / 包格式按文件名推断（CI 产物命名是固定的），也可用 --os/--arch/--pkg 强指；
  * 一律走分片上传：每个请求都小，失败只重传那一片，几十 MB 的包过 frp 也不会中途报废。
"""

import argparse
import hashlib
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

DEFAULT_BASE = "https://downloads.tywmc.cn"
SKIP_SUFFIXES = (".sha256", ".sha256sum", ".md5", ".txt", ".json", ".yml", ".yaml", ".log")
CHANNEL_MAP_DEFAULT = "snapshot=debug,beta=debug"


def log(msg):
    print(msg, flush=True)


def human(n):
    n = float(n)
    for unit in ("B", "KB", "MB", "GB"):
        if n < 1024 or unit == "GB":
            return ("%d %s" % (n, unit)) if unit == "B" else ("%.1f %s" % (n, unit))
        n /= 1024.0
    return "%.1f GB" % n


class UploadError(RuntimeError):
    pass


class Client:
    def __init__(self, base, key, timeout=180):
        self.base = base.rstrip("/")
        self.key = key
        self.timeout = timeout

    def _call(self, params, body=None, method="GET"):
        url = self.base + "/api/upload.php?" + urllib.parse.urlencode(params, doseq=True)
        req = urllib.request.Request(url, data=body, method=method)
        req.add_header("X-Upload-Key", self.key)
        req.add_header("User-Agent", "TYW-Upload-CI/1.0 (+https://downloads.tywmc.cn)")
        if body is not None:
            req.add_header("Content-Type", "application/octet-stream")
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as r:
                raw = r.read().decode("utf-8", "replace")
        except urllib.error.HTTPError as e:
            raw = e.read().decode("utf-8", "replace")
            try:
                data = json.loads(raw)
            except ValueError:
                data = {"ok": False, "msg": "HTTP %d %s" % (e.code, raw[:200])}
            data["_status"] = e.code
            return data
        except Exception as e:  # 网络层错误也返回结构化结果，交给重试
            return {"ok": False, "msg": "网络错误：%s" % e, "_network": True}
        try:
            return json.loads(raw)
        except ValueError:
            return {"ok": False, "msg": "返回不是 JSON：%s" % raw[:200]}

    def ping(self, **kw):
        p = {"action": "ping"}
        p.update({k: v for k, v in kw.items() if v})
        return self._call(p)

    def post(self, params, body=None):
        p = dict(params)
        p["action"] = "chunk"
        return self._call(p, body=body, method="POST")

    def existing(self, app, sha256):
        return self._call({"action": "list", "app": app, "sha256": sha256, "limit": 1})

    def delete(self, release_id):
        return self._call({"action": "delete", "id": release_id}, body=b"", method="POST")


def with_retry(fn, tries=4, what="请求"):
    delay = 2
    last = None
    for i in range(tries):
        res = fn()
        if res.get("ok"):
            return res
        # 4xx（密钥错 / 参数错）重试也没用，直接失败
        st = int(res.get("_status") or 0)
        if 400 <= st < 500 and st not in (408, 429):
            raise UploadError("%s失败：%s" % (what, res.get("msg") or res))
        last = res
        if i < tries - 1:
            log("    ! %s失败（%s），%.0f 秒后重试 %d/%d" % (what, res.get("msg"), delay, i + 1, tries - 1))
            time.sleep(delay)
            delay *= 2
    raise UploadError("%s失败：%s" % (what, (last or {}).get("msg")))


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for block in iter(lambda: fh.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def classify(platform, name, forced_channel=None, forced=None):
    """按文件名推断 平台 / 架构 / 包格式 / 通道。返回 None 表示不是要上传的东西。"""
    low = name.lower()
    if low.endswith(SKIP_SUFFIXES) or low.startswith("sha256sums"):
        return None
    forced = forced or {}

    if platform == "windows":
        if not low.endswith(".exe"):
            return None
        meta = {"os": "windows", "arch": "x64", "pkg": "exe"}
    elif platform == "macos":
        if low.endswith(".dmg"):
            pkg = "dmg"
        elif low.endswith(".zip"):
            pkg = "zip"
        elif low.endswith(".pkg"):
            pkg = "pkg"
        else:
            return None
        arch = "arm64" if re.search(r"arm64|aarch64", low) else (
            "x64" if re.search(r"x64|x86_64|amd64", low) else None)
        meta = {"os": "macos", "arch": arch, "pkg": pkg}
    elif platform == "android":
        if low.endswith(".apk"):
            pkg = "apk"
        elif low.endswith(".aab"):
            pkg = "aab"
        else:
            return None
        meta = {"os": "android", "arch": "universal", "pkg": pkg}
    elif platform == "linux":
        if low.endswith(".pkg.tar.zst"):
            pkg = "archpkg"
        elif low.endswith(".pkg.tar.xz"):
            pkg = "archpkg"
        elif low.endswith(".tar.gz") or low.endswith(".tgz"):
            pkg = "tar.gz"
        elif low.endswith(".tar.xz"):
            pkg = "tar.xz"
        elif low.endswith(".deb"):
            pkg = "deb"
        elif low.endswith(".rpm"):
            pkg = "rpm"
        elif low.endswith(".appimage"):
            pkg = "appimage"
        else:
            return None
        arch = "arm64" if re.search(r"arm64|aarch64", low) else (
            "x64" if re.search(r"x64|x86_64|amd64", low) else None)
        meta = {"os": "linux", "arch": arch, "pkg": pkg}
    else:
        raise UploadError("未知平台：%s" % platform)

    if "snapshot" in low:
        ch = "snapshot"
    elif "beta" in low or "debug" in low:
        ch = "beta"
    else:
        ch = "release"
    meta["channel"] = forced_channel.lower() if forced_channel else ch

    for k in ("os", "arch", "pkg", "channel"):
        if forced.get(k):
            meta[k] = forced[k]
    if not meta.get("arch") or not meta.get("pkg"):
        return None
    return meta


def parse_channel_map(raw):
    out = {}
    for part in (raw or "").split(","):
        part = part.strip()
        if not part:
            continue
        if "=" in part:
            a, b = part.split("=", 1)
            out[a.strip().lower()] = b.strip()
    return out


def upload_chunked(client, path, meta, app, args):
    size = os.path.getsize(path)
    chunk = max(1, args.chunk_mb) * 1024 * 1024
    total = max(1, (size + chunk - 1) // chunk)
    uid = uuid.uuid4().hex
    if size > args.max_bytes > 0:
        raise UploadError("文件 %s 超过站点上限（%s），请调大后台「单文件上限」" % (human(size), human(args.max_bytes)))

    log("    → 分片上传 %d 片 × %s" % (total, human(chunk)))
    res = None
    with open(path, "rb") as fh:
        for i in range(total):
            part = fh.read(chunk)
            params = {
                "app": app,
                "upload_id": uid,
                "chunk": i,
                "chunks": total,
            }
            if args.node:
                params["node_path"] = args.node
            if i == total - 1:
                params.update({
                    "version": meta.get("version", ""),
                    "channel": meta["channel"],
                    "os": meta["os"],
                    "arch": meta["arch"],
                    "pkg": meta["pkg"],
                    "name": os.path.basename(path),
                })
                if args.hidden:
                    params["visible"] = "0"
                if args.notes:
                    params["notes"] = args.notes
            log("      %d/%d %s" % (i + 1, total, human(len(part))))
            res = with_retry(lambda p=params, b=part: client.post(p, b), what="第 %d 片" % (i + 1))
    return res


def main():
    ap = argparse.ArgumentParser(description="把构建产物推到下载站")
    ap.add_argument("--platform", required=True, choices=["windows", "linux", "macos", "android"])
    ap.add_argument("--root", default="artifacts", help="产物目录（递归查找）")
    ap.add_argument("--version", default="", help="版本号，例如 1.1.38")
    ap.add_argument("--app", default="tyw-launcher", help="下载站里的应用标识")
    ap.add_argument("--node", default="", help="挂到某个分支项目下（路径，如 extensions/java）")
    ap.add_argument("--channel", default="", help="强制通道（默认按文件名判断）")
    ap.add_argument("--channel-map", default=os.environ.get("TYW_UPLOAD_CHANNEL_MAP", CHANNEL_MAP_DEFAULT),
                    help="CI 通道 → 站点通道，默认 snapshot=debug,beta=debug")
    ap.add_argument("--base", default=(os.environ.get("TYW_UPLOAD_URL") or DEFAULT_BASE))
    ap.add_argument("--key", default=os.environ.get("TYW_UPLOAD_KEY", ""))
    ap.add_argument("--os", default="", help="强指平台代码")
    ap.add_argument("--arch", default="", help="强指架构代码")
    ap.add_argument("--pkg", default="", help="强指包格式代码")
    ap.add_argument("--chunk-mb", type=int, default=4, help="分片大小（MB，默认 4，上限 16）")
    ap.add_argument("--notes", default="", help="写进构建说明")
    ap.add_argument("--hidden", action="store_true", help="上传后先不上架")
    ap.add_argument("--force", action="store_true", help="即使同 sha256 已存在也重传")
    ap.add_argument("--dry-run", action="store_true", help="只打印计划，不真的上传")
    args = ap.parse_args()

    if not args.key:
        # fork / 没配 Secret 时不要因为上传把整个发布构建搞挂
        log("::warning::未配置 TYW_UPLOAD_KEY，跳过上传下载站")
        return 0
    args.chunk_mb = max(1, min(16, args.chunk_mb))

    client = Client(args.base, args.key)
    ping = client.ping(app=args.app)
    if not ping.get("ok"):
        log("::error::上传密钥校验失败：%s" % (ping.get("msg") or ping))
        return 2
    limits = ping.get("limits") or {}
    args.max_bytes = int(limits.get("max_bytes") or 0)
    app_slug = (ping.get("app") or {}).get("slug") or args.app
    channels = list((ping.get("config") or {}).get("channels") or {})
    default_channel = (ping.get("config") or {}).get("default_channel") or (channels[0] if channels else "release")
    log("密钥：%s（应用 %s，单文件上限 %s）" % (
        (ping.get("key") or {}).get("name", "-"), app_slug, human(args.max_bytes) if args.max_bytes else "-"))

    cmap = parse_channel_map(args.channel_map)
    if args.channel:
        forced_ch = args.channel
    else:
        forced_ch = None

    if not os.path.isdir(args.root):
        log("::error::产物目录不存在：%s" % args.root)
        return 2

    files = []
    for base, _dirs, names in os.walk(args.root):
        for n in sorted(names):
            p = os.path.join(base, n)
            meta = classify(args.platform, n, forced_ch, {"os": args.os, "arch": args.arch, "pkg": args.pkg})
            if not meta:
                continue
            ch = meta["channel"]
            ch = cmap.get(ch, ch)
            if channels and ch not in channels:
                log("    ! 站点没有通道 %s，改用默认通道 %s" % (ch, default_channel))
                ch = default_channel
            meta["channel"] = ch
            meta["version"] = args.version
            files.append((p, meta))

    if not files:
        log("::error::在 %s 下没找到 %s 的产物（检查文件名）" % (args.root, args.platform))
        return 2

    log("计划上传 %d 个文件（版本 %s）：" % (len(files), args.version or "-"))
    for p, m in files:
        log("  %-52s %-8s %-6s %-8s %-8s %s" % (
            os.path.basename(p), human(os.path.getsize(p)), m["os"], m["arch"], m["pkg"], m["channel"]))

    summary = []
    failed = 0
    for p, m in files:
        name = os.path.basename(p)
        log("→ %s" % name)
        try:
            sha = sha256_of(p)
            if not args.force:
                ex = client.existing(app_slug, sha)
                if ex.get("ok") and (ex.get("releases") or []):
                    row = ex["releases"][0]
                    log("    已存在（#%s），跳过。用 --force 强制重传。" % row.get("id"))
                    summary.append((name, "已存在", "#%s" % row.get("id"), row.get("title", "")))
                    continue
            if args.dry_run:
                log("    [dry-run] 不上传")
                summary.append((name, "dry-run", "-", m["channel"]))
                continue
            res = upload_chunked(client, p, m, app_slug, args)
            state = "复用" if res.get("duplicate") else "已上传"
            log("    ok #%s %s" % (res.get("id"), res.get("title", "")))
            summary.append((name, state, "#%s" % res.get("id"), res.get("title", "")))
        except UploadError as e:
            failed += 1
            log("::error::%s 上传失败：%s" % (name, e))
            summary.append((name, "失败", str(e)[:80], ""))
        except Exception as e:  # noqa: BLE001
            failed += 1
            log("::error::%s 上传异常：%s" % (name, e))
            summary.append((name, "异常", str(e)[:80], ""))

    log("")
    log("结果：成功 %d，失败 %d，共 %d" % (len(summary) - failed, failed, len(summary)))

    out = os.environ.get("GITHUB_STEP_SUMMARY")
    if out:
        with open(out, "a", encoding="utf-8") as fh:
            fh.write("### 下载站上传（%s）\n\n" % args.platform)
            fh.write("| 文件 | 状态 | 编号 | 构建 |\n| --- | --- | --- | --- |\n")
            for row in summary:
                fh.write("| `%s` | %s | %s | %s |\n" % row)
            fh.write("\n")

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
