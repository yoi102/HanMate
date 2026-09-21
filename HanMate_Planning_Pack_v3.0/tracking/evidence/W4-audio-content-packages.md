# WAV内容包、音轨映射与文件安装恢复

日期：2026-09-18。连续完成含录音包严格读写、资产/绑定合并及失败重试、录音选择与接收预览三个相连子阶段，再集中验证。当前22/48 DONE保持不变；W4-01/02/03/08包含备份、更多音频来源/格式及跨平台交付，仍IN_PROGRESS。

## 实现

- StrictTextArchive仅在content路径开放精确SHA256.wav白名单，纯文字资源限制保持；TextContentPackageCodec扩展已有v2/audio-index v1。全部媒体真实PCM16 WAV、大小/时长/采样/声道/哈希、资产和目标闭包、默认绑定、全局UUID、confirmed文本/读音哈希都验证。拒绝外来路径、孤立媒体、无绑定资产、catalogRef及未支持压缩格式。
- ContentShareStore固定正文/音轨元数据快照，只接收明确选中的本人user录音和单独权利确认。export租约读取真实字节并核对快照哈希；未选音轨/preference不携带，相同SHA字节只存一份，逻辑资产保留。正文/译文/手工拼音仍完整，不带草稿/收藏/设置。
- ContentAudioImport在正文全图映射后规划asset/binding；content.audio.v1映射复用重核现存语义。正文冲突、嵌套冲突同步重映射音轨target；绑定删除/修改后新增而不覆盖。接收方统一source_role=imported，本机已有默认优先，needsReview不建新默认。
- 文件安装使用私有audio/packages/SHA256.wav和随机.part；关闭前Flush(true)，同目录无覆盖改名，现存文件实际验证。正文/索引/目标/资产/绑定/默认/映射/收据同SQLite事务可见。失败可能留下未引用的完整文件，重试重新验证复用；不覆盖损坏文件，不清旧音频，不声称文件和DB单一事务。强杀残留.part/孤立文件无自动GC。
- ContentAudioSelectionPage每页10条、默认不选、来源/正文片段/时长/字节/复核/默认状态、清空/应用选择；UI说明32MiB包及16MiB单文件。导入预览明确正文与音轨各自新增/复用和待复核数量。13个新增三语言键。

## 集中验证：PASS

所有dotnet测试和构建串行，无新依赖、无协议或DDL版本变化。

- Core Release：188/188。
- Infrastructure Release：150/150，新增16项。包含真实WAV往返和SHA去重、正文/语法冲突目标映射、重复包/删除绑定再导入、本机默认保留、待复核不自动播放、本人音频选择与独立权利、提交触发器失败/文件保留/重试、现存坏文件不覆盖、取消/过期计划，以及10类音频坏包在创建目标库前拒绝。最终无警告。
- Windows Release、Android Release完整`-t:Rebuild`、Windows宿主iOS simulator managed编译：0警告/0错误。iOS不等于Mac/Xcode/原生运行。
- 404个简中/日/英resx键集一致、非空、数字占位符集合一致。

命令沿用`dotnet test <Core/Infrastructure.Tests项目> -c Release --no-restore`，以及`dotnet build HanMate.App/HanMate.App.csproj -c Release -f <windows/android/ios目标> --no-restore`（Android加`-t:Rebuild`）。日志：开发机临时目录hanmate-audio-package-{windows,android,ios}.log。

最终APK SHA256：`ffc017d347aa1d17d4248594d43c43bcf5e86276ee1b010d4fb4fe68b8136044`。

## Android16/API36模拟器：PASS

emulator-5554，1080×2400。Release覆盖安装保留旧库，沿用当前日语界面和150%阅读偏好，没有清数据或采集新声音，没有向任何接收者发送文件。

1. 草稿→内容分享，选择原Draft-test并预览：正文1，原音轨2、草稿1默认排除。
2. 录音选择页显示Emulator-silence 17.4秒/1502.9KiB/要确认，可勾选；Audio-smoke-tone 1秒/15.7KiB/导入音频不可选。选择一条后返回：包含1、排除1。确认文字权利及录音隐私后生成并校验成功，打开系统分享显示hanpack，取消返回。
3. 工程audio-transfer.zip从系统选择器导入：新增正文1/冲突另存1，新增音轨1，待复核0，15.7KiB。再次导入：正文新增0/复用1，音轨新增0/复用1；提交成功，不重复增加。
4. 学习文章列表显示新增Audio-transfer-smoke和原有Draft-test/Transfer-text。进入新内容→音频管理，Synthetic-440Hz-test 1秒标为导入音频/默认可用，无本人录音分享入口。实际点击默认播放，原生播放器进入播放中并返回播放完成。

工程夹具为规划样例的一条课文改标题，加1秒8000Hz单声道PCM16合成440Hz信号，SHA256=`f9efab5003000788f6851091398494c71506d3319e03f2abea702aa8214bc8f0`；其confirmed仅验证哈希关联机制，不代表信号是正确汉语发音。来源保持testFixture/draft。原录音、草稿和其他内容未删除。夹具及export.png/repeat.png/playback.png位于开发机临时目录HanMate-audio-package-smoke；后两张已人工查看。最终停留导入内容音频页，播放已完成。

## 剩余范围 / NOT RUN

含WAV内容包已经实现，但MP3/M4A、标准/第三方音轨的许可与随包导出、资源音频安装/跨版本升级、完整备份/替换/旧包迁移、系统另存文件及GC仍未完成。当前内存有界实现不承诺最终512MiB产品上限：读取32MiB总包/展开、16MiB单文件、1000资产/绑定；导出预留manifest空间，音频最多30MiB。

Windows新UI交互、iOS原生、Android真机、真实外部接收端、真实设备强杀/满磁盘、最大容量/性能/读屏、六方向完整迁移及176项全量NOT RUN。正式教学音频保持6/161，无新增真人录音或素材终审。
