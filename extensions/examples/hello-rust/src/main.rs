// Hello Rust —— TML 扩展 API v1 的 Rust 示例。
//
// 宿主把 .tmlext 解开后直接拉起包里的 bin/tml_ext，双方只用 stdin/stdout 的
// NDJSON 通信；能调用什么由 manifest.json 的 permissions 决定。
use tml_ext::{Event, Ext, Handler};

struct Hello;

impl Handler for Hello {
    fn ready(&mut self, ext: &mut Ext) {
        ext.log("info", "Hello Rust 扩展已启动（TML Extension API v1）");

        if let Some(info) = ext.host_info() {
            ext.log("info", &format!("host.info = {}", info));
        }
        if let Some(list) = ext.instances() {
            ext.log("info", &format!("instances = {}", list));
        }

        ext.subscribe(&["launcher.started", "game.launched", "settings.changed"]);

        let version = tml_ext::env("launcher_version").unwrap_or_default();
        ext.storage_set("lastRun", &version);
        if let Some(v) = ext.storage_get("lastRun") {
            ext.log("info", &format!("storage.get(lastRun) = {}", v));
        }
        ext.settings_set("greeted", "1");
        if let Some(v) = ext.settings_get("greeted") {
            ext.log("info", &format!("settings.get(greeted) = {}", v));
        }

        ext.toast("Hello Rust 已就绪");
    }

    fn event(&mut self, ext: &mut Ext, ev: Event) {
        match ev.kind.as_str() {
            "launcher.started" => ext.log("info", "启动器已就绪"),
            "game.launched" => ext.log("info", &format!("开始游戏：{}", ev.data)),
            other => ext.log("info", &format!("事件 {}", other)),
        }
    }
}

fn main() {
    tml_ext::run(Hello);
}
