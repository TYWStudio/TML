# -*- coding: utf-8 -*-
"""把示例目录打成 .tmlext 或 .jar，并输出 SHA-256。

    python extensions/pack.py extensions/examples/hello-python
    python extensions/pack.py extensions/examples/hello-node
    python extensions/pack.py extensions/examples/hello-java

.tmlext 会自动把对应的 SDK 文件放进包根目录；
.jar 会先用 javac 编译 SDK + 示例源码（需要本机有 JDK），再写 MANIFEST.MF。
"""
import hashlib
import json
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SDK = ROOT / "sdk"
ID_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$")
ALLOWED_PERMISSIONS = {
    "log", "toast", "host.info", "instances.list", "events", "storage", "settings"
}
ALLOWED_EVENTS = {
    "launcher.started", "launcher.shutdown", "game.launching", "game.launched",
    "game.exited", "instance.listChanged", "settings.changed"
}
ALLOWED_PLATFORMS = {"windows", "linux", "macos", "android"}
MAX_FILE_BYTES = 2 * 1024 * 1024
MAX_TOTAL_BYTES = 8 * 1024 * 1024
MAX_FILES = 64


def load_manifest(src):
    plain = src / "manifest.json"
    jar = src / "META-INF" / "tml-extension.json"
    path = plain if plain.exists() else jar
    if not path.exists():
        raise SystemExit("找不到 manifest.json 或 META-INF/tml-extension.json：" + str(src))
    return path, json.loads(path.read_text(encoding="utf-8"))


def validate(manifest):
    if manifest.get("apiVersion") != 1:
        raise SystemExit("apiVersion 必须为 1")
    if not ID_RE.match(str(manifest.get("id", ""))):
        raise SystemExit("id 不合法：" + str(manifest.get("id")))
    fmt = manifest.get("format")
    runtime = manifest.get("runtime")
    entry = str(manifest.get("entry", ""))
    if ".." in entry or "\\" in entry or ":" in entry or entry.startswith("/"):
        raise SystemExit("entry 路径不合法：" + entry)
    if fmt == "tmlext":
        if runtime not in ("python3", "node"):
            raise SystemExit("tmlext 的 runtime 只能是 python3 / node")
        if not re.match(r"^[A-Za-z0-9._/\-]+\.(py|js|mjs|cjs)$", entry):
            raise SystemExit("tmlext 的 entry 必须是 .py / .js 文件：" + entry)
    elif fmt == "jar":
        if runtime != "java":
            raise SystemExit("jar 的 runtime 必须是 java")
        if not re.match(r"^[A-Za-z_$][A-Za-z0-9_$]*(\.[A-Za-z_$][A-Za-z0-9_$]*)+$", entry):
            raise SystemExit("jar 的 entry 必须是主类全名：" + entry)
    else:
        raise SystemExit("format 只能是 tmlext / jar")
    for key, allowed in (("permissions", ALLOWED_PERMISSIONS),
                         ("subscribe", ALLOWED_EVENTS),
                         ("platforms", ALLOWED_PLATFORMS)):
        values = manifest.get(key, [])
        if not isinstance(values, list) or any(v not in allowed for v in values):
            raise SystemExit(key + " 含有未开放 / 非法的值：" + str(values))


def safe_files(src):
    files, total = [], 0
    for f in sorted(src.rglob("*")):
        if not f.is_file():
            continue
        rel = f.relative_to(src).as_posix()
        if any(part.startswith(".") or part == "__pycache__" for part in f.relative_to(src).parts):
            continue
        if rel.endswith((".sha256", ".tmlext", ".jar", ".class", ".pyc")):
            continue
        size = f.stat().st_size
        if size > MAX_FILE_BYTES:
            raise SystemExit("单文件超过 2 MiB：" + rel)
        total += size
        files.append((rel, f))
    if len(files) > MAX_FILES or total > MAX_TOTAL_BYTES:
        raise SystemExit("文件数 / 总大小超限：%d 个，%d 字节" % (len(files), total))
    return files


def build_tmlext(src, manifest, out):
    files = safe_files(src)
    sdk_file = SDK / "python" / "tml_ext.py" if manifest["runtime"] == "python3" else SDK / "node" / "tml_ext.js"
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for rel, f in files:
            z.write(f, rel)
        z.write(sdk_file, sdk_file.name)
    return out


def build_jar(src, manifest, out):
    javac = shutil.which("javac")
    if not javac:
        raise SystemExit("需要 JDK 的 javac 才能打包 Java 扩展")
    java_sources = sorted((SDK / "java").rglob("*.java")) + sorted((src / "src").rglob("*.java"))
    if not java_sources:
        raise SystemExit("找不到 Java 源码")
    with tempfile.TemporaryDirectory() as tmp:
        classes = Path(tmp) / "classes"
        classes.mkdir()
        cmd = [javac, "-encoding", "UTF-8", "-source", "8", "-target", "8",
               "-d", str(classes)] + [str(p) for p in java_sources]
        # 中文 Windows 下 javac 输出可能是 GBK，不能用 text=True 强制 UTF-8 解码
        result = subprocess.run(cmd, capture_output=True)
        if result.returncode != 0:
            err = result.stderr.decode("utf-8", "replace") if isinstance(result.stderr, bytes) else str(result.stderr)
            raise SystemExit("javac 失败：\n" + err)
        with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
            jar_manifest = src / "META-INF" / "tml-extension.json"
            z.write(jar_manifest, "META-INF/tml-extension.json")
            z.writestr("META-INF/MANIFEST.MF",
                       "Manifest-Version: 1.0\r\nMain-Class: " + manifest["entry"] + "\r\n\r\n")
            for f in sorted(classes.rglob("*.class")):
                z.write(f, f.relative_to(classes).as_posix())
    return out


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    src = Path(sys.argv[1]).resolve()
    if not src.is_dir():
        raise SystemExit("不是目录：" + str(src))
    _, manifest = load_manifest(src)
    validate(manifest)
    suffix = ".tmlext" if manifest["format"] == "tmlext" else ".jar"
    out = Path(sys.argv[2]).resolve() if len(sys.argv) > 2 else src.parent / (src.name + suffix)
    out.parent.mkdir(parents=True, exist_ok=True)
    if manifest["format"] == "tmlext":
        build_tmlext(src, manifest, out)
    else:
        build_jar(src, manifest, out)
    digest = hashlib.sha256(out.read_bytes()).hexdigest()
    (Path(str(out) + ".sha256")).write_text(digest + "  " + out.name + "\n", encoding="utf-8")
    print("打包完成：", out)
    print("SHA-256：", digest)


if __name__ == "__main__":
    main()
