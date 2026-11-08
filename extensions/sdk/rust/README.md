# TML Extension SDK · Rust

crate `tml-ext`，零第三方依赖（自带极简 JSON），编译产物是一个原生可执行文件。

## 引用

仓库根部有一份 `Cargo.toml`（workspace），就是给这个 crate 当 git 依赖用的：

```toml
[dependencies]
tml-ext = { git = "https://github.com/TYWStudio/TML", tag = "sdk-rust-v1.0.0" }
```

也可以跟 `branch = "main"` 走最新版（不固定），或在本仓库里就地构建：
`cargo build --release --manifest-path extensions/sdk/rust/Cargo.toml`。

SDK 有改动时**先在本仓库改，再打 tag**，外部工程（含 `template-tml-extension-rust`）
只升级 `tag`，不要复制 `lib.rs` 出来另养一份。

## 最小扩展

```rust
use tml_ext::{Event, Ext, Handler};

struct Hello;

impl Handler for Hello {
    fn ready(&mut self, ext: &mut Ext) {
        ext.subscribe(&["game.launched"]);
        ext.log("info", "已就绪");
    }
    fn event(&mut self, ext: &mut Ext, ev: Event) {
        ext.log("info", &ev.kind);
    }
}

fn main() { tml_ext::run(Hello); }
```

`manifest.json` 里 `runtime` 写 `rust`（或 `native`），`entry` 指向编出来的二进制。

## 构建与打包

```bash
cargo build --release          # 产物：target/release/<包名>
# 或在工程目录里直接打包，pack.py 会自动 cargo build 并把二进制放进包里
python extensions/pack.py <扩展目录>
```

建议在扩展工程里加：

```toml
[profile.release]
opt-level = "z"
lto = true
codegen-units = 1
strip = true
panic = "abort"
```

完整协议见 `extensions/README.md`，示例见 `extensions/examples/hello-rust`。
