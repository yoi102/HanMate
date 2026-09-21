# 音频与其他资源实查 · 2026-09-17

本次为开发素材选择与来源核查，不是教学终审。App 仍完全离线；网络请求只发生在开发机的显式资源生成步骤。

## 音频

| 来源 | 查到的许可/来源事实 | 当前选择 |
|---|---|---|
| [Wikimedia Commons](https://commons.wikimedia.org/) 的 Zh-带调音节.ogg | 使用 API 的逐文件 author、LicenseShortName、LicenseUrl、descriptionurl；161 个例字读音中找到 109 个精确命名文件，75 个 CC BY 2.0 fr、33 个 CC BY-SA 3.0 us、1 个 CC BY 2.0 | 收录实际下载且可解码的 6 段；保留原许可与署名，教学匹配 needsReview |
| [zispace/hanyu-pinyin-audio](https://github.com/zispace/hanyu-pinyin-audio) | README 汇集网站资源且注明“仅供参考”，没有足以覆盖录音的明确许可 | 不随包分发 |
| [hugolpz/audio-cmn](https://github.com/hugolpz/audio-cmn) | README 列 ChenWang 音节与 YueTan 词汇，称 CC-by-sa 但未明确版本；原 packs.shtooka.net 下载地址本次 DNS 失败 | 保留候选；不把仓库描述当逐文件授权。树快照提交 ff9ed3d0c631195bd2c06f39450f3264c7124040 |
| [davinfifield/mp3-chinese-pinyin-sound](https://github.com/davinfifield/mp3-chinese-pinyin-sound) | 根 Unlicense 未解决录音原作者及来源证明 | 不随包分发 |
| [byhow/yanyu](https://github.com/byhow/yanyu) | 根 MIT 未解决音频来源及适用范围 | 不随包分发 |

实际 6 段：ba1、ba2、ba3、ba4、pa2、pa4，覆盖 b/p 两项的可用例字示范。WAV 来源及署名见同级源码 `HanMate.App/Resources/Raw/Pinyin/NOTICE.txt`，每段原始/转换后 SHA256、时长、URL、改动说明见 course.json。原文件以 Ogg 保存于 `tools/resource-audit`；转换为原采样率、单声道 PCM16 WAV，转换件继续使用原许可。

不得把这一小样本称为“拼音音频齐全”。161 个独立例字仍缺 155 段：52 个未找到该精确读音文件，103 个已找到元数据但未取得文件。Commons 下载端返回 HTTP 429 / Retry-After 600 秒；生成器记录全局冷却时间并停止后续请求，没有用并行重试绕过限流。`tools/resource-audit/missing-audio.json` 是逐项缺口清单。

文件名与声调匹配用于候选筛选，不能代替试听。部分录音原词与展示例字为同音字，页面明确说明；仍需核对普通话读音、声调、噪声、音量及教学一致性。当前不把完整例字音节称为正式声母本音/呼读音。

## 字典与字音数据

| 来源 | 实查结论 | 用途及边界 |
|---|---|---|
| [CC-CEDICT 官方 Wiki](https://cc-cedict.org/wiki/start) | 明示 Creative Commons Attribution-Share Alike 3.0；证据文本保存为 tools/resource-audit/cc-cedict.txt | 中文—英语查询字典候选；不是中日字典，也不是上下文注音真值。本次未嵌入整个词典 |
| [Unicode UCD 17.0.0](https://www.unicode.org/Public/17.0.0/ucd/ReadMe.txt) 与 [Unicode License V3](https://www.unicode.org/license.txt) | 版本目录及版权/许可条款已保存；允许按条款使用、修改与分发，需保留版权许可告知 | Unihan 字级读音/属性候选；本次未下载、转换或收录完整 Unihan，不宣称已实现分词或词义 |
| pinyin-data / phrase-pinyin-data | 继续使用 W0-03 已固定提交的审计结论，未擅自升级 | 已有离线候选原型；逐来源许可及准确率仍待关闭 |

没有采集商业词典释义、教材音频或现代译文来填补缺口。日英教学译文仍是开发草稿；正式语料规模和审核要求保持文档 16 的目标。

## 可复现输入

`tools/pinyin-examples.tsv` 为例字/译文草稿源，`tools/build_pinyin_course.py` 生成 63 项、161 个真实 ContentDocument 及媒体清单。`commons-metadata.json` 保留这次逐文件 API 响应。默认使用缓存、无网络；`--fetch` 明确刷新元数据，`--download` 明确下载缺失音频并遵循冷却。

`tools/build_starter_catalog.py` 从规划包原有开发样例生成不同资源/正文/子对象 ID 的 learning.zip（20 条：15 词、3 语法、1 文、1 诗）及 dictionary.zip（3 条）。保留原始来源与 draft 状态；原规划样例和归档未重写。它们与拼音静态练习数据是不同目录，不能把拼音例字重复计入 SQLite 学习库。

## 同日后续：注音字音正式接入

上表Unicode行记录的是早先未下载时状态。本轮已下载官方Unihan17 ZIP并检查Readings文件头及完整License V3；按kMandarin/kHanyuPinyin生成44,350字/55,328读音，当前解析器接受44,347字/55,301读音，27条特殊形式显式报告、不近似替换。另有24条原创上下文种子，均needsReview。许可/来源manifest随Infrastructure嵌入，源码生成离线可复现。

ZIP SHA256为f7a48b2b545acfaa77b2d607ae28747404ce02baefee16396c5d2d7a8ef34b5e；完整派生哈希、范围、测试见W3-02-W3-03-W3-07-annotation-editor.md。本次未新增音频，仍6/161；不把字音数量当成真实录音数量或教材准确率。

## 2026-09-18增量

新增取得Commons的ma2/ma3原OGG，按已有逐文件SHA1核验，生成PCM16 WAV/署名及SHA256。已取得8/161段，153段缺失；音文匹配/译文和终审仍待完成。ma2为Yue Tan/CC BY-SA 3.0 US，ma3为Wei Gao/Vion Nicolas/CC BY 2.0 FR。串行有界下载随后收到429，记录全局Retry-After并停止；本轮未绕过冷却继续尝试。工具新增`--download-limit`（0—50、默认12），缓存仍可离线生成。应用端没有下载器或新增联网能力。详见W3-W4-compressed-speech-migration.md。
