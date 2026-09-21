# 资源更新：音频草稿与教学引用迁移

日期：2026-09-18。承接外部纯文字资源升级，实现已知引用迁移、独立草稿判定与迟到录音收尾保护，再集中验证。W3-08/W4-07继续IN_PROGRESS，22/48 DONE不变。

## 实现

- ResourceReferenceMigration为计划和提交共用明确契约，不做任意JSON的UUID字符串替换。textDraft.v1/v2严格验证个人内容图、target及revision；独立当前/撤销图不引用旧资源实体时，source来源声明和正文中的UUID文字不构成迁移依赖，草稿原始字节/revision不动，也不为纯来源声明创建旧快照。实际资源图引用和未知格式仍拒绝。
- localAudio.v1仅允许ready草稿，验证字段、目标所属图、哈希与WAV元数据。只将Target.Id/ContentId映射到完整retained旧图；草稿ID、来源、文件路径/字节和读音哈希不变，revision递增，updated时间不动。过期读音不会被迁移自动确认；pending录音阻止更新。
- LocalAudioStore.FinalizeAsync在文件操作前读取原始body，写ready时以原body作compare-and-swap，迟到收尾不能覆盖迁移/修改后的草稿。旧页面保存被拒绝，重新读取后可继续操作。没有文件搬移、删除或GC。
- pinyin_item.body_json按现有pinyin-catalog.schema.json的单项契约严格验证，核对表字段、四声唯一性、可用状态闭包；验证例字content/unit/token所有权、声调及文本元素高亮。只映射example.contentId和highlight内已知ID，保留item/demoAudio ID、reviewStatus、注记文字和高亮范围。提交重新读取教学行，使跨两个旧图的同一项累计应用映射；未知字段/失效高亮/重复声调阻止整笔更新。
- 沿用epoch、依赖Guard和单一SQLite事务：创建旧快照与目标后迁移引用，再替换资源图，失败一起回滚。UI逐项预览和最终结果显示迁移的音频草稿/教学例字数量，三语言421键。
- 当前PinyinPage仍使用随附Pinyin/course.json；本轮只实现数据库规范引用的迁移，不是外部教学目录安装及UI接入。没有新依赖、DDL或schema版本变化。契约见docs/31 §17和ADR-072—074。

## 集中验证：PASS

dotnet测试/构建串行执行：

- Core Release：188/188。
- Infrastructure Release：177/177，本轮新增13项。覆盖ready草稿迁移/旧页面拒绝/重读保存及真实WAV读取、过期读音仍待复核、独立文字草稿字节和revision不变且仍可提交、教学变更/移除后旧图保留、UUID注记不替换、无效unit/highlight/未知字段/重复tone拒绝、真实pending录音阻止及finalize后计划失效、事务中途触发器故障回滚和重试、同教学行多图累计迁移、未知音频字段不丢失。
- Windows Release、Android Release完整Rebuild：0警告/0错误。
- Windows宿主iOS simulator managed编译：0警告/0错误；不代表Mac/Xcode或iOS原生运行。
- 简中/日/英421键的键集合、非空、数字占位符一致。

构建日志：开发机TEMP/hanmate-reference-migration-{windows,android,ios}.log。最终APK SHA256：`1ae3035b0fb74b5c1fee2b2d57143ce96a80c41d577934c26eca19321e91c91e`。

## Android16/API36模拟器：PASS

emulator-5554，1080×2400。保留既有库、日语和150%阅读偏好；未打开麦克风、未清库、未外发。

1. 在前轮独立工程资源Resource-update-smoke（16f71db8-7f8c-4ea1-b651-eb52fa7f1088）2.0.0的Update-smoke-v2主单元，从Download导入既有1秒440Hz工程WAV，留作ready草稿，未保存为音轨。
2. 覆盖安装最终Release APK，草稿仍在；选择update-3.0.0.zip升级，预览changed1/retained1/audio drafts1/teaching examples0，确认2.0.0→3.0.0成功。
3. 从学习→草稿→保留内容打开Update-smoke-v2，音频页仍显示可试听1.0秒草稿。试听后勾选默认并保存，草稿转成已保存imported音轨。
4. 点击适用音频播放，显示“再生が完了しました”；保存列表显示“読み込んだ音声 · 1.0s”和“既定・適用可能”。最终停在此音频管理页。

临时夹具和截图位于`C:\Users\yoiri\AppData\Local\Temp\HanMate-resource-update-smoke`：update-3.0.0.zip、references.png、reference-draft.png、reference-playback.png。三张本轮截图已查看，迁移数量、草稿控件及保存/播放状态可见。滚动区边缘裁剪属于正常滚动，不遮挡操作。工程v3资源、v2旧快照及保存音轨、前轮v1收藏继续保留，未删除既有内容。工程合成音不计正式教学录音。

## 边界 / NOT RUN

教学迁移与新版音轨隔离由SQLite测试验证，本轮模拟器没有教学目录引用夹具，也未再次检查新版音轨隔离。pending录音和未知/损坏引用安全拒绝；没有放宽为猜测迁移。

资源媒体安装/升级、外部教学目录接入UI、已卸载跨版接回、完整备份/恢复及自动GC仍未实现。正式录音仍6/161，本轮无新增教材审校或正式声音素材。

Windows新UI、iOS原生、Android真机、读屏/IME、手机10k更新性能、真实强杀/满磁盘、六方向迁移及完整176项验收NOT RUN；聚焦模拟器验证不替代这些验收。
