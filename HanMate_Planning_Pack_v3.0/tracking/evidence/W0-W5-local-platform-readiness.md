# 本机平台补齐与发布准备 · 2026-09-18

## 范围和真实进度

用户原请求推进至40/48，随后明确答复“目前没有，先完成本机可做的部分”（暂无Mac/iOS环境）。本轮完成五块本机实现/准备工作，**工作包DONE仍为35/48**。没有删除原验收、改变48项分母或以编译代替iOS原生运行。

W0-04/05继续IN_PROGRESS；W0-06、W5-03/04推进到IN_PROGRESS。W3-05的MP3/M4A、W4-06六方向迁移及M5正式素材/全量验收仍未完成。

## 实现

1. `HanMate.App/Files/NativeFileSaver.cs`：备份/四类内容包/本人录音通过系统另存。Windows FileSavePicker+StorageStreamTransaction；Android CREATE_DOCUMENT+ContentResolver流；Apple UIDocumentPicker export-as-copy。包提供原后缀和附加`.zip`两入口；同一私有验证副本，不重新生成不同数据。取消不显示成功，失败提示目标可能残留不完整文件。文件提供方负责云端同步。
2. `Audio/NativeSpeech.cs`、`Pages/SpeechSettingsPage.cs`：显式检测普通话语音、固定中文句试听；Android排除需网络/未安装的音色。共用PlaybackCoordinator会话，支持停止/离页/后台取消、超时、录音忙及清理失败封锁。没有自动下载、没有提交用户正文、没有阅读TTS回退，人工拼音不控制系统语音。
3. Android移除INTERNET和ACCESS_NETWORK_STATE，关闭自动备份；旧/新备份配置排除云端和设备迁移。FileProvider从缓存/外部根收紧到专用`cache/sharing/`。设置中增加三语言隐私页，解释系统语音、外部保存/分享、未加密备份、其他平台系统备份、回收站和安全副本/媒体仍保留。UI诊断仅输出异常类型，不输出异常路径/正文。七天分享清理只在下一次生成副本时执行。
4. MAUI Controls固定10.0.20，各工程NuGet锁文件记录传递版本和contentHash；增加`tools/release_preflight.py`审计源配置、最终APK权限/分享目录、484键三语言和剩余发行门禁；`tools/RELEASE_READINESS.md`记录可复现命令和恢复边界。当前预检按设计返回exit2/BLOCKED：App ID、Windows publisher仍占位，签名/原生/终审证据未齐。
5. TeachingCatalogStore按数据库已有ID与输入ID并集累计最多200项，规划和提交均校验；精确重复不占新名额，并发计划拒绝。避免单次导入都合法但累计超过完整备份协议上限。没有清理/重写现有教学项。

## 自动检查

| 检查 | 结果 |
|---|---|
| Core Release tests | PASS，200/200 |
| Infrastructure Release tests | PASS，234/234 |
| 新增针对性回归 | PASS，4项：语音操作与录音/清理隔离，清理失败封锁，教学累计200/重复/备份恢复，并发教学计划拒绝 |
| NuGet restore --locked-mode | PASS，解决方案五工程 |
| Windows Release build | PASS，0警告/错误 |
| Android Release完整Rebuild | PASS，0警告/错误 |
| Windows宿主iOS managed build | PASS，0警告/错误；不是iOS原生证据 |
| 三语言资源 | PASS，484键键集/非空/占位符一致 |
| 规划包校验 | PASS，16组；176项完整用例仍NOT RUN |
| 最终APK权限 | PASS，仅RECORD_AUDIO及组件私有DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION，无INTERNET/全盘权限 |
| 最终APK备份/共享配置 | PASS，allowBackup=false、单一cache/sharing provider路径 |
| 发行资格 | BLOCKED，4组：Android身份、Windows发行主体、未完成工作包、原生与商店证据 |

一次Android编译因新增错误回调类型名不匹配失败，查阅安装的Mono.Android API后改为TextToSpeechError；最终完整Rebuild通过。没有删断言或隐藏构建错误。

最终APK：`HanMate.App/bin/Release/net10.0-android/com.companyname.hanmate.app-Signed.apk`。
SHA256：`caa78ad9aa972656df506631d3488edf8138d971ca717c60298f05db2370fac6`。
此为开发签名包，不是已批准的正式签名/商店提交。

## 实际运行

### Windows

- Release应用实际检测到已安装普通话候选；固定句试听到`Playback finished`，再次试听后Stop显示`Stopped`。未测试断网和人工发音质量，未调用麦克风。
- 备份实际预览/生成，系统FileSavePicker写入TEMP中的`windows.hanbackup`，UI显示保存成功；第二次对话框取消显示Cancelled。
- 保存文件16,723字节，通过生产BackupPackageCodec严格回读：23正文、1夹、0收藏/音轨、2资源。SHA256 `2a9a679ef69776f1504d3cd0cf8ebf2ef7409fac3fc800da22947851013136cb`。
- UIAutomation同步Invoke原生Save曾触发Windows COM输入同步限制；改用该应用拥有的原生按钮异步PostMessage后实际保存成功。这是测试驱动限制，不冒充应用功能修复。测试启动的进程46644已关闭。

### Android16/API36模拟器

- 最终APK覆盖安装，不清数据。原日语/150%保留，64教学项。备份预览115正文、2夹、7收藏、26音轨/1,628.1KiB、8完整资源、1草稿排除，与安装前一致。
- 系统语音检查无可用普通话候选，明确提示并禁用试听；没有自动切英文或下载。
- 备份另存打开系统CREATE_DOCUMENT，退出目录再取消，返回明确Cancelled；再次保存成功。该文件提供方按ZIP MIME给自定义后缀自动补`.zip`；显式ZIP入口也保存成功，没有依赖后缀来放宽校验。
- 真实保存文件拉回私有TEMP，通过生产校验器：1,214,018字节、115正文、7媒体资产/26绑定、2夹/7收藏、8资源、1外部教学项。SHA256 `563c6ce6eece8abafac94f13532af99ebb0d1f819d8addbaea0d35479202aa83`。
- FileProvider收紧后备份系统分享面板仍正常打开，立即取消，没有向任何接收者发送。
- 已查看`%TEMP%/HanMate-platform-smoke/speech-unavailable.png`、`backup-saved.png`，文本/按钮可读、日语布局正常。最终停在备份页。

实际备份含用户资料，保存文件仅在系统选择的本地位置和私有TEMP，未加入仓库。Android保存测试文件在Download/HanMate-recovery-smoke中，没有覆盖原文件；系统冲突自动另命名。

回读命令：

```powershell
dotnet run --project tools/HanMate.RecoveryProbe/HanMate.RecoveryProbe.csproj -c Release -- inspect-backup <saved-file>
```

该分支只读文件并输出数量/哈希，不创建数据库、不运行破坏性恢复场景。普通无参数的恢复探针保持原行为；本轮未重复执行前轮5项强杀/FULL探针。

## 未执行/未完成

iOS原生文件保存/试听、Android真机及真人录音、飞行模式TTS、耳机/来电路由、MP3/M4A、阅读TTS回退、内容包和独立WAV新增保存按钮的逐平台UI运行、系统提供方写失败/满盘、六方向迁移、正式音频仍6/161、GC、完整176用例、发布身份/签名/商店审核。以上不记PASS。
