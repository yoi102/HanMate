# W1-02 · 内容契约与验证器

执行时间：2026-09-14。结果：PASS（内容文档层；包级全局闭包仍属于 W1-09/W4-01）。

实现：

- ContentDocument v2 的四类型、来源、TextUnit、Token、Segment、拼音和 Grammar C# DTO。
- 严格 camelCase 枚举 JSON 映射、未知字段拒绝和 null 字段省略。
- 基于 `StringInfo.ParseCombiningCharacters` 的文本元素到 UTF-16 边界表与安全切片。
- 稳定 ID 非空/文档内唯一、token/segment 全覆盖且可还原、语法角色全引用、学段/年级和正文/标题上限。
- 拼音 base/tone/erhua 到 NFC 带调 display 的一致性检查，覆盖 `iu`、`ui`、`ü`、轻声和儿化样例。
- Infrastructure JSON codec 在写正式流之前验证，失败保持输出为空。

验证命令：

```text
dotnet test HanMate.Core.Tests/HanMate.Core.Tests.csproj -c Release
PASS 11/11

dotnet test HanMate.Infrastructure.Tests/HanMate.Infrastructure.Tests.csproj -c Release
PASS 2/2
```

测试直接加载规划包的 `contents.json`、`grammar-content.json` 和 `dictionary-entry.json`；24 条 ContentDocument 全部通过 C# 验证。负例覆盖 UTF-16 边界漂移、grammar 角色错配和写出前拒绝无效内容。

未宣称完成：跨文档/包全局 ID、ZIP 路径和哈希、音频闭包、资源拥有关系、数据库投影及 v1 转换。这些按 W1-03/W1-09/W4 实施。
