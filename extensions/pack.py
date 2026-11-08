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
import os
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
# 原生扩展（编译型语言）的 entry 是二进制，不套解释器，也不需要 SDK 文件进包
NATIVE_RUNTIMES = {"c", "cpp", "rust", "go", "native", "zig"}
MAX_FILE_BYTES = 2 * 1024 * 1024
MAX_TOTAL_BYTES = 8 * 1024 * 1024
# 编译产物天然偏大（Go 静态二进制起步就 ~2 MiB），给原生扩展放宽上限
MAX_FILE_BYTES_NATIVE = 8 * 1024 * 1024
MAX_TOTAL_BYTES_NATIVE = 16 * 1024 * 1024
MAX_FILES = 64
SKIP_DIRS = {"__pycache__", "target", "node_modules", ".git", "vendor"}


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
        if runtime in NATIVE_RUNTIMES:
            if not re.match(r"^[A-Za-z0-9._/\-]+$", entry) or entry.endswith("/"):
                raise SystemExit("原生扩展的 entry 必须是包内可执行文件路径：" + entry)
        elif runtime in ("python3", "node"):
            if not re.match(r"^[A-Za-z0-9._/\-]+\.(py|js|mjs|cjs)$", entry):
                raise SystemExit("tmlext 的 entry 必须是 .py / .js 文件：" + entry)
        else:
            raise SystemExit("tmlext 的 runtime 只能是 python3 / node，"
                             "或原生运行时 c / cpp / rust / go / native / zig")
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


def safe_files(src, max_file=MAX_FILE_BYTES, max_total=MAX_TOTAL_BYTES):
    files, total = [], 0
    for f in sorted(src.rglob("*")):
        if not f.is_file():
            continue
        parts = f.relative_to(src).parts
        if any(part.startswith(".") or part in SKIP_DIRS for part in parts):
            continue
        rel = f.relative_to(src).as_posix()
        if rel.endswith((".sha256", ".tmlext", ".jar", ".class", ".pyc")):
            continue
        size = f.stat().st_size
        if size > max_file:
            raise SystemExit("单文件超过 %d MiB：%s" % (max_file // (1024 * 1024), rel))
        total += size
        files.append((rel, f))
    if len(files) > MAX_FILES or total > max_total:
        raise SystemExit("文件数 / 总大小超限：%d 个，%d 字节" % (len(files), total))
    return files


def zip_add(z, path, rel):
    """按 0644 写入（不带可执行位）：宿主会拒绝带可执行位的归档条目，
    原生扩展的执行位由宿主解压后自己补。"""
    info = zipfile.ZipInfo.from_file(str(path), rel)
    info.compress_type = zipfile.ZIP_DEFLATED
    info.external_attr = 0o644 << 16
    with open(path, "rb") as fh:
        z.writestr(info, fh.read())


def run_tool(cmd, cwd):
    result = subprocess.run(cmd, cwd=str(cwd), capture_output=True)
    if result.returncode != 0:
        err = result.stderr.decode("utf-8", "replace") if isinstance(result.stderr, bytes) else str(result.stderr)
        raise SystemExit("%s 失败：\n%s" % (Path(cmd[0]).name, err[-1500:]))


def rust_bin_name(src):
    """从 Cargo.toml 里读 [[bin]] name，读不到就退回包名。"""
    text = (src / "Cargo.toml").read_text(encoding="utf-8")
    m = re.search(r"\[\[bin\]\][^\[]*?name\s*=\s*\"([^\"]+)\"", text, re.S)
    if m:
        return m.group(1)
    m = re.search(r"^\s*name\s*=\s*\"([^\"]+)\"", text, re.M)
    return m.group(1) if m else src.name


def build_native(src, manifest):
    """把 Go / Rust 源码编译成 manifest.entry 指向的可执行文件（已存在就跳过）。"""
    runtime = manifest["runtime"]
    entry = manifest["entry"]
    target = src / entry
    exe = target.with_name(target.name + ".exe") if os.name == "nt" else target
    if exe.exists() or target.exists():
        return
    if runtime == "go":
        tool = shutil.which("go")
        if not tool:
            raise SystemExit("需要 Go 工具链（go build）才能打包原生扩展，或自行把二进制放到 " + entry)
        target.parent.mkdir(parents=True, exist_ok=True)
        run_tool([tool, "build", "-trimpath", "-ldflags", "-s -w", "-o", str(exe), "."], src)
    elif runtime == "rust":
        tool = shutil.which("cargo")
        if not tool:
            raise SystemExit("需要 Rust 工具链（cargo build）才能打包原生扩展，或自行把二进制放到 " + entry)
        run_tool([tool, "build", "--release"], src)
        built = src / "target" / "release" / (rust_bin_name(src) + (".exe" if os.name == "nt" else ""))
        if not built.exists():
            raise SystemExit("cargo 没有产出可执行文件：" + str(built))
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(built, exe)
    else:
        raise SystemExit("runtime=%s 的二进制需要自己先编译好放到 %s" % (runtime, entry))
    if not exe.exists() and not target.exists():
        raise SystemExit("编译后仍找不到 entry：" + entry)


def build_tmlext(src, manifest, out):
    runtime = manifest["runtime"]
    native = runtime in NATIVE_RUNTIMES
    if native:
        build_native(src, manifest)
        files = safe_files(src, MAX_FILE_BYTES_NATIVE, MAX_TOTAL_BYTES_NATIVE)
        names = [rel for rel, _ in files]
        if manifest["entry"] not in names and manifest["entry"] + ".exe" not in names:
            raise SystemExit("包里找不到 entry：" + manifest["entry"])
    else:
        files = safe_files(src)
    with zipfile.ZipFile(out, "w") as z:
        for rel, f in files:
            zip_add(z, f, rel)
        if not native:
            # 解释型扩展把对应 SDK 一起放进包根目录，用户 clone 后无需再拷
            sdk_file = SDK / "python" / "tml_ext.py" if runtime == "python3" else SDK / "node" / "tml_ext.js"
            zip_add(z, sdk_file, sdk_file.name)
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
