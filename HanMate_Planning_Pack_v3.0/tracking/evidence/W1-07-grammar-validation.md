# W1-07 · 语法数据与角色验证

日期：2026-09-17。结果：PASS / DONE。无 Git 元数据。

新增 `HanMate.Core/Content/GrammarValidator.cs`，验证说明/例句/备注的角色与完整引用、句型部件类型/数量、非空文本、Unicode 标量长度、ja/en 翻译与 topic codes。拒绝缺失/null 集合，不改变输入顺序、不自动修复引用；旧 word/text/poem 验证继续保留。

ContentDocument 的 grammar 必需字段在 JSON 解码时受 required 约束；协议必需的可空分类与拼音字段始终输出。新增 token.modifiedAtUtc，UTC 时间输出 Z，兼容读取旧偏移格式。SQLite 无新字段/DDL 升级；修改范围只涉及现有 JSON 字段的正确保留。

验证：Core Release 77/77 PASS；Infrastructure Release 45/45 PASS（含本阶段安装测试）。其中 grammar 新增37项，持久化覆盖坏更新不改变正文/版本、人工锁定/时间保留、24条四类型样例的实际序列化往返。24个实际 .NET 导出另由 Python jsonschema 校验协议 v2，PASS。

代码：`HanMate.Core.Tests/Content/GrammarValidatorTests.cs`、`HanMate.Infrastructure.Tests/Content/GrammarPersistenceTests.cs`。执行命令：

```text
dotnet test HanMate.Core.Tests/HanMate.Core.Tests.csproj -c Release --no-restore
dotnet test HanMate.Infrastructure.Tests/HanMate.Infrastructure.Tests.csproj -c Release --no-restore
```

此项不代表语法编辑、Ruby 排版、例句播放或素材终审完成。
