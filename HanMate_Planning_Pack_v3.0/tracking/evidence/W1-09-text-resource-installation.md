# W1-09 · 首次文本资源安装

日期：2026-09-17。结果：PASS / DONE（限定本工作包的文本基础链）；W0-05 IN_PROGRESS。无 Git 元数据。范围调整见 docs/26 ADR-030—032。

## 代码与行为

- `HanMate.Infrastructure/Packages/PackageJson.cs`：严格 UTF-8/重复键/Unicode/整数、内嵌 schema 验证、H1 规范化和内容指纹。
- `TextResourcePackageReader.cs`：有界暂存非 seek 流，ZIP 四文件白名单/重复路径/符号链接/实际解压量、manifest 哈希/数量、schema v2、所有 ID/来源/UUIDv5 条目闭包、纯 word 字典和严格语法校验。暂存退出清理，不执行外部内容，不提取包路径。
- `TextResourceInstaller.cs`：预览不保存内容；计划绑定数据库与 data_epoch；单 SQLite 事务提交正文、播放目标、基础词条索引、资源/条目、epoch 和操作收据。取消及末端 SQL 失败回滚，跨已有正文 token/segment ID 也检查。相同版本载荷核对原文/条目闭包后幂等返回，改变载荷/更新/已卸载重装拒绝。
- `ResourceStateStore.cs`：提取共享事务内登记方法，保留原有公共仓储行为。
- `HanMate.App/Pages/ResourceLibraryPage.cs`、SettingsPage、MauiProgram 与三份 resx：系统 FilePicker→流校验→名称/版本/发布者/数量预览→确认→保存列表；每页50条，只读预览语法/正文/拼音/译文。取消和错误有对应文案，禁止重复触发。

## 自动测试

Core 77/77、Infrastructure 45/45，122/122 PASS。新增安装测试27项，含两种真实 ZIP、独立 Python 指纹一致性、预览零写入、重启/幂等、15+种坏包输入、跨包 token 冲突、过期计划/取消、32 MiB 上限、非 seek 流、保存正文改变与末端收据失败触发的全图回滚。24个 .NET 实际导出的四类型 JSON 经 Python jsonschema 独立校验 PASS。

```text
dotnet test HanMate.Core.Tests/HanMate.Core.Tests.csproj -c Release --no-restore
dotnet test HanMate.Infrastructure.Tests/HanMate.Infrastructure.Tests.csproj -c Release --no-restore
dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-windows10.0.19041.0 --no-restore
dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-android --no-restore
dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-ios --no-restore
```

应用三份 resx 的89个键均非空且键集合/格式占位符一致，PASS。

三种构建均 PASS，0 warnings、0 errors。Android Release 执行 trimming 与 arm64/x64 AOT；iOS 仅 Windows 上 iossimulator-x64 managed 编译。

新增依赖 JsonSchema.Net 9.4.0，集中版本 Directory.Packages.props；读取实际安装包 XML 核对 FromText / Evaluate(JsonElement,EvaluationOptions) / RequireFormatValidation，MAUI 10.0.20 的 PickAsync / OpenReadAsync 同样核对实际 API。所有 schema 通过 EmbeddedResource 随程序加载，应用不运行 Python，不读取外部 $ref。

## Android 聚焦运行

Android 16 / API 36，Pixel 7 模拟器 emulator-5554，x86_64。通过 ADB 更新本地开发应用（保留已有数据），启动实际 Release APK；使用系统文件选择器及 UI 层级观察值：

| 操作 | 实际结果 |
|---|---|
| 设置→管理内容 | 显示已安装资源与空列表 |
| 选择 sample-learning.hanresource | 显示入门语法示例包、1.0.0、HanMate 工程样例、4条及来源未独立核实提示 |
| 预览取消 | 显示已取消安装，仍无条目 |
| 最新 Release 中选择同包 learning.zip | 同样校验并预览，确认后显示已安装4条内容 |
| 选择 sample-dictionary.handict | 预览3条，确认后共7条；不同来源的“学习”分别保留 |
| 再选择 sample-learning.hanresource | 显示此资源包已安装（4条），列表仍为7条 |
| 打开“在”表示位置 | 显示中文句型、说明、两条例句与拼音；简中无辅助译文 |
| 切换 English 再打开 | 中文/拼音不变，显示 I study at school. / He reads at home. |
| 结束进程后重启 | 英语设置保持，管理页仍显示 Showing entries 1–7，7条内容与来源均保留 |
| 切换日本語 | 标题/安装按钮/提示/分页刷新为日语，条目仍7条；验证后恢复测试前的简体中文 |

系统 Downloads 分类未列出 ADB 推入文件，设备存储→Download→HanMate-smoke-20260917 能选择全部自定义后缀及 zip。这是测试文件提供路径差异，没有通过添加存储权限绕过选择器。

## Windows 与未验证边界

Windows Release 实际进程启动且 Responding=True。但 computer-use 返回的 HanMate 窗口错误归属 OneDrive.App.exe，get_window 连续两次报 `window id ... no longer belongs to ...; current owner is ...`（两路径相同），新页面交互 smoke 为 BLOCKED。旧 W1-04 Windows Debug 验证不能替代本次安装界面验收。

本阶段只支持四JSON纯文本包，归档/总展开32 MiB、单JSON16 MiB、manifest1 MiB、深度32、100个资源登记、单资源10,000条。超限/含音频整包拒绝，不截断。10,000条性能/内存峰值、Android 真机、iOS AOT/签名/运行、Windows交互、系统保存/分享取消、崩溃恢复和176项完整应用验收均未由本轮覆盖。

更新、启停/卸载/重新安装、媒体文件恢复、个人内容合并、正式查询排序、Ruby/编辑/播放仍由后续任务实现。只读预览不是正式教学详情页完成；草稿素材不是已审校发行素材。

Android 简中语法预览截图：[W1-09-android-grammar-preview.png](W1-09-android-grammar-preview.png)。
