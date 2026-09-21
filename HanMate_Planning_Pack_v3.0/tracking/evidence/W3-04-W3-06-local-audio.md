# W3-04 / W3-05 / W3-06 · 本地音频、录音草稿与复核

日期：2026-09-17。工作目录C:\Users\yoiri\source\repos\HanMate，无Git元数据。用户授权连续推进多阶段后集中测试。W3-04、W3-06 DONE；W3-05 IN_PROGRESS，压缩格式和真机声音/完整平台中断仍待验收。

## 实现

- Core/Audio/PlaybackCoordinator：新增RecordAsync独占会话，包含权限与Finalizing。期间Play/Record返回Busy，Stop/替换等待旧会话原生清理；owner隔离、迟到完成保护、清理错误封锁保持。Shell导航deferral等待停止；窗口Stopped/Destroying继续停止；页面回调以owner/UI版本保护。
- Core/Audio/PcmWave：有限读取RIFF/chunk，严格核对容器长度、重复format/data、PCM16、8–192kHz、1–2声道、byteRate/blockAlign、完整帧、非零时长及100MiB限额。PCM16帧不存在压缩解码状态；不能由此声称MP3/M4A已验证。
- Infrastructure/Database/LocalAudioStore：localAudio.v1草稿行先登记pending，随后写文件/严格校验/哈希/重命名，再ready；最终asset/binding/preference/epoch/草稿完成同SQLite事务。文件不与数据库伪装为单一事务。重启后可显式恢复完整遗留文件；部分文件/空录音保留错误草稿供丢弃。播放建立file_lease、验证路径/实际容器/哈希，播放结束释放租约。移除绑定暂不删除物理文件。
- 保存、确认草稿、确认绑定均检查当前target快照。默认选择与自动播放同时要求confirmed、文本哈希、读音哈希一致；needsReview只能主动试听。资源跟读需显式勾选才替代标准；完整unit和segment目标独立，语法slot无target。
- App/Audio/NativeWaveRecorder：Android AudioRecord写入任务由应用等待、修正WAV长度、停止/Release；Windows MediaCapture；Apple AVAudioRecorder/会话释放及中断观察。记录44100Hz mono PCM16请求，持久化前探测实际输出。Android容量按/data实际路径StatFs检查，不能用只读根分区。录音20分钟与100MiB先达者停止；Windows/Apple按实际文件大小监测并保留停止余量。
- App/Pages/AudioTracksPage：从阅读页进入全文unit/片段管理，重新获取当前正文和拼音上下文，以96原子分页；录音、WAV导入、草稿试听/保存/另录/丢弃/恢复、默认选择、绑定复核/移除。新录音不会直接覆盖旧草稿。显式Record才申请麦克风，拒绝后同页不重复弹窗，提供系统设置。
- 三平台权限声明已补；没有新增包、外部协议或数据库版本。新增41个三语言资源键，共312键。

## 依赖核对

Plugin.Maui.Audio 4.0.0，NuGet锁定版本。核对NuSpec所指源码commit `931337eecb5bc3895262b035b69e82a56c5ea8ce` 的Android/Windows/Apple recorder、IAudioRecorder、IAudioSource、FileAudioSource。Android库实现未等待WriteAudioDataToFile任务就改头，使用包含44字节头的长度作为音频长度，并未明确Release/Dispose AudioRecord，因此本轮自行持有平台录制生命周期；播放仍使用已有CreateAsyncPlayer/PlayAsync并明确Stop/Dispose。代码来源核对不代替平台录音实测。

## 自动测试与构建

| 项目 | 结果 | 覆盖/边界 |
|---|---|---|
| Core Release | PASS 178/178 | 新增16项：PCM16参数/损坏头/空和截断/附加chunk、录音预约覆盖等待播放清理/权限/落盘、Busy、owner、清理失败封锁 |
| Infrastructure Release | PASS 102/102 | 新增10项：导入保存重启、真实文件租约、改音失效、显式试听复核、过期草稿拒绝及确认、rename前后恢复、坏文件不能晋升、资源默认规则、全文不替代片段、移除保留资产/租约、篡改拒绝、无可信Length流100MiB边界 |
| Windows Release | PASS | 0警告/0错误；本轮新音频UI/真人录音未运行 |
| iOS Windows-host managed | PASS | net10.0-ios / iossimulator-x64，0警告/0错误；不是Mac/Xcode/原生运行 |
| Android Release完整重建 | PASS | net10.0-android -t:Rebuild，0警告/0错误；最终覆盖安装保留旧数据 |
| 三语言资源 | PASS 312键 | 英/简中/日键集、非空、数字占位符一致 |

命令为两个测试项目各自 `dotnet test -c Release --no-restore --logger trx`，App逐个目标 `dotnet build -c Release -f <TFM> --no-restore`，Android最终加 `-t:Rebuild`；所有.NET进程串行完成。测试结果为Core.Tests/TestResults/audio-final.trx与Infrastructure.Tests/TestResults/audio-final.trx。

初轮SQLite测试发现SQL原始字符串拼接缺空格形成WHEREt/WHEREb，4项失败；修复后102/102通过。Android增量包曾在业务启动前出现 `Compressed assembly ... expected at most 22368, got 171008`；完整重建后恢复，不清应用数据、不以禁用测试绕过。

最终APK SHA256：`9af348fb617eb8630f835e96ac127eee413e3c932a345894dad0d0f1b0116468`。

## Android聚焦操作

设备emulator-5554，Android16/API36，1080×2400，Release包com.companyname.hanmate.app。保留原有内容、收藏、资源及编辑夹具，未清库。测试目标为前轮工程个人课文Draft-test；不会把这些操作计为正式教材审校。

1. 生成16044字节的1秒440Hz、8000Hz mono PCM16工程合成WAV，推入Download。阅读正文unit→音频与录音→系统文件选择器导入→出现1.0秒可试听草稿→试听→命名Audio-smoke-tone→保存为默认。草稿完成，保存列表有默认且适用音轨，播放适用音轨显示“播放完成”。PASS。截图W3-audio-import-playback.png。
2. 显式录音出现系统麦克风授权；点Don't allow，页面显示权限说明并保留数据；同页再次Record不重复弹窗。重新进入后再次申请并允许。PASS。
3. 在录音前已执行 `adb emu avd hostmicoff` 并收到OK，避免采集宿主环境声音。录制17.4秒模拟器输入→停止→生成可试听草稿→命名Emulator-silence并取消设默认→保存。PASS仅指原生采集/完整容器/草稿持久化，不能称为真人发音或声音质量验收。
4. 再次录制，计时出现后按Home；回前台无自动续录，存在5.4秒可试听草稿，旧默认及保存音轨保留。PASS。截图W3-audio-background-draft.png。
5. 通过已有编辑UI将测试课文“你”从ni3改为ni4并正式保存。默认自动播放拒绝；两条已保存音轨及旧草稿出现需复核，文件保留。显式试听Audio-smoke-tone→确认仍适用→再次自动播放完成。PASS仅指复核流程，不代表合成音匹配正文。截图W3-audio-needs-review.png。
6. 测试结束恢复“你”ni3并锁定；两个已保存工程音轨最终保留needsReview，5.4秒草稿保留。不把工程合成音/静音当成可供学习的正式发音。
7. 最终源码完整重建APK后再次覆盖安装并重新启动，原Draft-test/收藏/工程资源仍在；两个已保存音轨及5.4秒草稿保留，ni3已恢复，待复核状态不丢失。新包手动试听Audio-smoke-tone后显示“播放完成”。PASS。截图W3-audio-final-restart.png。

## 尚未完成

MP3/AAC-LC M4A真实探测/解码与跨平台播放；真人录音、来电/耳机路由/锁屏/焦点抢占、真机20分钟及满磁盘/强杀窗口；Windows新音频交互和iOS原生。TTS、全文片段队列、分享/备份与音频资源安装、物理GC及拆并片段绑定迁移仍待后续。完整176项未运行。

W3-04/W3-06作为会话/绑定契约工作包完成不解除上述门禁；W3-05仍IN_PROGRESS。正式拼音教学录音仍6/161、缺155段，本轮没有增加正式素材。
