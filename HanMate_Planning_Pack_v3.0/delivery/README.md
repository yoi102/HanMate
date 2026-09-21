# HanMate 内部开发交付

范围：Windows x64、Android。按用户2026-09-20确认，正式审校、发行签名、商店发布后置；应用测试本轮暂缓。这里的可运行开发包与正式发行分别记录。

交付目录包含：

- `HanMate-Windows-x64-internal.zip`：解压到新的目录，运行 `HanMate.App.exe`。保留同目录的 `AppxManifest.xml`、DLL、资源和语言文件。首次使用需要机器已具备对应 .NET/Windows App SDK 运行条件；本轮使用已有开发机工具链，不宣称离线净机自包含安装。
- `HanMate-Android-development-signed.apk`：开发签名版本，Application ID仍为 `com.companyname.hanmate.app`。没有自动安装到手机；签名不兼容时停止，不能通过卸载/清数据强行覆盖。
- `HanMate-source-internal.zip`：应用和测试源代码、实际嵌入资源、构建配置/锁文件、协议与历史兼容夹具、交付工具。没有Git提交标识，源码快照及逐文件SHA256是本次可追溯标识。
- `content-review/content-review.html`：离线内容/释义/译文和录音审阅入口；只在点击时播放随附音频。人工结论仍未批准。默认词库293批清单另见同目录CSV。
- `delivery-manifest.json`、`source-files.json`、`windows-files.json`：交付文件和两个ZIP内部文件的长度/哈希。哈希只能检查一致性，不能代替正式签名。
- `release-preflight.json` 与 `deferred-release-gates.json`：正式发行仍阻塞的理由；内部交付不改写这些结果。

先阅读 [安装与复现](BUILD_AND_INSTALL.md)、[数据与恢复](RECOVERY.md)、[数据流与隐私](PRIVACY.md)、[内容交接](CONTENT_HANDOFF.md)和[技术决策](TECHNICAL_BASELINE.md)。

安装/运行前通过应用自己的“备份与合并恢复”生成并另存需要保留的资料。开发包不附带任何人的数据库、录音、备份、下载模型、声音偏好或签名私钥。

已知问题：HM-D019长释义Mono原生崩溃根因未关闭；自动拼音/释义/译文待审，音源许可版本及默认词库权利未核清；iOS不在本轮；设备访问性、音频路由与压力、全量应用回归未完成。
