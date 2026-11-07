# -*- coding: utf-8 -*-
"""TML Extension API v1 / Python 3 SDK.

只读 stdin、只往真正的 stdout 写协议消息。作者的 print() 会自动改道 stderr，
避免把日志混进协议流。SDK 只封装宿主开放的白名单方法，不提供文件 / 网络 / 进程 API。

用法：
    from tml_ext import Ext

    class MyExt(Ext):
        def on_event(self, event_type, data):
            self.log("info", "got " + event_type)
            self.toast("Hello from Python")

    MyExt(name="Hello", version="1.0.0").run()
"""
import itertools
import json
import queue
import sys
import threading


class ExtError(Exception):
    """宿主返回了错误，或请求超时。"""


class PermissionDenied(ExtError):
    """调用了未授权 / 不存在 / 被禁止的方法。"""


class _StdoutToStderr:
    """把扩展作者的 print 改道到 stderr，保护 stdout 协议通道。"""

    def __init__(self, stderr):
        self._stderr = stderr

    def write(self, text):
        try:
            self._stderr.write(text)
            self._stderr.flush()
        except Exception:
            pass
        return len(text)

    def flush(self):
        try:
            self._stderr.flush()
        except Exception:
            pass

    def isatty(self):
        return False


class Ext:
    def __init__(self, name="python-ext", version="1.0.0", runtime="python3"):
        self.name = name
        self.version = version
        self.runtime = runtime
        self.ext_id = ""
        self.platform = ""
        self.launcher_version = ""
        self.api_version = 1
        self.permissions = []
        self.private_dir = ""
        self.nonce = ""

        self._stdin = sys.stdin
        self._stdout = sys.__stdout__ or sys.stdout
        sys.stdout = _StdoutToStderr(sys.stderr)

        self._out_lock = threading.Lock()
        self._ids = itertools.count(2)
        self._pending = {}
        self._pending_lock = threading.Lock()
        self._ready = threading.Event()
        self._stop = threading.Event()
        self._events = queue.Queue()

    # ---------- 协议底层 ----------

    def _send(self, obj):
        line = json.dumps(obj, ensure_ascii=False, separators=(",", ":"))
        if len(line.encode("utf-8")) > 65536:
            return
        with self._out_lock:
            try:
                self._stdout.write(line + "\n")
                self._stdout.flush()
            except Exception:
                self._stop.set()

    def _reply(self, msg_id, result=None, error=None):
        body = {"id": msg_id}
        if error is not None:
            body["error"] = error
        else:
            body["result"] = result if result is not None else {}
        self._send(body)

    def _reader(self):
        try:
            for raw in self._stdin:
                if self._stop.is_set():
                    break
                line = raw.strip()
                if not line or len(line.encode("utf-8", "ignore")) > 65536:
                    continue
                try:
                    msg = json.loads(line)
                except Exception:
                    continue
                if not isinstance(msg, dict):
                    continue
                if "method" in msg:
                    method = msg.get("method")
                    if method == "initialize":
                        self._on_initialize(msg.get("params") or {})
                        self._reply(msg.get("id"), {
                            "name": self.name,
                            "version": self.version,
                            "runtime": self.runtime,
                            "ready": True,
                        })
                    elif method == "event":
                        self._events.put(msg.get("params") or {})
                    elif method == "shutdown":
                        self._stop.set()
                    elif "id" in msg:
                        self._reply(msg.get("id"), error={
                            "code": -32601, "message": "method not allowed for extensions"})
                elif "id" in msg:
                    with self._pending_lock:
                        slot = self._pending.pop(msg.get("id"), None)
                    if slot is not None:
                        slot["message"] = msg
                        slot["event"].set()
        except Exception:
            pass
        finally:
            self._stop.set()

    def _event_worker(self):
        while not self._stop.is_set():
            try:
                params = self._events.get(timeout=0.2)
            except queue.Empty:
                continue
            try:
                self.on_event(str(params.get("type", "")), params.get("data") or {})
            except Exception as exc:
                try:
                    self.log("error", "on_event failed: %s" % exc)
                except Exception:
                    pass

    def _on_initialize(self, params):
        self.api_version = params.get("apiVersion", 1)
        self.ext_id = params.get("extId", "")
        self.platform = params.get("platform", "")
        self.launcher_version = params.get("launcherVersion", "")
        self.permissions = list(params.get("permissions") or [])
        self.private_dir = params.get("privateDir", "")
        self.nonce = params.get("nonce", "")
        self._ready.set()

    def _call(self, method, params=None, timeout=5.0):
        msg_id = next(self._ids)
        slot = {"event": threading.Event(), "message": None}
        with self._pending_lock:
            self._pending[msg_id] = slot
        self._send({"id": msg_id, "method": method, "params": params or {}})
        if not slot["event"].wait(timeout):
            with self._pending_lock:
                self._pending.pop(msg_id, None)
            raise ExtError("host timeout: " + method)
        msg = slot["message"] or {}
        if "error" in msg:
            err = msg.get("error") or {}
            code = err.get("code", -32000)
            text = str(err.get("message", "host error"))
            if code in (-32001, -32601):
                raise PermissionDenied(text)
            raise ExtError(text)
        return msg.get("result", {})

    # ---------- 开放给扩展的白名单 API ----------

    def log(self, level, message):
        return self._call("log", {"level": str(level), "message": str(message)})

    def toast(self, message):
        return self._call("toast", {"message": str(message)})

    def host_info(self):
        return self._call("host.info")

    def instances(self):
        return self._call("instances.list")

    def subscribe(self, event_types):
        return self._call("event.subscribe", {"types": list(event_types)})

    def storage_get(self, key, default=None):
        result = self._call("storage.get", {"key": str(key)})
        return result.get("value", default)

    def storage_set(self, key, value):
        return self._call("storage.set", {"key": str(key), "value": value})

    def settings_get(self, key, default=None):
        result = self._call("settings.get", {"key": str(key)})
        return result.get("value", default)

    def settings_set(self, key, value):
        return self._call("settings.set", {"key": str(key), "value": value})

    # ---------- 生命周期 ----------

    def on_start(self):
        """握手完成后调用，可在此订阅事件 / 打第一条日志。"""
        self.log("info", "%s %s started on %s" % (self.name, self.version, self.platform))

    def on_event(self, event_type, data):
        """收到白名单事件；覆写此方法。"""
        pass

    def on_stop(self):
        pass

    def run(self):
        reader = threading.Thread(target=self._reader, daemon=True)
        worker = threading.Thread(target=self._event_worker, daemon=True)
        reader.start()
        worker.start()
        # 宿主在 10 秒内不发 initialize 就会主动结束进程，这里不必自行超时
        self._ready.wait()
        try:
            self.on_start()
        except Exception as exc:
            try:
                self.log("error", "on_start failed: %s" % exc)
            except Exception:
                pass
        self._stop.wait()
        try:
            self.on_stop()
        except Exception:
            pass
