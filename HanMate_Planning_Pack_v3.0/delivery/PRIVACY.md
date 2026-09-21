# 内部版本数据流与隐私说明

本页对照当前源码与清单，供内部交付和后续正式声明准备。没有代填商店提交表，也没有虚构发布者、联系方式或隐私政策托管地址。

| 操作 | 数据与去向 | 控制与保留 |
|---|---|---|
| 编辑、查词、收藏 | 正文、拼音、关系和索引在本机；默认词库独立只读 | 不需要账号，不默认上传查询或正文 |
| 录音、音频导入 | 用户显式操作后采集麦克风或读取所选文件；内部规范化PCM16 WAV | Android RECORD_AUDIO、Windows microphone声明；拒绝有提示。原文件与应用内记录分开保留 |
| 本地/AI播放 | 已下载模型在本机合成；普通正文按文字读，人工拼音不普遍控制读音 | 模型与音色仅在本机；拼音课程有明确注音合成分支；没有录音则不会用某个单字录音冒充整词 |
| AI模型下载 | 用户主动点击后访问固定Hugging Face源与其HTTPS CDN；服务可见IP和下载请求 | 固定revision/文件大小/SHA256检查；不上传正文或录音，无账号；Android INTERNET，Windows internetClient |
| 系统语音 | 仅显式检测/试听或允许阅读回退并确认后交给系统引擎 | Android排除声明需联网/未安装候选；第三方系统引擎行为由系统及提供者决定，不能承诺任意设备绝对离线 |
| 分享、另存与备份 | 用户选择的内容/音轨或备份交给系统提供方 | 云盘/接收应用可能联网；应用不保证送达。Android FileProvider只暴露cache/sharing |
| 自动系统备份 | Android清单禁用allowBackup并排除云/设备迁移 | Windows及其他平台由系统策略决定；逻辑备份需用户主动保管 |
| 删除、清理、诊断 | 回收站、旧媒体、草稿和恢复安全副本可能保留；外部副本不受应用控制 | 不宣称安全擦除；生成分享副本时处理超过7天的已识别副本，并非定时后台清理。异常诊断避免正文/异常消息，部分路径保留类型/HResult/代码堆栈 |

实现依据：`VoicePackStore`、`NativeSpeech`、`NativeWaveRecorder`、`AudioFileImport`、`BackupExportStore`、`SafetyRecoveryStore`、AndroidManifest及FileProvider配置、Windows Package.appxmanifest、三语 `Privacy.*` / `Voice.Privacy` 文案。

正式发行前另补：发布者/联系信息、分发地区及年龄分级、真实最终签名产物、当时商店隐私分类、系统TTS/第三方依赖披露、支持与删除请求方式。当前不能仅凭“应用不上传正文”在商店一概填写不发生任何网络数据处理。
