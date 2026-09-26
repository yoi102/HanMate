# 开发交接 · 2026-09-26

`v0.1.4` 为 GitHub 公开预览版本；发布源码以该标签指向的提交为准。Windows / Android 主要功能已实现，内部账本为 **45/48**，未结项 W5-01、W5-05、W5-07；iOS 暂缓。请从 [STATUS](STATUS.md) 和 [REMAINING_WORK](REMAINING_WORK.md) 读取当前结论，不把下方旧阶段记录的包哈希、设备状态或数量当作当前值。

接续顺序：先在隔离数据和实际 Windows / Android 设备上补性能、读屏、音频路由与资源/分享/迁移回归，再按适用平台逐项完成 P0。保留用户资料，签名冲突时不得通过卸载旧版绕过。HM-D019 为用户要求关闭、根因未知；再次出现同类崩溃须重新登记。

正式发布另按 [RG-01—07](deferred-release-gates.json) 核对内容/权利、eSpeak NG 分发义务、签名升级、商店声明与最终包。Melo 中文男声仍缺合适模型来源。原始平台证据见 [W5 记录](evidence/W5-REGRESSION-20260923.md)，发布预览说明见 [0.1.4 版本说明](../../docs/releases/0.1.4.md)。以下为历史交接记录，仅供追溯。

<details>
<summary>历史阶段记录（仅保留当时结论，不作为当前待办）</summary>

## 最新：字典详情与释义注音 · 2026-09-20

独立字典页已接通文字搜索/手写/拼音查词；词头、释义、例句分层，支持拼音显隐及长释义展开，目标字词与拼音高亮无下划线。pypinyin 0.55.0的89,034条导出读音由C#离线调用，不覆盖源内容或人工注音。新增75条辅助例句覆盖70常用字词，来源例句优先；自动读音和例句仍待独立审校。

44项受影响自动测试、Windows/Android Release零警告错误及Android聚焦交互通过，574键一致；内容审核767对象仍待审，5个发行门禁保留。最终Windows UI、真机、大字号/读屏和iOS未验。35/48及原176聚合NOT RUN不变。见[本轮证据](evidence/DICTIONARY-DETAIL-20260920.md)。下一步校对常用词读音/释义并继续D5；保留用户现有声音及拼音显隐选择。

## 最新：拼音语音包与例词 · 2026-09-20

拼音已优先接入启用的离线AI声音，按课程拼音/声调合成，支持bo/po/mo/fo及独立ong；设置可切回原录音，导入录音优先。新增24常用例词，共204条内容/224录音，所有释义汉字有注音草稿。原音频来自audio-cmn，与繁体字典独立。

126项受影响自动测试、13段原生合成信号检查、两平台Release及Android实际点读/查词/AI开关通过；声音088保留。听感终审、高保真替换、真机、最新Windows UI及iOS仍未验，35/48和原176聚合NOT RUN不变。[本轮证据](evidence/PINYIN-VOICE-20260920.md)。下一步针对具体错音试听复核，并继续D5。

## 最新：简体字典与搜索布局 · 2026-09-19

用户改选chinese-xinhua，默认库已替换为简体字头/释义：合并去重后292,114条，原始字/词/成语记录均保留。原库不再打包，真实个人资料不变。自动补注标为待校对并排除已确认读音索引；常用词按jieba固定词频优先。字典不是官方授权新华字典，来源内容未全面审校。

手机标题同行文字/手写切换、提示移除、图标和更大田字格、左字拼音/右例词换行均已实现。文字搜索改为下滑加载。18项测试及Windows/Android Release通过；实际UI范围、构建哈希和剩余限制见[本轮证据](evidence/XINHUA-20260919.md)。Windows新版在TEMP单独输出，不强制重启原窗口。

下一阶段继续D5设置大字号及Windows/Android剩余验收，并复核常用词释义和多音字。iOS暂缓，35/48及原176聚合NOT RUN不变。下方为历史记录。

## 用户追加：单字手写搜索 · 2026-09-19

已实现顶部切换、下半屏单字田字格、逐笔离线识别、上半屏字词和点击进入原释义页；缺释义显示明确缺失页。8项识别及15项搜索测试通过，两平台Release零警告错误，Android逐笔/点击字词/返回/撤销清空/模式保留已实际验证。识别按常规笔顺，9,574个字形不等于9,574条释义。详见[本轮证据](evidence/HANDWRITING-20260919.md)。

搜索语言刷新HM-D018已有Android聚焦复验；下一步仍为D5设置200%长按钮及Windows/Android剩余验收。Windows最终构建另存在TEMP，未强制重启正在使用的窗口；完整手写交互仍待验。iOS暂缓，35/48及原176聚合NOT RUN不变。下方为历史记录。

## 下一执行入口：D第五轮 · 2026-09-19

Android大字体导航换行/完整宽高/手势栏高度及独立列表计数已修复；Windows例词整行点击/右键/键盘焦点已修复。8项目录测试、两平台最终Release零警告错误；Android中日英200%导航计数/筛选无匹配清除、Windows右键/F10查释义、AI实际下载试听启停/未知字符拒绝和文件取消重试预览已有证据。[D4完整证据](evidence/STAGE-D4-20260919.md)及[机器摘要](evidence/STAGE-D4-verification-20260919.json)。

下一阶段D5先修设置长按钮在200%时截字和搜索页切换语言后旧文案，再扩展Windows搜索/资源管理、系统大字体及隔离的多页列表检查。真机、读屏、正式音源审核和发行仍未关闭，iOS暂缓，35/48不变。

最终APK SHA256 `64c46e7791f084faeebf1d8944cf82b3d8ae47e859168c448c99dd613bbd67ab`。Android中文/系统100%/阅读150%/拼音/AI067保持，停在语音包页。Windows英语/100%/拼音/AI停用，停在混排页，本轮PID41660仅参考，使用前验证工作区Release路径。Windows本轮新下载模型保留，后续不要重下或删除。

D3工程夹具仍复用；没有新增正文或重新合并。原始截图/日志位于TEMP/HanMate-stage-d4-20260919，不入库。设置长按钮和搜索语言残留见HM-D017/018；不要用完整UI树名称冒充视觉PASS。列表只有23词语/14课文，跨50条实际翻页仍未验。下方均为历史记录。

## 下一执行入口：D第四轮 · 2026-09-19

D3阅读修复与本机验证已交付，见[证据](evidence/STAGE-D3-20260919.md)。13阅读测试、3工程投影、两平台Release通过。最终Android系统200%下长文209/209、混排阅读200%、诗词4/4及标签返回日语刷新已验。Windows已恢复真实窗口通路，Tab/Enter/End和200%混排有实际UIA/截图证据；不要继续沿用“全部Windows UI BLOCKED”。35/48不变。

**D4先做**：修复HM-D013（Android系统200%日英底部标签省略、列表筛选计数被裁切），覆盖中日英、空列表/筛选/分页；再复用`tools/windows_acceptance_ui.ps1`验证Windows拼音点击/右键例词、解释页、AI设置和文件取消/重试。后续识别Windows进程必须验证工作区Release路径和窗口归属；工具只重试观察，不能在观察报错时盲目重复操作。现有选择器不支持右键，需要先实现有归属校验的手势通路。

APK SHA256 `45b5e67c5f63d796ff047ca3ed75d9336a7ea5b60f1118fe3ad6e8e7d65caff7`。Android已恢复中文、系统font_scale=1.0、阅读150%、拼音显示、AI067启用，停在语音包页；Windows英语/100%/拼音显示，停在混排页（本轮PID21480，使用前重新查询）。两平台各合并3条D3工程夹具，不重复生成；原始证据与夹具TEMP路径见D3证据。未替换真实库或清除资料，工程注音含问号/误音且未终审。

真机麦克风/路由、完整读屏、Windows系统字体200%、设备真实满盘与破坏性恢复仍未验；独立ong、200录音许可版本、人工听审/日英审核仍OPEN。iOS暂缓，不重复询问；下一阶段不能靠窗口通路恢复就关闭完整工作包。下方为历史记录。

## 下一执行入口：D第三轮 · 2026-09-19

D2文件保存分享/迁移已交付，详见[证据](evidence/STAGE-D2-20260919.md)。95项测试、两平台Release、Android系统保存/取消/重试和5条工程内容/5音轨往返通过。原118条、音频收藏/资源/设置保持；内容包失败后不会再分享旧文件。Windows端为生产服务宿主，不是Windows UI。35/48与原176聚合状态不变。

下一阶段推进长文混排、Android系统字体200%及可访问名称/焦点，修复实测问题；Windows窗口工具仍有错误识别历史，仅做有界诊断。没有真机/读屏证据就不关闭门禁；不重复询问iOS。

当前APK SHA256为`4eebafe84d19b636b4c5a7ffeb5d32b42e6874fbda96d8cbb084378df4f6efcc`，Android中文/150%/AI067启用，PID17776，停在AI语音包页。新增5条D2工程内容（四类+波浪释义）、5条合成提示音和D2收藏夹保留；不是教学录音。私人返回包和原始证据在TEMP/HanMate-stage-d2-20260919，夹具expected路径见证据；不要再生成并重复添加一套。未替换数据库、未删除真实资料、未外发。以下为历史记录。

## 下一执行入口：D第二轮 · 2026-09-19

180处拼音例词释义共1334个汉字已补上下文注音稿，当前无缺注音；稳定ID、全部词头、教学映射与200音频保持。Android实测发现并修复旧窗口复用导致的IServiceProvider已释放崩溃：Shell/五主页改为每窗口新建，阅读/数据页语言事件限制在当前页面，停止播放不再解析旧窗口服务。

152个不同自动测试PASS（66 Core、78 Infrastructure、8 Python），含180条内容两跳备份及重复导入；Windows/Android Release零警告错误。最终Android三轮退出重进、英语200%/日语100%/中文150%阅读及拼音显隐通过，恢复中文/150%。见[阶段C2/D1证据](evidence/STAGE-C2-D1-20260919.md)与[机器摘要](evidence/STAGE-C2-D1-verification-20260919.json)。

下一阶段做D第二轮：文件选择/另存/分享成功与取消、带新注音与工程音轨的设备双向迁移、失败/重复恢复、长文与访问性验收。两跳宿主测试不代替Windows↔Android平台迁移；Windows UI、真机/读屏/系统字体200%、正式听审/日英复核、200录音许可版本和独立ong仍开放。iOS暂缓，35/48不变。

以下为历史记录，以本节为最新入口。

## 最新执行入口：阶段C之后 · 2026-09-19

阶段C内容修正与审核工具已交付：随附1.0.1补17词/18处释义、逐字拼音及日英译文；安全升级保留旧收藏/录音/草稿和资源偏好。实际203文档/408单元/200音频等643对象有哈希审核清单，正式审核尚未通过。176个Infrastructure、15个Core、6个Python用例及两平台Release PASS；Android新版释义、目录1.0.1和中文/150%/AI067保留已验。见[阶段C证据](evidence/STAGE-C-20260919.md)。

下一步先补180处拼音例词释义的注音候选与校对，再推进D的Windows/Android阅读、分享及双向迁移验收。200录音许可版本、独立ong及正式内容/声音听审继续开放；Windows UI阻塞、真机与iOS边界不变，35/48不变。

下方为历史阶段记录，以本节为最新入口。

## 后续执行入口 · 2026-09-19

阶段B本机实现及聚焦验证已交付：[STAGE-B证据](evidence/STAGE-B-20260919.md)。设置中的备份页已有安全副本恢复向导、7天观察期孤立音频清理和保留最近3份完整副本。恢复使用已知表单事务复制并提高revision/epoch；恢复前生成新安全副本。共享维护锁、草稿/回收站/租约及副本引用保护，真实库没有替换/删除验证。

73项受影响测试与7个独立进程强杀/SQLite满额场景PASS，两平台Release通过。Android实际修复了SafePath检查越过应用目录而误拒绝系统上级路径的问题，列表、整理预览取消、草稿阻止清理通过。Android当前没有安全副本，因此设备实际恢复与物理删除不算验收PASS；Windows UI通路仍BLOCKED。详细构建哈希和原始证据位置以STAGE-B及机器摘要为准。

**下一阶段做C：正式内容与资源审核**。复用阶段A已有63项拼音/200音频清单、22段AI样本及ong录制规格，优先厘清逐文件许可版本和缺音源；检查四类正文、字典、拼音及日英译文，建立明确的待修/待审核表。可做来源核验与内容修复，不伪造人工听审、审核人或正式签字。A/B未关平台门禁留给D；不重下已有模型，不再重复询问iOS。35/48不变。

Android原中文、11课文、150%阅读字号及AI模型/067声音保留；本轮未切换网络、未清应用数据。原始UI及测试位于TEMP/HanMate-stage-b-20260919。下方“本轮/下一步”均为历史记录，以此入口为准。

## 本轮：语音可靠性与本机性能 · 2026-09-19

当前实际结果见[evidence/W5-local-completion-20260919.md](evidence/W5-local-completion-20260919.md)。本轮修复损坏模型删除、句读优先分块、语音页旧异步回写，增加三语言可访问标签与模型外输入提示。原生引擎会省略并打印OOV，已在整队列准备和实际合成前双重检查，不让未知输入进入原生路径。

Core218+Infrastructure245=463项PASS，174音色真实非静音生成、4正常/4拒绝输入和30次取消恢复PASS。性能探针包含1k/5k/20k注音分页、10k/100k同词头查询各30次原始样本；W5-01从TODO进入IN_PROGRESS，不关闭真机/键盘/读屏门禁。Android长数字连续合成曾让1.5GiB模拟器失去响应，AI改为最多48文本元素逐块生成；保留userdata冷启动复验，不清库。最终APK/UI结果以证据末节为准。

Windows computer-use仍将显式HanMate启动错误识别为OneDrive；当前不可拿Windows构建代替UI。Commons精确ong文件仍missing，SWAC原许可链接DNS失败；不把这些外部缺口写成已完成。iOS保持用户暂缓，35/48不变。

## 历史：可下载AI中文语音包 · 2026-09-19

用户明确选择内置可下载AI中文声音。已接入AISHELL-3 VITS固定版本、31.2 MiB/174音色和sherpa-onnx 1.13.8。设置→AI离线语音包→下载→选音色→使用；普通阅读缺录音时本地合成，已有录音优先。手工拼音不影响生成读音；模型/音色设置不随备份迁移。仅明确下载时联网，不上传用户正文。

受影响21项PASS；Windows/Android最终Release零警告错误。Windows生产推理双音色成功；Android实际下载与自建TXT正文飞行模式两段朗读成功，最终安装后模型/067音色保留，停用/启用/试听已验。恢复原网络、日语/150%；保留原库及新增个人工程条目AI-voice-smoke。514键一致。证据与APK哈希见[evidence/W3-offline-ai-voices-20260919.md](evidence/W3-offline-ai-voices-20260919.md)。35/48和发行阻塞不变。

模型缓存位于开发机TEMP/HanMate-voice-research/files，生产探针TEMP/HanMate-voice-probe，已有数据不要重下。后续关注中文多音字与8kHz声音实际质量、真机性能；iOS继续按用户要求暂缓，Windows原生推理不代表UI验收。旧拼音独立ong和教学许可版本问题仍独立待办。

## 历史：拼音练习界面与音源 · 2026-09-19

用户改为“首页不要顶部工具；点常见bo/po/mo/fo教学读音；长按进例词；例词直接点读/长按查释义”。已实现并在Android实际验证。管理/逐文件署名移至设置；180例/200音频，直接示范62/63，独立ong缺失，正式听审/许可版本仍待确认。词条解释使用同一ReadingPage，完全匹配的已安装词条优先，否则随附只读草稿；释义正文未注音不伪造confirmed。

Core208+Infrastructure238=446 PASS；最终拼音10项复验通过。Windows/Android Release零警告错误，492文案键一致。Android保留原库与日语/150%，中日显示、单字/整词点读长按、滚动及设置入口已验。Windows computer-use把HanMate窗口识别成OneDrive并报所有权错误，两次恢复失败，UI仍BLOCKED；iOS暂缓。证据和最终APK哈希见[evidence/W2-pinyin-practice-20260919.md](evidence/W2-pinyin-practice-20260919.md)。DONE仍35/48。

不要再把旧“首个例字ba/ma示范”或顶部按钮恢复回来。音频生成默认离线，用缓存MP3和固定Git blob；不要重下全部源文件。当前200个音频不是终审200项通过。后续优先补独立ong、音文/许可复核及Windows实际手势验证。

## 历史：压缩音频、显式语音与两平台往返 · 2026-09-18

**DONE仍35/48（72.9%）**。本机实现继续推进，W4-06和W5-02进入IN_PROGRESS；无Mac/iOS、真人录音、正式终审与发行条件，不能据此宣布48/48。详见[本轮证据](evidence/W3-W4-compressed-speech-migration.md)。

- 独立MP3/AAC-LC M4A先严格有界检查，再由系统解码成PCM16 WAV；原文件不变，完成后才登记草稿。Windows实际生产解码/截断/取消/备份5场景通过；Android实际选择两种文件、ready草稿、试听操作和保存通过，保留原默认音轨。
- 阅读新增默认关闭的持久化系统语音策略；缺录音时仍需明确点击/确认/选音色，普通点击保持本地。每块重验正文/读音hash，人工拼音不传给TTS，语法slot排除。修复语音设置页返回的取消状态，并修复Android初始化超时后的迟到Java回调崩溃；平台复验范围见证据。
- Windows→Android合并→Android系统另存→Windows新TEMP库导入实际PASS：工程正文/锁定ni3、收藏、两条音频SHA256和默认一致。不是六方向全量验收；原Android资料、日语/150%保留。
- 新增取得有署名许可的ma2/ma3，8/161段已取得，153缺失，教学终审未完成；429后遵守冷却停止。Core207+Infrastructure238共445项PASS；Windows Release及Windows宿主iOS managed编译通过，iOS原生NOT RUN。


## 历史：本机平台补齐与发布准备 · 2026-09-18

用户确认暂无Mac/iOS环境，先完成本机可做部分。**DONE仍为35/48（72.9%）**；W0-04/05继续IN_PROGRESS，W0-06及W5-03/04进入IN_PROGRESS。详见[本轮证据](evidence/W0-W5-local-platform-readiness.md)。

- 备份/内容包/本人录音接入系统另存，包提供ZIP后缀兼容入口；Windows事务写、Android文档流、Apple导出副本。失败不报成功，说明可能遗留目标文件；私有原件保持。
- 设置增加显式普通话检测/固定句试听，复用音频协调器。Windows实际试听完成/停止，Android模拟器无音色正确禁用；不提交用户正文，阅读仍本地音频。TTS离线/原生门禁及压缩音频仍待完成。
- Android移除联网权限、关闭自动备份/设备迁移，分享仅暴露cache/sharing；三语言隐私说明及日志只记录异常类型。教学目录累计200项限制，重复不占额度，并发拒绝，保护完整备份容量。
- 固定MAUI Controls 10.0.20、保存NuGet依赖锁；发布预检检查最终APK，身份/签名/原生/终审不齐保持BLOCKED。Core200+Infrastructure234共**434项PASS**，484键三语言一致；Windows/Android Release及Windows宿主iOS managed最终0警告错误，iOS原生NOT RUN。
- Windows与Android系统保存成功/取消已实测，两个保存结果均通过生产严格回读。Android备份仍115正文/2夹/7收藏/26音轨/8资源/1外部教学项，日语/150%与原库保留；系统分享打开后取消，无外发/无录音/无真实替换。

下一步继续MP3/M4A实际解码、阅读TTS回退与离线实测、Windows/Android双向迁移。用户确认暂缺Mac/iOS，不反复询问、不把宿主managed编译当原生。不要重做本轮五块或改为40/48。最新APK/保存文件哈希和可复现命令在本轮证据。桌面语音与另存已实测，原生Save的UIAutomation同步Invoke有限制，使用应用拥有按钮的异步PostMessage；不要因此反复改保存逻辑。


## 历史：恢复、资源媒体与旧版迁移 · 2026-09-18

W3-08、W4-04、W4-05、W4-07、W4-09 DONE，**35/48（72.9%）**。完整首版验收仍未完成。详见[本轮证据](evidence/W4-recovery-resource-compatibility.md)。

- 完整替换先预览影响，生成私有SQLite一致安全副本并复制/核验实际音频，再二次确认。未保存草稿/活动租约阻止；旧库、音频及回收站均在安全副本中。正式删除/写入/资源状态/设置/每次operation收据同事务，epoch与状态摘要拒绝过期计划。同备份修改后可用新operation再恢复；旧编辑revision失效。
- 资源包接入真实PCM16 WAV校验与不可变文件安装；保留式卸载/同版接回/更高版重新安装已实现。新版占用发布binding ID前，将旧binding与依赖迁往历史快照；资产ID不可被不同字节冒用。已卸载跨版安装保持停用，启停/优先级/撤下决定保留。含媒体更新保守保留旧图，暂不自动GC。
- 拼音页导入独立本地教学JSON，验证已安装内容、声调/高亮/示范资产，按资源启停/撤下过滤；实际播放每次重验目标hash。备份resource-state增加可选teachingItems，完整携带并重映射例字/高亮/示范资产；旧包缺字段不推断资源卸载。
- v1包先按归档schema和原始哈希验证，再只转换内容版本；保留原包指纹、UUID、锁定/译文及builtinSnapshot。v1 SQLite文件先BackupDatabase安全复制，在单事务重建约束、复制所有旧行、新增revision/资源/operation结构，foreign_key_check通过才升版本；失败原库保持v1。夹具由归档DDL构造，没有真实已发布用户库的证据。
- Core198/198、Infrastructure232/232，共**430项PASS**（新增20项）。独立进程在after-safety/after-clear/before-commit/after-commit四点被真实强杀；SQLite真实SQLITE_FULL后旧库/旧WAV不损坏，共5场景PASS。465键三语言一致。Windows/Android Release和Windows宿主iOS managed编译通过，iOS原生NOT RUN。
- Android保留原库/日语/150%，工程媒体1.0→2.0升级预览保留1条旧内容及1个教学例字；教学导入后64项、四声页局部高亮、升级后旧例字音轨播放完成。测试音是440Hz工程信号，不计正式教学声音；正式录音仍6/161。真实用户完整替换未执行。
- 六方向迁移、真机/原生iOS、实际文件系统满盘、录音/音频路由与压缩格式、系统TTS、GC及176项全量验收仍待完成，不以工作包计数代替发行通过。

下一步优先W3-05压缩媒体/原生录音、W0-04 TTS与离线状态、W4-06六方向迁移，之后M0/M5平台和正式素材门禁。不要再重做本轮五项，或把W4-05桌面故障证据解释为手机存储卷满盘通过。

运行 `dotnet run --project tools/HanMate.RecoveryProbe/HanMate.RecoveryProbe.csproj -c Release` 可复现四点强杀与SQLite限页满盘；输出私有TEMP报告。内部Checkpoint只供独立probe程序集，App未配置。安全副本位于数据库旁recovery/<operation>/，ready.json仅证明准备完成，import_operation才证明提交。当前无自动GC，不要删除这些副本、孤立媒体或.part。

本轮Android夹具由tools/build_resource_media_fixture.py生成；TEMP/HanMate-recovery-smoke中media-v1/v2.zip、teaching.json与截图保留，resourceId=98dd9460-7bb6-42ef-8130-de1a4c3cc5de。仅新增工程数据，未清库、未开麦克风、未外发。最终APK SHA256=8414b8fb7043b1342791bba2574c253ff5946637abfd9b099cfb7fe183e4212e；最终备份页预览115正文/2夹/7收藏/26音轨/8完整资源/1草稿排除，生成成功。规划包仍执行生成/验证/再生成/check。


## 最新：30/48，逻辑备份与增量合并交付 · 2026-09-18

W4-01/02/03/08 DONE，30/48；四项验收措辞不变，W4-02的硬依赖调整及完整平台门禁见ADR-079。Core198/198、Infrastructure212/212，共410项PASS；新增24项，末次体积/资源总量边界后BackupTests24/24复验。451键三语言一致，Windows/Android Release及Windows宿主iOS simulator managed最终0警告错误。证据W4-backup-merge.md。

BackupPackageCodec复用严格ZIP及PCM16校验器，加入收藏/设置/资源/发布载荷闭包。BackupExportStore同事务快照+文件租约，读取后finally释放；预览有界不可变字节，每次导出新packageId。缺失资源明确referenceOnly需接受，必要快照无许可或音频坏阻止；草稿/回收站排除计数，pending录音阻止。BackupImportStore复用ContentPackageImportStore规划和提交钩子，完整关系同事务；epoch+实际状态摘要覆盖不递增epoch的设置/收藏修改。备份音轨保留角色，普通分享仍为imported。settings v1可选scalePercent保存150%等设置，旧fontSize输入兼容。

资源只安装无冲突完整载荷，外部不得冒充bundled；已有相同资源保留本机状态，不同版本/碰撞转retained且显示缺口，不覆盖降级。默认夹映射唯一默认，同名夹保留双方，成员去重；设置默认保留，勾选才应用并刷新语言。BackupPage重建前必须从旧父级移除复用控件，否则应用设置后Android出现空白；本轮已修复并最终APK验证。只有存在缺失资源才显示部分资源确认。

最终APK SHA256=af814867ed48fe1e9da4e9dac87acd4a86572064ba3cceef50db916ce1295ac9。Android16/API36保留旧库/ja/150%，工程包首次5内容/10音轨/1新夹/5成员，重复新增0/复用5/音轨0/夹0；应用设置后页面正常，Backup-smoke收藏5条、词头音轨播放完成。最终再次预览113正文/2夹/7收藏/24音轨（1612.4KiB去重）/7完整资源/1草稿排除，备份生成成功。最初108条资料的.hanbackup系统分享面板打开后取消，无外发。TEMP/HanMate-backup-smoke保留夹具及截图；工程资料保留，最终停在备份页面。

Windows Release真实UI预览23正文/2资源并生成文件成功；自己启动的进程已关闭。Windows导入/接收、iOS原生/真机、系统另存、六方向迁移、满磁盘强杀、手机大库和176项完整验收NOT RUN。正式音频6/161仍未扩充；备份标准WAV不等于媒体资源包全生命周期已完成。

下一阶段优先W4-04完整替换与恢复协议，再衔接W4-09旧版兼容；W3-05压缩音频、W3-08/W4-07媒体生命周期与教学目录仍待做。真实个人数据的替换动作仍必须准备安全备份并在执行前获得明确授权，可直接在disposable测试库实现验证。不要改写已完成的增量合并或把全平台/正式素材标DONE。规划包收尾仍生成/验证/再生成/check。

## 历史：26/48，四项阅读交互完成 · 2026-09-18

W2-01/03/04/07转DONE，26/48，M2九项工作包DONE。不是素材/发行完成：系统TTS及M0全平台、正式音频及W5终审保持原待办。Core198/198、Infrastructure188/188，共386项（新增21）；433键一致。Windows/Android Release及Windows宿主iOS managed最终0警告错误。完整证据W2-reading-playback.md，ADR-075—077。

ReadingAudioStore在同一个读快照核对屏幕正文与持久正文，预检所有目标自动适用音轨；缺失显示数量且整组不启动。词语/语法取完整unit，正文/诗词优先完整unit录音否则speech段；layout不进队列、诗行不变。LocalAudioStore.OpenExpectedPlaybackAsync在播放每步的事务中核对contentId/targetId/两种hash，仍使用真实WAV校验与文件租约。PlaySequenceAsync将准备和全部步骤纳入一个owner会话，后台/导航/同目标切换不会续播旧队列；修复同步取消清空_session及取消后误判toggle。

ReadingPage.Playback.cs提供全文/选段/整句播放、状态与目标高亮；管理操作移到正文后。点词语/例句直接播整unit，点课文/诗词speech段放大自动播放；上一/下一段停止旧声音但不自动播放。自动播放意图等Handler与偏好加载后消费，别回退为仅OnAppearing调用。PinyinCourse.ClickExample明确选择可用例字，Audio只允许一音节资产对应单token整unit；拼音页标注完整例字音节、四声行高亮、缺音频/录音忙状态和迟到UI守卫保持。

最终APK SHA256=33274e49a5466ddb169503730b790ccc79eeb8875c68eb2eea8b665fffa81e3a。Android16/API36保留库与ja/150%，导入独立5条Playback-*和10绑定，1个5秒440Hz工程音。词头/逗号例句整unit、语法例句、诗词全文两段及目标高亮已运行；最后APK证明单段初进自动播放→完成、切段仅显示、整篇音轨不能冒充段、Home后台停止且不续播。最终在Playback-text已停止页面。7张截图已查看，夹具/截图在TEMP/HanMate-playback-smoke，生成器tools/build_playback_fixture.py。不清库、不录麦克风、不外发。

Windows本轮实际运行拼音b→Playback finished及Examples按钮/第四声；文件选择器聚焦失败，未导入工程包，四类阅读运行NOT RUN。测试启动进程已关闭，曾因apphost锁导致一次构建失败，关闭该测试进程后重建0警告错误。iOS仅Windows宿主managed，真机/读屏/系统字号/强杀满磁盘/完整176项NOT RUN。正式录音6/161不变。

下一阶段继续W4完整备份/恢复或资源媒体闭环；W0-04系统TTS与离线状态、W3-05压缩音频仍需实现/验证。不要把M2结项解释为这些音频能力完成。以下均为历史记录，以本节和STATUS为准。

## 历史：资源草稿与教学引用迁移 · 2026-09-18

22/48 DONE不变，W3-08/W4-07继续IN_PROGRESS。Core188/188、Infrastructure177/177 PASS（新增13，共365），421键一致。Windows/Android Release及Windows宿主iOS simulator managed编译0警告错误；Android完整Rebuild。完整证据W4-resource-reference-migration.md。

ResourceReferenceMigration共用查询和严格契约：独立textDraft.v1/v2的source声明不再误阻止升级或多保留旧图，原body/revision不变；ready localAudio.v1只映射Target.Id/ContentId到retained图，保留文件、读音状态与草稿身份并推进revision；pinyin_item严格按catalog schema和目标/声调/高亮校验，只映射已知例字字段，多旧图共享教学行累计重读映射。未知字段、无效引用及pending录音仍阻止。LocalAudioStore.FinalizeAsync以原body比较后写ready，迟到收尾不得覆盖新草稿。提交仍依赖Guard/epoch和单事务，回滚测试已通过。

重要边界：PinyinPage仍读随附Pinyin/course.json，本轮没有把外部教学目录接到UI，也未迁移demoAudio。下一阶段可继续完整备份/恢复，或资源媒体与教学目录接入；保留pending/未知引用门禁，不重写已经完成的WAV分享和文字资源更新。

最终APK SHA256=1ae3035b0fb74b5c1fee2b2d57143ce96a80c41d577934c26eca19321e91c91e。Android16/API36覆盖安装保留库、ja/150%。独立工程资源v2先留1秒imported WAV ready草稿，再升级3.0.0：changed1/retained1/草稿1；从retained v2打开草稿、试听、保存默认、原生播放完成。最终停在旧v2音频页，标签“読み込んだ音声”，1.0秒，既定・適用可能；v1旧收藏也保留。没有开麦克风、清库或外发。TEMP/HanMate-resource-update-smoke有v3夹具和references/reference-draft/reference-playback.png，已查看。教学引用及新版音轨隔离只有数据库测试证据。

资源媒体、外部教学UI、已卸载跨版、完整备份和GC仍未实现；正式音频6/161不变。Windows新UI、iOS原生、真机、强杀/满磁盘、手机规模性能和完整176项NOT RUN。下面均为历史记录，计数与限制以本节和STATUS为准。

## 历史：外部纯文字跨版本更新 · 2026-09-18

22/48 DONE不变，W4-07进入IN_PROGRESS，W3-08继续IN_PROGRESS。Core188/188、Infrastructure164/164 PASS（新增14）；420键一致。Windows/Android Release及Windows宿主iOS managed编译0警告错误。完整证据W4-resource-version-updates.md。

TextResourceInstaller.PlanAsync严格读取后在读事务建立ResourceUpdateState。TextResourceUpdate只接受已安装external更高数值版本；同kind、完整UUID图及旧目标hash核验。差异只额外忽略source.resourceVersion；其他语义变化均保守算变更。未变原位更新，变更/移除且被引用或rowRevision>1时全图新UUID retained，迁移favorite_item、audio_binding.target_id、preference、图类型import_mapping。资产、字节、binding ID/复核不改；原稳定contentId装新版，不继承旧音轨。旧source版本保持，个人副本不动。

提交重扫正文及相关依赖比较Guard+epoch；新快照/映射/旧图替换/索引/descriptor/operation同SQLite事务，失败回滚，重复operation幂等。原正文rowRevision/membershipRevision递增防过期界面写入；enabled/priority/override保留。未知草稿/教学引用涉及变更图或resourceId时预览阻止，不能用字符串替换猜迁移。已卸载跨版、bundled更新、降级、资源音频与完整备份未做；rowRevision保守策略可能保留额外历史快照。

ResourceManagementPage提供指定resourceId更新入口，ResourceLibraryPage检查文件身份，ResourceUpdateReviewPage20条分页、返回取消、继续到最终确认。_reviewing仅覆盖内部预览导航，原安装取消守卫保持；保留资料及收藏文案已更正。

最终APK SHA256=8d6ddacf6be4106bfc356c372607f441b27304dd97710ff7865bc8fdca755760。Android16/API36覆盖安装不清库，ja/150%不变，未开麦克风或外发。新增工程Resource-update-smoke（16f71db8-7f8c-4ea1-b651-eb52fa7f1088），1.0.0的Update-smoke-v1加默认收藏后更新2.0.0：预览changed1/retained1，新版Update-smoke-v2可读，原收藏打开旧retained。既有你好收藏保留，最终停留旧快照阅读。新工程包/旧快照/收藏继续保留；夹具和review.png/favorite.png在TEMP/HanMate-resource-update-smoke。没有对旧资源执行更新。

下一阶段优先补草稿/教学引用的类型化迁移或完整备份设计与实现；继续压缩音频/发布音轨权利闭包。不要重写已完成的WAV分享与文字更新。手机10k更新性能、读屏、强杀/满磁盘、Windows新UI/iOS原生/真机及176项全量NOT RUN；本轮旧音轨验证为SQLite真实WAV读取，模拟器升级夹具没有音轨。文档收尾仍四步生成/验证/重生成/check。

## 历史：WAV内容包阶段

2026-09-18：22/48 DONE；W4-01/02/03/08继续IN_PROGRESS，新增本人WAV随包链路与文件安装失败重试。Core188/188、Infrastructure150/150 PASS，404键三语言一致；Windows/Android Release与Windows宿主iOS managed编译0警告错误。最新证据W4-audio-content-packages.md。

## 最新：WAV包、选择与资产恢复

TextContentPackageCodec已扩展audio-index v1和精确哈希WAV路径，StrictTextArchive仅content路径allowWave；资源reader仍纯文字。PackageAudio检查真实PCM16容器/metadata/哈希、资产和文件闭包、全局UUID、unit/speech target、confirmed读音哈希和preference同目标；needsReview保留。读取32MiB包/展开、单文件16MiB、1000资产/绑定；导出音频合计30MiB并给manifest留空间。不支持catalogRef或压缩媒体。

ContentSharePlan包含同一读事务的正文及音轨元数据。ExportAsync新重载接选择的binding IDs和单独音频权利确认；只允许user来源本人录音。LocalAudioStore.OpenExportAsync使用export租约，读取后核对快照字节，内存持有选中数据，按哈希去重。未选preference不导出。ContentAudioSelectionPage10条分页、默认不选、返回只改音轨选择；正文选择/重新预览清音轨和旧文件，Shell内部导航例外有明确标记。

ContentAudioImport用content.audio.v1命名空间保存asset/binding映射；binding指纹含源绑定、目标本地ID、源资产指纹。正文全图冲突时音轨目标同步变换，复用重验，删除/修改另存。接收source_role统一imported，已有默认优先，needsReview不建新默认。文件安装到audio/packages/SHA256.wav，.part写完Flush(true)后无覆盖改名；已存在必须验证，不覆盖坏文件。失败只回滚DB，完整孤立文件重试可复用，强杀残留.part不自动清理；未来GC必须考虑在途安装。没有完整备份替换或媒体资源安装。

最终APK SHA256=ffc017d347aa1d17d4248594d43c43bcf5e86276ee1b010d4fb4fe68b8136044。Android模拟器覆盖安装保留库和当前ja/150%偏好；原Emulator-silence 17.4秒needsReview被选择随包生成，导入音轨Audio-smoke-tone不可选；系统分享打开后取消，无外发。工程audio-transfer.zip首次正文/音轨各新增1，正文冲突另存1；再次各复用1，原生默认播放完成。新增Audio-transfer-smoke及Synthetic-440Hz-test保留，不计正式声音素材。最终停留新条目的音频管理页，播放已完成。

## 前轮交接摘要

2026-09-18：22/48 DONE，W3-07完成语法编辑及完整图分享准备；W4-01/02/03/08 IN_PROGRESS，W3-05/08继续保持IN_PROGRESS。Core188/188、Infrastructure134/134 PASS；391键三语言一致。最新证据W4-text-content-sharing.md。用户允许多阶段连续实现后集中测试，不需逐阶段停下来确认。

## 前轮：文字包与录音分享

StrictTextArchive供resource/content共用有界ZIP读取、严格JSON/schema/文件哈希/规范包身份；不解压到外来路径。TextContentPackageCodec只接受v2纯文字content包，manifest/contents/空audio index三文件，1—10000条且全局实体ID唯一；归档与展开32MiB、JSON16MiB、manifest1MiB。含音频包整体拒绝，不能丢媒体后声称导入成功。

ContentShareStore在同一SQLite读快照取得完整图和相关草稿/音轨计数，UI最多选择100条，导出完整拼音/译文。CanShare=false的资源不能通过确认提升权利；仅原始个人输入NOASSERTION允许显式确认文字及译文权利，只修改导出快照的CanShare和permissionNotes，保留本机source及CanDistribute。三文件白名单不带设置/收藏/管理/草稿；生成后重新读包校验。

ContentPackageImportStore先校验再读取epoch和全库图ID。import_mapping命名空间content.v2，source_fingerprint使用整个源ContentDocument语义指纹；每个实体记录source/local ID。同图任一嵌套ID冲突即整图新UUID，grammar引用及segment.tokenIds同步映射。旧映射要重新核验本机图等价和active personal，改动或进回收站时保留旧图另存；同标题不合并。receipt/operation/mapping/正文/索引/targets同事务，同operation重试幂等。包身份以canonical manifest为准，archive哈希仅诊断。

ContentTransferPage从文字草稿列表和正式阅读详情进入，50条分页多选→固定预览→授权确认→生成→系统分享；导入使用系统选择器和数量确认。ShareFileStore只生成私有GUID文件名，.part关闭后改名，7天保护期和256MiB总配额；系统可提前清理缓存。LocalAudioStore.ExportRecordingAsync只允许已保存user来源录音，经WAV/哈希和export租约读取，失败释放租约；needsReview允许独立声音导出，不改变音文复核状态。

最新APK SHA256=87fa313ec98af01891743aba6364ad7f2ee507925ceb5f0c8485280aa52b444e。Android16/API36覆盖安装保留库；2条选择排除2音轨/1草稿、未授权拒绝、生成hanpack与WAV分享面板均通过（打开后取消，无外发）。工程transfer-smoke.zip首次新增4/冲突另存1、重复新增0/复用4；四条Transfer-*仍保留供后续验证，不计教材。已有Draft-test/Grammar-smoke、2音轨/录音草稿/150%偏好保留，hostmicoff不变。

资源升级、完整备份/替换/旧包迁移、含音频分享包、系统另存文件、Windows新界面交互/iOS原生/Android真机及176项全量未完成。新代码无DDL/schema版本变化和新依赖；三平台相关编译已通过，iOS仅Windows宿主managed编译。

## 前轮内容生命周期与继续边界

GrammarEditing.From/Apply按unit UUID编辑，多说明/例句/注记、Literal/Slot/Operator、翻译/主题，未变单元完整保留tokens/segments/锁定；变动只使用该单元作为注音旧图。GrammarEditorPage显式保存和丢失人工项确认，原始简表返回后只允许进入结构表单，不再重新生成复杂图。AnnotationPage和DraftsPage/ReadingPage路由已接入；EditorCommitStore删目标还保护audio draft JSON中的ID，不只检查audio_binding。

ContentTrashStore用draft_kind=content + format=contentTrash.v1 + target_content_id存可恢复标记。正文与引用不删；移动/恢复推进正文rowRevision和dataEpoch，旧编辑草稿可另存副本，恢复后重开原文编辑。Learning/OfflineSearch/FavoriteStore过滤标记，SqliteContentDocumentStore/FavoriteMembershipStore/LocalAudioStore阻止对已移入目标的正式变更。回收站从Drafts入口进入，允许只读预览，无物理清理。未来备份必须处理内部标记，不能将其视为textDraft或教材。

ResourceManagementStore新增逐条保留计划、提交重扫及原位retained转换；收藏/音频/草稿/教学/导入映射/修改内容保护，资源级引用保守保留全包。旧UninstallAsync仍保持无引用限定，UI改用UninstallRetainingAsync。ResourceEntriesPage50条分页，ResourceStateStore拒绝不存在的entry操作。保留资料入口也在Drafts。TextResourceInstaller只在同包重装时对来源/entry/version且归一化origin后语义指纹完全一致的retained图接回，保留现有UUID/targets/audio/favorites；冲突整笔回滚，不猜测重映射。未支持跨版本更新、发布音频包、GC。

前轮APK SHA256=b70113e384860983beada3fccf2b0ae6a1c7d21e2bc79516ecd00a29cd332337。模拟器仍emulator-5554/API36、hostmicoff，不清库。Grammar-smoke现在有Lifecycle-note及negation主题（纯工程输入）；已完成回收站往返。61条搜索夹具完成保留1/删除60→同包接回61；优先级仍17，测试撤下已恢复、临时新增收藏已移除，原有收藏与音频未删除。最终停在Grammar-smoke阅读页，150%偏好。

## 新增音频链路

PlaybackCoordinator.RecordAsync预约权限/采集/收尾全程，期间Play/Record返回Busy，Stop等待安全落盘；原生清理错误抛PlaybackCleanupException并封锁后续会话。Shell.Navigating使用deferral。AudioTracksPage有owner及UI版本守卫，不自动续录。

LocalAudioStore用draft表持久化localAudio.v1(PascalCase Envelope)：pending→写audio/local/{id}.part→完整PCM16 WAV验证与SHA256→rename .wav→ready。Apple录制临时使用.part.wav以明确容器，恢复也检查它。保存写asset/binding/preference/epoch并删草稿同SQLite事务；DB/文件不是单一原子事务。失败留草稿，管理页显式恢复完整文件。正式保存前再验文件，自动播放再验绑定哈希/confirmed，并建立file_lease。无物理GC，未来回收须同时保护草稿、资产、租约。暂不支持MP3/M4A。

阅读页按unit/segment稳定UUID进入AudioTracksPage；页面重新取当前正文/目标并核对快照，Ruby上下文96单元分页。标准音轨不能用用户移除/复核操作破坏；资源新录音默认不勾选覆盖。ReviewDraftAsync/UpdateTrackAsync在显式确认后再检查目标快照，禁止迟到确认覆盖新正文。全unit音轨不能成为segment音轨。

NativeWaveRecorder：Android AudioRecord受控循环，等待写入结束后写正确WAV头，Stop/Release；Windows MediaCapture；Apple AVAudioRecorder及音频会话清理。Plugin4.0.0只用于播放，其Android录音源码有写任务未等待/长度头/释放问题，见ADR-056。Android空间检查使用实际/data目录的StatFs，不能检查只读根分区/。三平台权限声明已加；仅点击录音才申请。

模拟器hostmicoff已关闭宿主输入；Draft-test新增Audio-smoke-tone(1秒440Hz工程合成音)和Emulator-silence(17.4秒模拟采集)，另有5.4秒后台停止草稿。测试曾改你ni3→ni4验证失效/确认，随后恢复ni3锁定；因此两个已保存工程音轨最终保留needsReview，不能称为正确发音。不得清库。这些不计正式素材6/161。最终重启/截图详见本轮证据。

## 先读

AGENTS.md、STATUS.md和最新证据。CSV是真源，派生文档由build_bundle生成。现有App/Core/Infrastructure继续复用，无Git元数据，不清库。

## 既有注音与编辑实现

Core/Content/DraftAnnotation负责生成、稳定锚点、人工改音与grammar句型解析。唯一未变句段+唯一公共前后缀映射；冲突映射不保留。锁定未知也保留，未确定范围先提示数量并确认；旧文档存在PreviousAnnotation，撤销修订继续增长。语法slot只在GrammarDefinition内，不生成token/target。自动词典读音都是needsReview，歧义null汉字在ContentDocument内必须unknown。

Infrastructure/Pinyin/BundledAnnotationLexicon使用固定Unicode17嵌入TSV与24条原创新上下文种子。55,301可解析读音、44,347字，27条特殊音节不支持，UnsupportedReadings显式报告。候选引擎独立于查询字典，支持CancellationToken及Unicode17汉字范围。tools/build_annotation_lexicon.py离线可复现，ZIP哈希固定，许可/manifest嵌入；不要从待厘清许可的聚合词典随意替换。

TextDraft新增v2字段Annotation/PreviousAnnotation/EngineVersion/Pattern/Example/ExpectedContentRevision/StructuredOnly，旧v1仍兼容。TextDraftStore读两格式；已有个人编辑的target_content_id正确记录，用于依赖保护。原文变化时注音快照可以旧，但正式保存严格拒绝过期快照。

EditorCommitStore统一正式保存：Schema+内容验证、草稿revision和精确body核对、个人正文revision守卫，同事务正文/索引/epoch/targets/audio复核/草稿删除。SqliteContentDocumentStore拆出internal SaveInTransactionAsync，原API行为不变。SyncTargets按与资源安装器一致的规范哈希计算；保留资产和binding，变化标needsReview，移除已绑定target时整笔拒绝。不要DELETE重建targets导致音轨级联丢失。

StartAsync读取最新正文；资源先Copy全图重映射并保留来源，个人使用expected revision。单unit词语/课文/诗词可改原文；语法StructuredOnly路由完整结构表单，其他复杂图保持现有token改音。副本不自动携带音频/收藏。分享权利不自行提升，原始输入NOASSERTION、canShare=false、backup allowed。

TextDraftPage接v2和语法字段、后台取消/输入版本守卫；AnnotationPage48字分页、上下文、候选/手输/未知锁定/撤销/筛选/另存/预览/提交。失败的改音停留内存，提供保存重试并拦截离开；成功提交才完成草稿并进入阅读。ReadingPage的allowEditing=false用于未正式保存的预览，包括子段，避免预览创建收藏/再编辑。保存完成回调使旧原文页停止自动保存，不能重建已经完成的草稿。

## 保持原有修复

DataPage每次重建layout必须创建新Status Label，Android不允许控件重复挂载。阅读偏好Handler就绪后加载，不能只放OnAppearing。Shell导航/后台必须继续停止音频。dotnet测试与构建串行，不并行争共享输出。

提交后导航使用捕获的Shell.Current.Navigation做PopToRoot/Push；原编辑页的Navigation代理随出栈脱离，且Shell隐式根的NavigationStack[0]可能为空，不能拿它继续Push。最终Android已验证保存直接打开正式详情。

搜索仍由WordSearchIndex统一重建、confirmed词头限制；SQL个人/来源/撤下过滤、epoch游标、50分页不要回退。资源首次登记后才可查。现有FavoriteMembershipStore差量修改保持旧时间/排序，删夹推进membership_revision；reading.scale合并键不冒充完整settings.schema。

## 前轮设备数据

Android emulator-5554/API36，保留原有数据库。此前Draft-test TXT草稿已通过UI转成个人课文（组合字符、空行、emoji保持），“你”“好”分别被人工确认锁定；覆盖安装重启已复核，“你”输入裸nu被拒绝且旧读音保持。Grammar-smoke为纯英文工程表单测试，source仍draft，不作汉语教材。旧工程字典/阅读夹具和默认收藏继续保留；不要清库。正式教学录音仍6/161；本轮用户层工程音频与截图见上文及最新证据。

## 下一阶段

继续W3-05压缩音频真实探测/解码与设备录音验证，推进W4其他音频格式/来源的分享闭包和资源跨版本更新，之后完整备份及六方向迁移。拆并有绑定或录音草稿目标仍拒绝提交；迁移需独立预览并证明关联，不能猜测。W3-07完整图分享准备已完成，音轨选择随包传输仍属W4-08；W3-08更新保护仍缺。10k资源依赖预览手机性能、回收站完整备份语义、物理音频GC均未完成。不要重写已有WAV和文字包链路。

手机20k生成性能/取消时序、完整软键盘/中文IME/读屏、Windows音频交互/iOS原生、真人录音/20分钟实际限额/路由/来电和176项全量NOT RUN。没有改外部协议、数据库版本或新增包依赖。Android增量压缩程序集曾出现expected22368/got171008启动失败；-t:Rebuild后覆盖安装恢复，不能以清数据解决。dotnet测试/构建继续串行。文档收尾：build_bundle --write、verify_bundle --write-report、build_bundle --write、build_bundle --check。


</details>
