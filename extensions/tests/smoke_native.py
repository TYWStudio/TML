#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""模拟宿主，跑一遍 TML-Ext/1 协议，验证原生扩展（Go / Rust）能正常工作。

    python extensions/tests/smoke_native.py extensions/examples/hello-go/bin/tml_ext

做的事和启动器一样：
  1. 以独立进程拉起扩展（清空环境变量，只下发 TML_EXT_* 协议配置）；
  2. 发 initialize 握手，等 ready=true；
  3. 后台线程持续应答扩展发起的调用（log / toast / host.info / storage.* …）；
  4. 广播一条 launcher.started 事件；
  5. 发 shutdown，要求进程在 5 秒内正常退出。
"""
import json
import os
import subprocess
import sys
import tempfile
import threading
import time

READY_TIMEOUT = 10.0
EXIT_TIMEOUT = 5.0


def host_env():
    keep = ("PATH", "Path", "SystemRoot", "TEMP", "TMP", "HOME", "LANG", "LC_ALL")
    env = {k: os.environ[k] for k in keep if k in os.environ}
    env.update({
        "TML_EXT_ID": "com.tyw.demo.smoke",
        "TML_EXT_API": "1",
        "TML_EXT_PLATFORM": "linux" if sys.platform.startswith("linux") else
                           ("macos" if sys.platform == "darwin" else "windows"),
        "TML_EXT_LAUNCHER_VERSION": "1.1.37",
        "TML_EXT_CHANNEL": "Release",
        "TML_EXT_LANGUAGE": "zh-CN",
    })
    return env


def result_for(method):
    """按协议给扩展的调用回一个合理的结果。"""
    if method == "host.info":
        return {"platform": "smoke", "arch": "x64", "version": "1.1.37",
                "channel": "Release", "language": "zh-CN", "apiVersion": 1}
    if method == "instances.list":
        return {"instances": [{"id": "a1", "name": "生存", "gameVersion": "1.20.1", "loader": "Fabric"}]}
    if method in ("storage.get", "settings.get"):
        return {"value": "smoke"}
    if method == "event.subscribe":
        return {"subscribed": True}
    return {}


class Host:
    def __init__(self, binary, workdir):
        self.proc = subprocess.Popen(
            [binary], cwd=workdir,
            env=host_env(), stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
        self.lock = threading.Lock()
        self.calls = []
        self.ready = threading.Event()
        self.error = None
        self.stderr_tail = []
        threading.Thread(target=self._read_loop, daemon=True).start()
        threading.Thread(target=self._stderr_loop, daemon=True).start()

    def _read_loop(self):
        while True:
            line = self.proc.stdout.readline()
            if not line:
                if not self.ready.is_set() and self.error is None:
                    self.error = "扩展提前退出（没等到握手）"
                return
            line = line.strip()
            if not line:
                continue
            try:
                msg = json.loads(line.decode("utf-8", "replace"))
            except ValueError:
                continue
            if "method" in msg and "id" in msg:
                self.calls.append(msg["method"])
                self.send({"id": msg["id"], "result": result_for(msg["method"])})
                continue
            if msg.get("id") == 1:
                if "error" in msg:
                    self.error = "initialize 返回错误：%s" % (msg["error"],)
                elif (msg.get("result") or {}).get("ready"):
                    self.ready.set()
                else:
                    self.error = "initialize 没有回 ready=true：%s" % (msg,)

    def _stderr_loop(self):
        for line in self.proc.stderr:
            self.stderr_tail.append(line.decode("utf-8", "replace").rstrip())
            del self.stderr_tail[:-40]

    def send(self, obj):
        with self.lock:
            try:
                self.proc.stdin.write((json.dumps(obj, ensure_ascii=False) + "\n").encode("utf-8"))
                self.proc.stdin.flush()
            except Exception as ex:
                self.error = "写 stdin 失败：%s" % ex

    def stop(self):
        try:
            self.proc.kill()
        except Exception:
            pass


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    binary = os.path.abspath(sys.argv[1])
    if not os.path.isfile(binary):
        raise SystemExit("找不到可执行文件：" + binary)

    workdir = tempfile.mkdtemp(prefix="tml-ext-smoke-")
    host = Host(binary, workdir)

    host.send({"id": 1, "method": "initialize", "params": {
        "apiVersion": 1, "extId": "com.tyw.demo.smoke", "platform": "smoke",
        "launcherVersion": "1.1.37", "channel": "Release", "language": "zh-CN",
        "privateDir": workdir, "permissions": ["log", "toast", "host.info",
                                              "instances.list", "events", "storage", "settings"],
        "nonce": "smoke-nonce",
    }})

    deadline = time.time() + READY_TIMEOUT
    while time.time() < deadline:
        if host.ready.wait(0.2):
            break
        if host.error:
            break

    def fail(msg):
        host.stop()
        tail = "\n".join(host.stderr_tail)
        if tail:
            msg += "\n--- stderr ---\n" + tail
        raise SystemExit("SMOKE FAILED: " + msg)

    if not host.ready.is_set():
        fail(host.error or "10 秒内没有完成 initialize 握手")

    host.send({"method": "event", "params": {"type": "launcher.started", "data": {"version": "1.1.37"}}})
    time.sleep(0.6)
    host.send({"method": "shutdown"})

    try:
        host.proc.wait(timeout=EXIT_TIMEOUT)
    except subprocess.TimeoutExpired:
        fail("shutdown 后 5 秒内没有退出")

    if host.proc.returncode != 0:
        fail("退出码 %s（应为 0）" % host.proc.returncode)

    print("SMOKE OK: ready=true, 调用 %s, 退出码 0" %
          (", ".join(sorted(set(host.calls))) or "（无）"))


if __name__ == "__main__":
    main()
