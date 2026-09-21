# HanMate · 汉语小伴 · 完整规划与开发文档 v3.0

> 更新日期：2026-09-20。正式名称已确认。.NET MAUI · iOS / Android / Windows。
> 当前已有应用实现和聚焦运行证据；本规划包记录规格、实际进度与未完成门禁，不代表正式发布完成。

## 先看什么

本次整体评估先读 [复审结论与修订](REVIEW_NOTES.md)，版本差异见 [更新摘要](CHANGE_SUMMARY.md)；完整阅读用 [汇总版Markdown](HanMate_Complete_Planning_v3.0.md)。业务原意见 [用户确认需求](USER_REQUIREMENTS.md)。专题正文以拆分文档为真源，任务/用例表以 tracking CSV 为真源，派生文件由工具生成。

开发AI先读 [AGENTS](AGENTS.md)、[状态](tracking/STATUS.md)、[交接](tracking/HANDOFF.md)，再按 [任务](docs/21_Implementation_Backlog.md) 继续。有现成代码先审计复用，不无授权重建或清库。

D8已完成45/48内部开发交付（ADR-106）：两平台开发构建、源码/哈希、待审内容、隐私/恢复指南见[交付入口](delivery/README.md)与[D8证据](tracking/evidence/STAGE-D8-20260920.md)。新增42个例字组成词，251/256处同字同读音配对并相邻展示。本轮应用测试暂缓、iOS排除；正式审校/签名/商店发布单列后置。

D9已完成584个不同自动用例的回归收口，修复D8新增例词后的测试基线；用户回复“先不调试”，设备/UI验收仍暂停，当前保持45/48。见[D9证据](tracking/evidence/STAGE-D9-20260920.md)。

收藏、学习及设置已完成一轮界面简化；两平台Release构建通过，设备/界面调试按用户要求暂缓，见[界面改动记录](tracking/evidence/UI-CLEANUP-20260920.md)。

历史D5后续以[剩余工作清单](tracking/REMAINING_WORK.md)和[收尾计划](tracking/COMPLETION_PLAN.md)为入口：iOS暂缓；D5已补设置换行、字典展开保位/原文入口及默认库审核工具；测试恢复后先定位原生崩溃、复验字体重建语言切换和Windows UI。简体字典、离线释义注音、例句高亮和拼音显隐保位已实现；内容审核与正式发行仍未完成。

## 版本范围

四类学习：词语、课文、语法、诗词。五入口不变，三语言/教学中文固定不变。新增内容与字典管理、选择分享和以后下载的资源协议；现在支持本地安装路径的设计，不建设服务器/在线商城。

35份专题文档、22项需求、48个任务、176个原始应用用例及59个补充场景、10份Schema、21张表的SQLite草案、24条草稿样例、64条规格三语言文案（应用现有594键）。当前完成45/48个内部开发工作包（ADR-106；Windows/Android，iOS依ADR-105排除；正式条件见后置清单）；完整176项应用验收仍NOT RUN，已执行的聚焦运行另见 tracking/TEST_MATRIX.md 与 tracking/execution-ledger.csv。4种实际ZIP样例不含音频。

## 文档索引

| 文档 | 主题 |
|---|---|
| [00_Project_Master_Plan](docs/00_Project_Master_Plan.md) | 项目总体规划 |
| [01_Product_Requirements](docs/01_Product_Requirements.md) | 产品需求与范围基线 |
| [02_User_Flows_and_Navigation](docs/02_User_Flows_and_Navigation.md) | 用户流程与导航规则 |
| [03_Page_Specifications](docs/03_Page_Specifications.md) | 页面与组件详细规格 |
| [04_Design_System_and_Accessibility](docs/04_Design_System_and_Accessibility.md) | 视觉系统、注音组件与无障碍 |
| [05_Localization_and_Copy](docs/05_Localization_and_Copy.md) | 多语言、教学译文与界面文案 |
| [06_Architecture_and_Dependencies](docs/06_Architecture_and_Dependencies.md) | 技术架构与依赖选型 |
| [07_Data_Model_and_Persistence](docs/07_Data_Model_and_Persistence.md) | 数据模型、持久化与版本策略 |
| [08_Pinyin_Engine_and_Correction](docs/08_Pinyin_Engine_and_Correction.md) | 离线注音、标调与人工校正 |
| [09_Ruby_Layout_and_Reading](docs/09_Ruby_Layout_and_Reading.md) | 注音排版、标点分段与阅读器 |
| [10_Search_and_Indexing](docs/10_Search_and_Indexing.md) | 离线搜索、拼音查询与索引 |
| [11_Audio_Playback_and_Recording](docs/11_Audio_Playback_and_Recording.md) | 朗读、录音、音轨与音频生命周期 |
| [12_Text_Import_and_Editor](docs/12_Text_Import_and_Editor.md) | 文本导入、编辑、草稿与修改关联 |
| [13_Favorites_and_Settings](docs/13_Favorites_and_Settings.md) | 收藏夹、设置与本地数据管理 |
| [14_Package_Format_and_Validation](docs/14_Package_Format_and_Validation.md) | 分享、备份与可安装资源包协议 |
| [15_Backup_Restore_and_Conflict_Resolution](docs/15_Backup_Restore_and_Conflict_Resolution.md) | 备份、恢复、冲突与崩溃一致性 |
| [16_Content_Production_and_Licensing](docs/16_Content_Production_and_Licensing.md) | 教学内容、音频素材与来源治理 |
| [17_Privacy_Security_and_Error_Catalog](docs/17_Privacy_Security_and_Error_Catalog.md) | 隐私、安全与错误处理 |
| [18_Service_Contracts](docs/18_Service_Contracts.md) | 服务接口、用例契约与事件 |
| [19_Test_Strategy_and_Acceptance](docs/19_Test_Strategy_and_Acceptance.md) | 测试策略与验收标准 |
| [20_Performance_and_Reliability](docs/20_Performance_and_Reliability.md) | 性能、容量与可靠性设计 |
| [21_Implementation_Backlog](docs/21_Implementation_Backlog.md) | 实施阶段、任务分解与依赖 |
| [22_Build_Release_and_Operations](docs/22_Build_Release_and_Operations.md) | 构建、发布与维护手册 |
| [23_AI_Execution_Guide](docs/23_AI_Execution_Guide.md) | AI 开发执行、审查与接续规则 |
| [24_User_Guide_and_FAQ](docs/24_User_Guide_and_FAQ.md) | 用户使用说明与常见问题草案 |
| [25_Risk_Register](docs/25_Risk_Register.md) | 风险清单与应对措施 |
| [26_Decisions_and_Open_Items](docs/26_Decisions_and_Open_Items.md) | 决策记录与待验证事项 |
| [27_References](docs/27_References.md) | 参考资料与核查边界 |
| [28_Change_Log](docs/28_Change_Log.md) | 版本变更与需求影响 |
| [29_Acceptance_Cases](docs/29_Acceptance_Cases.md) | 逐项验收用例 |
| [30_Grammar_Module](docs/30_Grammar_Module.md) | 语法学习与编辑详细设计 |
| [31_Content_Resource_Lifecycle](docs/31_Content_Resource_Lifecycle.md) | 学习内容与资源包生命周期 |
| [32_Dictionary_Management](docs/32_Dictionary_Management.md) | 字典安装、查询与删除设计 |
| [33_Sharing_Workflows](docs/33_Sharing_Workflows.md) | 内容与录音分享详细设计 |
| [34_Compatibility_and_Migration](docs/34_Compatibility_and_Migration.md) | v2 文档迁移与协议兼容 |

## 配套文件

| 位置 | 用途 |
|---|---|
| [specs](specs/README.md) | Schema、SQLite新库目标、限额；不能拿新DDL覆盖旧数据 |
| [examples](examples/README.md) | 草稿内容、语法、字典与资源描述、四种包 |
| [tools](tools/README.md) | 开发机校验脚本；不在手机端运行Python |
| tracking | 需求/任务/用例CSV、进度、交接、素材清单、环境模板 |
| templates | 缺陷、任务、审查、决策、发布、隐私说明模板 |
| [archive](archive/README.md) | v1/v2旧规范和旧协议夹具，只用于追溯 |

## 校验与边界

从本包目录按 [工具说明](tools/README.md) 生成派生文档、运行 `python -X utf8 tools/verify_bundle.py --write-report` 并刷新/检查文件清单，结果见 [VALIDATION_REPORT](VALIDATION_REPORT.md)。只覆盖文档/契约/样例，不代表真实音频、自动注音质量、设备排版、生产安装安全或MAUI测试完成。

依赖SDK/插件版本和所有平台能力在M0重新核对；新实施已核对 Plugin.Maui.Audio 4.0.0 和部分资源来源，见 tracking/evidence/RESOURCE_SOURCES-20260917.md；其余技术来源不自动视为重新验证。教学内容、译文、字典授权和音频仍须独立审核。

文档v3.0与协议版本不同，详见 [兼容迁移](docs/34_Compatibility_and_Migration.md)。FILE_MANIFEST.json记录本包文件字节完整性，不是数字签名。

字典解释页已增加星形收藏，并按用户要求改为点击词头播放、移除独立播放按钮。验证边界见[改动记录](tracking/evidence/DICTIONARY-FAVORITES-20260920.md)。

字典释义及例句已接点读，中文语音包随应用内置；新安装默认可用，已有音色和停用选择保留。见[音频改动证据](tracking/evidence/DICTIONARY-AUDIO-BUNDLED-20260920.md)。
