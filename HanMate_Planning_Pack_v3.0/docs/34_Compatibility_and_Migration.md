# 34 · v2 文档迁移与协议兼容

> HanMate · 汉语小伴 · 文档 v3.0 · 2026-09-14
> 正式产品名已确认；这是开发规格与数据契约，不是已实现或三平台测试报告。


## 1. 版本对照

| 层 | v2.0 文档基线 | 本次 v3.0 | 策略 |
|---|---|---|---|
| 产品名称 | 暂定工程代号 HanMate | 正式 HanMate / 汉语小伴 | 更新 UI 和发行文案；不无授权改已发布 App ID |
| 学习类型 | word/text/poem | word/text/grammar/poem | 新增 grammar，不重解释旧内容 |
| ContentDocument/contents | schemaVersion=1 | schemaVersion=2 | 显式 v1→v2 内容转换器 |
| 包 manifest | schemaVersion=1，content/backup | schemaVersion=2，content/backup/resource | 老包先用老规则校验再转换 |
| 资源描述与状态 | 未定义 | schemaVersion=1 | 新增；不是 settings 中一串临时名称 |
| 音频/设置 | schemaVersion=1 | 保持 1 | 不需要仅因文档变更而改号 |
| 收藏 | schemaVersion=1 | 支持1/2 | ADR-107：v2新增独立字典引用；无引用导出v1 |
| SQLite 草案 | user_version=1 | user_version=2 | 数据保留型迁移，不能运行建库脚本覆盖旧库 |

文档版本是修改记录；没有实际已发布应用的证据。本包保留 v2 样例作为兼容夹具，不声称用户已使用该协议存了生产数据。开工需先检查真实仓库与已发布格式，存在不同格式时另写迁移。

## 2. 旧包读取步骤

检测 manifest 版本→选择独立 v1 验证器→验证原始文件哈希/白名单/范围/引用→在 staging 内转换→验证 v2 内存模型→生成正常导入计划→用户确认→事务写入。

不允许先把旧 manifest 版本改成 2 就忽略原哈希；不允许删除字段“直到 schema 通过”。保留源包 packageId/原始指纹和迁移来源，用于多次导入幂等。升级后新导出的包给新的 packageId，不冒用旧包 ID。

## 3. v1 内容映射

旧 word/text/poem、正文、原始边界、ID、锁定注音、译文、三个 revision 和来源保持原样，唯一强制变化为内容 schemaVersion=2。旧 word 的 role=headword/example 继续有效，definition 是新允许的可选角色。旧文本不生成 grammar 字段。

旧 builtinSnapshot 保持兼容，不全部自动改名为 personal。旧包没有安装资源登记，不推断某个标题属于新字典或下载包；resources.json 初始化为空登记。audio/collections/settings 按原协议验证后原样迁移。

## 4. 旧备份到新状态

添加空 `resources` 状态只能表示“旧包未携带安装资源登记”，不能表示已经把新机所有资源卸载。合并导入保留本机资源与启停；完整替换时由预览显式决定资源状态迁移范围。

旧备份中的内容快照和个人录音必须能恢复，不因新包系统不存在对应资源就丢弃。遗失的旧原始音频仍按声明的 catalogRef 提示，不能伪造已恢复。

## 5. 数据库 v1→v2

安全备份→维护门→事务迁移。旧 content 的 CHECK 枚举需支持 grammar/resource/retained；SQLite 重建受约束表时须完整迁移关联关系并执行 foreign_key_check，不能随意 DROP 父表造成级联删除。

旧 search_index 的 contentId 主键迁为 `(contentId,aliasOrdinal)`，旧行 aliasOrdinal=0；这是补齐旧搜索文档与 DDL 的不一致。新增五张资源表、import_operation 执行收据及相关约束；旧 draft/user_settings 新增 row_revision，content 新增 membership_revision，初值为 1，保留原收藏/音轨/设置。历史 import_receipt 保留，不伪造没有证据的旧 operationId。迁移成功才更新 user_version=2。

`specs/database.sql` 是**新建数据库的目标 DDL**，不是可以直接用于旧库的 migration SQL。应用已实现归档v1→当前v2迁移，并以归档DDL构造真实SQLite文件（含WAL）验证安全副本、数据保留及失败回滚；尚无已发布生产旧库证据。本次修订未发布的目标 v2 草案，若发现真实库已经采用复审前的 user_version=2，须根据实际结构另建下一版本 migration，不能因版本号相同跳过字段核对或直接运行新建脚本。

## 6. 资源更新与保留快照冲突

资源 ID 稳定且条目来源独立；同 ID/版本不同内容拒绝。上游改变条目且用户仍引用旧内容，先将旧内容派生为 retained 历史快照（全图新 ID），把旧依赖映射过去，再使用资源原稳定 ID 安装新版本。

卸载但没有新版替换时可以保留原 contentId 为 retained。重新安装时，先核对同一 resourceId/entryId/version，并仅在临时比较副本中将 retained.origin 还原为 resource 后核对内容指纹及嵌套身份；一致才可恢复原拥有关系。不能直接比较含不同 origin 的 contentFingerprint，也不能丢掉其他字段做宽松匹配。不一致按上一段迁移历史内容。不能把这两种场景混成“所有快照都必须改 ID”或“所有版本都抢同一 ID”。

## 7. 协议分派与未知版本

文件后缀仅用于选择器；manifest 决定真实类型。新类型 resource 按 resourceKind 分 learning/dictionary。更高未知 manifest/content/schema 版本拒绝，提示需要兼容版本，不能只取认识的字段导入。

老应用读取新 grammar 或资源包失败是正常不兼容，不应宣传 v1 客户端也可无损导入。需要分享给旧版用户时优先升级接收端；仅导出 TXT/音频是降级资料，不是完整可编辑内容包。

## 8. 迁移核对清单

版本检查、文件哈希、旧 ID 保留、textElement 与 UTF-16 映射、人工锁定、默认收藏夹唯一、音轨 targetId、grammar unit 引用、来源许可、撤下记录、优先级、missing 资源不假装安装、搜索可重建。

规划工具验证旧样例→新模型与目标DDL；应用专项已验证v1原包、归档DDL构造的SQLite文件升级及失败保护。没有对真实用户旧库执行升级；原生iOS、六方向迁移与完整176项仍NOT RUN。具体平台证据以tracking/TEST_MATRIX.md为准。

## 9. 既有代码升级顺序

先读取本版 AGENTS 和更新摘要；比对旧代码模型→落实 schema/SQL 迁移方案→增加 grammar/资源仓储→补 UI→接通分享/恢复→执行旧回归与新增用例。不要把本包跟旧包平铺混用，让 AI 随机选择两个不同规范。

所有 archive 文件仅供历史追溯，不作为当前实现真源。新增下载适配器时保持同一安装协议，不再建立第二套不兼容字典格式。


## 10. 已实现路径与安全边界 · 2026-09-18

StrictTextArchive保留原v1规范manifest指纹与archive SHA，先逐文件原始字节长度/哈希及归档schema检查，才转换contents集合和每个Document版本。v1 builtinSnapshot原样保留且重复导入可复用。未知更高版本、格式/范围/关联错误整包拒绝；PCM16以外媒体和catalogRef继续明确不支持，不能声称完整恢复这些输入。

LegacyDatabaseMigration先用SQLite BackupDatabase保存包含WAL的私有.v1-<UUID>.safety.sqlite；事务内暂存原表、以新约束复制完整公共列，旧行默认aliasOrdinal/membershipRevision/草稿和设置revision，保留旧收据/映射。内容先按旧schema验证，再转换并验证新图/重算指纹。foreign_key_check通过才设置user_version=2并提交；失败回滚DDL/数据和版本号，安全副本仍保留。未知额外表/字段拒绝，不猜测其迁移。

新resources.teachingItems是可选字段；旧包无字段保持已有教学目录。旧包空resources不卸载新机现有目录。完整替换若保留的旧教学还引用待替换快照，拒绝并建议合并；新包显式教学集合随正文映射，保障闭包。

## ADR-107增量兼容

本地数据库不增加表和user_version；旧user_settings JSON缺少dictionaryBookmarks时读取空集合，写入通过既有CAS保留其他字段。逻辑备份v2引用按新schema严格校验，v1按原结构读取。仅支持collections v1的旧应用应拒绝v2，不可降级后静默丢弃引用；需要移除引用才能导出v1。稳定来源ID用于重新解析当前字典，标题不参与身份匹配。测试覆盖旧包合并/替换、跨两次备份迁移、事务失败与陈旧预览。

