# W3-08 / W4-04 / W4-05 / W4-07 / W4-09 · 35/48

日期：2026-09-18。当前工作区无Git元数据。五项实现与受影响验证完成，不代表首版发行通过。

## 实现

- BackupReplacementStore：事务内模拟预览；同快照SQLite BackupDatabase及所有audio_asset实际文件安全复制；安全副本哈希/完整性复验；界面两次确认；未保存草稿/租约保护；正式数据和replace operation同事务；实际状态摘要+epoch防过期；新operation允许同备份再次恢复；新图revision高于旧库。已安装资源保留，备份准确携带的同版资源才恢复启停/优先级/撤下。
- StrictTextArchive/LegacyDatabaseMigration：原v1哈希/schema先验，保存原包身份，转换后再验证v2；builtinSnapshot不推测为resource。真实归档DDL文件v1→v2迁移含WAL，原图/锁定/译文/收藏/设置/草稿/收据/映射保留，默认新增revision及aliasOrdinal；失败回滚版本和DDL，未知结构保留拒绝。
- ResourceAudio与资源生命周期：标准PCM16 WAV原始字节/metadata/闭包校验，发布fingerprint规范化；不可变文件后发布DB引用；保留式卸载、同包接回、更高版重装保持停用。旧发布binding及默认/映射搬到新历史ID，旧图与教学/ready草稿/用户音频保留，新版不继承旧音频；不同字节不得复用asset ID。
- TeachingCatalogStore：本地教学JSON1MiB/200项限额、严格schema/四声/所属/高亮/示范资产校验，预览/提交守卫；拼音页与四声示例页使用本地匹配音轨。资源启停/撤下过滤，保留快照可读。备份可选teachingItems完整重映射例字/高亮/示范资产并复用重复语义。
- 三语言新增14键，465键集、非空及数字占位符一致；修正已过时的“媒体/替换未支持”界面说明。

## 自动验证 PASS

- Core 198/198；Infrastructure 232/232，共430项（本轮新增20项）。测试包括安全副本损坏/设置及收藏过期/活动租约/草稿阻止/删除后故障回滚和重试/同备份再次恢复/教学替换闭包及旧revision失效。
- 归档v1 content+backup实际ZIP输入成功、错误旧schema拒绝、builtinSnapshot保持且重复复用；真实v1 SQLite文件普通/WAL迁移，内容、外键、索引/旧收据及安全副本复验；坏图/外键/未知列回滚。
- 资源标准WAV安装及真实读取、含本人音轨卸载/同版接回、已安装和已卸载跨版、教学引用迁移/播放/备份往返、坏WAV及提交故障回滚、启停过滤与坏高亮拒绝。
- `dotnet run --project tools/HanMate.RecoveryProbe/HanMate.RecoveryProbe.csproj -c Release`：after-safety、after-clear、before-commit、after-commit四个真实子进程被Kill并重开；SQLite `max_page_count`触发真实SQLITE_FULL错误13。每场景复验integrity_check、foreign_key_check、old/new正文、operation收据、恢复判定及真实旧/新WAV，5/5 PASS。
- 故障报告：TEMP/HanMate-recovery-probe-874c57168fdb42689fff60ed74e3a9dc/report.json；测试目录保留。不是设备文件系统存储卷耗尽实验。
- Windows Release、Android完整Release Rebuild、Windows宿主iOS simulator managed：0警告0错误。最终文案修正后三目标重新编译；iOS没有Mac/Xcode/原生运行证据。

## Android实际交互 PASS

Android16/API36 emulator-5554，1080×2400。覆盖安装保留原库、ja与150%阅读偏好，无清库/麦克风/外发。

独立resourceId=98dd9460-7bb6-42ef-8130-de1a4c3cc5de；tools/build_resource_media_fixture.py生成Media-lifecycle-smoke 1.0.0/2.0.0及teaching.json。系统选择器安装1条内容和1条标准440Hz工程信号，教学导入确认1项后拼音页64项。四声页在“在 zài”位置局部标红，真实本地播放显示再生終了。

升级2.0.0预览changed1/retained1/教学例字1；最终确认后更新成功，回到拼音主页重新加载，再打开旧例字仍能播放完成。未对原有资源执行更新。工程条目/音频/旧快照/教学目录保留，不计正式教材；本夹具zai分组仅用于UI定位，不计教学审校。

截图/夹具保留TEMP/HanMate-recovery-smoke：teaching-playback.png、media-update.png。最终APK SHA256：8414b8fb7043b1342791bba2574c253ff5946637abfd9b099cfb7fe183e4212e。

最终文案修正APK覆盖安装后，新替换/恢复记录按钮与日语说明正常；实际预览115正文/2夹/7收藏/26音轨（1628.1KiB去重）/8完整资源/1草稿排除/0回收站，教学目录及历史快照随备份严格回读成功，生成文件成功。最后停在备份页，未调用系统分享或替换真实资料。截图final-backup.png，已检查布局。

规划包16组验证PASS，48任务依赖/证据及176用例原始状态一致；派生文件重新生成后核验。

## NOT RUN / 保留边界

真实用户数据的完整替换未执行；破坏性恢复与崩溃测试只用disposable库。Android/iOS设备卷满盘、全部OS终止窗口、Windows本轮新UI操作、原生iOS、六方向迁移、真机及完整176项验收未验。MP3/M4A/catalogRef、系统TTS、正式素材6/161之外的声音、自动GC/空间回收仍未完成。

包上限32MiB/单文件16MiB保持；教学JSON最多200项/1MiB。含媒体更新采取保守历史保留，可能增加磁盘使用；安全副本、.part与孤立文件不会自动删除。用户继续授权不等于可以在真实库上未经最终确认执行替换。
