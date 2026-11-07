# TML Extension API v1（TMLExt / jar）

一套在 **Windows / Linux / macOS / Android** 四端行为一致的扩展接口。
扩展**永远运行在启动器进程之外**，宿主与扩展之间只通过标准输入输出的
**NDJSON（每行一个 JSON 对象）** 通信，扩展拿不到宿主内存、对象、凭据或任意系统权限。

> 设计前提：**默认全部拒绝**。不声明就不能用；声明了也不代表一定给。
> 敏感能力和一切可能造成漏洞的能力，不开放——不是「默认关闭」，而是**协议里根本不存在**。

---

## 1. 两种包格式

### 1.1 `.tmlext`（Python / Node.js / C / Rust / Go）

一个 ZIP 压缩包（改后缀为 `.tmlext`），根目录必须有 `manifest.json`：

```json
{
  "format": "tmlext",
  "apiVersion": 1,
  "id": "com.example.hello",
  "name": "Hello 扩展",
  "version": "1.0.0",
  "author": "example",
  "runtime": "python3",
  "entry": "main.py",
  "platforms": ["windows", "linux", "macos", "android"],
  "permissions": ["log", "toast", "events", "storage"],
  "subscribe": ["launcher.started", "game.launched"]
}
```

| 字段 | 说明 |
| --- | --- |
| `format` | 固定 `tmlext` |
| `apiVersion` | 固定 `1`，不匹配直接拒绝加载 |
| `id` | 仅 `[a-z0-9._-]`，全局唯一，同时是私有目录名 |
| `runtime` | `python3`、`node`，或原生运行时 `c` / `cpp` / `rust` / `go` / `native`（原生扩展的 `entry` 直接是可执行文件，宿主不套解释器） |
| `entry` | 包内相对路径，禁止 `..`、绝对路径、`\`、盘符、符号链接 |
| `platforms` | 允许运行的平台；当前平台不在其中则禁用 |
| `permissions` | 见第 3 节，**只能出现白名单里的值，出现未知值直接拒绝加载** |
| `subscribe` | 想订阅的事件，同样只能出现事件白名单里的值 |

包内约束：单文件 ≤ 2 MiB、整包解压后 ≤ 8 MiB、文件数 ≤ 64、禁止绝对路径 /
`..` / ZIP Slip、禁止符号链接、禁止可执行位、禁止嵌套压缩包。

### 1.2 `.jar`（Java）

标准 JAR，`META-INF/tml-extension.json` 内容与上面的 manifest 相同，但：

* `format` = `"jar"`，`runtime` = `"java"`；
* `entry` 是**主类全名**（例如 `cn.tyw.launcher.ext.demo.HelloJava`），必须有一个
  `public static void main(String[])`（SDK 基类已提供）；
* 宿主用 `java -cp <jar> <主类>` 启动独立 JVM 进程，同样走 stdio 协议；
* **不会把 jar 加载进启动器进程**（不做反射 / ClassLoader / JNI / dex 加载）。
  宿主进程里永远没有 JVM，Android 也不需要动态加载 dex。

`extensions/sdk/java` 里的 `TMLExt.java` 是可直接拷进工程的基础类，
`extensions/examples/hello-java` 是完整可编译示例。

---

## 2. 通信协议（TML-Ext/1，NDJSON）

* 每行一个 JSON 对象，UTF-8，单行 ≤ 64 KiB；扩展的 `stdout` **只能**写协议消息
  （写日志请调 `log` 方法），`stderr` 由宿主收集进日志。
* 宿主 → 扩展 `请求`：`{"id":1,"method":"initialize","params":{...}}`
* 扩展 → 宿主 `响应`：`{"id":1,"result":{...}}` 或
  `{"id":1,"error":{"code":-32001,"message":"..."}}`
* 宿主 → 扩展 `事件通知`（无 id，不需要响应）：
  `{"method":"event","params":{"type":"launcher.started","data":{...}}}`
* 扩展 → 宿主 `调用`：`{"id":2,"method":"log","params":{"level":"info","message":"hi"}}`
* 扩展发来的任何 `id` 必须与自己发起的请求对应；宿主忽略格式错误的行。

### 握手

宿主启动扩展后立刻发：

```json
{"id":1,"method":"initialize","params":{
  "apiVersion":1,
  "extId":"com.example.hello",
  "platform":"windows",
  "launcherVersion":"1.1.37",
  "channel":"Release",
  "language":"zh-CN",
  "privateDir":"/…/extensions/data/com.example.hello",
  "permissions":["log","toast","events","storage"],
  "nonce":"一次性随机串"
}}
```

扩展须在 **10 秒内**回：

```json
{"id":1,"result":{"name":"Hello 扩展","version":"1.0.0","runtime":"python3","ready":true}}
```

之后才能调用宿主方法。宿主发 `{"method":"shutdown"}` 时扩展应尽快退出；
超时或崩溃会被直接结束进程并自动禁用该扩展。

---

## 3. 开放的能力（白名单，默认全部拒绝）

manifest 的 `permissions` 只能填下面这些；运行时调用未授权方法返回 `-32001`。

| 能力 | 对应方法 | 说明 |
| --- | --- | --- |
| `log` | `log` | 写启动器日志；`level` ∈ info/warn/error，`message` ≤ 4 KiB，限频 |
| `toast` | `toast` | 显示一条提示；`message` ≤ 300 字，限频，不接收 HTML / 链接 / 命令 |
| `host.info` | `host.info` | 只读：平台、架构、启动器版本、通道、语言、API 版本 |
| `instances.list` | `instances.list` | 只读实例列表：**仅** id / 名称 / 游戏版本 / 加载器类型，不含路径、账号、token |
| `events` | `event.subscribe` | 订阅第 4 节白名单事件；数据一律脱敏 |
| `storage` | `storage.get` / `storage.set` | 读写**扩展私有目录内**的键值；key 仅 `[A-Za-z0-9._-]`，value ≤ 64 KiB |
| `settings` | `settings.get` / `settings.set` | 只读写 `ext.<extId>.` 命名空间；value ≤ 8 KiB |

`storage` 的路径校验：解析为绝对路径后必须仍落在 `privateDir` 内，
禁止 `..`、绝对路径、符号链接、设备名；写操作原子替换，单扩展总量 ≤ 1 MiB。

---

## 4. 事件白名单

`launcher.started`、`launcher.shutdown`、`game.launching`、`game.launched`、
`game.exited`、`instance.listChanged`、`settings.changed`（仅扩展自身命名空间）。

事件 `data` 只包含脱敏后的简单类型（字符串 / 数字 / 布尔 / 数组），
不含文件路径、命令行、环境变量、账号、设备码、服务器地址。

---

## 5. 明确不开放（协议里根本不存在）

即使 manifest 声明、即使扩展自己实现，宿主也**永远不会**提供这些方法：

* 任意文件读写 / 目录遍历 / 读取启动器配置、数据库、日志、缓存中的凭据；
* 任意进程启动、shell / 命令行执行、`exec` / `spawn` / `system`；
* 任意网络访问：HTTP / socket / DNS / 代理 / 下载 / 上传（防 SSRF、防数据外带）；
* 账户与凭据：微软/外置登录 token、刷新令牌、设备码、内测码、更新码、服务端地址、通信密钥；
* 注册表、系统环境变量（子进程只拿到最少白名单）、剪贴板、输入注入、截图、提权；
* 修改启动器设置（除自身命名空间）、安装/卸载、下载、启动或结束游戏、触发自更新、退出启动器；
* 反射、JNI、宿主内部对象、往宿主进程里加载任何代码；
* 读取其它扩展的私有目录、跨扩展通信、读取 `trusted.json`；
* 访问 `server.txt`、更新包、版本分发配置。

宿主侧另有一份**硬编码方法黑名单**（`DeniedMethods`），
收到这些方法名一律拒绝并记入日志，防止后续误加 API 时被绕过。
扩展进程启动时还会**清除** `NODE_OPTIONS`、`PYTHONPATH`、`PYTHONSTARTUP`、
`JAVA_TOOL_OPTIONS`、`_JAVA_OPTIONS`、`CLASSPATH` 等可被用来注入代码的变量，
只保留 `PATH` / 临时目录 / 语言等最小集合，且不继承启动器自身的任何环境变量。

---

## 6. 启用流程（安全闸门）

1. 用户手动把 `.tmlext` / `.jar` 放进启动器的 `extensions/` 目录。
2. 宿主校验：ZIP/JAR 结构、manifest 字段、apiVersion、平台、runtime、entry 路径、
   大小与文件数、权限与事件白名单、id 合法性。**校验失败不加载**。
3. **默认全部禁用**。必须在设置里看到扩展名称、版本、作者、申请的权限清单后，
   由用户手动点「启用」。启用时记录包 SHA-256 到 `trusted.json`。
4. 包内容一旦变化（SHA-256 不匹配）→ 自动重新禁用，等待用户再次确认。
5. 运行时隔离：
   * 子进程工作目录 = 扩展私有目录；不继承宿主环境变量；
   * 初始化 10s、单次请求 5s、单行 64 KiB、输出总量限制；
   * 每扩展最多 1 个进程，宿主最多同时 8 个扩展进程；
   * 超时 / 崩溃 / 协议错误 → 结束进程并自动禁用，不影响启动器；
   * 找不到对应 runtime（`python3` / `node` / `java`）→ 显示「缺少运行时」并禁用，
     **宿主绝不自动下载或安装运行时**；原生运行时（`c` / `rust` / `go` 等）不需要解释器，
     但包内找不到 `entry` 指定的可执行文件同样拒绝启动。
6. 用户可随时一键禁用；禁用会结束进程，但保留私有数据。

---

## 7. 四端一致性与差异

| 平台 | 宿主实现 | python3 | node | java | c / rust / go |
| --- | --- | --- | --- | --- | --- |
| Windows | `TYW Launcher/Modules/Ext/*.vb` | `py -3` / `python` | `node` | `java` / `JAVA_HOME` | 直接执行包内二进制 |
| Linux | `TYWLauncher-Linux/src/TYW.Core/Ext/ExtManager.vb` | `python3` | `node` | `java` | 直接执行包内二进制（自动补执行位） |
| macOS | `TYWLauncher-macOS/src/TYW.Core/Ext/ExtManager.vb` | `python3` | `node` | `java` | 直接执行包内二进制（自动补执行位） |
| Android | `…/cn/tyw/launcher/core/ext/ExtManager.kt` | 需设备 PATH 中已有 | 需设备 PATH 中已有 | 需设备 PATH 中已有 | 受系统限制，见下方说明 |

协议、manifest、权限、事件、信任机制四端完全相同，**同一个扩展包四端通用**。
Android 出于系统限制（应用不能执行自身数据目录下的二进制、也不允许动态加载 dex），
默认找不到任何运行时；只有设备系统 PATH 里确实存在解释器、且用户显式配置并启用时才会运行，
宿主不会引入脚本引擎、不会加载 jar/dex、不会下载运行时。这是有意为之的安全限制。

---

## 8. SDK 与示例

```
extensions/
├── README.md                    本规范
├── schema/manifest.schema.json  manifest 的 JSON Schema
├── sdk/python/tml_ext.py        Python SDK
├── sdk/node/tml_ext.js          Node.js SDK
├── sdk/java/…/TMLExt.java       Java SDK 基类
├── examples/hello-python/       Python 示例（可直接打包）
├── examples/hello-node/         Node.js 示例
├── examples/hello-java/         Java 示例（含 manifest 与源码）
└── pack.py                      把示例目录打成 .tmlext / .jar 并输出 SHA-256
```

Python 与 Node 的 SDK 负责读 stdin、写 stdout、分发事件、发请求并等待响应；
扩展作者只需写 `on_event` / 调 `Ext.log()` 之类的方法。Java SDK 同理，
继承 `TMLExt` 覆写 `onEvent` 即可，主类由 SDK 提供 `main`。

### 六种语言的官方模板仓库

| 运行时 | 仓库 | 说明 |
| --- | --- | --- |
| Python 3 | <https://github.com/TYWStudio/template-tml-extension-python> | SDK + 示例 + 打包脚本 + CI |
| Node.js | <https://github.com/TYWStudio/template-tml-extension-node> | SDK + 示例 + 打包脚本 + CI |
| Java | <https://github.com/TYWStudio/template-tml-extension-java> | 独立 JVM 进程，SDK 基类 + 示例 |
| C | <https://github.com/TYWStudio/template-tml-extension-c> | 零依赖原生可执行，gcc / clang / MSVC |
| Rust | <https://github.com/TYWStudio/template-tml-extension-rust> | 仅标准库，cargo / rustc |
| Go | <https://github.com/TYWStudio/template-tml-extension-go> | 仅标准库，静态二进制 |

模板仓库都带 `build.sh` / `build.bat`（编译 + 打包成 `.tmlext`）、`manifest.json`、
`tests/smoke.py`（模拟宿主跑一遍协议）与 GitHub Actions 构建，克隆即可开工。

打包：

```bash
python extensions/pack.py extensions/examples/hello-python
# → hello-python.tmlext + hello-python.tmlext.sha256
```

---

## 9. 宿主侧接口契约（四端一致）

四端宿主暴露同一组方法（按语言习惯调整大小写）：

| 方法 | 作用 |
| --- | --- |
| `Configure(...)` | 注入宿主回调：写日志、显示提示、宿主信息、实例列表、设置读写、目录位置。核心库不直接依赖 UI 框架 |
| `Scan()` | 扫描 `extensions/` 下的 `.tmlext` / `.jar`，校验清单，返回扩展列表（不启动任何进程） |
| `GetAll()` | 返回当前已发现的扩展（`ExtInfo`：id、名称、版本、作者、runtime、权限、平台、SHA-256、状态、错误原因） |
| `Enable(id, userConfirmed)` | 用户看过权限清单并确认后启用；`userConfirmed = false` 一律拒绝。启用时记录包 SHA-256 |
| `Disable(id)` | 结束进程并禁用，保留私有数据 |
| `IsEnabled(id)` | 查询状态 |
| `Broadcast(type, data)` | 向所有已启用且已订阅该事件的扩展广播（只接受事件白名单，数据脱敏） |
| `ShutdownAll()` | 启动器退出时结束所有扩展进程 |

实现位置：

* Windows：`TYW Launcher/Modules/Ext/ModExt.vb` + `ModExtHost.vb`
* Linux：`TYWLauncher-Linux/src/TYW.Core/Ext/ExtManager.vb`
* macOS：`TYWLauncher-macOS/src/TYW.Core/Ext/ExtManager.vb`
* Android：`TYWLauncher-Android/app/src/main/java/cn/tyw/launcher/core/ext/ExtManager.kt`

宿主应答示例：

```json
{"id":7,"result":{"instances":[{"id":"a1","name":"生存","gameVersion":"1.20.1","loader":"Fabric"}]}}
{"id":8,"error":{"code":-32001,"message":"permission denied: network.fetch"}}
```

错误码：`-32001` 未授权 / 被禁止的方法，`-32600` 参数不合法，
`-32601` 协议里不存在的方法，`-32002` 超出配额，`-32000` 宿主内部错误。

---

## 10. 信任边界（必读）

「接口不开放」≠「操作系统级沙箱」。宿主保证的是：

* 不向扩展提供任何敏感数据或敏感能力，扩展拿不到启动器进程内的对象、凭据与配置；
* 子进程不继承启动器环境变量（清除 `NODE_OPTIONS`、`PYTHONPATH`、`PYTHONSTARTUP`、
  `JAVA_TOOL_OPTIONS`、`_JAVA_OPTIONS`、`CLASSPATH` 等可注入变量，只留最小白名单，
  并强制 `PYTHONIOENCODING=utf-8`）；
* 默认全部禁用、必须由用户确认权限清单后启用、包内容变化自动重新禁用；
* 超时 / 崩溃 / 协议错误自动结束进程并禁用，不影响启动器。

但 **Python / Node / Java 解释器进程内部仍能调用各自语言的标准库**（文件、网络、进程），
这是解释器自身能力，不是宿主授予的 API，宿主无法在四端用统一方式剥夺。
因此：**只启用你信任来源、并审阅过源码的扩展**，不要启用来路不明的 `.tmlext` / `.jar`。
需要 OS 级强隔离时应自行配合系统沙箱（AppContainer / seccomp-bpf / sandbox-exec / SELinux），
启动器不做提权、不自动配置沙箱、不替用户下载运行时、不自动启用、不静默更新扩展包。
Android 端更严格：不加载 dex/jar 到宿主进程，默认找不到任何运行时。

---

## 11. 接入示例（宿主线程）

扩展不会自己启动：`Scan()` 只列清单，`Enable(id, True)` 才启动进程，
第二个参数必须是**用户在界面上确认过权限清单**之后才能传 `True`。
宿主在合适的时机调用 `Broadcast` 把白名单事件送出去，退出时 `ShutdownAll()`。

Windows（VB.NET，`ModMain` 初始化后）：

```vb
ModExt.Configure(
    toastCallback:=Sub(msg) RunInUi(Sub() Hint("[扩展] " & msg, HintType.Blue)),
    instancesProvider:=Function() GetSafeInstanceArray(),
    rootProvider:=Function() Path & "extensions")
Dim list As List(Of ModExt.ExtInfo) = ModExt.Scan()      ' 展示名称 / 版本 / 权限清单
ModExt.Enable("com.tyw.demo.hellopython", userConfirmed:=True)
ModExt.Broadcast("launcher.started", JObject.Parse("{""version"":""1.1.37""}"))
' 退出时：ModExt.ShutdownAll()
```

Linux / macOS（VB.NET Core）：

```vb
Dim mgr = TYW.Core.Ext.ExtensionManager.Instance
mgr.Configure(logCallback:=..., toastCallback:=..., hostInfoProvider:=...,
              instancesProvider:=..., settingsReader:=..., settingsWriter:=...,
              extensionRootProvider:=...)
Dim list = mgr.Scan()
mgr.Enable("com.tyw.demo.hellopython", True)
mgr.Broadcast("game.launched", ...)
```

Android（Kotlin）：

```kotlin
ExtManager.configure(
    logCallback = { msg -> Log.d("Ext", msg) },
    toastCallback = { msg -> /* 只在主线程弹 Toast */ },
    rootProvider = { File(filesDir, "extensions") },
    hostInfoProvider = { JSONObject().put("version", BuildConfig.VERSION_NAME) })
val list = ExtManager.scan()
ExtManager.enable("com.tyw.demo.hellonode", userConfirmed = true)
ExtManager.broadcast("launcher.started", JSONObject())
```

`Configure` 的参数都可省略：不注入时退化为「只写日志、不弹提示、只读默认信息、
设置与实例列表为空」，**绝不会回退成调用任意系统能力**。
