//! TML 扩展 API v1 的 Rust SDK（运行时：`rust` / `native`）。
//!
//! TYW Launcher 的扩展永远运行在启动器进程之外，只通过 stdin/stdout 的
//! NDJSON（每行一个 JSON 对象）通信。本 crate **只依赖 Rust 标准库**，
//! 不引入 serde 之类的第三方依赖，`cargo build --release` 出来的可执行文件
//! 直接就是扩展包里的 `entry`。
//!
//! ```no_run
//! use tml_ext::Ext;
//!
//! fn main() {
//!     tml_ext::run(|ext, ev| {
//!         ext.log("info", &format!("收到事件 {}", ev.kind));
//!     });
//! }
//! ```
//!
//! 可调用的能力由 `manifest.json` 的 `permissions` 决定：没申请的调用会被宿主
//! 拒绝（`-32001`）。协议细节见 `extensions/README.md`。

use std::io::{self, BufRead, Write};

/// 与宿主一致：单行消息上限 64 KiB。
pub const MAX_LINE: usize = 64 * 1024;

// ------------------------------------------------------------------ JSON 工具

mod json {
    /// 把字符串转义成 JSON 字面量（不含两侧引号）。
    pub fn escape(s: &str) -> String {
        let mut out = String::with_capacity(s.len() + 8);
        for c in s.chars() {
            match c {
                '"' => out.push_str("\\\""),
                '\\' => out.push_str("\\\\"),
                '\n' => out.push_str("\\n"),
                '\r' => out.push_str("\\r"),
                '\t' => out.push_str("\\t"),
                c if (c as u32) < 0x20 => out.push_str(&format!("\\u{:04x}", c as u32)),
                c => out.push(c),
            }
        }
        out
    }

    /// 在 JSON 文本里定位 `"key"` 之后的值起点。
    fn value_start<'a>(text: &'a str, key: &str) -> Option<&'a str> {
        let pat = format!("\"{}\"", key);
        let mut from = 0usize;
        while let Some(pos) = text[from..].find(&pat) {
            let mut rest = &text[from + pos + pat.len()..];
            rest = rest.trim_start();
            if let Some(r) = rest.strip_prefix(':') {
                return Some(r.trim_start());
            }
            from += pos + pat.len();
        }
        None
    }

    /// 取字段的字符串值（支持 \" \\ \n \r \t）。
    pub fn get_str(text: &str, key: &str) -> Option<String> {
        let v = value_start(text, key)?;
        let mut it = v.chars();
        if it.next()? != '"' {
            return None;
        }
        let mut out = String::new();
        let mut escaped = false;
        for c in it {
            if escaped {
                match c {
                    'n' => out.push('\n'),
                    'r' => out.push('\r'),
                    't' => out.push('\t'),
                    other => out.push(other),
                }
                escaped = false;
            } else if c == '\\' {
                escaped = true;
            } else if c == '"' {
                return Some(out);
            } else {
                out.push(c);
            }
        }
        None
    }

    /// 取字段的整数值（`id` / `apiVersion` 等）。
    pub fn get_int(text: &str, key: &str) -> Option<i64> {
        let v = value_start(text, key)?;
        let digits: String = v
            .chars()
            .take_while(|c| *c == '-' || c.is_ascii_digit())
            .collect();
        digits.parse::<i64>().ok()
    }

    /// 取字段的对象原文（`{"a":1}`），用于把 `params` / `result` / `data` 转交出去。
    pub fn get_obj(text: &str, key: &str) -> Option<String> {
        let v = value_start(text, key)?;
        if !v.starts_with('{') {
            return None;
        }
        let bytes = v.as_bytes();
        let mut depth = 0i32;
        let mut in_str = false;
        let mut escaped = false;
        for (i, b) in bytes.iter().enumerate() {
            if in_str {
                if escaped {
                    escaped = false;
                } else if *b == b'\\' {
                    escaped = true;
                } else if *b == b'"' {
                    in_str = false;
                }
                continue;
            }
            match *b {
                b'"' => in_str = true,
                b'{' => depth += 1,
                b'}' => {
                    depth -= 1;
                    if depth == 0 {
                        return Some(v[..=i].to_string());
                    }
                }
                _ => {}
            }
        }
        None
    }
}

// ---------------------------------------------------------------------- 类型

/// 宿主广播过来的一条白名单事件；`data` 是脱敏后的 JSON 原文。
pub struct Event {
    /// 事件名，例如 `game.launched`。
    pub kind: String,
    /// 事件数据的 JSON 原文（对象），没有数据时是 `{}`。
    pub data: String,
}

/// 一条与宿主的会话，由 [`run`] 创建。
pub struct Ext {
    next_id: i64,
    stop: bool,
}

/// 扩展的业务入口：两个方法都可以不实现（默认什么都不做）。
pub trait Handler {
    /// 握手完成、可以调用宿主能力之后触发。
    fn ready(&mut self, _ext: &mut Ext) {}
    /// 收到宿主广播时触发。
    fn event(&mut self, _ext: &mut Ext, _ev: Event) {}
}

/// 只处理事件的简易写法。
pub struct Events<F: FnMut(&mut Ext, Event)>(pub F);

impl<F: FnMut(&mut Ext, Event)> Handler for Events<F> {
    fn event(&mut self, ext: &mut Ext, ev: Event) {
        (self.0)(ext, ev);
    }
}

/// 完成握手并进入事件循环，宿主发 `shutdown` 或 stdin 关闭时返回。
pub fn run<H: Handler>(mut handler: H) {
    let mut ext = Ext {
        next_id: 2, // 1 留给宿主的 initialize
        stop: false,
    };

    if !ext.handshake() {
        return;
    }
    handler.ready(&mut ext);

    loop {
        let line = match ext.read_line() {
            Some(l) => l,
            None => break,
        };
        if line.is_empty() {
            continue;
        }
        if json::get_str(&line, "method").as_deref() == Some("event") {
            let params = json::get_obj(&line, "params").unwrap_or_else(|| "{}".to_string());
            let kind = json::get_str(&params, "type").unwrap_or_default();
            let data = json::get_obj(&params, "data").unwrap_or_else(|| "{}".to_string());
            handler.event(&mut ext, Event { kind, data });
            if ext.stop {
                break;
            }
        } else if !ext.handle(&line) {
            break;
        }
    }
}

impl Ext {
    fn handshake(&mut self) -> bool {
        loop {
            let line = match self.read_line() {
                Some(l) => l,
                None => return false,
            };
            if line.is_empty() {
                continue;
            }
            let is_init = json::get_str(&line, "method").as_deref() == Some("initialize");
            self.handle(&line);
            if is_init {
                return true;
            }
            if self.stop {
                return false;
            }
        }
    }

    fn send(&self, line: &str) {
        let stdout = io::stdout();
        let mut lock = stdout.lock();
        let _ = lock.write_all(line.as_bytes());
        let _ = lock.write_all(b"\n");
        let _ = lock.flush();
    }

    fn read_line(&self) -> Option<String> {
        let stdin = io::stdin();
        let mut buf = String::new();
        match stdin.lock().read_line(&mut buf) {
            Ok(0) => None,
            Ok(_) => {
                let line = buf.trim_end_matches(['\r', '\n']).to_string();
                if line.len() > MAX_LINE {
                    Some(line[..MAX_LINE].to_string())
                } else {
                    Some(line)
                }
            }
            Err(_) => None,
        }
    }

    /// 处理一条宿主消息；返回 false 表示应当退出。
    fn handle(&mut self, line: &str) -> bool {
        if line.contains("\"result\"") || line.contains("\"error\"") {
            return !self.stop; // 不是发给我们的响应
        }
        let method = match json::get_str(line, "method") {
            Some(m) => m,
            None => return !self.stop,
        };
        let id = json::get_int(line, "id").unwrap_or(0);
        match method.as_str() {
            "initialize" => {
                self.send(&format!("{{\"id\":{},\"result\":{{\"ready\":true}}}}", id));
            }
            "shutdown" => {
                self.stop = true;
            }
            "event" => {
                // 事件在 run() 里分发；这里只保证不会误回响应
            }
            _ => {
                self.send(&format!("{{\"id\":{},\"result\":{{}}}}", id));
            }
        }
        !self.stop
    }

    /// 同步调用宿主能力，阻塞到拿到同 `id` 的响应。
    fn call(&mut self, method: &str, params: &str) -> Option<String> {
        let id = self.next_id;
        self.next_id += 1;
        self.send(&format!(
            "{{\"id\":{},\"method\":\"{}\",\"params\":{}}}",
            id,
            json::escape(method),
            if params.is_empty() { "{}" } else { params }
        ));
        loop {
            let line = self.read_line()?;
            if line.is_empty() {
                continue;
            }
            let is_reply = line.contains("\"result\"") || line.contains("\"error\"");
            if is_reply && json::get_int(&line, "id") == Some(id) {
                if line.contains("\"error\"") {
                    return None;
                }
                return json::get_obj(&line, "result").or_else(|| Some("{}".to_string()));
            }
            self.handle(&line);
            if self.stop {
                return None;
            }
        }
    }

    /// 请求退出事件循环。
    pub fn stop(&mut self) {
        self.stop = true;
    }

    // ------------------------------------------------------------ 白名单能力

    /// 写启动器日志；`level` 只能是 info / warn / error。
    pub fn log(&mut self, level: &str, message: &str) {
        let level = match level {
            "warn" | "error" => level,
            _ => "info",
        };
        let p = format!(
            "{{\"level\":\"{}\",\"message\":\"{}\"}}",
            json::escape(level),
            json::escape(&truncate(message, 4096))
        );
        self.call("log", &p);
    }

    /// 显示一条提示（纯文本，最长 300 字）。
    pub fn toast(&mut self, message: &str) {
        let p = format!(
            "{{\"message\":\"{}\"}}",
            json::escape(&truncate(message, 300))
        );
        self.call("toast", &p);
    }

    /// 只读：平台、架构、启动器版本、通道、语言、API 版本（JSON 原文）。
    pub fn host_info(&mut self) -> Option<String> {
        self.call("host.info", "{}")
    }

    /// 只读实例列表（仅 id / 名称 / 游戏版本 / 加载器类型，JSON 原文）。
    pub fn instances(&mut self) -> Option<String> {
        self.call("instances.list", "{}")
    }

    /// 订阅白名单事件（清单里也要写在 `subscribe` 里）。
    pub fn subscribe(&mut self, types: &[&str]) -> Option<String> {
        if types.is_empty() {
            return Some("{}".to_string());
        }
        let list: Vec<String> = types
            .iter()
            .map(|t| format!("\"{}\"", json::escape(t)))
            .collect();
        self.call("event.subscribe", &format!("{{\"types\":[{}]}}", list.join(",")))
    }

    /// 读扩展私有目录内的键值；不存在时返回 `None`。
    pub fn storage_get(&mut self, key: &str) -> Option<String> {
        self.read_value("storage.get", key)
    }

    /// 写扩展私有目录内的键值（value ≤ 64 KiB）。
    pub fn storage_set(&mut self, key: &str, value: &str) {
        let p = format!(
            "{{\"key\":\"{}\",\"value\":\"{}\"}}",
            json::escape(key),
            json::escape(&truncate(value, 64 * 1024))
        );
        self.call("storage.set", &p);
    }

    /// 读 `ext.<extId>.` 命名空间下的设置项。
    pub fn settings_get(&mut self, key: &str) -> Option<String> {
        self.read_value("settings.get", key)
    }

    /// 写 `ext.<extId>.` 命名空间下的设置项（value ≤ 8 KiB）。
    pub fn settings_set(&mut self, key: &str, value: &str) {
        let p = format!(
            "{{\"key\":\"{}\",\"value\":\"{}\"}}",
            json::escape(key),
            json::escape(&truncate(value, 8 * 1024))
        );
        self.call("settings.set", &p);
    }

    fn read_value(&mut self, method: &str, key: &str) -> Option<String> {
        let p = format!("{{\"key\":\"{}\"}}", json::escape(key));
        let r = self.call(method, &p)?;
        json::get_str(&r, "value")
    }
}

/// 读宿主下发的协议配置（`TML_EXT_ID` / `TML_EXT_API` / `TML_EXT_PLATFORM` …）。
pub fn env(name: &str) -> Option<String> {
    std::env::var(format!("TML_EXT_{}", name.to_uppercase())).ok()
}

fn truncate(s: &str, max: usize) -> String {
    if s.len() <= max {
        return s.to_string();
    }
    let mut end = max;
    while end > 0 && !s.is_char_boundary(end) {
        end -= 1;
    }
    s[..end].to_string()
}
