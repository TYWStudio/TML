package cn.tyw.launcher.ext.demo;

import cn.tyw.launcher.ext.TMLExt;

import java.util.Arrays;
import java.util.List;
import java.util.Map;

/** 一个最小的 Java 扩展：只调用宿主开放的白名单方法。 */
public final class HelloJava extends TMLExt {

    public static void main(String[] args) {
        launch(new HelloJava());
    }

    public HelloJava() {
        super("Hello Java", "1.0.0");
    }

    @Override
    protected void onStart() {
        log("info", "Hello Java started on " + platform + " (" + launcherVersion + ")");
        subscribe(Arrays.asList("launcher.started", "game.launched"));
        List<Object> instances = instances();
        log("info", "实例数量：" + instances.size());
    }

    @Override
    protected void onEvent(String type, Map<String, Object> data) {
        log("info", "收到事件：" + type);
        if ("game.launched".equals(type)) {
            toast("游戏已启动");
        }
    }
}
