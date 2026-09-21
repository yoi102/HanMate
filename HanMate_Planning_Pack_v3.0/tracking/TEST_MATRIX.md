# 平台测试矩阵

## 当前：D9自动回归 · 2026-09-20

283 Core全量PASS；Infrastructure首轮300 PASS/1 FAIL（旧数量断言），修正后该项复跑PASS。584不同用例最终通过，新增配词次序/同字同读音/原音频保持和42词缺录音白名单检查；417内容两跳备份及重复导入通过。只修改测试源码与夹具链接，应用无需重编译。用户回复“先不调试”，设备/UI验收暂停；原176聚合及82执行行不改，45/48保持。详见[D9证据](evidence/STAGE-D9-20260920.md)。

## 历史：D8内部开发交付与例字配词 · 2026-09-20

应用测试、UI验收、真机安装均NOT RUN（用户明确暂缓）。只执行locked restore、Windows Release publish、Android Release build、APK开发签名检查、编译权限/分享/备份声明检查、内容审核材料一致性及交付ZIP/文件哈希核验；这些检查PASS不代表应用用例PASS。原176聚合和82执行行未改，未新增虚拟通过行。251/256处例字有同字同读音词；新版页面交互仍待验。按ADR-106内部范围45/48，正式审核/发行6组BLOCKED。见[D8证据](evidence/STAGE-D8-20260920.md)。

## 历史：D7实际设备音频 · 2026-09-20

54项自动音频测试、Windows生产MP3/M4A及失败/取消/备份往返PASS；Windows/Android真实短录音获用户试听确认，保存/再录/默认保护、Android断网本地播放/权限拒绝恢复/后台草稿、Windows离页草稿与最终发布采集通过。按构建分开的[D7证据](evidence/STAGE-D7-20260920.md)记录边界：手机用已安装Debug，最终Release仅构建；Android系统普通话不可用提示通过，未称合成通过。W0-04/W3-05结项，40/48。新增4个补充场景和8条执行记录；原176聚合不变。耳机/蓝牙/电话、读屏、全量回归及正式审核签名仍未验。

以下为历史轮次，数量与设备缺失描述只代表当时。

## 历史：D6两平台范围 · 2026-09-20

13阅读+95文件/迁移测试PASS；当前Windows原生文件流/ZIP及原后缀保存、取消、工程合并/重复、原26资料保护和200%混排/长文翻页PASS，证据分范围见[D6](evidence/STAGE-D6-20260920.md)。Android本轮只观察当前状态与安装包哈希；既有D2/D3/D4证据按原构建保留，不搬成新APK全量PASS。W0-02/05、W4-06按用户排除iOS后的范围结项，当前38/48。新增3补充场景及5执行行；原176聚合不改。真实录音/读屏/全量P0仍待验。

以下是历史轮次结果。

## 最新：D5局部修复与审核门禁 · 2026-09-20

23个C#字典测试、10个Python审核测试PASS，最终Windows/Android Release零警告错误；575三语键及APK权限/备份/分享范围预检PASS。Android三语言200%设置/英语七入口、后续长字条两轮保位/原文查看以及最终字体重建语言切换分别绑定各轮APK，见[证据](evidence/STAGE-D5-20260920.md)与[机器摘要](evidence/STAGE-D5-verification-20260920.json)。

HM-D019记录中间APK一次Mono原生SIGSEGV，后续两轮未复现不关闭根因；Windows本轮工具归属错配BLOCKED。iOS、真机、读屏、完整回归NOT RUN。当前52个补充用例、69条分范围执行记录，原176聚合不变。审核1402对象全部待审；默认库293批覆盖292114条但0条完成批次终审；发布预检仍5组BLOCKED。

下方均为历史执行轮次，不相加为本轮全量测试。

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

## 阶段D4实际验证 · 2026-09-19

Android大字体导航换行/完整宽高/手势栏高度及独立列表计数已修复；Windows例词整行点击/右键/键盘焦点已修复。8项目录测试、两平台最终Release零警告错误；Android中日英200%导航计数/筛选无匹配清除、Windows右键/F10查释义、AI实际下载试听启停/未知字符拒绝和文件取消重试预览已有证据。HM-D013新增PASS并保留旧FAIL；HM-D014—D016按Windows实际范围记录PASS，HM-D017/018记录Android视觉与语言刷新FAIL。[D4完整证据](evidence/STAGE-D4-20260919.md)及[机器摘要](evidence/STAGE-D4-verification-20260919.json)。

无回环采样或人工听审，不把点按扩大为音质通过；下载取消是确认阶段，未验网络中断；文件重试只到复用预览并取消，未执行替换。多页列表/完整读屏/真机/Windows系统大字体仍NOT RUN，原176聚合与35/48保持。下方为历史记录。

## 阶段D3实际验证 · 2026-09-19

13/13阅读测试和3组工程投影PASS，Windows/Android最终Release零警告错误。Android系统200%下长文209页、混排阅读200%、诗词4页、分页边界及标签语言刷新已验；Windows原生窗口、Tab/Enter/End、长文末页和混排200%已有UIA与截图PASS。补充HM-D009—D013，执行台账按实际范围记录；HM-D013明确FAIL（日英导航标签省略与列表计数裁切），交D4修复。

不是完整Windows UI或读屏验收；真机/Windows系统字体200%/完整迁移/正式审核未关闭，iOS暂缓。恢复两端原偏好，35/48及原176聚合状态不变。[完整证据](evidence/STAGE-D3-20260919.md)。下方为历史记录。

## 阶段D2实际验证 · 2026-09-19

95/95 Infrastructure测试、6个资料保护核验故障注入、两平台Release通过。Android实际合并/重复/取消/损坏拒绝、系统另存/ZIP/分享取消、内容包失败后拒绝旧文件均PASS。生产宿主严格回读5条四类/释义内容、5音轨/收藏及完整注音/译文，原118条和资源设置保持。新增HM-D004—D008按层级记录；Windows原生UI、真机/满盘/读屏和正式审核未关闭，iOS暂缓，35/48不变。[完整证据](evidence/STAGE-D2-20260919.md)。

## 阶段C2/D1实际验证 · 2026-09-19

180处拼音例词释义共1334个汉字已补上下文注音稿，当前无缺注音；稳定ID、全部词头、教学映射与200音频保持。Android实测发现并修复旧窗口复用导致的IServiceProvider已释放崩溃：Shell/五主页改为每窗口新建，阅读/数据页语言事件限制在当前页面，停止播放不再解析旧窗口服务。

152个不同自动测试PASS（66 Core、78 Infrastructure、8 Python），含180条内容两跳备份及重复导入；Windows/Android Release零警告错误。最终Android三轮退出重进、英语200%/日语100%/中文150%阅读及拼音显隐通过，恢复中文/150%。见[阶段C2/D1证据](evidence/STAGE-C2-D1-20260919.md)与[机器摘要](evidence/STAGE-C2-D1-verification-20260919.json)。

下一阶段做D第二轮：文件选择/另存/分享成功与取消、带新注音与工程音轨的设备双向迁移、失败/重复恢复、长文与访问性验收。两跳宿主测试不代替Windows↔Android平台迁移；Windows UI、真机/读屏/系统字体200%、正式听审/日英复核、200录音许可版本和独立ong仍开放。iOS暂缓，35/48不变。

以下为历史记录，以本节为最新入口。

## 阶段C受影响验证 · 2026-09-19

阶段C内容修正与审核工具已交付：随附1.0.1补17词/18处释义、逐字拼音及日英译文；安全升级保留旧收藏/录音/草稿和资源偏好。实际203文档/408单元/200音频等643对象有哈希审核清单，正式审核尚未通过。176个Infrastructure、15个Core、6个Python用例及两平台Release PASS；Android新版释义、目录1.0.1和中文/150%/AI067保留已验。见[阶段C证据](evidence/STAGE-C-20260919.md)。

下一步先补180处拼音例词释义的注音候选与校对，再推进D的Windows/Android阅读、分享及双向迁移验收。200录音许可版本、独立ong及正式内容/声音听审继续开放；Windows UI阻塞、真机与iOS边界不变，35/48不变。

下方为历史阶段记录，以本节为最新入口。

## 本轮：阶段B · 2026-09-19

73项备份/恢复/本地音频/资源音频受影响测试PASS；7个隔离宿主进程强杀及真实SQLite限页满额场景PASS。Windows/Android Release零警告错误。Android覆盖安装后列表空状态、整理预览取消、草稿/回收站阻止扫描清理PASS；实际恢复和删除仅在TEMP夹具验证，设备端NOT RUN。Windows UI BLOCKED，iOS用户暂缓。三语言键一致；35/48与原176聚合状态不变。见[阶段B证据](evidence/STAGE-B-20260919.md)。

## 本轮：阶段A · 2026-09-19

受影响Core40/40、Infrastructure29/29 PASS（本轮不是463项全量重跑）。Windows/Android Release零警告错误；Android覆盖安装、模型/067保持、点读长按释义、试听/未知英文提示、停用启用、后台停止和个人全文飞行模式朗读PASS。20分钟原生探针1010轮/30取消恢复PASS，最终预检另用新探针回归；合成不等于设备播放/听审。63项/200录音清单哈希核验PASS，正式听审/许可版本仍待办。Windows UI BLOCKED，iOS NOT RUN。[证据](evidence/STAGE-A-20260919.md)；平台/层级/部分覆盖分别记录在 [台账](execution-ledger.csv)。

## 本轮：语音可靠性与本机性能 · 2026-09-19

Core218、Infrastructure245共463项PASS；174个真实音色非静音信号检查、4正常/4拒绝输入、30次连续取消恢复PASS。Windows服务性能每组30样本，报告已归档；不是原生UI/听审结论。Windows最终Release零警告错误，Android长数字压力问题与48元素限额的最终复验见[证据](evidence/W5-local-completion-20260919.md)。517键三语言一致。Windows UI工具错误目标仍BLOCKED，iOS暂缓，正式发行未放行。

## 历史：可下载AI中文语音包 · 2026-09-19

受影响VoicePack/ReadingAudio/Speech测试21/21 PASS，其中6项新增下载完整性、取消重试、持久选择/卸载、读取租约和路径边界用例。Windows生产源代码双音色真实推理与预取消PASS；Windows/Android最终Release零警告错误。Android API36实际下载、试听、启用、TXT导入保存后飞行模式全文两段朗读/点段/停止PASS；最终更新安装保持模型/音色，停用→重新启用→试听PASS。514键三语言一致，最终APK权限/自动备份/分享范围PASS。Windows UI、174音色逐一听审、真机长时性能NOT RUN；iOS暂缓，正式发行仍BLOCKED。本轮未重跑上一轮446项全量测试。详见[证据](evidence/W3-offline-ai-voices-20260919.md)。

## 历史：拼音练习界面与音源 · 2026-09-19

Core208/208、Infrastructure238/238 PASS；来源元数据最终调整后拼音10/10复验。Windows/Android Release零警告错误。Android API36主界面b/p/m/f点读、b/m长按、例词波浪/妈妈直接播放、八/波浪长按释义、滚动/连续切换、中文隐藏译文及设置音源入口PASS，保留原库和日语/150%。Windows UI工具窗口归属错误BLOCKED；iOS NOT RUN。492键一致和APK隐私范围PASS，正式听审/发行仍BLOCKED。完整边界与SHA256见[证据](evidence/W2-pinyin-practice-20260919.md)。

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


## 历史：恢复、资源媒体与旧版迁移 · 2026-09-18

W3-08、W4-04、W4-05、W4-07、W4-09 DONE，**35/48（72.9%）**。完整首版验收仍未完成。详见[本轮证据](evidence/W4-recovery-resource-compatibility.md)。

- 完整替换先预览影响，生成私有SQLite一致安全副本并复制/核验实际音频，再二次确认。未保存草稿/活动租约阻止；旧库、音频及回收站均在安全副本中。正式删除/写入/资源状态/设置/每次operation收据同事务，epoch与状态摘要拒绝过期计划。同备份修改后可用新operation再恢复；旧编辑revision失效。
- 资源包接入真实PCM16 WAV校验与不可变文件安装；保留式卸载/同版接回/更高版重新安装已实现。新版占用发布binding ID前，将旧binding与依赖迁往历史快照；资产ID不可被不同字节冒用。已卸载跨版安装保持停用，启停/优先级/撤下决定保留。含媒体更新保守保留旧图，暂不自动GC。
- 拼音页导入独立本地教学JSON，验证已安装内容、声调/高亮/示范资产，按资源启停/撤下过滤；实际播放每次重验目标hash。备份resource-state增加可选teachingItems，完整携带并重映射例字/高亮/示范资产；旧包缺字段不推断资源卸载。
- v1包先按归档schema和原始哈希验证，再只转换内容版本；保留原包指纹、UUID、锁定/译文及builtinSnapshot。v1 SQLite文件先BackupDatabase安全复制，在单事务重建约束、复制所有旧行、新增revision/资源/operation结构，foreign_key_check通过才升版本；失败原库保持v1。夹具由归档DDL构造，没有真实已发布用户库的证据。
- Core198/198、Infrastructure232/232，共**430项PASS**（新增20项）。独立进程在after-safety/after-clear/before-commit/after-commit四点被真实强杀；SQLite真实SQLITE_FULL后旧库/旧WAV不损坏，共5场景PASS。465键三语言一致。Windows/Android Release和Windows宿主iOS managed编译通过，iOS原生NOT RUN。
- Android保留原库/日语/150%，工程媒体1.0→2.0升级预览保留1条旧内容及1个教学例字；教学导入后64项、四声页局部高亮、升级后旧例字音轨播放完成。测试音是440Hz工程信号，不计正式教学声音；正式录音仍6/161。真实用户完整替换未执行。
- 六方向迁移、真机/原生iOS、实际文件系统满盘、录音/音频路由与压缩格式、系统TTS、GC及176项全量验收仍待完成，不以工作包计数代替发行通过。

以下为历史验证记录。


## W4逻辑备份与增量合并结项 · 2026-09-18

W4-01/02/03/08 DONE，30/48。Core198/198、Infrastructure212/212，共410项PASS；备份/单音频24项新增，最后体积/登记总数保护后24项复验通过。451键三语言一致；Windows/Android Release、Windows宿主iOS simulator managed最终0警告错误，Android完整Rebuild。完整证据evidence/W4-backup-merge.md。

Android实际备份预览/生成/.hanbackup分享面板取消；独立工程包首次5条/10音轨/1夹/5收藏，重复不再增加；最终APK应用设置后正常重建、收藏可读、恢复的音轨完成原生播放。原资料/ja/150%保持。Windows实际预览和生成私有备份文件通过。资源发布WAV/身份/默认/状态往返为SQLite真实文件专项验证；资源媒体更新卸载仍未完成。

iOS只编译managed。系统另存/真正接收、Windows导入、真机/完整键盘读屏、满磁盘强杀、六方向迁移与完整176项NOT RUN，不以本轮410测试代替完整验收。以下为历史记录。

## W2四项阅读交互结项 · 2026-09-18

W2-01/03/04/07 DONE，26/48。Core198/198、Infrastructure188/188，共386项PASS（新增21）；Windows/Android Release及Windows宿主iOS simulator managed最终0警告错误，Android完整Rebuild。433键集/非空/数字占位符一致。队列准备/顺序/失败/取消/清理/录音抢占、同步取消重入、四类目标/整句/布局/旧页面/改音/删除/回收站/默认资格及单音节资产范围均有自动回归。

Android16/API36保留旧库、ja/150%；拼音长按/文字点播，词头和逗号例句整unit，语法例句，诗词两行顺序及范围高亮已验。最后APK复验点段自动播放→完成、切段只显示、整篇录音不作为段音源、后台停止不续队列。独立夹具5条正文/10音轨均为工程合成音。Windows运行拼音点播及显式Examples等价入口；文件选择器聚焦受阻，四类阅读播放NOT RUN。详情evidence/W2-reading-playback.md。

M2结项为交互与本地音轨阅读分支；系统TTS/设备离线及其设置、正式声音6/161终审、iOS原生/真机、Windows四类阅读、完整键盘读屏/系统缩放、176项全量仍未完成。下方均为历史记录，不覆盖本节。

## W4资源草稿与教学引用迁移 · 2026-09-18

Core188/188、Infrastructure177/177 PASS（新增13，共365项）；Windows/Android Release与Windows宿主iOS simulator managed编译0警告错误，Android完整Rebuild。421键简中/日/英集合、非空及数字占位符一致。新增回归覆盖ready草稿迁移/保存/真实WAV、旧读音复核和旧界面失效、独立文字草稿不误阻止、规范教学引用与高亮校验、未知字段/重复声调拒绝、pending录音及迟到计划、同教学行多图映射和事务失败回滚重试。

Android16/API36：独立工程资源v2导入WAV并留ready草稿→覆盖安装→2.0.0升级3.0.0（保留1/草稿1）→retained v2继续试听/保存默认→原生播放完成；日语/150%及旧库保留，未开麦克风、未外发。三张截图已查看。详见evidence/W4-resource-reference-migration.md。

教学引用只验数据库，拼音页仍读随附课程；外部教学UI、资源媒体/备份/GC未完成。Windows新UI、iOS原生、Android真机、强杀满磁盘、手机规模性能、六方向迁移和完整176项NOT RUN。W3-08/W4-07仍IN_PROGRESS，22/48 DONE不变；以下为历史阶段记录。

## W4资源版本差异、快照与更新入口 · 2026-09-18

Core188/188、Infrastructure164/164 PASS（新增14项）；Windows/Android Release及Windows宿主iOS simulator managed编译0警告错误。420键三语言一致。覆盖完整语法图、收藏顺序时间、旧WAV默认/改音隔离、移除/重新引入override、导入映射、个人副本、重复、数值版本/kind/发布者、引用阻止/迟到依赖/取消、故障回滚重试及外部嵌套冲突。

Android16/API36：独立工程资源1.0.0安装并收藏→2.0.0差异changed1/retained1→确认成功→新版独立阅读→收藏继续打开旧版retained；日语/150%和原收藏保留。截图查看通过。资源旧音轨验证仅SQLite真实WAV，本轮模拟器升级夹具无音轨。详见evidence/W4-resource-version-updates.md。

W4-07进入IN_PROGRESS，W3-08继续IN_PROGRESS，22/48 DONE不变。草稿/教学迁移、资源音频、已卸载跨版、完整备份和GC仍未完成；Windows新UI/iOS原生/真机/10k手机更新性能/读屏/强杀满磁盘/六方向迁移/176项全量NOT RUN。下方为历史阶段记录。

## W4含WAV内容包与资产恢复 · 2026-09-18

Core188/188、Infrastructure150/150 PASS（新增16项）；Windows/Android Release、Windows宿主iOS simulator managed编译均0警告错误。404键简中/日/英一致。新回归覆盖选音轨与去重、目标冲突映射、重复/删除再导入、本机默认保留、needsReview、文件安装后DB失败/重试、坏文件不覆盖、取消/过期和10类音频攻击输入。

Android16/API36模拟器保留库及ja/150%偏好，原录音选择→随包导出→分享面板取消；含工程WAV包正文/音轨各新增1→重复各复用1；导入默认音轨原生播放完成。详见evidence/W4-audio-content-packages.md，不代表真实接收方交付或真人录音质量。其他音频来源/格式、另存、资源音频/升级、备份替换、GC及Windows交互/iOS原生/真机/满磁盘强杀/176项全量仍未完成。下方保留历史阶段记录。

## W4文字内容包与录音分享 · 2026-09-18

Core188/188、Infrastructure134/134 PASS，新增17项包/合并/分享回归；四类完整图、人工锁定/译文、嵌套ID冲突、映射重验/修改与回收后另存、重复operation、重压缩、坏包/权限白名单、事务回滚/取消、真实WAV导出及租约释放。391键三语言一致。

Windows/Android Release（Android完整Rebuild）及Windows宿主iOS simulator managed编译均0警告错误。Android16/API36模拟器：2条预览排除2音轨/1草稿、个人权利拒绝/确认、hanpack与17.4秒WAV分享面板打开后取消；四类工程包新增4/冲突另存1→重复新增0/复用4，原资料保留。未外发、未新增正式音频。详见evidence/W4-text-content-sharing.md。

W3-07完整图分享准备完成，22/48 DONE；W4-01/02/03/08仍IN_PROGRESS。含音频包、另存文件、资源升级、完整备份/旧包迁移未实现；Windows新UI/iOS原生/Android真机、接收方实际读取、满磁盘/强杀、大包容量、完整176项及六方向迁移NOT RUN。下方早期阶段数据保留为历史记录，不覆盖最新状态。

## W3-07 / W3-08 连续实施 · 2026-09-18

Core188/188、Infrastructure117/117 PASS；新增10项Core和15项SQLite回归。覆盖稳定unit重排/锁定/Operator、局部注音不跨unit映射、结构/Unicode拒绝、回收站过滤/恢复/并发/回滚、六类卸载依赖/同包接回、冲突拒绝、录音草稿目标删除保护。359键简中/日/英键集、非空和占位符一致。

Windows/Android Release及Windows宿主iOS simulator managed编译0警告错误。Android16/API36覆盖安装保留旧库，复杂语法添加注记/主题并正式保存、回收站7→6→7、撤下/恢复、61条工程字典卸载保留1/删除60、保留内容阅读、重装61并保留收藏/优先级/撤下状态PASS。详细范围及APK哈希见evidence/W3-07-W3-08-content-lifecycle.md。

W3-07/W3-08仍IN_PROGRESS：分享准备与跨版本更新保护未完成。Windows新界面交互、iOS原生/Android真机、IME/读屏、10k依赖扫描设备性能、强杀/满磁盘、完整备份迁移及176项全量NOT RUN。回收站不物理释放空间；本轮无新增正式音频。

当前文档v3.0。完整平台应用验收尚未完成；以下完整范围均NOT RUN，已执行的聚焦 smoke 单列。详细176项见acceptance-cases.csv。

| 范围 | iOS | Android | Windows |
|---|---|---|---|
| 三语言/拼音/词语/课文/诗词 | NOT RUN | NOT RUN | NOT RUN |
| 语法详情/编辑/例句 | NOT RUN | NOT RUN | NOT RUN |
| 学习资源和字典管理 | NOT RUN | NOT RUN | NOT RUN |
| 来源搜索/收藏/撤下保护 | NOT RUN | NOT RUN | NOT RUN |
| 真实录音/导入/分享 | NOT RUN | NOT RUN | NOT RUN |
| 备份/资源状态/旧协议 | NOT RUN | NOT RUN | NOT RUN |
| 字号/键盘/读屏/Release | NOT RUN | NOT RUN | NOT RUN |

跨平台六方向迁移单独留证：Android↔iOS、Android↔Windows、iOS↔Windows，全部NOT RUN。测试机、SDK、包版本、commit、输入、期望、实际结果、日志/截图填写实际值，不生成虚构证据。

文档工具验证在VALIDATION_REPORT单独登记，不能将其中PASS搬到此表。

## W3-04 / W3-05 / W3-06 本地音频聚焦 · 2026-09-17

Core178/178、Infrastructure102/102 PASS；312键三语言一致。Windows/Android Release、Windows宿主iOS托管编译PASS且0警告错误。Android16/API36模拟器实际WAV导入/试听/默认播放、权限拒绝不重复提示、关闭宿主麦克风的17.4秒采集保存、5.4秒后台停止草稿、改音失效及显式复核恢复播放PASS。工程合成音与模拟输入不是真人录音或发音质量验收。详细命令、修复和截图见evidence/W3-04-W3-06-local-audio.md。

MP3/M4A、Android真机/Windows录音/iOS原生、来电/耳机路由/20分钟/满磁盘/强杀/完整176项NOT RUN，不改写上方完整验收表。录音6/161正式素材覆盖未变。

## 纯逻辑与 Infrastructure 自动测试

2026-09-17：Core 89/89、Infrastructure 48/48，137/137 PASS。覆盖原有内容/注音/本地化/SQLite与资源状态，新增严格语法、24条JSON往返、锁定修改时间、资源包校验/指纹/非seek流/限额、事务回滚/跨包ID冲突/幂等/取消/过期计划。24个实际.NET导出另经Python schema检查 PASS。非平台测试不改写上表或176项完整用例。

## W1-04 Windows 聚焦 smoke

2026-09-15：Windows Debug 应用实际启动且进程响应正常。通过 Windows UI Automation 实际选择五个 Tab 和三种语言按钮；简中/日/英导航立即刷新，教学 `nǐ hǎo / 你好` 保持不变，简中隐藏辅助译文，日/英分别显示对应译文，日语选择在关闭重启后保留。结果为 PASS，完整观察值见 `tracking/evidence/W1-04-five-entry-localization.md`。本 smoke 没有覆盖 HM-T001—HM-T012 的全部步骤和三平台组合，因此不改写 acceptance-cases.csv。

2026-09-14 文档复审：扩充 HM-T105/141/148/152/174 的再次恢复、阶段安装限制、教学引用、卸载重启与音频备份预期；未执行这些应用用例，三平台状态仍为 NOT RUN。


## W1-09 文本资源聚焦 smoke · 2026-09-17

Android 16 / API36 模拟器 Release：PASS。实际打开系统选择器、自定义后缀与zip回退、预览取消、安装学习包4条/字典包3条、幂等重复安装和只读语法预览；简中无译文、英文显示对应译文、日语界面刷新；结束进程再启动仍保留7条内容与语言设置。完整步骤及重启观察见 W1-09 证据。

Windows Release 构建/进程启动：PASS；安装界面交互：BLOCKED（computer-use窗口识别错误）。iOS managed编译：PASS；iOS运行：NOT RUN。Android模拟器结果不代表Android真机或其他平台；保存/分享、Ruby、音频和完整176项仍NOT RUN。

## W1-05 / W2-01 拼音与随附资源 · 2026-09-17

Android Release 聚焦验证包括真实原生音频播放/完成释放、长按四声、目标声母红色下划线、缺音频状态、随附学习分类和内容显示。新增161个ContentDocument的独立schema、6段原音SHA1/WAV哈希与118键三语言文案校验PASS。详细构建/回归结果及边界见 `tracking/evidence/W1-05-W2-01-pinyin-and-starter.md`。

W1-05完成；W0-02/W0-04/W1-06/W2-01保持IN_PROGRESS。上述短词和播放证据不改变完整176项的NOT RUN，也不代表真实录音、长文Ruby、素材终审或全平台交互通过。

## W1-06 原生分页阅读 · 2026-09-17

本轮Core102/102 PASS（新增13项）；Infrastructure沿用前阶段48/48，本轮未重跑。三平台对应managed目标编译通过，0警告错误；141键三语言一致、设备测试包四JSON独立schema通过。Android16/API36 Release已验证20,000字/209页末尾不丢、应用阅读字号200%、拼音开关、片段放大与返回、混排空行及保留页面三语言刷新。详见W1-06-native-paged-reader证据和三张截图。

W1-06 DONE；W0-02与W2-03/04/07仍IN_PROGRESS。系统字体200%、真机/iOS/Windows交互、完整键盘读屏/性能和正文音频仍NOT RUN；完整176项不由本次聚焦测试替代。

## W2-05 / W2-09 离线查词 · 2026-09-17

Core140/140、Infrastructure63/63 PASS；Windows/Android Release、Windows宿主iOS managed目标0警告错误；149键三语言一致。10k合成词头实际SQLite基准，最新55次热查询P95 67.36ms，前一轮122.34ms，均仅为开发机结果。

Android16/API36模拟器已验证安装61词测试字典、ni3hao3共55条及50+5分页、阅读详情返回保留第二页、来源选择、nu:/nu区分、xian/xi an边界、Enter与三语言译文切换。测试包source仍draft/testFixture，未提升随附资料审校状态。详见evidence/W2-05-W2-09-offline-search.md。

W2-05/W2-09 DONE，R10 IN PROGRESS；完整176项、Windows/iOS交互、Android真机、通常软键盘及中文IME组合输入、手机10k与100k容量NOT RUN。资源管理基础交付见下面W2-08，受引用卸载继续由W3-08承担。

## W2-08 资源与字典管理 · 2026-09-17

Infrastructure Release 75/75 PASS，新增12项真实SQLite生命周期与回滚测试；Core沿用前阶段140/140，本轮未重跑。Windows/Android Release和Windows宿主iOS simulator managed目标均0警告错误；172键三语言非空/键集/占位符一致。

Android16/API36模拟器Release聚焦PASS：管理列表/详情与来源许可；停用后搜索立即无结果、启用恢复；优先级200→17；卸载预览取消不改61条，确认后0条；重启保持缺失；系统文件选择器从原ZIP重装61条并保留优先级17；nihao再次55条。列表截字已通过独立卡片修复，截图见W2-08证据。

W2-08 DONE，R19/R20仍IN_PROGRESS。保护依赖、提交重扫和故障回滚有SQLite证据；不冒充受引用快照迁移/音频文件GC已实现。Windows管理交互、iOS原生运行、Android真机及176项完整用例保持NOT RUN。

## W2-02 / W2-06 / W3-01 连续实施 · 2026-09-17

集中执行Core148/148、Infrastructure83/83 PASS，共新增16项解码/限额/查询/收藏事务/草稿版本/偏好测试。最终Windows/Android Release与Windows宿主iOS managed编译0警告错误；242键三语言键集/非空/占位符一致。Android修复SQL拼接、原生Label重复挂载、首次阅读偏好加载时机后验证通过。

Android16/API36模拟器：16词列表、高级筛选无匹配、清除恢复；双夹收藏、删普通夹保留默认夹引用、重启仍可读；125%阅读偏好重启并可继续调150%；UTF-8 BOM TXT导入（空行/中文/组合字符/emoji）、自动保存、重启恢复、错误ZIP拒绝且原文不变。详见evidence/W2-02-W2-06-W3-01-learning-favorites-drafts.md。

剪贴板系统粘贴、真实满磁盘/进程强杀保存窗口、完整软键盘/读屏/系统缩放、Windows交互、iOS原生与Android真机仍NOT RUN。三工作包DONE不替代176项完整验收；W3-02/03自动注音、改音和正式保存尚未完成。

## W3-02 / W3-03 / W3-07 连续实施 · 2026-09-17

Core162/162、Infrastructure92/92 PASS，新增14项核心与9项资源/SQLite用例。Windows/Android Release和Windows宿主iOS simulator managed编译0警告错误；271键三语言键集/非空/数字占位符一致。

验证唯一句段/公共前后缀映射、重复歧义拒绝、人工及未知锁定、明确声调/ü/轻声、grammar槽位及必填、取消/范围；真实SQLite提交的草稿和正文并发守卫、body一致性、索引/epoch/音轨原子性、失败回滚、资源完整图复制及v1草稿兼容。候选库55,301可解析读音，27条不支持形式明确记录，自动候选不冒充confirmed。

Android模拟器实际完成旧TXT草稿注音、候选锁定、混排预览、正式课文保存及草稿完成、最小语法表单保存阅读。详情与最终复测见evidence/W3-02-W3-03-W3-07-annotation-editor.md。W3-02/03 DONE，W3-07因多unit表单/删除/分享准备未齐保持IN_PROGRESS。完整平台/录音/IME/读屏/容量与176项NOT RUN不变。
