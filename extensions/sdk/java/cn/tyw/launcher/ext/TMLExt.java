package cn.tyw.launcher.ext;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * TML Extension API v1 / Java SDK 基类。
 *
 * 宿主用 {@code java -cp <jar> <主类>} 启动独立进程，本类负责 stdio 上的
 * NDJSON 协议。stdout 只写协议消息，日志请用 log()；System.out 已被改道 stderr。
 * 只封装白名单方法，不提供文件 / 网络 / 进程 API。
 *
 * 主类示例：
 *   public final class HelloJava extends TMLExt {
 *       public static void main(String[] args) { launch(new HelloJava()); }
 *       protected void onEvent(String type, Map<String, Object> data) { ... }
 *   }
 */
public abstract class TMLExt {

    // ---------- 扩展信息（由 initialize 填充） ----------
    protected String extId = "";
    protected String platform = "";
    protected String launcherVersion = "";
    protected String channel = "";
    protected String language = "";
    protected String privateDir = "";
    protected List<String> permissions = new ArrayList<String>();
    protected int apiVersion = 1;

    private final String name;
    private final String version;
    private final AtomicInteger nextId = new AtomicInteger(2);
    private final AtomicBoolean stopped = new AtomicBoolean(false);
    private final ConcurrentHashMap<Integer, Slot> pending = new ConcurrentHashMap<Integer, Slot>();
    private final BufferedReader in;
    private final PrintWriter protocolOut;
    private final ExecutorService events = Executors.newSingleThreadExecutor(r -> {
        Thread t = new Thread(r, "tml-ext-events");
        t.setDaemon(true);
        return t;
    });

    protected TMLExt(String name, String version) {
        this.name = name == null ? "java-ext" : name;
        this.version = version == null ? "1.0.0" : version;
        this.in = new BufferedReader(new InputStreamReader(System.in, StandardCharsets.UTF_8));
        this.protocolOut = new PrintWriter(new OutputStreamWriter(System.out, StandardCharsets.UTF_8), true);
        // 作者的 System.out.println 改道 stderr，保护协议通道
        try {
            System.setOut(System.err);
        } catch (SecurityException ignored) {}
    }

    /** 主类里调用：{@code public static void main(String[] a) { launch(new MyExt()); }} */
    protected static void launch(TMLExt ext) {
        ext.run();
    }

    // ---------- 协议 ----------

    private static final class Slot {
        final CountDownLatch latch = new CountDownLatch(1);
        Map<String, Object> message;
    }

    private synchronized void send(Map<String, Object> obj) {
        String line = Json.stringify(obj);
        if (line.getBytes(StandardCharsets.UTF_8).length > 65536) return;
        protocolOut.println(line);
        protocolOut.flush();
    }

    private void reply(Object id, Map<String, Object> result, Map<String, Object> error) {
        Map<String, Object> body = new LinkedHashMap<String, Object>();
        body.put("id", id);
        if (error != null) body.put("error", error);
        else body.put("result", result == null ? new LinkedHashMap<String, Object>() : result);
        send(body);
    }

    private void onInitialize(Map<String, Object> params) {
        apiVersion = intOf(params.get("apiVersion"), 1);
        extId = Json.str(params.get("extId"));
        platform = Json.str(params.get("platform"));
        launcherVersion = Json.str(params.get("launcherVersion"));
        channel = Json.str(params.get("channel"));
        language = Json.str(params.get("language"));
        privateDir = Json.str(params.get("privateDir"));
        permissions = new ArrayList<String>();
        for (Object p : Json.asList(params.get("permissions"))) permissions.add(Json.str(p));
    }

    private static int intOf(Object o, int fallback) {
        if (o instanceof Number) return ((Number) o).intValue();
        try { return Integer.parseInt(Json.str(o)); } catch (Exception e) { return fallback; }
    }

    @SuppressWarnings("unchecked")
    private void handle(String line) {
        if (line == null || line.isEmpty() || line.getBytes(StandardCharsets.UTF_8).length > 65536) return;
        Object parsed;
        try { parsed = Json.parse(line); } catch (Exception e) { return; }
        if (!(parsed instanceof Map)) return;
        Map<String, Object> msg = (Map<String, Object>) parsed;

        if (msg.containsKey("method")) {
            String method = Json.str(msg.get("method"));
            Map<String, Object> params = Json.asMap(msg.get("params"));
            if ("initialize".equals(method)) {
                onInitialize(params);
                Map<String, Object> result = new LinkedHashMap<String, Object>();
                result.put("name", name);
                result.put("version", version);
                result.put("runtime", "java");
                result.put("ready", Boolean.TRUE);
                reply(msg.get("id"), result, null);
                events.submit(() -> {
                    try { onStart(); } catch (Exception e) { safeLog("onStart failed: " + e); }
                });
            } else if ("event".equals(method)) {
                final String type = Json.str(params.get("type"));
                final Map<String, Object> data = Json.asMap(params.get("data"));
                events.submit(() -> {
                    try { onEvent(type, data); } catch (Exception e) { safeLog("onEvent failed: " + e); }
                });
            } else if ("shutdown".equals(method)) {
                stop();
            } else if (msg.containsKey("id")) {
                Map<String, Object> error = new LinkedHashMap<String, Object>();
                error.put("code", -32601);
                error.put("message", "method not allowed for extensions");
                reply(msg.get("id"), null, error);
            }
            return;
        }

        if (msg.containsKey("id")) {
            int id = intOf(msg.get("id"), -1);
            Slot slot = pending.remove(id);
            if (slot != null) {
                slot.message = msg;
                slot.latch.countDown();
            }
        }
    }

    private void safeLog(String text) {
        try { log("error", text); } catch (Exception ignored) {}
    }

    /** 调用宿主方法；未授权 / 被禁止的方法抛 PermissionDenied。 */
    protected Map<String, Object> call(String method, Map<String, Object> params) {
        if (stopped.get()) throw new IllegalStateException("extension stopped");
        int id = nextId.getAndIncrement();
        Slot slot = new Slot();
        pending.put(id, slot);
        Map<String, Object> request = new LinkedHashMap<String, Object>();
        request.put("id", id);
        request.put("method", method);
        request.put("params", params == null ? new LinkedHashMap<String, Object>() : params);
        send(request);
        try {
            if (!slot.latch.await(5, TimeUnit.SECONDS)) {
                pending.remove(id);
                throw new IllegalStateException("host timeout: " + method);
            }
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("interrupted: " + method);
        }
        Map<String, Object> msg = slot.message == null ? new LinkedHashMap<String, Object>() : slot.message;
        if (msg.containsKey("error")) {
            Map<String, Object> err = Json.asMap(msg.get("error"));
            String text = Json.str(err.get("message"));
            if (intOf(err.get("code"), 0) == -32001 || intOf(err.get("code"), 0) == -32601) {
                throw new SecurityException(text);
            }
            throw new IllegalStateException(text);
        }
        return Json.asMap(msg.get("result"));
    }

    private static Map<String, Object> pair(String key, Object value) {
        Map<String, Object> m = new LinkedHashMap<String, Object>();
        m.put(key, value);
        return m;
    }

    // ---------- 白名单 API ----------

    public void log(String level, String message) {
        Map<String, Object> p = pair("level", level);
        p.put("message", message);
        call("log", p);
    }
    public void toast(String message) { call("toast", pair("message", message)); }
    public Map<String, Object> hostInfo() { return call("host.info", null); }
    public List<Object> instances() { return Json.asList(call("instances.list", null).get("instances")); }

    public void subscribe(List<String> types) {
        Map<String, Object> p = new LinkedHashMap<String, Object>();
        p.put("types", types);
        call("event.subscribe", p);
    }

    public Object storageGet(String key, Object fallback) {
        Object v = call("storage.get", pair("key", key)).get("value");
        return v == null ? fallback : v;
    }

    public void storageSet(String key, Object value) {
        Map<String, Object> p = pair("key", key);
        p.put("value", value);
        call("storage.set", p);
    }

    public Object settingsGet(String key, Object fallback) {
        Object v = call("settings.get", pair("key", key)).get("value");
        return v == null ? fallback : v;
    }

    public void settingsSet(String key, Object value) {
        Map<String, Object> p = pair("key", key);
        p.put("value", value);
        call("settings.set", p);
    }

    // ---------- 生命周期（供扩展覆写） ----------

    protected void onStart() { log("info", name + " " + version + " started on " + platform); }
    protected void onEvent(String type, Map<String, Object> data) { }
    protected void onStop() { }

    public void stop() {
        if (!stopped.compareAndSet(false, true)) return;
        try { onStop(); } catch (Exception ignored) {}
        events.shutdownNow();
        for (Slot slot : pending.values()) slot.latch.countDown();
        pending.clear();
    }

    public void run() {
        Thread reader = new Thread(() -> {
            try {
                String line;
                while (!stopped.get() && (line = in.readLine()) != null) handle(line);
            } catch (Exception ignored) {
            } finally {
                stop();
            }
        }, "tml-ext-reader");
        reader.setDaemon(true);
        reader.start();
        while (!stopped.get()) {
            try { Thread.sleep(100); } catch (InterruptedException e) { break; }
        }
    }
}
