# 07 · 数据模型、持久化与版本策略

> HanMate 文档包 v3.0 · 2026-09-14 · 状态：开发基线提案，非已实现报告。
> 正式名称：HanMate / 汉语小伴；R01—R22 为需求基线，其余明确标注的规则为可调整默认设计。

## 1. 数据真源与派生数据

| 数据 | 唯一真源 | 可重建内容 |
|---|---|---|
| 词语/课文/语法/诗词、注音、例句、译文 | ContentDocument 版本化 JSON | 列表摘要、搜索字段、播放目标投影 |
| 用户收藏和设置 | SQLite 关系表 | 页面显示状态、启动缓存 |
| 用户音频文件 | 不可变音频存储，按 SHA-256 定位 | 波形/试听缓存等 |
| 音轨绑定及默认选择 | audio_binding / audio_preference 表 | 当前播放策略缓存 |
| 内置内容源 | 应用随附的经过审核目录包 | 运行时 SQLite 内置投影 |

`content.body_json` 为完整内容文档；表中的 title/kind/revision 等是查询投影，由一次事务同步生成，不能独立更新。全文、拼音不再存进 `.resx`。音轨绑定只在音频关系表/包 `audio/index.json` 中维护，不同时以另一套可编辑字段嵌入 ContentDocument。

## 2. ContentDocument

关键字段：`schemaVersion=2`、UUID `id`、`kind=word|text|grammar|poem`、`origin=personal|builtin|builtinSnapshot|resource|retained`、`title`、`contentRevision`、`annotationRevision`、`metadataRevision`、`difficulty`、`schoolStage`、`grade`、`scenes`、`source`、`textUnits`、时间。

一条 word 内容表示一个明确读音/义项；同字不同音义为不同 contentId，不再使用可空 senseId 参与收藏唯一性。这是首版简化规则，不阻止将来增加词头聚合。

| TextUnit.role | 数量/要求 | 用途 |
|---|---|---|
| headword | word 恰好一个 | 词语头部发音与注音 |
| definition | word 可零或多个 | 中文释义与辅助译文；字典复用 |
| example | word 可零或多个；grammar 至少一个，默认上限 20 | 例句，各自独立音源 |
| body | text/poem 恰好一个 | 正文和句段阅读 |
| grammarExplanation | grammar 至少一个 | 语法中文用法说明 |
| grammarNote | grammar 可空 | 中文注意事项 |

每个 TextUnit 都有 UUID `id`、原样保存的 `text`、`offsetUnit=textElement`、`elementBoundariesUtf16`、`tokens`、`segments`、`translations`。标题不混入正文范围；例句不冒充根正文的一段。

## 3. 范围与字素

主范围仍为文本元素序号：`start`、`length`。额外持久化从 0 到原文 UTF-16 总长度的边界表 `elementBoundariesUtf16`，含结尾 sentinel；从该表得到 token 与 segment 对应的 UTF-16 片段。范围不得负数、越界、重叠或丢字符。

.NET StringInfo 可用于创建文本元素映射；ParseCombiningCharacters 返回的是 UTF-16 起点，不是文本元素序号。[S15](27_References.md) **持久化边界表用于校验和跨版本保护，不能在恢复时丢掉它后用新算法静默重解释旧整数。**

导入检查：边界单调、首项 0、末项等于 UTF-16 长度、不拆代理项，token.text 与范围完全匹配；再与支持的文本元素实现核对。不一致返回 `TEXT_BOUNDARY_MISMATCH`，在修改数据库前停止并给出纯文本另存/显式迁移路径，不自动移动人工校正。

正文保留原来的组合字符、换行与繁体；不对原文做静默 NFC/简繁转换。拼音 display 则规范为 NFC。常规汉字一字一个 token，儿化可以一 token 覆盖“花儿”。非汉字 token 的拼音为 null。

## 4. token 与 segment

Token：稳定 id、start、length、text、kind、pinyin、annotationSource、locked、reviewState。拼音包含 base、tone、erhua、display；base 保留 ü；tone=0 表示轻声；未知读音用 pinyin=null 且 reviewState=unknown，不用 0 冒充未知。

Segment：稳定 id、start、length、text、kind=speech|layout、boundarySource=auto|manual、translations、tokenIds。speech 为可点击朗读范围；layout 承载纯空白/换行/非朗读标点。**全部 segment 顺序连接必须还原 text，全部 token 同样必须还原 text。** token 不跨 segment；分段变化必须重新校验 token 列表。

只有包含实际可朗读文字的范围才能是 speech。词语例句的整句朗读目标是 unit，不受课文标点切段入口影响。

## 5. 播放目标和音频模型

所有可播放目标投影进 `playback_target`：ID 等于 unit、speech segment 或 pinyinItem 的稳定 UUID；owner 指向一个 content 或一个 pinyinItem；含当前 textHash、pronunciationHash 和 role。目标投影可由真源重建，但其 ID 不重新随机生成。

AudioAsset：id、sha256、relativePath、byteLength、durationMs、container、codec、sampleRate、channels、origin=user|catalog。文件相对路径由应用生成，不能来自原设备绝对路径。

AudioBinding：id、targetId、assetId、boundTextHash、boundPronunciationHash、reviewState=confirmed|needsReview、sourceRole=standard|user|imported。AudioPreference 单独保存 targetId → bindingId。复合外键保证默认音轨确实属于同一 target，不以一个全局“默认录音”串到其他片段。

修改标题/标签/译文不影响声音；原文或有效读音变化使相关绑定 needsReview。仅锁定状态或审核备注变化不应让未变的音频失效。

## 6. 收藏、设置和导入记录

FavoriteFolder：id、name、nameKey、description、sortOrder、systemRole。只有一个 systemRole=default；其名称由 UI 资源显示。普通夹名经 trim + NFC + 不受文化区域影响的大小写归一化生成 nameKey；同名拒绝，导入冲突用确定后缀。

FavoriteItem 的主键为 `(folderId, contentId)`，没有可空 senseId 陷阱。SQLite 唯一索引对 NULL 的行为必须明确，不能以为可空三列会自动实现业务去重。[S12](27_References.md)

UserSettings 用单例行存可迁移 JSON；设备专属 voiceId 不作为跨设备设置真源。ImportReceipt 记录 packageId、archiveSha256、提交结果；ImportMapping 记录原实体 ID、源语义指纹与本地 ID，确保冲突派生幂等。

## 7. 版本和哈希

| 变化 | contentRevision | annotationRevision | metadataRevision | 音频处理 |
|---|---|---|---|---|
| 修改正文/例句文字 | +1 | 受影响后 +1 | 不强制 | 对受影响目标复核 |
| 修改有效拼音 | 不变 | +1 | 不变 | 读音哈希变化的目标复核 |
| 仅标题/分类/译文 | 不变 | 不变 | +1 | 保持 |
| 锁定/解锁、不变读音 | 不变 | +1 | 不变 | 哈希不变时保持 |
| 修改默认音轨 | 不变 | 不变 | 不变 | 单独音频关系事务 |

`textHash`：对应目标原文 UTF-8 字节的 SHA-256。`pronunciationHash`：按目标内 token 顺序，以 `start/length/base/tone/erhua` 的规范语义序列计算；不含 UI 语言、display 冗余、修改时间、锁定标记。具体规范化见文档 14。

同一文档保存采用数据库 rowRevision 乐观锁；旧编辑器提交返回 `REVISION_CONFLICT`，保留草稿并让用户比较，而不是按设备时间强制覆盖。

## 8. SQLite 草案和迁移

可执行 DDL 见 `specs/database.sql`；它是设计参考和文档校验对象，不是已集成到 MAUI 的数据库实现。外键须在每个连接启用并检查，不能只在创建表时启用一次。[S13](27_References.md)

迁移流程：检测版本 → 取得维护门 → 安全备份 → 事务执行逐版 migration → 检查外键/内容投影/用户数据计数 → 提交 → 更新版本。失败保留原库，禁止 `DROP DATABASE` 式重建用户资料。

内容目录更新与用户数据迁移分开。更新内置内容前检查用户收藏/音轨依赖：稳定 ID 保留；删除项保留必要快照；读音变化使用户音轨复核。内置资料不是让用户覆盖的共享可写文本。

## 9. 文件一致性与清理

数据库事务不能同时原子提交文件系统；音频先暂存验证，再写为不可变正式文件，最后在 SQLite 事务中引用。SQLite 事务只能保证其数据库操作边界，外部文件需要应用级恢复协议。[S11](27_References.md)

GC 只清理无数据库引用、无导出租约且超过保护期的文件；不能用“清缓存”删除用户音频。磁盘不足、进程终止和数据库忙都要保留旧数据，具体步骤见文档 15。


## 10. 语法、资源和字典模型补充

`ContentDocument.grammar` 仅在 kind=grammar 必需，包含结构化 patternParts、patternTranslations、explanation/example/note 的 unit ID 列表和 topicCodes。每个引用必须属于本内容且 role 正确；它不是单独一份与 textUnits 相互冲突的例句正文。patternParts 没有播放目标，说明/例句才有。

| 实体/表 | 关键字段 | 真源与约束 |
|---|---|---|
| ResourceDescriptor | resourceId/version/resourceKind/names/publisherId/许可/entries | 包资源声明；发布者身份需独立核验 |
| installed_resource | descriptor_json、descriptor_sha256、payload_fingerprint、distribution、is_present、enabled、priority、row_revision | 安装/可用性；is_present=0 不得 enabled=1 |
| resource_entry | resourceId、entryId、contentId | 一个内容最多一个资源拥有者；dictionary 只能 word |
| resource_entry_override | resourceId、entryId、removed、更新时间 | 用户撤下状态；上游删除 entry 后仍保留 |
| retained_content | contentId、源resource/entry/version、reason | 保留收藏/用户音频所需快照；不进入普通列表 |
| resource_operation | operationId、resourceId、操作、epoch、提交结果 | 成功事务收据，辅助故障恢复 |

对于 is_present=1 的资源，ResourceDescriptor.entries 与 resource_entry 为真源/投影关系，安装更新必须校验相同集合；is_present=0 时登记保留历史 descriptor 与偏好，当前拥有关系为空；不能分别手工编辑。移除资源允许保留登记为 is_present=0、enabled=0，便于以后重装恢复撤下偏好；来源记录不是“已安装”的证据。

查词来源投影从安装状态、条目撤下与 content.kind 派生，不能只靠 search_index 有行就返回。search_index 主键改为 `(content_id,alias_ordinal)`，旧行 aliasOrdinal=0，修复旧 DDL 与 alias 文字设计不一致。

## 11. 新字段的版本与哈希

source 可以记录 resourceId/resourceVersion/entryId，作为来源而非覆盖授权；旧内容没有这些字段仍可经 v1 转换保留。语法 patternParts 改变记 contentRevision；说明/例句文本变化同时影响注音修订；仅翻译/主题标签改动记 metadataRevision。某个例句未变则它的 textHash/pronunciationHash 不变。

安装状态/优先级/撤下不改变正文和拼音哈希，而是更新资源 rowRevision 与 dataEpoch。恢复设置和资源状态后一起刷新页面/查询缓存。重复安装 identity=(resourceId,version,descriptor 与载荷语义指纹)，不只对比标题。

完整 schema、目标新库 DDL 和 v1 样例迁移位于 specs、archive/v2_baseline。新 DDL 并不是无损升级脚本，真实迁移另做并测试。


发布音轨身份由资源descriptor的`audioBindingIds`和`audioDefaults`固定保存；不能把用户后录音轨、改选的默认声音算入发布载荷指纹。此字段随descriptor_json迁移，完整规则见文档14第11节。

## 12. 并发版本与执行收据

草稿和设置使用 draft.row_revision / user_settings.row_revision，内容的收藏成员集合使用 content.membership_revision，初值均为 1。expectedDraftRevision / expectedRevision / expectedMembershipRevision 分别对应这些字段。保存时在同一事务执行带旧版本条件的 UPDATE 并自增，受影响行数为 0 返回 REVISION_CONFLICT；成员增删和版本递增同事务完成，删除夹导致成员变化时同样递增相应内容的 membership_revision。只改收藏不递增正文修订，不使音频失效。

import_receipt 继续按 package_id 记录原包指纹和首次导入结果，import_mapping 保留首次包关联；新增 import_operation 按 operation_id 记录本次 package_id、merge/replace 模式、计划 epoch、提交时间和结果。同包可有多个经明确确认的操作，同一 operationId 不可重复提交。当前操作的数据变更与该收据同事务提交，取消/回滚不留下成功记录。启动按 staging 的 operationId 查收据，不只查 packageId。资源操作继续使用 resource_operation。

本次只补齐目标 DDL 并做内存 SQL 约束/回滚检查；仓储、服务及真实旧库迁移仍需 W1-03/W4 实现。设置/收藏/音频包协议未新增运行时锁字段，它们不能从另一设备覆盖本机并发版本。重复恢复见文档 15 第 14 节。
