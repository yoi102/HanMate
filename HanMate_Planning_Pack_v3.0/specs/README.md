# 数据与接口契约

当前10份JSON Schema：content/contents为协议2；package-manifest为协议2；audio-index、collections、settings、pinyin-catalog沿用1；新增resource-descriptor/resource-state协议1和dictionary-entry限制模型。

- `content.schema.json`：四类内容、字音、语法结构、来源。
- `contents.schema.json`：内容集合。
- `dictionary-entry.schema.json`：复用content，仅word。
- `resource-descriptor.schema.json`：资源/字典元数据和稳定条目映射。
- `resource-state.schema.json`：备份的启停、优先级、撤下、缺失与保留状态。
- `package-manifest.schema.json`：content/backup/resource文件清单、计数、哈希。
- `audio-index.schema.json`、`collections.schema.json`、`settings.schema.json`、`pinyin-catalog.schema.json`：既有能力。

Schema结构合法不等于数据业务正确，必须补范围、ID、角色、音频引用、来源状态和资源拥有关系检查。schema中format是约束，验证器须开启；additionalProperties=false不允许夹带执行指令。

`database.sql` 为SQLite目标v2，21张表，新库建表/约束验证用途；不是无损升级脚本。现有库须单独实现迁移，外键逐连接启用。限制在limits.json集中管理，数值为产品初值，不是已测框架上限。

中文正文原文不做静默Unicode变换；display拼音规范化。Schema整数位置为文本元素，UTF-16边界表用于精确切片。读详规：数据07、协议14、资源31、字典32、迁移34。
