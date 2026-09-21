# 平台执行台账规则

[execution-ledger.csv](execution-ledger.csv) 记录每次实际执行，`executionId` 唯一，保留历史行，不用新结果覆盖旧结果。原始 176 条用例仍在 [acceptance-cases.csv](acceptance-cases.csv)；新增 AI 语音/例词交互场景在 [acceptance-supplement.csv](acceptance-supplement.csv)，不修改原始总数。

- `platform`：Shared / Windows / Android / iOS；Shared 只用于共享逻辑验证。
- `layer`：unit / integration / native / platform。原生合成不等于 UI 播放或人工听审。
- `coverage`：full 是本行对应单个平台/层级用例全步骤；partial 是只执行 `scope` 明示部分；none 表示尚未执行。PASS + partial 只能说明这部分通过。
- `executedAt`、`device`、`buildSha256`、`evidence`：记录真实时间、环境、被测产物哈希及规划包内可追溯证据。Android 为 APK 哈希，共享测试为测试程序集哈希，原生探针为探针程序集哈希。无法确认旧构建的结果不补造哈希。
- 原始用例聚合 PASS 必须分别检查全部适用平台/层级与完整步骤，不能由某行 PASS 自动计算。按ADR-105，iOS从本轮Windows/Android里程碑排除，历史iOS记录保留且不得填PASS；以后恢复三平台目标时补验；Windows 窗口工具阻塞不等于应用测试失败。

从规划包目录运行 `python tools/verify_execution_ledger.py` 检查台账字段、用例引用、证据路径和哈希格式；它不重新执行应用，也不修改任何用例状态。

Android UI 辅助工具在仓库 `tools/android_acceptance_ui.py`，只依据实时 UI 树选择确切 ID/描述；保存 XML、截图与操作记录到私有 TEMP。操作日志只是发生过动作，必须另行核对预期结果才能填 PASS。不要把含个人内容的设备截图/日志自动放入源码。
