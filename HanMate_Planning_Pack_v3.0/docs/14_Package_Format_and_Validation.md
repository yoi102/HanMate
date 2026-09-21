# 14 · 分享、备份与可安装资源包协议

> HanMate · 汉语小伴 · 文档 v3.0 · 2026-09-14
> 正式产品名已确认；这是开发规格与数据契约，不是已实现或三平台测试报告。


## 1. 当前协议

文档 v3.0，包 manifest.schemaVersion=2，ContentDocument/contents.schemaVersion=2；audio/settings 保持 1；collections 无字典引用时为1，有字典引用时为2（ADR-107）；resource 描述与 resources 状态为 1。旧 v1 包只经独立兼容读取器转换，不能将版本数字改了后忽略原哈希。详见 34。

| 输出 | packageType | 用途 | 管理权 |
|---|---|---|---|
| `.hanpack` | content | 分享选中四类内容及音轨 | 导入个人资料，不安装资源 |
| `.hanbackup` | backup | 自用设置/收藏/个人资料和资源状态迁移 | 预览后合并或替换用户资料 |
| `.hanresource` | resource，resourceKind=learning | 可安装学习资料 | 显式资源安装/更新 |
| `.handict` | resource，resourceKind=dictionary | 可安装查询字典 | 仅 word 词条 |
| `.wav/.mp3/.m4a` | 无 manifest | 分享实际单音频 | 无正文/注音恢复语义 |

四种包为 ZIP 容器，扩展名仅帮助文件选择。首版不传嵌套 ZIP、可执行插件、数据库原文件或脚本。

## 2. 文件白名单

| 文件 | content | backup | resource |
|---|---|---|---|
| manifest.json | 必需 | 必需 | 必需 |
| contents.json | 必需 | 必需 | 必需 |
| audio/index.json | 必需，可空 | 必需，可空 | 必需，可空 |
| settings.json | 禁止 | 必需 | 禁止 |
| collections.json | 禁止 | 必需 | 禁止 |
| resources.json | 禁止 | 必需 | 禁止 |
| resource.json | 禁止 | 禁止 | 必需 |
| audio/64位小写sha256.wav/mp3/m4a | 仅所选音轨 | 按实际打包集合 | 资源所需音轨 |

除 manifest 外所有文件必须在 files 列表中恰好出现一次；禁止显式目录 entry 和未知文件。UTF-8 JSON，禁止重复属性、NaN/Infinity、注释、尾逗号、无效 Unicode。content 包出现任何全局设置或资源管理文件直接拒绝，不默默忽略。

## 3. manifest

`schemaVersion=2`；packageType；packageId(UUID)；createdAtUtc；producerAppVersion；contentCatalogVersion（可空，兼容来源说明）；counts；files；warnings。

counts 为 contents/folders/favorites/audioAssets/audioBindings/**resources**。content 的 resources/folders/favorites 为 0；resource 的 resources=1，其余管理计数为 0；backup 的 resources 为 resources.json 登记数。实际解析数量与清单必须一致。

files 包含相对 path、实际 byteLength、sha256，不含 manifest 自身。manifest 限 1 MiB；其他上限见 limits。packageId 标识一次分发文件，不等于跨版本稳定 resourceId。

## 4. 内容与音频闭包

contents 外层 `{schemaVersion:2, contents:[...]}`，所有 content/unit/segment/token UUID 包内全局唯一。词语恰好一个 headword，可有 definition/example；课文诗词恰好一个 body；grammar 的字段和 unit 引用按 30 校验。

分享资源条目时以 personal 导入视角序列化，保留 source.resourceId/version/entryId 作为来源，不携带安装所有权。既有 builtinSnapshot 为兼容/历史快照类型，不授权覆盖内置目录。资源包则必须所有条目 origin=resource，来源与 resource.json 完全对应。

audio/index 沿用 v1：assets/bindings/preferences。storage=package 必有真实文件、哈希和大小正确；catalogRef 是可缺失的明确版本引用。用户音频在完整备份必须真实打包；音轨 target 需从随包正文闭包解析。pref 指向同 target 的 binding。

源资源标准音频属于 catalog；resource包不得引用不在包里的外部目录音频：所有资源音频资产必须 storage=package，缺素材应不声明该资产并列 warnings。安装一个资源不偷偷下载依赖。

## 5. resource.json（安装描述）

字段 schemaVersion=1、resourceId、version、resourceKind、三语言 names、publisherId/name、descriptionZh、licenseIdentifier/permissionNotes/canShare/backupPolicy/reviewStatus、entries[]。

每个 entry={entryId,contentId}。entryId 资源内唯一，contentId=UUIDv5(resourceId,entryId) 且包内存在；条目集合必须与 contents 集合相等。字典所有内容 kind=word，学习资源可包含四类。resourceKind 在同 resourceId 生命周期中不可更改。

版本采用三段整数，序列比较按数值；相同 ID/版本但指纹不同拒绝。publisher/approved 等字段为声明，实际信任来源由安装管线判断，外部包不能以字段指定可信 bundled 身份。

## 6. resources.json（备份状态）

外层 schemaVersion=1、resources[]、retainedContentIds[]。每个资源记录完整 descriptor、descriptorSha256（H1 规范 JSON 的哈希）、payloadFingerprint（完整资源语义指纹）、payloadStatus=included|referenceOnly、enabled、priority、missingReason、removedEntryIds。

included 的 descriptor.entries 必须全部在 contents 中且来源/版本一致。referenceOnly 不谎称字节已包含，missingReason 必填；在目标设备恰好存在同 ID/版本/指纹可复用，否则保持缺失/禁用并提示。enabled 记录源设备偏好，不是允许缺失资源立即可用。

retainedContentIds 指向随包 origin=retained 内容；其 source 能说明历史资源版本。这类内容不应出现在任何 included 资源的 active entries 集合，防止同一内容既当前安装又历史快照。撤下 ID 在资源内去重，可包含上游已移除的旧 entry。

settings 仍为 v1；collections 支持 v1/v2，备份内所有收藏、最后使用夹和默认音轨需可解析。分享/安装包不包含这些全局数据。

## 7. 哈希与规范化 H1

文件哈希为文件原始字节 SHA-256。正文哈希为 target 原文的 UTF-8 字节 SHA-256。它们不先做简繁、换行或 Unicode 正规化。

语义比较使用项目规范 JSON `H1`：对象 ASCII 属性按 ordinal 排序；数组保留领域顺序（集合字段 scenes 在输出前按 ordinal 排序并去重）；无无效 Unicode、无浮点；整数十进制无多余前导零；UTF-8 无 BOM、无空白分隔；字符串仅转义引号、反斜杠、控制字符，常用控制符用 `\b\t\n\f\r`，其他 U+0000—001F 用小写十六进制 `\u00xx`，非 ASCII 字符原样编码。

contentFingerprint 对 ContentDocument 去除根 createdAtUtc/updatedAtUtc、三个 revision 计数，以及 token 的修改时间后计算；保留正文、拼音、锁定状态、译文、分类、稳定 ID 与来源信息。相同 ID 但标题/译文等语义不同也按冲突保留双方，避免合并时丢修改。

pronunciationHash 对目标相对位置的 `[start,length,base,tone,erhua]` 数组计算；未知项使用 null；标点没有虚构音节。锁定状态、注音来源和时间不进入读音哈希。

packageFingerprint 对规范化 manifest（files 按 path 排序）计算。重新压缩但 manifest/有效载荷一致可视为同一包；archiveSha256 另作诊断。哈希证明一致性，**不证明作者身份、版权授权或文件可信来源**。


## 8. 校验阶段

物理大小→ZIP entry/路径/限额→manifest 版本分派→严格 JSON/Schema→逐文件流式哈希/大小→实体数量→范围/带调显示→跨引用→grammar角色→resource身份/词典种类→资源状态闭包→来源与权限→只读计划。

JSON Schema 只涵盖结构和部分条件，不替代跨文件引用、hash、token还原、音频解码、资源信任和配额。Schema format 必须显式启用。开发验证器只验证示例，不是可直接移植上线的导入器。

## 9. 安全边界

只接受固定 JSON 和应用生成的 ASCII 音频路径。拒绝 `..`、绝对路径、盘符、反斜杠、重复/大小写碰撞、符号链接、NUL、尾随空格点、可执行文件及嵌套包。解压到私有暂存前先检查目录项，解压时仍累计实际字节，不能相信 ZIP 头。

默认 ≤512 MiB 压缩、≤1 GiB 展开、≤10,000 entries、单 JSON ≤64 MiB、深度≤32、单音频≤100 MiB，单资源≤10,000内容、安装登记≤100。超限报告冲突，不静默删音频或截断字典。

所有输入来源声明都不能执行代码或提升系统权限；SHA-256 检查一致性，不是签名、版权证明或作者认证。

## 10. 兼容与例子

本包提供四种实际 ZIP 样例：sample-content.hanpack、sample-backup.hanbackup、sample-learning.hanresource、sample-dictionary.handict；不含音频字节，全部草稿。它们验证文件与模型，不证明 MAUI 应用已实现导入。

archive/v2_baseline 保留真实旧协议样例。更高未知版本拒绝，不解析一部分字段就覆盖资料。版本转换和新旧数据库迁移详见 34。


## 11. 资源完整载荷指纹与音轨身份

`payloadFingerprint = SHA-256(H1({descriptor, contents, audioIndex}))`。contents为按contentId排序的{id,fingerprint}数组，fingerprint按本文件contentFingerprint规则生成。descriptor按H1整体规范化，条目顺序保留发布语义。

资源描述新增必填的 `audioBindingIds` 和 `audioDefaults`：前者明确属于发布载荷的绑定ID，后者记录发布者的targetId→bindingId默认关系。无录音时两者均为空数组。它们随descriptor_json持久化，不从用户当前音轨偏好推测。每个发布默认只能指向本资源已声明且同目标的绑定。

指纹中的audioIndex只取audioBindingIds声明的绑定及其资产，assets/bindings按id排序，preferences使用audioDefaults并按targetId排序。音频资产去除path/storage这两个传输位置字段，保留ID、字节哈希、编码等内容信息。备份目录改变不应产生一个新发布版本。发布绑定作为源数据不可由用户直接改写，用户确认/录制/默认选择属于独立用户层。

安装包内全部绑定必须被audioBindingIds声明，每个音频资产至少有一个绑定，audio/index的默认项必须与audioDefaults一致。备份合并audio/index可以额外包含个人跟读和用户默认选择；这些不纳入发布载荷指纹，不会因用户录了一段跟读就误判原资源已损坏。声明的发布绑定缺失时不能当成完整资源备份。

仅descriptorSha256相同不足以证明正文/音频未改变。installed_resource保存payload_fingerprint；相同ID和版本但该指纹不同拒绝，正常重新压缩ZIP不改变指纹。文件字节哈希、发布语义指纹及用户音轨引用分别验证。SHA-256不是作者身份认证，资源描述中的自我声明不是许可/信任证明。

## 12. W1-09 当前文本安装范围

2026-09-17：应用 `TextResourceInstaller` 已实现文本 learning/dictionary 资源的校验、预览和首次安装。仅接受 manifest.json、contents.json、resource.json、audio/index.json，后三者必须完整列入 manifest；音频资产/绑定/默认声明均需为空。原始流可不支持 seek，不解压至目标路径，暂存文件退出即删除。扩展名不决定包类型。

本阶段比 specs/limits.json 的最终产品上限更低：归档和总展开各 32 MiB、manifest 1 MiB、其他单 JSON 16 MiB；JSON 深度 32。100 个资源登记和单包 10,000 条仍适用；这不代表已通过最大容量性能测试。后续含音频阶段提高限额须补测，不默认承诺 512 MiB 包现在可安装。

内嵌协议 schema 使用 JsonSchema.Net 9.4.0，引用只从随程序编译的 schema 解析，不下载外部 schema。校验包含 UTF-8/重复键/Unicode/整数、白名单、实际长度/哈希、数量、UUID/来源/语法引用和字典类型。全部内容、播放目标、基础词条索引、资源登记、拥有关系、epoch 和操作收据同事务提交；取消或写入失败不留下半包。

资源 ID/版本相同且语义载荷一致时核查保存正文和条目闭包，不重复插入。后续已接入缺失资源的同包重装及retained图接回，详见31；已安装external纯文字资源的更高版本更新见31第16节，资源图冲突仍拒绝。个人包保留双方重映射见下节；媒体文件恢复仍待后续。

## 13. 当前文字内容包实现 · 2026-09-18

TextContentPackageCodec接入v2 content：完整四类ContentDocument、空audio/index，固定三个文件；origin只能是personal，不能借源声明安装资源。StrictTextArchive与文字资源共用32MiB归档/展开、1MiB manifest、16MiB单JSON限额，实际流长度、哈希、UTF-8/重复键/schema、全局实体UUID、grammar角色/引用、数量逐项验证；不向目标目录解压。读取最多10000条，UI导出最多100条；这些上限不等于设备容量性能验收。

导出包含完整已保存图和所有日英译文，不根据UI语言过滤；写完后重新读取严格验证，再交给私有分享区。正文及人工拼音不重新生成。含音频输入整体拒绝，manifest的TEXT_ONLY警告与UI共同说明当前载荷范围，不把排除的音轨算作迁移成功。

packageFingerprint按H1规范化manifest计算，files按path排序；重新压缩允许，同packageId不同fingerprint拒绝。archiveSha256仅作诊断。现有schema/DDL版本不变；后续WAV扩展见下节，v1转换及backup包仍未接通。

## 14. 已实现的WAV内容包扩展 · 2026-09-18

content读取路径增加精确的audio/64位小写SHA256.wav白名单；资源安装路径仍只接受纯文字。资产仅接受origin=user、storage=package、PCM16 WAV，拒绝catalogRef、MP3/M4A及标准音轨。单文件16MiB、归档/实际展开32MiB保持不扩大，最多1000资产/绑定；导出为manifest预留1MiB，音频合计最多30MiB。超限整体拒绝，不删除部分音轨后输出残包。

音频索引必须通过schema、全局UUID唯一、真实WAV容器/字节数/时长/采样率/声道/SHA256、资产被引用、媒体文件恰好匹配资产路径、目标为随包unit或speech segment、preference目标一致校验。confirmed绑定的文本/读音哈希必须等于随包目标；needsReview保留旧哈希和状态，不自动升级。多个asset相同字节只存一个物理文件，逻辑asset/binding仍保留。

导出仅包含用户明确选中的已保存本人录音及相关preference；未选音轨与其默认引用都排除。导入将音轨标为imported，不能把他人的声音当作接收方本人录音。此扩展不代表资源音频安装、完整备份或全部压缩格式已经实现。

## 15. 逻辑备份包实现 · 2026-09-18

BackupPackageCodec沿用StrictTextArchive和PackageAudio验证v2 backup的六个JSON/manifest文件及PCM16 WAV。复用归档/展开32MiB、manifest 1MiB、单JSON/音频16MiB、1000音轨/资产限额；本地导出也在读取正文时限制体积。内容、收藏夹和资源UUID、默认夹唯一性、收藏/最后使用夹引用、资源descriptor hash/UUIDv5/版本/条目、retained集合及完整发布载荷指纹都校验。用户音轨覆盖层不参与资源发布指纹，资源声明的标准绑定与默认必须完整存在。未知格式、catalogRef音频和MP3/M4A当前整包拒绝，不伪造解码通过。

settings v1的reading新增可选整数scalePercent（100—200），保留现有必需fontSize供旧夹具使用。当前应用使用百分比字号，导出保留该整数避免150%等设置往返丢失；没有该字段的输入按fontSize/20约束到100%—200%。本项目尚未发布此协议，旧样例仍合法；不宣称外部旧客户端已支持新增字段。

resources支持included与referenceOnly。完整资源校验正文、发布音轨和发布默认；受限、缺失或已改变的资源记录referenceOnly及原因。收藏/用户音轨依赖的资源正文必须以允许备份的retained快照携带，许可不明则阻止导出。需要的用户音轨不可丢弃；草稿、回收站和缓存明确不在本次已保存有效资料集合。许可判断仍是来源声明，不是新增版权认证。


## 16. 资源WAV、旧包分派与教学备份 · 2026-09-18

以上实现阶段记录按时间演进；当前resource读取共用StrictTextArchive/PackageAudio，支持标准catalog来源PCM16 WAV。descriptor.audioBindingIds必须精确覆盖发布bindings，audioDefaults与index默认一致，target读音hash和真实WAV元数据/哈希全部验证。resourceId也进入全局UUID闭包；安装后文件为不可变哈希路径。payloadFingerprint沿用规范发布索引（资产不含path/storage）与发布默认，不能以本机用户默认改变资源身份。

resource-state v1可选teachingItems（最多200项）保存现有pinyin-catalog item契约。每个item ID与全部内容/媒体/文件夹/资源ID唯一，例字content/unit/token所属、声调和文本元素高亮合法；demoAudioAssetId必须对应例字的标准音轨。新备份写入该集合，即使为空；旧包缺字段表示未携带教学状态。恢复按全图映射已知字段，重复相同语义的条目复用，不猜改注记文字。

v1分派必须先校验原始哈希与独立归档schema，转换后再运行当前业务验证。32MiB归档/展开、16MiB单文件、1000音轨与100资源上限不变。MP3/M4A/catalogRef仍拒绝，不静默丢媒体。

## 字典引用扩展（ADR-107）

collections.schemaVersion=2必须带dictionaryBookmarks数组，每条仅provider、entryId和title；禁止额外字段，不含释义/音频。v1禁止该字段，新读取器继续接受v1；无引用的导出仍为v1。manifest.counts.favorites为items与dictionaryBookmarks数量之和。默认库正文及启停选择仍不随个人备份复制。示例见examples/collections-dictionary-v2.json。

