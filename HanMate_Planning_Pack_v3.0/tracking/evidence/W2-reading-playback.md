# 拼音交互与四类阅读播放

日期：2026-09-18。W2-01、W2-03、W2-04、W2-07转为DONE，22/48→26/48，M2九项工作包完成。实现先完成，再集中测试与平台检查。无新依赖、数据库DDL或包schema版本变更。完成范围为交互与本地音轨阅读，不替代M0/M5的平台及素材验收，见ADR-077。

任务依赖按实际实现调整：W2-01依赖已完成的W3-04协调器，W2-03/04/07依赖W3-04及W3-06目标音轨绑定，不再将W0-04整项作为本地播放交互的硬前置。四项验收措辞不变；W0-04的系统TTS与完整平台门禁仍未完成。文档校验仍严格要求所有DONE任务的硬依赖均已DONE。

## 交付

- W2-01：拼音分组、长按/拖动取消/抑制尾随点击、Windows显式示例入口与右键已有实现；本轮补齐可用例字音源选择、录音忙状态、播放行高亮和过期停止回调保护。条目点击明确显示完整例字音节，不冒充声母自身四声。单音节资产不能作为多字例词的整词录音，缺失音源不调用TTS。红色下划线仍由具体token及拼音范围决定。
- W2-03：词头置前，点击词头/例句或显式播放按钮直接播放整unit；含逗号的例句不会被缩成第一段。完整正文、当前读音和同义不同ID分开取音轨，收藏/编辑/分享管理按钮放在正文后面。
- W2-04：课文/诗词单段放大播放、上一/下一段只切换显示、全文顺序队列、当前目标高亮及缺失数提示。全文优先整个unit音轨，无完整音轨时按speech段；layout空行不进队列。保留诗行，不估算整篇录音的分段时间轴。
- W2-07：语法仍按说明/例句/注记引用顺序阅读，句型Literal/Slot/Operator结构不送给播放器；例句按整unit播放，保留中文说明、拼音和辅助译文规则，收藏导航沿用已完成入口。

ReadingAudioStore在一个读事务核对屏幕ContentDocument，再预检音轨集合。任何缺失都会阻止开始，避免静默少读；每一步LocalAudioStore.OpenExpectedPlaybackAsync重新验证目标contentId/文本hash/读音hash，自动选择只接受适用默认、standard或个人confirmed绑定，打开实际文件时持有租约并验证WAV及SHA256。过期/被回收/改音/删除绑定后不能沿旧计划继续播放。

PlaybackCoordinator.PlaySequenceAsync预约覆盖准备与整段队列，所有项顺序await。停止、返回、切Tab/后台取消整个会话；录音独占与迟到原生清理保持。修复测试发现的同步取消重入：取消可能清空_session，停止前捕获对象；toggle状态也必须在取消前计算，防同目标再次点击错误重播。MAUI首次进入放大页的自动播放意图等待Handler服务上下文及偏好加载完成后再消费。

## 自动验证：PASS

- Core Release：198/198，新增10项。覆盖队列顺序、准备取消/失败、项失败、切换等待清理、重复点击/后台、录音抢占、同步取消、拼音精确例字和单音节不能冒充整词。
- Infrastructure Release：188/188，新增11项。覆盖四类完整目标、整篇/分段隔离、诗行和布局跳过、缺失预检/pending草稿不可播、屏幕过期、改音后旧计划失效、移除绑定/回收站、资源默认资格和取消。
- 共386项通过。Core最终日志：HanMate.Core.Tests/TestResults/reading-playback.trx。没有删除断言；测试夹具构造的TextDraft参数修正后通过。
- 三语言433键：键集、非空、数字占位符一致。
- Windows Release、Android Release完整Rebuild、Windows宿主iOS simulator managed构建最终均PASS，0警告/0错误；不是iOS原生或Mac/Xcode运行证据。日志TEMP/hanmate-reading-playback-{windows,android,ios}.log。最终APK SHA256=`33274e49a5466ddb169503730b790ccc79eeb8875c68eb2eea8b665fffa81e3a`。

代码入口：HanMate.Core/Audio/PlaybackCoordinator.cs、HanMate.Infrastructure/Database/ReadingAudioStore.cs、LocalAudioStore.cs、HanMate.App/Pages/ReadingPage.cs及ReadingPage.Playback.cs、PinyinPage.cs、PinyinExamplesPage.cs、Controls/RubyTextView.cs、Core/Pinyin/PinyinCourse.cs。

## 平台检查

Android16/API36，emulator-5554，1080×2400：覆盖安装Release保留现有库、日语和150%偏好。通过正常内容包导入独立工程playback-smoke.zip：新增5条Playback-{word,grammar,text,poem,whole}、10音频绑定，合用一个5秒440Hz合成WAV。未清库、未开麦克风、未分享外发。这些是工程数据，不计教材或正式声音。

- 拼音b长按打开四声，松手后无额外播放；点击第一声的字面文字触发播放并自然完成，截图只有b局部红色下划线。
- 最终布局词头在管理操作之前可见，点“你好”直接播放1/1并高亮词头；滚到“你好，欢迎。”点击正文时完整例句高亮，最终显示播放完成。
- 课文两speech段全文1/2→完成，开始后点停止返回已停止。诗词两行保持，全文1/2→完成，第一行播放时仅第一行高亮。
- 语法显示句型槽位、中文说明和带调例句；全文1/3，点击例句按钮替换为单unit 1/1；退出页面不继续旧队列。
- 最终APK复验：点诗词第二行进入选择2/2并自动播放1/1→自然完成；上一段只切显示1/2、状态已停止；手动播放后下一段立即停止、改显示2/2，不续播。初次Handler晚于Appearing的漏播放已修复并通过真实页面复验。
- Playback-whole的完整unit录音1/1播放完成；点其第一speech段进入放大页，显示1个目标缺少适用音频且不播放整篇录音。没有把全文录音切成虚构片段。
- Playback-text全文进入1/2后按Home切后台，重新回到应用显示已停止，未自动续队列。最终停留此阅读页；5条工程内容与10条音轨保留，原资源/收藏/音频未清除。

Windows Release真实进程/UIAutomation：点b显示“Example for b: 八 · bā”，最终Playback finished；Examples按钮打开纵向四声，第四声进入Playing example bà。已证明Windows等价按钮入口，未将右键/键盘/读屏全覆盖算通过。Windows内容包选择器无法可靠聚焦，未导入该工程包，Windows四类阅读原生播放继续NOT RUN。关闭本轮启动的测试进程后解除apphost文件锁，重新构建通过；未结束其他程序。

夹具生成器tools/build_playback_fixture.py；夹具及截图在开发机TEMP/HanMate-playback-smoke。已查看pinyin.png、word.png、example.png、grammar.png、poem.png、selection.png、missing.png，范围高亮正确，正文可见；滚动边缘裁剪为正常滚动。除selection/missing为最后的Handler修复APK外，前五张为同轮修复前APK，已分别说明其验证范围；最后APK额外覆盖单段、整篇/段隔离和后台停止。

## 未完成范围

W0-04仍承担系统TTS语音选择/实际离线验证及其设置，本轮仅使用本地匹配音轨；没有调用系统合成或暗中联网。正式录音仍6/161，例字/译文仍待W5-02审校；工程音不能证明普通话发音正确。M0完整平台门禁、iOS原生、Android真机、Windows四类阅读运行、系统字号/读屏/IME、来电路由、手机大队列性能、完整176项与六方向迁移仍NOT RUN。完整备份、压缩音频和资源媒体继续原后续任务。
