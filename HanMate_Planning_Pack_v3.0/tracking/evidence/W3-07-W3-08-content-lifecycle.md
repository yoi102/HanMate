# 复杂语法与内容生命周期

日期：2026-09-18。用户授权连续推进多个阶段，测试集中收尾。四项本地流程实现并验证：复杂语法编辑、个人回收站、资源单条撤下/恢复、保留式卸载及同包接回。W3-07/W3-08仍IN_PROGRESS，分别保留分享准备和跨版本更新保护；整体21/48 DONE，不等于首版全部验收。

## 实现

- GrammarEditing按unit UUID映射；未改文字保留完整tokens/segments/锁定，变化仅在该单元内计算注音差异，失去人工项先确认。表单支持Literal/Slot/Operator、说明/例句/注记、日英译文、主题、添加移除与同类上移。总文字20k元素、1MiB，说明10/例句20/注记10上限。无效引用/角色/译文/Unicode拒绝。简表返回后不会重生成截断复杂图。
- EditorCommitStore正式保存继续使用正文/草稿并发守卫、schema和事务；删除音频target同时保护audio_binding与audio草稿中全部引用，不先删除目标再重绑。
- ContentTrashStore使用contentTrash.v1内部draft marker，保存原图与全部依赖。预览与提交核对正文revision、membership、epoch和依赖计数；隐藏学习/搜索/收藏，阻止正文/收藏/音轨目标变更，恢复移除精确marker。移入/恢复均推进revision，旧草稿可另存，原文编辑需重新打开。无物理删除/空间回收，不声明已可完整备份。
- ResourceManagementStore扫描收藏、音频、草稿嵌套UUID、教学、导入映射、已修改和保留内容。预览保留/删除，提交重扫；受引用图原位retained、保存来源及原因，其余删除，同事务收据/epoch。源级依赖保守保留全包。
- TextResourceInstaller同包重装仅接回来源resource/entry/version和归一化origin后完整语义指纹一致的retained图；保留已有ID/目标/音轨/收藏/偏好，任何冲突整笔拒绝。新旧资源不能借同ID覆盖个人副本。ResourceEntriesPage50条分页撤下/恢复，验证entry实际存在。

## 集中自动验证：PASS

1. `dotnet test HanMate.Core.Tests/HanMate.Core.Tests.csproj -c Release --no-restore`：188/188。
2. `dotnet test HanMate.Infrastructure.Tests/HanMate.Infrastructure.Tests.csproj -c Release --no-restore`：117/117。新增加10 Core与15 SQLite测试，包含故障触发器事务回滚和收藏/音频/草稿/教学/修改内容的卸载重装。
3. Windows net10.0-windows10.0.19041.0 Release、Android net10.0-android Release完整Rebuild、Windows宿主net10.0-ios managed编译：0警告/0错误。iOS不是Mac/Xcode/原生运行证据。
4. 359个应用resx键在简中/日/英一致、非空、数字占位符一致。

构建日志：开发机临时目录hanmate-lifecycle-windows.log、hanmate-lifecycle-android.log、hanmate-lifecycle-ios.log。最终Android APK SHA256：`b70113e384860983beada3fccf2b0ae6a1c7d21e2bc79516ecd00a29cd332337`。所有dotnet命令串行。

## Android模拟器实际操作：PASS

emulator-5554，Android16/API36，1080×2400；Release覆盖安装，不清库，宿主麦克风保持关闭。

- 原有Grammar-smoke进入完整结构表单，添加Lifecycle-note注记和negation主题，保存草稿→校对→正式保存，自动打开正式详情。覆盖安装最终APK后仍可读新增注记和原有说明/例句。
- Grammar-smoke移入回收站，预览四类引用均0；语法列表7→6，不再出现；草稿→回收站可列出，恢复后回收站为空、语法列表回到7。原文与新注记保持。
- 工程61条搜索字典的“你好”临时加入默认收藏，撤下后管理页显示撤下状态与恢复入口；卸载预览明确保留1/删除60，确认后登记0条、缺失、优先级17、撤下1。
- 草稿→卸载后保留的内容可打开“你好”，正文、拼音及例句可读；原ZIP从系统文件选择器重新安装61条，收藏仍勾选、优先级17、撤下1保持，retained列表清空。最后恢复该条目并移除临时新增收藏，既有其他收藏/音频保持。
- 最终停在Grammar-smoke阅读页，150%偏好，注记可见。截图已人工查看：开发机临时目录hanmate-lifecycle-grammar.png、hanmate-lifecycle-trash.png；卸载预览另存hanmate-lifecycle-retain.png。

## 未完成与限制

分享闭包/导出、跨版本升级/旧依赖重映射、音频包安装、完整备份/回收站迁移、物理音频GC未实现。回收站不释放磁盘空间，撤下仅隐藏；不伪装为彻底删除。

Windows新界面交互、iOS原生、Android真机、完整IME/软键盘/访问性、10k依赖预览手机性能、满磁盘/强杀窗口、176项完整验收NOT RUN。MP3/M4A/TTS与真人录音验证仍待后续。本轮无新增教材或正式音频，正式示范仍6/161，缺155段。
