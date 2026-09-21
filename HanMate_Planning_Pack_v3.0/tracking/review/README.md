# 拼音、字典与AI声音审核入口

## 当前范围 · 2026-09-20

这是待审核材料，不能当作正式听审/语言审校结论。现有报告包含1444对象：440份学习/拼音内容、882单元、394录音、63教学项、174音色，以及资源/模型/展示注音、75条辅助例句、默认库版本/权利与293审校批次；1444项均待审。课程为417例字词，辅助例句覆盖70字词；这些不同口径不能相加成为“已审词条数”。

独立chinese-xinhua默认库已纳入发布审核门禁：1个库版本/权利对象和293批内容/拼音对象覆盖全部292,114条，每批最多1000条。扫描实际GZip/SQLite验证哈希与条数，按词频、字头、UUID固定顺序分批；任一成员或版本改变，审核哈希随之改变。[批次清单](dictionary-batch-inventory.csv)和[机器报告](content-audit.json)提供范围，当前**0条完成整批内容与拼音审核，292,114条未完成**。两个Catalog ZIP中的dictionary草稿库不等于该默认词库。

在仓库根目录运行`python tools/dictionary_review_batches.py --batch dictionary-batch:xinhua:0001 --output <temporary-path>/batch-0001.json`导出首批高频词，文件含完整源记录、逐条哈希和`reviewSubjectSha256`。人工批次决策还须填写`allEntriesReviewed: true`与准确`reviewedEntries`，抽样不能整批放行；程序不能证明审核人确实逐条看过，仍需要真实证据。`NOASSERTION`权利状态保持阻塞，不因用户选择本地使用而放行公开分发。

- 当前`content_review_audit.py --check`为派生文件一致性检查；`--release`会在待审/缺录音/许可不明时失败。440 CONTENT_DRAFT、394 LICENSE_VERSION_UNRESOLVED、47 READING_RECHECK、1 DEMO_MISSING、1 DICTIONARY_RIGHTS_UNRESOLVED仍存在；READING_RECHECK不等于已确认错音。
- 原录音分支缺独立ong；启用已下载AI后可按明确教学音素合成ong，不用dong冒充。教学和可解析字典词头已接明确拼音；普通全文AI与系统TTS仍按正文合成。
- 当前模型8kHz，174音色非静音验证不等于听审。默认067只是工具预设，不是已审推荐；用户现有选择（曾为088）不因审核而重设。
- [content-review.html](content-review.html)提供学习正文/注音/译文及394录音的浏览试听。展示注音、辅助例句和默认库审核对象在[content-audit.json](content-audit.json)中；默认库通过上述命令逐批导出，HTML不展示全部29万条。

在仓库根目录运行`python tools/content_review_audit.py --write --check`更新派生材料，运行`python tools/build_voice_review_inventory.py`重建63教学项和224录音的清单。人工决策在`review-decisions.json`中绑定subjectId、当前sha256、dimension、PASS/FAIL、method=human、真实reviewer、ISO日期和实际evidence；程序只检查记录结构，不验证审核者身份。没有真实审核就保留NOT RUN，不填示例签名。

每次正文/读音/音频改变必须使对应旧结论失效；不因非商业本机选用就推导公开分发权利。录制规范和2026-09-19来源调查保留如下历史参考，未重新联网核实，不代表今天的远端状态。

本轮内部交付新增42个例字组词，251/256个例字位置已有高亮目标同字同音的例词；394录音数量不变，42个新词无整词录音。配对是开发内容改进，正式审核继续后置。

<details>
<summary>历史阶段审核说明及录制规范（数量和实现边界以本页顶部为准）</summary>


## 阶段C2更新：释义注音已补齐

180处释义共1334个汉字已提供上下文注音稿，当前随包扫描没有ANNOTATION_MISSING。来源为 `tools/content/pinyin-definition-readings.tsv`，每条绑定精确释义正文，原文哪怕等长修改也必须修订对应读音；漏条、多条或音节数量不符时生成失败。`pinyin_definition_annotations.py`保留稳定unit/token/segment身份，只增加释义注音与相关revision，标记manual/locked/needsReview；manual表示编辑式输入，不代表人类审核。正文仍draft，不能计入正式审签。

“一、不”使用词典本调，轻声用0；惩处chǔ、量长度liáng cháng dù、降落jiàng luò、恶心ě xin/厌恶yàn wù、教学生jiāo xué sheng等按上下文编写。中文教学人员仍需独立复核。固定Unihan候选对部分简体多音字仅收录一个读音，因此当前18条READING_RECHECK仅为筛查提示，不是18项已确认错误。相比阶段C的6条，增加的是新释义覆盖后的提示。

200音频记录、63教学项映射及180词头数据与阶段C逐项相同；版本为draft-2026.09.19.2。旧180正文及其教学项的审核哈希失效，现有正式决策仍为空。日英辅助译文仍主要位于词头，新释义中文不自动生成未审核译文。下节的180缺注音为阶段C历史结果。

## 阶段C新增实际随包审核入口

打开 [content-review.html](content-review.html) 可筛选阅读中文、拼音、日英译文，并逐段播放200份本地音频；不联网、不自动播放。完整数据在 [content-audit.json](content-audit.json) 与 [content-unit-inventory.csv](content-unit-inventory.csv)。覆盖203文档/408单元、2资源描述、63教学项、200音频、1模型和174音色，共643个审核对象，当前均未完成正式审核。

`python tools/content_review_audit.py --write --check` 重建并核对实际ZIP、课程及WAV。`--release` 在未审、缺注音、缺示范、许可版本不明或记录失效时退出2；发布预检也直接扫描实际源文件，不信任旧报告。6个READING_RECHECK是候选字库未覆盖读音（乐yuè、应yìng、发fà、雌cī、哦ò、恶ě），需要看上下文，不能据此判错。180个ANNOTATION_MISSING均为拼音例词的释义正文，不是例词词头漏标。

人工结论单独写入 `review-decisions.json` 数组，每条包含 `subjectId`、当前对象的 `sha256`、`dimension`（取对象reviews键）、`status`（PASS/FAIL）、`method: human`、真实`reviewer`、ISO日期`date`、本目录内实际证据文件相对路径`evidence`。不填示例签名。记录只检查结构和文件存在，不替代人工身份与证据内容核验。重复、过期或越界记录阻止放行；修订需保留旧记录到历史证据文件，再在活动数组录入当前版本结论。教学项哈希同时绑定所有例文及示范/单字/整词录音；音色绑定模型和听审句集。`--write`不覆盖决策或人工证据。

已补齐随附17个词的18处释义草稿（学习在两个目录各一处），含全部汉字拼音及日英译文；修正“本”的量词英文。新增内容仍是AI辅助草稿，不能冒充独立终审。目录升级至1.0.1，协议示例夹具不改。

本轮来源请求摘要见 [source-check-20260919.json](source-check-20260919.json)：OpenSLR AISHELL-3明确声明Apache 2.0（仅训练集声明，不单独证明模型所有环节授权）；audio-cmn仍未写CC BY-SA版本。SWAC网页跳往无关站点，拒绝作为许可证据；原readme DNS失败。Commons精确查询三个ong文件名均missing，仅代表这些查询。未发送授权询问、未伪造新录音。

本目录是待审核资料，不是正式听审结论。`pinyin-demo-inventory.csv` 为 63 项教学按钮、例词拼音和直接音源映射；`pinyin-audio-inventory.csv` 为 200 段录音的文件、哈希、作者、来源和许可。文件位置相对于应用 `Resources/Raw`。

运行仓库根目录 `python tools/build_voice_review_inventory.py` 离线重建这两份清单，并核验实际音频 SHA256。它们是派生文件；人工审核意见另存审核记录，不直接修改后被生成器覆盖。审核记录至少包含条目/音频键、确切 SHA256、审核人、日期、发音/音文匹配/拼音/译文结论、缺陷和修订版本。改变音频或正文后相应旧结论失效。

## 当前来源核查 · 2026-09-19

- audio-cmn 固定提交 `ff9ed3d0c631195bd2c06f39450f3264c7124040` 的完整缓存树只有 README 许可声明，没有单独许可证文件，也没有 `cmn-ong1..5` 独立录音。README 只写 CC BY-SA，不能据此补写 3.0 或 4.0。
- GitHub 该提交的 `/license` API 返回 404；原 SWAC 地址 `https://packs.shtooka.net/cmn-caen-tan/readme.txt` 仍 DNS 失败。404 不是无版权结论，DNS 失败也不是授权撤回；两者都不能解决确切版本。
- Commons 的 `intitle:ong filetype:audio` 查询本次检查前 30 项，未找到可确认的普通话独立韵母示范；出现的闽南语、越南语、外语姓名等不能替代。不是断言全网没有音源。原始查询快照在本机 TEMP/HanMate-stage-a-20260919，阶段证据记录范围。
- 当前 200 段录音均保留原声明且仍需版本核实与音文听审，正式发行保持阻塞。可选择取得原权利人的明确授权，或按下列规格重新录制并替换；未对外发送授权询问信息。

## 独立 ong 与替换录音交付规格

1. 普通话教学示范，独立发出韵母 ong；不要在前面带 d/g/h，不用 dong 裁掉声母，不拼接音节，不用 AI 读拉丁字母。录制 3 个自然重复版本供中文教学审核人选择，演示读法由审核人确认。
2. 原始 WAV 单声道 PCM16，44.1 或 48kHz，安静环境、无音乐/混响、无削波，起止保留短静音。保留未经剪辑母带；进入应用时统一转换为现有 24kHz PCM16 并记录改动与哈希。
3. 文件附作者/录音人、日期、录制内容、确切授权文本或许可证版本、允许的分发/修改/署名要求。没有书面授权信息不进入正式包。
4. 审核独立 ong 与 ong 例字/例词的区别，绑定正确教学 ID；替换后运行课程/音轨测试并在两平台点读。缺音时现有界面仍明确提示，不用其他声音冒充。

## AI 听审资料

模型 AISHELL-3 VITS 固定 revision `57345c004e13ed640e408c4c29ab56187b18f065`，默认界面 067 = 模型 speaker 66，8kHz。174 音色信号检查通过只证明可合成；当前没有“已听审推荐音色”。

`ai-listening-corpus.csv` 是固定工程句及检查重点。以默认 067 和对比音色 011 开始；原生 probe 的 `--review-samples` 在私有 TEMP 生成逐句 WAV 和哈希清单，供人工播放审阅。每句检查字词是否遗漏、多音字/轻声/数字是否正确、爆音、断句和听感疲劳。不要把 AI 生成音标作真人录音。

人工拼音目前不控制模型。若与教材标音冲突，优先使用已审匹配录音并登记模型限制；不能仅把标音改错来迁就模型。正式提供的 174 音色继续在 C 阶段听审，不因默认音色通过而全部通过。


</details>
