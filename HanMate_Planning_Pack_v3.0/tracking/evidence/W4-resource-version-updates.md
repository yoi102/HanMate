# 资源跨版本差异、旧图快照与更新界面

日期：2026-09-18。连续实现差异计划、事务内快照迁移、分页预览与更新入口三个子阶段，再集中验证。W4-07从TODO进入IN_PROGRESS；W3-08继续IN_PROGRESS，22/48 DONE不变。

## 实现

- TextResourceInstaller继续作为统一入口，严格包读取后在同一读事务取得epoch和更新计划。TextResourceUpdate仅允许已安装external纯文字资源的更高三段数值版本，同kind；拒绝降级、跨图UUID冲突、bundled/保留ID和已卸载跨版更新。相同版本仍沿用载荷碰撞检查与完整安装幂等判断。
- 差异比较沿用语义指纹，只额外忽略source.resourceVersion。来源、标题、标签、正文或人工注音变化均保守归为变更；未变图保留既有UUID和音轨默认。计划包含新增/变更/移除/未变及保留数量、发布者声明变化提示。
- 受收藏/音轨/导入映射/保留关系引用或rowRevision>1的旧变更图，完整重映射content/unit/token/segment和语法引用为retained快照。收藏保留folder、顺序和时间；audio_binding UUID、asset与字节不变，只迁移target和默认；图类型import_mapping移到旧快照。新资源仍用稳定条目contentId，且不自动使用旧录音。个人副本不变。
- 提交重新读取相关正文、目标、收藏、音轨、默认、草稿、教学及映射，比较计划守卫并验证epoch。快照、关联、新版正文/索引/目标、descriptor、operation收据同SQLite事务；失败回滚，重复operation幂等。原content rowRevision和membershipRevision单调推进，避免过期页面误写新版。enabled、priority与全部entry override保持，包括上游暂时移除的条目。
- 草稿/教学引用涉及变更或移除图、或资源ID时，阻止整笔更新；不对任意JSON字符串猜测重写。无文件搬动或删除，不声称文件系统与SQLite单一事务。
- ResourceManagementPage增加外部资源专用更新入口并检查选择的resourceId。ResourceLibraryPage增加更新计划、风险提示、最终确认和结果；ResourceUpdateReviewPage每页20条，返回取消。保留内容/收藏提示区分旧快照，简中/日/英420键。

## 集中验证：PASS

所有dotnet命令串行；无新增依赖、schema或DDL版本变化。

- Core Release：188/188。
- Infrastructure Release：164/164，新增14项。覆盖语法完整图/锁定/译文保留、收藏顺序时间、真实PCM16 WAV读取及默认、拼音变化旧音轨与新版隔离、未变目标原位保留、新增/移除/再引入撤下记录、导入映射、个人副本隔离、重复operation及重复同包、数值版本/降级/kind/发布者变化、草稿/教学阻止、提交前新依赖/epoch过期、全事务中途故障/回滚重试、取消/已卸载拒绝、外部嵌套UUID碰撞。
- Windows Release、Android Release完整Rebuild：0警告/0错误。
- Windows宿主iOS simulator managed编译：0警告/0错误；不是Mac/Xcode或iOS原生验证。
- 420键简中/日/英：键集合、非空、数字占位符集合一致。

初轮测试暴露工程夹具没有poem/text条目，已改为使用实际存在的稳定entryId；修复后全部通过，未删断言。后续补充改音、移除映射与外部嵌套冲突三项回归同样通过。

构建日志位于开发机临时目录：hanmate-resource-update-{windows,android,ios}.log。最终APK SHA256：`8d6ddacf6be4106bfc356c372607f441b27304dd97710ff7865bc8fdca755760`。

## Android16/API36模拟器：PASS

emulator-5554，1080×2400。覆盖安装Release，保留原库、日语界面及150%阅读偏好；未清库、未采集麦克风、未分享外发。仅新增独立工程资源Resource-update-smoke（resourceId=16f71db8-7f8c-4ea1-b651-eb52fa7f1088）及其旧版收藏。

1. 从系统文件选择器安装update-1.0.0.zip，严格验证通过，资源仅1条Update-smoke-v1。
2. 从资源条目管理阅读旧版，将它加入默认收藏夹；原有“你好”收藏仍在。
3. 从资源详情新入口选择update-2.0.0.zip。分页差异页显示变更1条并保留旧内容，继续后最终确认显示1.0.0→2.0.0、新增0/变更1/移除0/未变0/保留1。
4. 提交成功，管理页显示2.0.0/1条；新版Update-smoke-v2可读，150%偏好仍在。
5. 默认收藏夹显示原有“你好”及Update-smoke-v1，旧条目明确显示更新/卸载后保留提示，点击仍打开旧版。最终停留旧快照阅读页。

临时夹具及截图：`C:\Users\yoiri\AppData\Local\Temp\HanMate-resource-update-smoke`；review.png和favorite.png已查看，未见遮挡。两版本仅用于升级工程验证，不计正式教学资料或音频。没有修改现有随附包、搜索/阅读夹具、个人正文或旧录音；测试资源及其收藏保留供后续验证。

## 剩余范围 / NOT RUN

草稿/拼音教学引用的完整迁移、资源发布音频安装/升级、已卸载资源跨版接回、降级、完整备份/替换/旧库迁移和自动GC仍待实现。保守rowRevision判断可能在多轮更新中保留额外历史快照；当前不承诺手机10k条更新性能。只有标题/来源/标签变化也可能产生旧快照，未自动猜测是否可让收藏/录音转到新版。

Windows新界面交互、iOS原生、Android真机、真实强杀/满磁盘、超大资源性能/取消时序、更新界面读屏、六方向迁移和完整176项验收NOT RUN。旧音轨可读由真实SQLite/WAV测试证明，本轮Android升级夹具没有音轨，未以此声称资源旧音轨原生播放验收。正式示范音频保持6/161。
