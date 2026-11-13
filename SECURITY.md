<p align="right">
  <b>English</b> · <a href="#简体中文">简体中文</a>
</p>

# Security Policy / 安全策略

TYW Launcher is an open-source, free-of-charge launcher for Minecraft: Java Edition, and we take security
seriously. This document covers which versions we support, how to report a vulnerability privately, how fast
we respond, and what is in scope. **Both an English and a Simplified Chinese version are provided in full.**

> **Security issues only.** Crashes, launch failures and mod incompatibilities belong in
> [Issues](https://github.com/TYWStudio/TML/issues). For the extension runtime's security boundary, see
> [`extensions/SECURITY.md`](extensions/SECURITY.md).
>
> 本文只处理**安全漏洞**。启动失败、模组不兼容、崩溃等使用问题请走 Issues。扩展安全边界见
> [`extensions/SECURITY.md`](extensions/SECURITY.md)。

---

## English

### 1. Supported versions

We provide security fixes for the **latest build of each channel** only. Older builds are not patched.

| Channel | Security fixes | Notes |
| --- | --- | --- |
| Release | ✅ | Fixed first; shipped with the next stable release |
| Snapshot | ✅ | Shipped with the next snapshot; may land after Release |
| Beta | ⚠️ Best effort | For testing; updates frequently; no guaranteed timeline |
| Older builds | ❌ | Please upgrade to a current build before reporting |

**Platforms:** Windows (x64), Linux (x64 / arm64), macOS (arm64 / x64, macOS 11+),
Android (8.0+, arm64-v8a). If an issue affects more than one platform, please say which.

### 2. Reporting a vulnerability

**Preferred — GitHub private vulnerability reporting.**
Open [Security → Advisories → Report a vulnerability](https://github.com/TYWStudio/TML/security/advisories/new).
That channel is visible to maintainers only, and supports follow-up collaboration, CVE requests and
coordinated disclosure.

**Alternative — email.**
Write to **tyw@tywmc.cn** with the subject prefix `[SECURITY]`. If the details are sensitive, first send only
"there is an issue of type X, how can we exchange details safely" — we will reply with a channel before you
share anything further.

**Please do not:**

- disclose an unfixed vulnerability in Issues, Discussions, pull requests or anywhere public;
- publish a PoC, recording or write-up before a fix has shipped;
- test against **other users** by planting malicious extensions or forged update packages.

### 3. What to include

1. **Affected platform and version** — platform, channel (Release / Snapshot / Beta), version or commit hash.
2. **Issue class** — e.g. credential leak, privilege escalation, path traversal, extension sandbox escape,
   signature or integrity bypass, supply-chain problem.
3. **Reproduction steps** — as short as possible, as long as it is reliable.
4. **Impact assessment** — what an attacker needs first, what they gain, whether user interaction is required.
5. **Attachments** — PoC scripts, packet captures, logs, sample files. **Redact first**: never paste real
   account tokens, another user's device code, or a complete beta key.
6. **Credit preference** — how you would like to be credited (name / handle / link), or whether to stay anonymous.

### 4. Response and disclosure timeline

| Stage | Target |
| --- | --- |
| Acknowledgement | **within 72 hours** |
| Initial severity and scope feedback | **within 7 days** |
| Fix shipped | **within 90 days** (we will share progress; extensions are negotiable) |
| Public disclosure | after the fix ships, at a time agreed with the reporter |

We follow **coordinated disclosure**: no public details before a fix is released. After release, and with your
consent, we will credit you in the acknowledgements below. If you plan to publish independently, tell us in
advance so we can try to align.

### 5. Testing boundaries

**Welcome**

- Testing with **your own** Microsoft account, your own instances and extensions.
- Compiling, modifying and debugging any part of the code on your own machine.
- Testing our public endpoints: launcher updates, version manifests, download distribution, the extension store API.
- Analysing the integrity, signatures and update flow of published artifacts.

**Not allowed**

- Accessing, modifying or deleting **other users'** data, accounts or devices.
- Denial of service, stress testing, or scanning/crawling to the point of degrading availability.
- Social engineering, phishing, or impersonating maintainers.
- Physical access, stolen devices, or environments that are already rooted / jailbroken / compromised.
- Uploading extensions or update packages containing malicious code to public distribution channels.

Provided you stay within the boundaries above and act in good faith as a security researcher, we **will not**
pursue complaints or legal action over your testing. Behaviour outside these boundaries is not covered by that
commitment.

### 6. Scope

**In scope**

- **All four clients** — Windows / Linux / macOS / Android — including account sign-in, token storage, instance
  and file management, download and extraction, the update flow, and handling of external links and file
  associations.
- **Update and distribution pipeline** — version manifests, channel switching, artifact integrity and signature
  verification, update source resolution.
- **Extension store and download API** — the read endpoints, link resolution and file delivery on
  `downloads.tywmc.cn`, and their authentication / scope isolation (an app credential must only reach its own app).
- **Extension runtime** — default-deny, permission allow-list, subprocess isolation, fingerprint verification,
  manifest parsing, and any path that bypasses these constraints
  (see [`extensions/SECURITY.md`](extensions/SECURITY.md)).
- **Credentials and build pipeline** — whether secrets linger in the repository, in CI configuration or in release
  artifacts; whether build-time injection can be bypassed or written to disk / logs.

**Out of scope**

- **Mojang / Microsoft account systems**, ownership verification and Xbox Live itself — report those to the vendor.
- Content and behaviour of **third-party game servers, mods and modpacks**.
- **A malicious extension doing what extensions are allowed to do**: extensions are untrusted input by design; the
  host provides verification and isolation, not a secure execution environment. "Some extension can read my files"
  is not a vulnerability in this project — but "an extension can **escape the host's restrictions** and do something
  the host declares it will never allow" is. Report that under the first in-scope bullet above.
- Issues that require **physical access**, a rooted / jailbroken / already infected device, or the user disabling OS
  security features.
- Unsupported old builds; unofficial repacks or modified builds.
- Purely theoretical speculation without a real impact path; UI copy, translation or layout issues.
- Known vulnerabilities in third-party dependencies — tell us, but also report upstream. It only counts as our
  vulnerability if we use that dependency unsafely.
- Generic scanner output with no bearing on this project's actual architecture (e.g. a missing `HttpOnly` flag on a
  cookie we do not have) — please show a reproducible, real impact.

### 7. Our security design

So that researchers do not re-report known limitations:

- **No secrets in the repository.** The server address, OAuth client ID and extension-store app credential are empty
  strings in source and are injected by CI **at build time** from GitHub Secrets; the public repository and public CI
  artifacts are offline by default.
- **No credential shown at runtime.** The client keeps the extension-store credential in memory only and never
  displays it in the UI (not even masked); it is sent solely as an `X-Api-Key` request header.
- **Extensions are denied by default.** A discovered extension starts disabled and can only be enabled after the user
  reviews its name / author / version / declared permissions / SHA-256. If the package content changes, it is
  disabled again and must be re-confirmed. Extensions always run in a separate subprocess: no injection, no loading
  of assemblies / jars / dex, no reflection.
- **Two-stage download links.** Download URLs are short-lived signed tokens that are consumed on use; files are
  stored under randomised names and their real paths are never exposed.
- **Account tokens stay local.** Sign-in tokens are stored on your machine and used to obtain launch credentials from
  Microsoft / Xbox Live. We never ask for your password and never forward it to any self-hosted service.
- **Minimal reporting.** Communication with our server is limited to what is necessary (version manifests, notices,
  beta verification) and carries no account tokens or in-game data.

These are **defence measures**, not a claim of absolute security. Paths that bypass them are exactly the reports we
most want to receive.

### 8. Acknowledgements

With thanks to the researchers who reported issues responsibly (listed with their consent):

<!-- Format: - [@handle](https://github.com/handle) — short description (YYYY-MM) -->

*None yet.*

---

## 简体中文

TYW Launcher 是开源、免费、无商业化的 Minecraft: Java 版启动器。我们重视安全问题，
欢迎以**负责任的方式**报告漏洞。

### 1. 支持的版本

只对**当前各通道的最新构建**提供安全修复，旧构建不回溯修补。

| 通道 | 是否接受安全修复 | 说明 |
| --- | --- | --- |
| 正式版 Release | ✅ | 优先修复，随下一次正式发布推送 |
| 快照版 Snapshot | ✅ | 随下一次快照推送，可能晚于正式版 |
| 内测版 Beta | ⚠️ 尽力而为 | 供测试使用，更新频繁，不承诺修复时限 |
| 更早的历史构建 | ❌ | 请先升级到当前构建再复现 |

**平台**：Windows（x64）、Linux（x64 / arm64）、macOS（arm64 / x64，macOS 11+）、
Android（8.0+，arm64-v8a）。同一个问题若存在于多端，请在报告中注明受影响平台。

### 2. 如何报告

**首选：GitHub 私密漏洞报告**

打开 [Security → Advisories → Report a vulnerability](https://github.com/TYWStudio/TML/security/advisories/new)，
在那里提交。该通道只有维护者可见，支持后续协作、CVE 申请与协调披露。

**备选：邮件**

发送到 **tyw@tywmc.cn**，主题前缀写 `[SECURITY]`。
如果内容敏感，请只先发「存在某类问题 + 如何安全地交换细节」，我们回复后再给完整材料。

**请不要**

- 在 Issues、Discussions、Pull Request 或任何公开渠道披露未修复的漏洞细节；
- 在修复发布前发布 PoC、录屏或分析文章；
- 通过注入恶意扩展包、伪造更新包等方式测试**其他用户**。

### 3. 报告里请包含

1. **受影响平台与版本**：平台、通道（Release / Snapshot / Beta）、版本号或 commit hash。
2. **问题类型**：例如凭据泄露、越权访问、路径穿越、扩展沙箱逃逸、签名 / 校验绕过、供应链问题。
3. **复现步骤**：越短越好，能稳定复现即可。
4. **影响评估**：攻击者需要什么前提，能拿到什么，是否需要用户交互。
5. **附件**：PoC 脚本、抓包、日志、样本文件。请先脱敏——**不要在报告里贴真实的账号令牌、
   他人设备码或完整的内测码**。
6. **署名方式**：希望如何在致谢中署名（姓名 / ID / 链接），或选择匿名。

### 4. 响应与披露时间线

| 阶段 | 目标时间 |
| --- | --- |
| 确认收到 | **72 小时内** |
| 初步定级与影响范围反馈 | **7 天内** |
| 修复并发布 | **90 天内**（复杂问题会同步进展，可协商延期） |
| 公开披露 | 修复发布后，与报告者协商具体时间 |

我们遵循**协调披露**：修复发布前不公开细节；发布后经你同意，在本文的致谢区记录你的贡献。
若你已计划独立公开，请提前告知，我们尽量配合时间点。

### 5. 测试边界

**欢迎的测试**

- 用**你自己的** Microsoft 账号、你自己的实例和扩展做测试；
- 在你自己的机器上编译、改动、调试任意代码；
- 测试我们公开的接口：启动器更新、版本清单、下载分发、拓展商店 API；
- 对发布产物做完整性、签名与更新流程分析。

**明确禁止**

- 访问、修改、删除**其他用户**的数据、账号或设备；
- 拒绝服务、压力测试、扫描或爬取到影响服务可用性的程度；
- 社会工程、钓鱼、冒充维护者；
- 物理接触、偷窃设备，或利用已 root / 已越狱 / 已被入侵的环境；
- 上传含恶意代码的扩展或更新包到公开分发渠道。

在遵守上述边界、且以善意研究者身份行事的前提下，我们**不会**因你的测试行为发起投诉或
法律追究。超出边界的行为不在此承诺范围内。

### 6. 范围

**✅ 在范围内**

- **四端客户端本体**：Windows / Linux / macOS / Android，包括账户登录、令牌存储、
  实例与文件管理、下载与解压、更新流程、外部链接与文件关联处理。
- **更新与分发链路**：版本清单、通道切换、安装包完整性与签名校验、更新源地址解析。
- **拓展商店与下载 API**：`downloads.tywmc.cn` 的读接口、取链接口、文件下发、
  鉴权与作用域隔离（应用凭证只能访问所属应用）。
- **扩展运行时**：默认拒绝、权限白名单、子进程隔离、指纹校验、
  清单解析，以及任何能绕过这些约束的路径（详见 [`extensions/SECURITY.md`](extensions/SECURITY.md)）。
- **凭据与构建流程**：仓库源码、CI 配置、发布产物中是否残留密钥；
  编译期注入是否可能被绕过或读写到磁盘 / 日志。

**❌ 不在范围内**

- **Mojang / Microsoft 账号体系**、正版验证、Xbox Live 服务本身的问题——请报告给对应厂商。
- **第三方游戏服务器、模组、整合包**的内容与行为。
- **扩展本身的恶意行为**：扩展按设计就是不可信输入，宿主只提供校验与隔离，
  不构成安全执行环境；「某个扩展能读我的文件」不是本项目的漏洞，
  但「扩展**能绕过宿主限制**去做宿主声明永不开放的事」是——请按本节「在范围内」第一条报告。
- 需要**物理接触**、需要设备已被 root / 越狱 / 感染、或需要用户主动关闭系统安全机制才能利用的问题。
- 已停止支持的旧构建；官方构建之外的第三方重打包、魔改版。
- 纯理论、无实际影响路径的猜测；UI 文案 / 翻译 / 排版问题。
- 第三方依赖的已知漏洞：欢迎告知，但请同时向该依赖的上游报告；只有在本项目
  **以不安全的方式使用**该依赖时，才算本项目的漏洞。
- 缺少 `HttpOnly` / `Secure` 之类与本项目实际架构无关的通用扫描器输出——
  请给出可复现的实际影响。

### 7. 我们已有的安全设计

供研究者在测试前了解现状，避免重复报告已知限制：

- **密钥不入库**：服务端地址、OAuth Client ID、拓展商店应用凭证等敏感配置在源码中恒为空值，
  只由 CI 在**编译期**从 GitHub Secrets 注入；开源仓库与公开 CI 产物默认是离线的。
- **运行时密钥不外显**：客户端仅在内存中使用拓展商店凭证，界面不显示密钥（连掩码都不显示），
  只以 `X-Api-Key` 请求头发送。
- **扩展默认拒绝**：扫描到的扩展一律处于禁用态，需用户确认名称 / 作者 / 版本 / 权限 / SHA-256
  后才可启用；包内容变化会自动禁用并要求重新确认；扩展永远运行在独立子进程，
  不注入、不加载程序集 / jar / dex、不反射。
- **分发两段取链**：下载链接为短时效签名令牌，用完即失效，文件以随机名存储，
  不直接暴露真实路径。
- **账户令牌本地保存**：登录令牌保存在本机，用于向 Microsoft / Xbox Live 换取启动凭据；
  本项目不会要求你提供密码，也不会把密码转发给任何自建服务。
- **最小化上报**：与服务端的通信仅限版本清单、公告、内测校验等必要内容，
  不含账号令牌与游戏内数据。

这些设计是**防御措施**，不是对「绝对安全」的承诺。绕过它们的路径正是我们最希望收到的报告。

### 8. 致谢

感谢以下研究者以负责任的方式报告问题（经本人同意后列出）：

<!-- 格式：- [@handle](https://github.com/handle) — 问题简述（YYYY-MM） -->

*暂无。*

---

*This policy may change as the project evolves; the version in this repository is authoritative.
本策略可能随项目发展调整，以仓库中的最新版本为准。*
