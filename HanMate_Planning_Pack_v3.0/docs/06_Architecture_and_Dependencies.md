# 06 · 技术架构与依赖选型

> HanMate 文档包 v3.0 · 2026-09-14 · 状态：开发基线提案，非已实现报告。
> 正式名称：HanMate / 汉语小伴；R01—R22 为需求基线，其余明确标注的规则为可调整默认设计。

## 1. 技术基线与已核查边界

上一版记录的 2026-09-14 Microsoft 支持页核查结果列出 .NET MAUI 10 为受支持版本，页面列出的补丁为 10.0.101、支持结束日期为 2027-05-11。**MAUI 补丁号不是 .NET SDK 号或 workload-set 号**；开工必须分别锁定，不把它直接填入 global.json 的 sdk.version。[S01](27_References.md)

本项目默认采用当前受支持稳定 .NET/MAUI，不自动切到预览版。最低 OS、Xcode、Android SDK/JDK 和 Windows SDK 以选定发行版本的依赖表及 M0 实测确定；不承诺“所有 iOS/Android/Windows 版本”。iOS 开发需要适用的 Mac 构建环境。[S02](27_References.md)

2026-09-14 文档复审已读取当前工程：根目录 HanMate.slnx 和 HanMate.App 为 .NET 10 MAUI 模板，MainPage 仍是计数器；未执行 SDK/workload 审计或 MAUI 构建。`tracking/environment-lock.template.json` 的未选择字段必须由真实环境填充，不能拿空值直接当作构建配置。

## 2. 工程结构

以下为职责示意，不要求搬迁现有 HanMate.App 到 src。优先在当前 HanMate.slnx 旁增补 Core/Infrastructure/tests；没有迁移收益时保留现有路径。工程模板额外声明 MacCatalyst，不把它自动纳入已确认的三平台发行范围；M0 核对后再决定是否保留开发目标。

```text
src/
  HanMate.App/                  MAUI、XAML、ViewModel、平台能力适配
    Views/ ViewModels/ Controls/ Resources/ Platforms/
    Services/                  语言、系统文件 UI、TTS/录音底层适配
  HanMate.Core/                 纯 .NET 模型、规则、接口、用例服务
    Domain/ Application/ Contracts/
  HanMate.Infrastructure/       SQLite、文件库、包协议、离线词典
    Persistence/ Packages/ AudioStorage/ PinyinData/
tests/
  HanMate.Core.Tests/
  HanMate.Infrastructure.Tests/
  HanMate.Platform.Tests/       可自动化的平台部分；真机记录另存
```

依赖方向：App → Core 与 Infrastructure；Infrastructure → Core；Core 不依赖 MAUI、文件选择器、麦克风或具体 SQLite 包。无需再拆微服务、事件总线服务器或十几个空程序集。项目小可以合并 Infrastructure，但不能让核心规则无法独立测试。

## 3. 单一数据链

```text
View/XAML → ViewModel 命令 → 应用用例服务 → 仓储/引擎接口
                                        ├→ SQLite 与本地文件
                                        ├→ 离线拼音词典
                                        └→ 平台音频/文件适配
```

ViewModel 不直接读取 raw JSON 后自行保存另一份；领域服务不显示弹窗。读取结果为不可变 DTO 或快照；所有编辑经 `expectedRevision` 检查提交，提交成功后发出局部内容变更通知。

首版采用**一个运行时 SQLite 数据库 + 版本化内置/外部资源包 + 不可变音频文件库**。安装服务统一装载学习包和字典包，查询统一索引；普通编辑仅修改 personal，内置/resource/retained 通过复制编辑。资源启停、撤下、版本和用户关系独立持久化，不在各页面各存一套状态。

## 4. 依赖建议与门禁

| 能力 | 默认路线 | 采用前验证 |
|---|---|---|
| MVVM | CommunityToolkit.Mvvm 或轻量自实现 | 源生成、编译绑定、裁剪 |
| UI 辅助 | CommunityToolkit.Maui 按需使用 | MAUI 版本、TouchBehavior、FileSaver |
| SQLite | Microsoft.Data.Sqlite 或 sqlite-net-pcl 二选一，M0 锁定 | 原生 SQLite 打包、事务、迁移、三平台 Release |
| 自动注音 | .NET 离线词典引擎；构建期加工字/词数据 | 数据许可、词组命中、儿化、字素、AOT |
| 系统朗读 | MAUI ITextToSpeech | 普通话 locale、取消、离线、生命周期 |
| 播放录音 | Plugin.Maui.Audio 为候选；不合适时局部平台适配 | 真录音、编码、完成事件、释放 |
| 文件选择/分享 | MAUI FilePicker、Share；保存适配按版本验证 | URI/流、后缀、取消、受限分享目录 |
| 包 | System.IO.Compression + System.Text.Json + SHA-256 | 资源限制、重复键、路径、流式计量 |

Microsoft 文档给出 MAUI SQLite 使用路线；具体库选型与打包仍需验证。[S09](27_References.md) Plugin.Maui.Audio 项目包含播放与录音能力，但不代表任意版本/编码/平台组合都已经适用于本项目。[S10](27_References.md)

不为调用 Python 拼音库在手机中嵌入 Python；文档验证脚本和数据构建脚本可以使用 Python，但只在开发/构建阶段运行。

## 5. 生命周期与实例范围

应用级 singleton：数据会话协调器、音频协调器、语言服务、字典加载器、内容变更通知。页面与 ViewModel 按导航范围创建并释放订阅；禁止通过 singleton 持有整棵页面树。

长任务使用 CancellationToken 和 operationId；状态回到 UI 线程更新。除 UI 事件入口外不写 `async void`。取消不是失败；异常带稳定错误码，不吞掉异常后继续“成功”。

启动顺序：应用目录 → 数据库迁移 → 未完成操作恢复 → 尊重已卸载/禁用记录的内置目录更新 → 设置加载 → 搜索索引检查 → 主页面。数据库迁移失败时保留原库，不以清库重建恢复启动。

## 6. 并发边界

单进程、单主窗口是首版默认。Windows 第二次启动不允许绕过数据会话锁并发破坏性恢复；是否支持多实例由 M0 记录，可限制为单实例。运行中的数据库写入由单写队列或可控事务门限串行协调。

普通读可并发，编辑提交使用乐观版本检查；导入、目录升级、替换恢复使用排他的数据维护门。音频文件为内容寻址不可变文件，删除由引用/租约检查后的 GC 处理。导出获取快照与文件租约，不阻塞整个压缩期间的普通读取。

## 7. M0 五项技术试验

每项有最小程序、版本、输入、输出和证据：三平台最小构建；注音排版与长文；离线多音字校正；实际录音与播放中断；文件保存—重新选择—导入。接口模拟只能证明协调逻辑，不能证明真机音频。

若候选库失败，先隔离到适配器，不重写全部架构；记录失败平台、复现输入、替代实现成本与决策。受阻平台不能标记完成，但不阻止纯逻辑测试继续。


## 8. v3 新增模块与数据流

```text
HanMate.App
  GrammarPage / GrammarViewModel           语法详情与结构化编辑
  ResourceManagerPage / ViewModel          学习包、字典管理复用
  ShareSelectionPage / ViewModel           选择内容、音轨与预览
HanMate.Core
  GrammarValidator                        unit 关系、句型与朗读边界
  ResourceLifecycleService                安装/启停/撤下/卸载/更新计划
  DictionaryQueryPolicy                   启用来源、优先级、稳定游标
  DeletionImpactPlanner                   收藏、录音、草稿依赖保护
  SharingUseCases                         白名单选择与内容闭包
HanMate.Infrastructure
  ResourceRepository / ResourceInstaller  清单、条目映射、SQLite/文件提交
  ContentSnapshotStore                    retained 资料和来源版本
  LegacyV1PackageReader                    独立旧协议校验与转换
```

依赖方向保持不变。基础注音词典与 QueryDictionaryRepository 分离；删除字典服务没有清空 IPinyinEngine 数据的权限。资源安装和普通分享导入都使用包验证基础设施，但安装保留包身份，内容导入创建个人资料，不能共用“无条件覆盖内容”实现。

当前 source 为文件选择器提供的流；未来 RemoteResourceSource 下载完成后复用 installer。没有必要添加 Web API、后台账号、消息队列或插件执行环境。

资源操作成功后发 ResourcesChanged/DictionarySourcesChanged，查询 epoch 更新；一次事务更新内容 JSON、资源映射、索引和用户引用。操作计划绑定 expectedDataEpoch，避免预览与执行之间的数据改变造成误删。

长课文阅读器在 M0 比较原生 Ruby 控件和必要的局部本地 HTML 实现；默认仍是 MAUI/XAML 主体。尚未选用 HybridWebView，不新增其依赖或声称已通过裁剪测试，决策以实际 Release 验证为准。
