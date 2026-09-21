# W4：逻辑备份、增量恢复与分享闭环

日期：2026-09-18。W4-01/02/03/08转DONE，26/48→30/48；四项原验收措辞保持。实现先落地，再集中测试。无新增NuGet依赖或数据库DDL版本变化；settings v1新增可选scalePercent，见ADR-078—081。

## 交付

- BackupPackageCodec复用StrictTextArchive和PackageAudio，校验六个固定JSON/manifest文件及PCM16 WAV。验证数量、全局UUID、完整四类图、语法引用、收藏/默认夹/最后使用夹、资源UUIDv5/descriptor hash/发布内容音轨指纹及retained闭包；无外部Schema下载，不向外部路径解压。32MiB包/展开，16MiB单JSON/音频，1000资产/音轨及100资源登记保持有界。压缩音频和catalogRef当前整体拒绝，不静默丢弃。
- BackupExportStore在同一事务捕获正文、音轨、收藏、设置和资源状态，并登记实际文件export租约。文件逐个校验，finally释放；预览持有不可变有界字节，后续编辑不混入。每次生成新packageId；生成包先严格回读，再进入ShareFileStore的七天私有交付目录。
- 有效个人/历史快照、全部关联用户WAV、人工拼音与译文、收藏夹成员/顺序、语言/阅读/音频设置、许可允许的完整资源及标准WAV可携带。资源缺失/受限/已改变时明确referenceOnly，用户必须接受缺口；收藏和用户音频依赖的正文必须携带retained快照，无许可或文件坏则整体拒绝。未保存草稿和回收站明确计数排除，pending录音阻止预览。
- BackupImportStore复用现有全图冲突映射及同事务提交。预览后重验epoch与设置/收藏/正文/音轨/资源/草稿/映射实际状态摘要；正文、语法引用、音轨/默认、收藏、设置、资源/retained和操作收据一次提交。旧内容不覆盖，回收站不复活，已改动副本再次恢复则保留双方。同operation幂等，新恢复重验映射。
- 默认夹映射唯一默认；同ID保留本机名称，不同ID同名保留双方并限制名称文本元素长度。成员去重、已有本机顺序和默认音轨优先。设置默认不应用；明确选择才更新，并刷新语言服务。备份保留user/imported/standard角色，普通内容分享仍统一导入为imported。
- 资源初次安装需完整载荷/身份无碰撞，不允许外部备份取得bundled信任。相同已安装资源复用并保留本机状态；版本/载荷冲突保留快照、不降级原资源，缺源记录禁用。备份资源PCM16恢复有专项证据；独立媒体资源包安装/更新/卸载与教学UI闭环仍属W4-07。
- 设置页新增“备份与合并恢复”，支持预览/生成/系统分享、选择备份/预览/确认增量恢复和取消。四类内容多选与单个本人WAV分享沿用已有白名单，新增实际单音频字节/拒绝外来音轨回归。私有文件生成、分享面板打开均不宣称外部接收或另存成功。

代码入口：Packages/BackupPackage.cs、BackupSnapshot.cs、BackupExportStore.cs、BackupImportStore.cs、ContentPackageImportStore.cs、ContentAudioImport.cs、ShareFileStore.cs；App/Pages/BackupPage.cs和设置/本地化服务。工程夹具生成器tools/build_backup_fixture.py。

## 自动验证

- Core Release198/198 PASS；Infrastructure Release212/212 PASS，共410项；备份及单音频新增24项。末次正文体积/总资源登记限制调整后BackupTests24/24再次通过。TRX为Core.Tests/TestResults/backup-core.trx、Infrastructure.Tests/TestResults/backup-infrastructure.trx及backup-focused.trx。
- 覆盖四类/人工拼音/语法引用、实际WAV、角色/默认/收藏/设置往返；同名夹、同根及嵌套冲突、重复/删除后再导入、设置/夹/草稿并发、写入收据前故障全部回滚、孤立文件重试、坏文件租约释放、取消、pending/草稿/回收站边界、资源完整载荷/标准WAV/撤下状态、不同版本不降级、referenceOnly明确接受、10类非法备份、单录音输出字节与分享拒绝外来音轨。
- 三语言451键，键集/非空/数字占位符一致。
- Windows Release及Windows宿主iOS simulator managed最终0警告/0错误。iOS managed不是Mac/Xcode、签名或原生运行证据。构建日志TEMP/hanmate-backup-{windows,android,ios}.log。

## 平台运行证据

Windows Release真实进程/UIAutomation：Settings.Backup进入、预览23条正文/1夹/0收藏/0音轨/2完整资源/0草稿回收站；生成文件显示Verified backup created。未尝试外发；Windows备份导入/接收端和完整键盘读屏仍NOT RUN。本轮自己启动的进程已关闭，没有结束其他应用。

Android16/API36模拟器覆盖安装、保留原库和ja/150%。初次预览108内容/1夹/2收藏/14音轨（1612.4KiB）/7完整资源/1草稿排除；备份生成成功，系统分享面板显示实际.hanbackup文件后取消，未发送。导入独立backup-smoke.zip，5条Backup-*、10条合成音轨、1新增夹及5收藏成员；原资料保留。重复预览新增0/复用5/音轨新增0/新夹0。合成5秒440Hz音只验证工程行为，不证明发音或教材审核。

应用设置后原生控件复用曾导致页面空白：数据已提交，重建布局时控件仍有旧父级；修复为先解除父级再构造新布局。设置入口名称改为备份与合并恢复，只有确有资源缺口才显示接受部分资源复选项。

## 未完成边界

W0-05系统另存/真正接收端与三平台完整交付、W3-05 MP3/M4A及录音门禁、W4-04完整替换/安全备份/崩溃恢复、W4-07媒体资源更新卸载/教学目录、W4-09旧包旧库、六方向迁移、满磁盘/强杀/大库手机性能、iOS原生/真机与176项完整验收仍未完成。草稿/回收站不是本次备份集合；文件系统与SQLite不宣称单事务。正式拼音录音6/161不变，无开麦克风、联网同步或外发。

## 最终构建与复验

Android Release完整Rebuild最终PASS，0警告/0错误。APK SHA256=af814867ed48fe1e9da4e9dac87acd4a86572064ba3cceef50db916ce1295ac9。覆盖安装后勾选应用设置再恢复工程包：新增0/复用5/音轨新增0/新夹0；页面和结果提示正常，空白页问题已修复。Backup-smoke夹仍5条，原默认夹2条仍在；打开Backup-word显示150%，词头原生播放1/1至完成。

最终再次预览113正文/2夹/7收藏/24音轨/7完整资源/1草稿排除，音频去重1612.4KiB，生成文件成功；最终停留备份页面。原有资料未删除，工程5条正文/10绑定/1夹/5成员保留，无真实麦克风或外发。Windows本轮启动PID31848已通过CloseMainWindow退出。

已查看TEMP/HanMate-backup-smoke的preview.png、repeat.png、favorites.png、playback.png，页面可读；preview/import为同轮首次APK，repeat/favorites/playback/final-preview为最终APK。import.png与final-preview.png另作为原始截图留存，未将未查看截图当作视觉验收。全部UI smoke仍不等于全平台验收。
