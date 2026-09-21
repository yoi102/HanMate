# W0-03 · 离线注音候选验证

执行时间：2026-09-14。结果：PASS（技术路线与候选规则）；发布用数据许可和教学终审仍为 OPEN。

## 来源审计

本次在临时目录浅克隆并读取维护者仓库，没有把外部数据复制进应用或规划包：

| 候选 | 审计提交 | 文件与结果 |
|---|---|---|
| `mozillazg/pinyin-data` | `923b108dc5d45dee061324c011b478fb649f8b73` | `pinyin.txt` 44,435 字符记录，其中8,624条有多个读音；`kMandarin_8105.txt` 可作为常用单字回退候选 |
| `mozillazg/phrase-pinyin-data` | `cee0ed6e6e4898580cafd2bd5e3723e20b214aa0` | `pinyin.txt` 47,113行、47,111个唯一词组、2个多行读音词组；词音数量检查0处不一致 |

两个仓库根 `LICENSE` 均为 MIT。固定审计哈希：

- pinyin-data `LICENSE`: `7EF92F09FEA8048C816A36994E8DC31A91D6C21E9D23A4B7411228846E618EDA`
- pinyin-data `pinyin.txt`: `5872B052347484B79D1EE8C558627C9BA5ED41D55AABB0C53026B627740400FE`
- pinyin-data `kMandarin_8105.txt`: `306CE2E64ABFE37878EB370A097B948A11A05769A9EA50D516DE1895B3E13E34`
- phrase-pinyin-data `LICENSE`: `2575ED9E21C00A0D5A1640146B449F306998A3D45CD0E15B96EF724E8BA673F6`
- phrase-pinyin-data `pinyin.txt`: `DFF030D54E9C9BA48D187FBA037D00AF410F01C9A867528DB6899F539F6E86F7`

聚合文件还注明 Unihan、汉典和 CC-CEDICT 等上游来源。根 MIT 文件不能替代逐文件来源与再分发审查，因此本工作包只锁定技术候选；正式收录、归属说明和发布许可仍由 INIT-03/W5-02 关闭。

## 已实现原型

- `PinyinSyllableParser`：解析带调、数字调、`v/u:`、轻声与儿化，拒绝重复/冲突/越界调号。
- `DeterministicPinyinCandidateEngine`：使用文本元素范围和 code point 识别汉字；人工锁定范围不可被词组跨越。
- 选择顺序固定为人工锁定、已审核短语、最长词组、显式来源优先级、单字回退。
- 同一最佳层级和优先级存在不同读音时，保留全部候选、`Selected=null`、`NeedsReview`；不随机确认。
- 未审核词组或单字即使只有一个结果也标 `NeedsReview`；未知字返回 `Unknown` 和空候选，不用轻声冒充。
- 标点、Emoji、拉丁字母和数字原样形成 `NotApplicable` 段。

审计数据中真实的同词多行例子为 `朝阳: zhāo yáng / cháo yáng` 和 `那些: nà xiē / nèi xiē`，验证了保留候选的必要性。

## 可解释误音与边界清单

| 场景 | 基线可能结果 | 必须处理 |
|---|---|---|
| `银行` 缺词组命中 | 单字回退会把 `行` 读成 `xíng` | 词组命中，否则待复核 |
| `朝阳`、`那些` | 数据本身有同级多读音 | 展示候选，不自动 confirmed |
| `得/地/的` | 最长匹配仍不能覆盖任意句法 | 已审核短语或人工复核 |
| 姓氏、人名、地名 | 常用字音可能不是专名读音 | unknown/needsReview，不虚构上下文准确率 |
| 古诗多音字 | 现代词组优先可能不合诗义 | 正式诗词必须有审核覆盖 |
| 生僻字及扩展区 | 基础数据可能缺失 | 保留原文并输出 unknown |
| 儿化和词音数不等长 | 直接按数组 zip 会丢字或音 | 需要显式映射；当前词音数量不等即拒绝 |
| `ê`、成音节 m/n/ng 等高级形式 | 当前基础 parser 可能拒绝 | 保留为显式高级候选任务，不能改成常用音 |

## 验证

```text
dotnet test HanMate.Core.Tests/HanMate.Core.Tests.csproj -c Release
PASS 33/33
```

测试直接读取 `examples/pinyin-cases.json`，并覆盖银行/行走、音乐/快乐、重新/重量、长大/长度、真实同级歧义、锁定边界、未知字和非法词音数量。这里是纯逻辑技术验证；HM-T076 等三平台应用用例仍为 NOT RUN。
