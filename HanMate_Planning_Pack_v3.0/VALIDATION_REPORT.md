# HanMate v3.0 文档包校验报告

校验时间（UTC）：2026-09-23T10:22:35.180697+00:00。此报告仅覆盖开发文档、契约与工程样例，**不是 MAUI 应用测试报告**。

执行命令：`python tools/verify_bundle.py --write-report`

| 项目 | 结果 | 实际检查 |
|---|---|---|
| 本地链接与代码围栏 | PASS | 390 local Markdown links exist; fences balanced (excluding archive) |
| JSON Schema 结构 | PASS | 10 Draft 2020-12 schemas structurally valid |
| 样例 JSON 与字音引用 | PASS | 15 JSON fixtures; 24 contents; 73 playback targets; ranges, tones, highlighter checked |
| 内容包清单/哈希 | PASS | content: 3 files, 24 contents, 0 resources, 0 audio assets; fixture structure/hash/references valid |
| 备份包清单/哈希 | PASS | backup: 6 files, 24 contents, 2 resources, 0 audio assets; fixture structure/hash/references valid |
| 学习资源包清单/引用 | PASS | resource: 4 files, 4 contents, 1 resources, 0 audio assets; fixture structure/hash/references valid |
| 字典包清单/引用 | PASS | resource: 4 files, 3 contents, 1 resources, 0 audio assets; fixture structure/hash/references valid |
| 需求与用例追踪 | PASS | 22 requirements mapped; 176 defined cases; {'PASS': 0, 'FAIL': 0, 'NOT RUN': 176, 'BLOCKED': 0}; evidence existence is not platform execution |
| 任务与三语言文案 | PASS | 48 tasks with valid acyclic dependencies and status/evidence checks; 64 three-language draft copy entries |
| 派生文档与真源一致 | PASS | task table, acceptance-case document and topic aggregate match their sources |
| SQLite DDL 与约束 | PASS | SQLite 3.53.1; 21 tables; favorites/FKs/audio/resource/alias constraints; stale revisions rejected; repeated-package operations and receipt rollback checked (in-memory only) |
| 破坏性样例拒绝 | PASS | 5 malformed packages and duplicate JSON properties rejected (fixture-level checks only) |
| 新增语法/资源/隐私拒绝 | PASS | 5 additional grammar/resource/privacy malformed fixtures rejected |
| 资源音轨与用户音轨隔离 | PASS | published identity excludes synthetic user audio/default overlays; missing declared source binding rejected; metadata-only test |
| 旧v1包显式内容转换 | PASS | 2 original v1 packages verified; 34 content instances converted in memory with IDs/text/annotation unchanged; no database or MAUI migration executed |
| 复审回归：严格解析/依赖/分享闭包 | PASS | 6 Unicode/depth/dependency/privacy regressions rejected; synthetic metadata only, no audio decode |

## 没有验证的部分

本次未构建 MAUI，未运行 iOS/Android/Windows 真机；未验证实际录音、系统 TTS、字体排版、文件交互、性能、生产级导入安全和崩溃恢复。示例包没有音频字节；不能以其通过推断音频跨设备迁移完成。

Python regex 文本元素校验针对当前工程样例，不证明与每个平台 StringInfo 的所有 Unicode 情况一致。工具按固定白名单校验当前协议样例，不是可以直接上线的通用导入器。应用用例状态仅从 CSV 读取，本工具从不修改它们；证据文件存在不证明实际执行通过。

更新报告后执行 `python tools/build_bundle.py --write` 刷新文件清单，再执行 `python tools/build_bundle.py --check` 独立核对；清单不嵌入本报告，避免循环哈希。
