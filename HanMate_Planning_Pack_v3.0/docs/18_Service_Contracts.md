# 18 · 服务接口、用例契约与事件

> HanMate 文档包 v3.0 · 2026-09-14 · 状态：开发基线提案，非已实现报告。
> 正式名称：HanMate / 汉语小伴；R01—R22 为需求基线，其余明确标注的规则为可调整默认设计。

## 1. 说明

以下均为**项目自定义接口设计**，不是 MAUI 或某个 NuGet 现成 API。可以调整类名，但输入、取消、版本、错误和副作用语义不能缺失。实现前先让 Core 中的 DTO 与 schema 一致，再生成页面。

## 2. 通用值与结果

ID 统一 UUID；OperationId/SessionId/QueryId 为操作身份而非内容身份。范围使用 TextRange(start,length) 与所在 TextUnit 的持久化 UTF-16 边界映射。所有时间持久化为 UTC；排序 code 使用 ordinal。

`Result<T>` 为 Success / Cancelled / Failure；Failure 包含稳定 Code、MessageKey、Retryable 和安全诊断上下文。长任务必须接收 CancellationToken；进度用阶段、已处理量、总量可空表达，不伪造百分比。

编辑提交携带 expectedRowRevision；数据维护计划携带 expectedDataEpoch。取消不抛给 UI 当成崩溃；领域验证失败不改变正式数据。

## 3. 内容用例

| 方法建议 | 输入 → 输出 | 关键约束 |
|---|---|---|
| QueryContentsAsync | ContentFilter + PageCursor → Page<ContentSummary> | 同维度 OR、跨维度 AND、稳定分页 |
| GetContentAsync | contentId → ContentSnapshot | 包括 rowRevision，不返回可直接写的仓储对象 |
| CreateDraftAsync | kind + 可选原文 → DraftSnapshot | 分配草稿稳定 ID；不进正式索引 |
| SaveDraftAsync | draftId + expectedDraftRevision + draft → DraftSnapshot | 保留原文/校正/音频临时引用 |
| CommitContentAsync | draftId + expectedRowRevision → ContentSnapshot | 原文、投影、索引、音频复核一次事务 |
| CopyBuiltinToPersonalAsync | contentId → DraftSnapshot | 全图新 ID，保留来源，不改内置 |
| PreviewDeleteContentAsync | contentId → DeleteImpact | 列收藏、音轨、草稿影响 |
| DeletePersonalContentAsync | contentId + expectedRowRevision + confirmationToken → Result | 非内置；安全事务；GC 延后 |

DeleteImpact 不是持续有效授权；数据变化后必须重算，确认 token 绑定实体与版本，避免确认甲却删除乙。

## 4. 拼音与分段

| 方法建议 | 契约 |
|---|---|
| AnnotateAsync(AnnotatedInput, options, token) | 返回完整原文映射、候选与来源；保留有效锁定项 |
| ParsePinyin(input, mode) | 区分 search/edit；无调不是轻声；返回合法性和规范值 |
| FormatSyllable(syllable) | 确定性输出 NFC 带调拼音 |
| ApplyManualCorrection(draft, tokenId, syllable) | 只改当前出现位置；更新锁定和复核关系 |
| SegmentText(text, boundaries, manualOverrides) | 全覆盖、可还原，保留 layout |
| ReconcileTextEdit(old,newText,editOperations) | 返回保留/新建/歧义 ID 映射与受影响音轨，不直接播放 |

注音服务不弹窗、不获取网络、不直接写数据库。校正能否自动映射由 ReconcileReport 明确决定，ViewModel 不能自行绕过“需复核”结果。

## 5. 搜索与收藏

SearchAsync(query, queryId, cursor, token) 返回匹配层级、是否近似、contentId 和当前版本摘要。旧 queryId 由调用者丢弃。RebuildIndexAsync 只从主数据重建，绝不删除主数据。

GetFolderMembershipAsync(contentId) 返回真实所属集合。SetFolderMembershipAsync(contentId, finalFolderIds, expectedMembershipRevision) 一次提交，不逐次网络式模拟增删。Create/Rename/Reorder/DeleteFolder 均验证 systemRole 与名称规范；内容删除不是 FolderRepository 职责。

Settings.UpdateAsync(patch, expectedRevision) 验证允许字段并持久化；触发通知后 UI 再刷新。设备语音枚举不写进可迁移设置 JSON。

## 6. 音频接口

| 方法建议 | 完成语义 |
|---|---|
| ResolveSourceAsync(targetId, mode, policy) | 返回具体适用音源或缺失原因，不启动播放 |
| PlayAsync(targetId, ownerScopeId, mode, token) | 等待自然结束/取消/失败，不只是“已调用播放器” |
| StopAsync(ownerScopeId?) | 确认取消并释放相应活动会话 |
| BeginRecordingAsync(targetId, ownerScopeId) | 权限通过且实际开始录音后返回 sessionId |
| StopRecordingAsync(sessionId) | 完成容器写入，返回 AudioDraft 或失败 |
| SaveRecordingAsync(audioDraftId, targetId, setDefault) | 安装不可变文件并提交绑定 |
| ImportAudioAsync(streamProvider, metadata, targetId, token) | 限额/解码/复制/试听前态；不保存外部绝对路径 |
| ConfirmBindingAsync(bindingId, expectedTargetHashes) | 确认读音仍适用，原子更新哈希和复核状态 |
| SelectBindingAsync(targetId,bindingId) | 必须属于同目标且适用；不自动覆盖标准源 |

平台底层 IAudioPlayerAdapter、IRecorderAdapter、ISystemSpeechAdapter 负责真实设备；协调器拥有业务状态机。平台事件回调携带 sessionId 或由适配器绑定当前会话，防止迟到回调更新新任务。

## 7. 包和文件接口

| 方法建议 | 契约 |
|---|---|
| ValidatePackageAsync(streamProvider, limits, token) | 仅 staging，只读验证结果，正式库不动 |
| PlanImportAsync(validatedPackage, options, token) | 返回 operationId、ID 映射、依赖闭包、冲突和 expectedDataEpoch |
| ExecuteImportAsync(planId, token) | 重验包指纹与 epoch；文件/事务协议；提交 receipt |
| ExportBackupAsync(options, token) | 一致快照与租约，返回已校验私有临时文件 |
| ExportContentAsync(selection, options, token) | 白名单选择，不含设置/未选资料 |
| SaveExportToUserLocationAsync(exportHandle) | 返回 Saved / Cancelled / Failed |
| ShareExportAsync(exportHandle) | 仅表示系统交互状态，不保证对方收到 |

Core/Infrastructure 不弹系统窗口：streamProvider 由 App 平台层提供。文件句柄/导出 handle 有生命周期与租约，不在 Share 返回后立即删除仍可能被系统读取的文件。

## 8. 领域通知

ContentChanged(contentId, revisions, changeKind)、ContentDeleted(contentId)、FoldersChanged、SettingsChanged(keys)、LanguageChanged、AudioSessionChanged(sessionSnapshot)、CatalogUpdated、DataRestored(epoch)。通知为进程内轻量机制，不引入远程消息总线。

提交前不广播正式变更；失败不发“成功更新”。接收者依据 ID 局部刷新，取消过期加载；页面释放时取消订阅。DataRestored 使旧详情快照和缓存失效，但必须先处理未保存草稿。

## 9. 示例调用顺序

改拼音：Editor → ApplyManualCorrection → DraftSaved → CommitContent(expectedRevision) → 内容/索引/绑定复核事务 → ContentChanged → 详情刷新。

分享导入：FilePicker → ValidatePackage → PlanImport → 用户确认 → ExecuteImport → 文件安装 → SQLite 提交 receipt → DataRestored/ContentChanged → 结果摘要。

点课文：RubyTextView.SegmentTapped → ReaderVM 更新选中与弹层 → AudioCoordinator.PlayAsync(segmentId) → ResolveSource → 实际播放 → 当前 session 完成状态。禁止 View 直接 new 播放器绕过协调器。


## 10. 语法、资源、字典与分享接口

以下为项目自定义契约名称，具体 C# 签名在实现中锁定，不能当作第三方现成 API。

| 接口/操作 | 输入→输出 | 约束 |
|---|---|---|
| ValidateGrammar | ContentDocument → ValidationResult | unit 引用/role、句型、最少例句；无副作用 |
| QueryResourcesAsync | kind/installed/enabled → summaries | 缺失资源与已安装明确区分 |
| PlanInstallResourceAsync | validatedPackage、options、epoch → plan | 检查种类、稳定ID、版本、信任和空间 |
| ExecuteResourcePlanAsync | planId、cancellation → result | 文件准备+事务提交收据；过期拒绝 |
| PreviewRemoveResourceAsync | resourceId、rowRevision → impact | 包含收藏/用户音频/草稿/教学例词/租约 |
| RemoveResourceAsync | planId、retentionPolicy → result | 默认保留依赖，不删个人副本 |
| SetResourceEnabledAsync | resourceId、enabled、expectedRevision | 保存后更新 source epoch |
| SetDictionaryPriorityAsync | orderedResourceIds、expectedEpoch | 校验去重和类型，一次事务排序 |
| RemoveResourceEntryAsync | resourceId、entryId、expectedRevision | 持久撤下，不改源原文 |
| RestoreResourceEntryAsync | resourceId、entryId、expectedRevision | 源存在才恢复，否则提示重新安装 |
| PreviewShareAsync | selectedContentIds/assetIds、options → summary | 全图闭包、许可、大小、隐私白名单 |
| ExportSelectedContentAsync | confirmedSelection、expectedEpoch → file | grammar/四类/音轨，不能带设置/资源状态 |

SearchAsync 扩展 sourceFilter 与 expectedSearchEpoch，结果含 sourceId/sourceName 和匹配等级。字典启停/卸载不会调用注音引擎的删除方法。通知增加 ResourcesChanged/DictionarySourcesChanged；提交成功后发送，旧查询和源列表局部刷新。

复制或导入冲突时 grammar 的 explanation/example/note unitIds 必须在全图映射中更新；只换 contentId 不算完成。资源更新计划与普通个人导入计划分开：前者版本冲突不能无声派生同 ID 字典。
