# W1-08 · 资源与字典持久模型

执行时间：2026-09-15。结果：PASS（Core/Infrastructure 持久模型）；资源 ZIP 安装链、完整卸载依赖处理和最终搜索服务属于后续任务。

## 实现

- `HanMate.Core/Resources/ResourceModels.cs` 定义 learning/dictionary、bundled/external、资源操作、撤下状态、保留原因和 search alias 数据模型。
- `ResourceStateStore` 在真实 SQLite 事务中登记资源、建立 `(resourceId,entryId)→contentId` 拥有关系、启停来源、撤下/恢复单条、写 `resource_operation` 收据并递增 `data_epoch`。
- 安装登记核对严格三段版本、哈希形状、descriptor 身份、安装状态与完整 entry 投影；每个 ContentDocument 必须为 `origin=resource` 且 source 的 resource/version/entry 身份一致。dictionary 只能拥有 word。
- 普通资源查询实际联结 `installed_resource`、`resource_entry` 和 `resource_entry_override`，排除 disabled、missing 和 removed，不依赖管理页的显示开关。
- `SearchAliasStore` 以 `(content_id,alias_ordinal)` 原子替换和读取多 alias，重复 ordinal 或坏 `syllables_json` 在删除旧索引前拒绝。
- `retained_content` 的来源、版本和 reason 可在重启后读取，retained 内容不进入普通资源查询。
- `HanMateDatabase` 对 user_version=2 核对全部 21 张必需表，缺任何资源表或其他目标表都会拒绝打开。
- App DI 已注册内容、资源和 alias 仓储。

## 事务与异常验证

- 登记、entry 映射、operation 收据和 epoch 同事务提交。
- stale epoch、stale row revision、重复 operationId、descriptor/source 不一致、dictionary→grammar 均拒绝；失败后资源状态、revision、epoch 和收据全部回滚。
- 撤下决定使用独立 override；恢复写 removed=0，重启后仍保持。
- 禁用只改变可查询集合，不删除拥有关系；重新启用恢复原稳定 contentId。
- 两个 alias 可共享 contentId；无效替换不破坏旧 alias 集合。

## 验证

```text
dotnet test HanMate.Core.Tests/HanMate.Core.Tests.csproj -c Release --no-restore
PASS 40/40

dotnet test HanMate.Infrastructure.Tests/HanMate.Infrastructure.Tests.csproj -c Release --no-restore
PASS 15/15

dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-windows10.0.19041.0 --no-restore
PASS, 0 warnings, 0 errors

dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-android --no-restore -t:Rebuild
PASS, 0 warnings, 0 errors

dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-ios --no-restore
PASS, iossimulator-x64 managed build, 0 warnings, 0 errors
```

Infrastructure 新增 5 项测试后为 15/15。Android 无连接设备，iOS 无 Mac/Xcode/签名/运行证据；平台应用用例不因 managed build 改为 PASS。

## 边界

- `.hanresource`/`.handict` 白名单、限额、哈希、schema 和音频整包验证由 W1-09 完成；当前 Store 接收上层已验证的 descriptor 数据。
- `distribution=bundled` 的可信入口判定属于 installer，尚未由 UI/安装链执行。
- 卸载时扫描收藏、录音、草稿和拼音教学引用，并将 ContentDocument 与 retained 关系同事务转换，属于 W3-08；本任务只落实持久模型和读取边界。
- 汉字/拼音匹配等级、游标失效和多来源排序属于 W2-05/W2-09；真实 10,000/100,000 词容量尚未测试。
- v1→v2 保留数据迁移仍属于 W4-09。
