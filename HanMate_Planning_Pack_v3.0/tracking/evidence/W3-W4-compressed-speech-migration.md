# 压缩音频、显式阅读语音与Windows/Android往返

日期：2026-09-18。用户要求推进48/48，并已确认暂无Mac/iOS环境，先完成本机工作。工作包验收口径保持，DONE仍35/48；W4-06由TODO进入IN_PROGRESS，W5-02素材生产进入IN_PROGRESS。本记录是局部实现/验收，不能将缺设备、终审与发行工作改写为通过。

## 实现

- `HanMate.Core/Audio/CompressedAudio.cs`：有限MP3帧与ISO M4A容器解析。输入及预计PCM各100MiB；单轨、AAC-LC、mono/stereo、连续样本闭包、self-contained data reference。拒绝截断、错误profile、外部文件/URL、加密/分片及未知变体；成功失败都还原流位置。此解析器不是解码器。
- `HanMate.App/Audio/NativeAudioDecoder.cs`：Windows MediaTranscoder、Android MediaCodec和Apple AVAudioFile输出有界PCM16 WAV，真实探测输出参数。Windows通过显式持有/释放随机访问流修复原生转换后源文件句柄未及时关闭；取消等待原生停止后清理本次随机缓存。
- `AudioFileImport.cs`和`AudioTracksPage.cs`：选择WAV/MP3/M4A，按实际字节判断，原件不变。解码完成才进入既有草稿协议；导入操作经协调器取消、后台和离页停止。备份/内容包/资源包仍用严格PCM16 WAV，不增加未经验证的压缩媒体协议。
- `ReadingAudioStore`、`SpeechText`、`LocalAudioBackend`和阅读页：显式缺失目标语音回退，本地已适用音轨优先；hash/当前正文/策略每步复核。按Unicode文本元素分块，不念语法slot，不向引擎提交拼音。设置默认关闭，开启后每次仍需确认与选择音色，声音ID不入库。
- `SpeechPolicyStore`复用audio.policy并纳入既有备份“保留本机/显式应用设置”规则。设置页返回时重建已取消的页面令牌，修复标签切换后无法再检测的问题。
- Android实际检测发现超时后的迟到OnInit调用已释放Java监听器，导致`Unable to activate instance ... SpeechInit`。给初始化和进度监听器补充JNI重建构造器，重建对象不持有任何播放完成源、忽略迟到回调；DynamicDependency保留原生入口。原始日志仅保留私有TEMP。不能把该崩溃解释为单纯环境缓慢。

## 自动及Windows原生证据

| 检查 | 结果 |
|---|---|
| Core Release | PASS，207/207 |
| Infrastructure Release | PASS，238/238 |
| 新增测试 | 容器/截断/外部引用/AAC profile、Unicode分块、混合队列、过期校正、语法槽位与回收站、语音策略备份恢复 |
| Windows生产解码探针 | PASS，MP3、M4A、截断拒绝、预取消、WAV备份恢复5场景 |
| Windows Release | PASS，0警告/错误；包括页面令牌修复 |
| Windows宿主iOS managed | PASS，0警告/错误；不是iOS原生证据 |
| 最终Android Release完整Rebuild | PASS，0警告/错误，包含JNI迟到回调修复 |

新增ma2后旧测试把m当作缺声音例字，实际失败；已改为断言m选择ma2，并保留f缺声音负例。最终全部207项通过，没有删除负例以绕过失败。

Windows探针输出`%TEMP%/HanMate-media-probe-3029d97fd45b425a85f2edf54d27224d/report.json`：MP3 1059ms，M4A1044ms，44100Hz/mono，实际PCM非静音。无失败草稿或decode缓存残留。工程合成音1秒440Hz，生成命令在Core.Tests/Fixtures/Audio/README.md，构建期FFmpeg未随App分发。

## Android实际运行与跨平台往返

Android16/API36 x86_64模拟器覆盖安装保留数据库和日语/150%。以下音频/迁移检查完成于JNI迟到回调修复之前的本轮包，修复仅涉及语音监听器/页面生命周期，未改变解码或迁移实现。

- 拼音m选择“麻 má”，例字页面显示Yue Tan/CC BY-SA署名，点击后显示再生終了。ma3已显示马mǎ及Wei Gao/Vion Nicolas/CC BY署名并触发播放；因页面状态在滚动区之外，未单独把其完成状态记PASS。
- 将Windows生产探针生成的`migration.hanbackup`通过Android应用“合并恢复”导入：新增1内容、2音轨、1夹、1收藏，保持本机设置。没有执行替换。
- Android实际预览116正文、3夹、8收藏、28音轨（1809.5KiB），8完整资源、1草稿排除；生成并经系统CREATE_DOCUMENT另存成功。返回文件1,334,958字节，只在Download/HanMate-media-smoke及私有TEMP保留，未入仓库。
- Windows生产BackupImportStore在新TEMP库导入该Android文件，逐项比较原工程ContentDocument（含锁定ni3）、Media-migration收藏引用、两条音频SHA256和唯一默认：**PASS**。这是1条工程内容的一组Windows→Android→Windows往返，不是四类全量/六方向迁移。
- 在另一工程内容Backup-word通过实际音频页面分别选择tone.mp3、tone.m4a：均成为1.0秒ready草稿，点击试听，再保存为独立imported音轨；两次均取消勾选设为默认，原5秒默认音轨保留。最终UI显示两条新音轨、无该目标遗留草稿。MP3试听完成状态已观察，M4A已执行试听但未单独回顶部读取完成状态。
- 已查看`%TEMP%/HanMate-media-smoke/imported-tracks.png`，日语页面按钮/两条音轨可读。语音设置初始switch=false，显式开启后显示已保存；后续检测出现上述迟到回调崩溃并修复。

往返复核命令：

```powershell
dotnet run --project tools/HanMate.MediaProbe/HanMate.MediaProbe.csproj -c Release -- verify-return "$env:TEMP/HanMate-media-smoke/android-return.hanbackup.zip" "$env:TEMP/HanMate-media-probe-3029d97fd45b425a85f2edf54d27224d/expected.json"
```

复核输出PASS，恢复库`%TEMP%/HanMate-media-probe-6591bc94de204ec0af4519bffa8f9b53`。原始工程与返回包均使用生产严格协议；返回包可能包含个人资料，不用于公开工件。

## 资源与未完成边界

`build_pinyin_course.py --download --download-limit 12`新增取得ma2、ma3后遇429，遵守Retry-After全局冷却停止。原始OGG核对Commons SHA1，WAV/哈希/许可由生成器更新。已取得8/161段，缺153段；教学音文匹配、翻译和终审仍未完成，不等于8段已审核教材。

NOT RUN/BLOCKED：真人麦克风录制/重录、真机耳机来电与长时/满盘，iOS原生三类解码/读屏/文件/音频及其四个迁移方向，Windows实际阅读正文TTS完成/停止，飞行模式TTS与教学质量，176完整用例、正式内容终审、发行身份/签名/商店资料与提交。GC仍未实现。无清库、无真实替换、无麦克风采集、无对外分享或发布。

## 最终语音修复复验与发行预检

最终APK覆盖安装成功，SHA256 `8a7bf681059b2670b6bcb19c6932e748f8b8d5036bc2b8fde95e97cc999eaad3`。语音策略跨进程保留已开启状态；检测正确显示无可用普通话/服务未准备，试听保持禁用。已将临时开启的策略恢复关闭，切换到拼音标签再返回设置后再次检测正常，应用保持运行。此次复验未强制制造Binder迟到回调；已修复原生重建入口，但不声称完整压力时序覆盖。

最终发布预检：源码/最终APK权限、auto-backup关闭、FileProvider仅sharing及依赖锁/489键三语言校验PASS；身份、Windows主体、未完成工作包和原生/商店证据4组继续BLOCKED。原临时测试设置恢复，原资料及新增工程内容保留。返回备份SHA256 `301fc1858d478b245c0a2e2ab305133b05209f00c64d9cd1be22fcbaf521944e`。

规划包verify_bundle的16组检查PASS，176个完整用例仍全部NOT RUN（不以445项代码测试覆盖冒充完整用例的平台执行）。实际平台聚焦证据单独记录。收尾执行生成、验证报告、再生成与check。
