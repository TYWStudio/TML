# AppImage runtime（内置）

打包 AppImage 需要一个 type2 runtime：一段静态 ELF，后面拼接 squashfs 就是可执行的 `.AppImage`。

这两个文件直接从上游拿，避免打包时联网（CI 上 GitHub 的 release CDN 偶发 504，国内网络也不一定能连上）：

- 来源：<https://github.com/AppImage/type2-runtime/releases/tag/continuous>
- 文件：`runtime-x86_64`、`runtime-aarch64`（约 900KB 一个）
- 许可：AppImage runtime 以 MIT 许可发布，允许随产物一起分发

打包脚本 `packaging/build.sh` 会优先用这里的文件；缺失时才回退到下载 `appimagetool`。
升级方式：用新版本的 `runtime-*` 覆盖同名文件即可，脚本按文件大小（>100KB）做基本校验。
