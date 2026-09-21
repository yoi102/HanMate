# 可下载AI中文语音包 · 2026-09-19

## 交付与来源

用户选择“内置可下载的AI中文声音”。设置页提供下载确认/进度/取消、174音色选择、自定义文字试听、启用/停用和确认删除。没有自动下载、自动启用或云端正文合成。

- 模型：`csukuangfj/icefall-tts-aishell3-vits-low-2024-04-06`，Hugging Face revision `57345c004e13ed640e408c4c29ab56187b18f065`。8文件合计32,740,562字节（31.2 MiB），见生产嵌入目录`HanMate.Infrastructure/Voices/catalog.json`。
- 模型卡：[README](https://huggingface.co/csukuangfj/icefall-tts-aishell3-vits-low-2024-04-06/blob/57345c004e13ed640e408c4c29ab56187b18f065/README.md)明示Apache-2.0；[AISHELL-3原数据](https://www.openslr.org/93/)明示Apache-2.0。App随附模型/数据/引擎NOTICE、Apache-2.0和ONNXRuntime MIT全文。
- NuGet `org.k2fsa.sherpa.onnx` 1.13.8，Android/Windows条件引用和依赖锁。CPU离线推理，8kHz单声道PCM16输出；音色编号001–174，默认067（模型ID66）。不是独立声音质量终审。
- 只在下载时访问固定HTTPS模型文件/CDN；逐文件限制长度、核验SHA256，私有暂存全部完成后才发布，失败清理并可重试。运行时不联网、不上传正文/音频、不动态加载模型脚本。
- 使用时重验模型文件，读取租约防并发卸载。选择独立保存在私有voices目录；停用不删除下载。生成声音只保留在内存，下载模型与音色设置不属于内容分享或完整备份。

## 阅读对接

明确选择AI音色后，已保存/导入个人正文和学习内容缺少匹配录音的片段用此音色朗读；既有录音优先，正文/读音hash每块重验。全文与点段沿用原有队列、突出显示和停止/导航取消。显式系统语音按钮仍单独检查系统语音策略/确认，不暗换为AI。

人工拼音不作为模型输入，模型自动读音可能与人工标注不一致；已在界面说明。拼音教学按钮继续使用独立录音，不让AI读拉丁字母代替bo/po/mo/fo。iOS合成能力暂未开放。

## 实际验证

| 检查 | 结果 | 证据及范围 |
|---|---|---|
| Infrastructure受影响回归 | PASS | VoicePackTests/ReadingAudioTests/Speech共21/21；新增6项覆盖下载、大小/hash损坏、取消重试、持久选择/卸载、读取租约和路径/HTTPS限制。未重跑上轮全量446项 |
| Windows生产推理 | PASS | `tools/HanMate.VoiceProbe`链接生产OfflineVoiceSynthesizer，实际下载及双音色生成、预取消；TEMP/HanMate-voice-probe/report.json |
| Windows最终Release | PASS | 0警告0错误；原生推理通过不代表Windows UI验收 |
| Android最终Release | PASS | 0警告0错误；`adb install -r`保留原有数据并成功安装；arm64-v8a/x86_64均含sherpa-onnx与onnxruntime原生库 |
| Android下载与试听 | PASS | API36模拟器实际31.2 MiB下载成功，默认067试听完成；NuPlayer属于应用进程、audio/raw、8kHz、state5 |
| Android个人正文离线朗读 | PASS | 正常TXT导入→注音→保存为个人条目AI-voice-smoke，无录音；飞行模式enabled、wifi_on=0时全文两段完成，点段自动播放及停止通过。未知注音仍needsReview，没有伪造确认 |
| 最终安装复验 | PASS | 更新后下载模型与067音色保持；实际停用后提示保持模型，再启用与试听完成 |
| 本地发布预检 | PASS/BLOCKED | 514键三语言一致；INTERNET/RECORD_AUDIO允许范围、关闭自动备份与cache/sharing范围PASS；发行身份/签名/工作包/正式原生验收仍BLOCKED |
| 未执行范围 | NOT RUN | Windows实际UI、174音色逐一听审、ARM真机/长时性能；iOS按要求暂缓 |

Windows固定示例：`这是我自己输入的文字。今天是2026年9月19日，我们一起学习汉语。`

- ID66：97,634字节WAV，SHA256 `2cd27243e69461b9ae19e0e6642027671a874a5b2b0dd39a864cd761a3a4d20f`，约1.11秒生成。
- ID10：99,854字节WAV，SHA256 `2f25ef6792303d1198e36c498e5c62cbf5575b42f9c4864f5a7668c5e89d8fa1`，约0.68秒生成。

Android个人工程正文：`这是我自己录入的文字，没有附带任何录音。今天我们一起学习汉语，明天去图书馆读书。` 截图及发布预检JSON在开发机`%TEMP%/HanMate-ai-voice-20260919`。网络恢复airplane disabled、wifi_on=1、mobile_data=1；原日语UI、150%字号与数据库保留，增加一个个人工程条目。未开麦克风、未清库/替换真实数据、未外发正文。

最终APK：`HanMate.App/bin/Release/net10.0-android/com.companyname.hanmate.app-Signed.apk`，66,564,593字节，SHA256 `216b1b52b749dd85afdbdeaffaa08983c36e740437e195005e2cf8f95a89d5e5`。

DONE仍35/48。AI模型接入不关闭正式教学音频审核、拼音ong缺音、真人录音、全平台和发行门禁。
