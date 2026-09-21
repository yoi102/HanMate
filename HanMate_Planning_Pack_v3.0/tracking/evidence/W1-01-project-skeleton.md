# W1-01 · Core / Infrastructure / App 骨架

执行时间：2026-09-14。结果：PASS。

新增并加入 `HanMate.slnx`：

- `HanMate.Core`：纯 `net10.0` 领域模型、验证规则和接口；不引用 MAUI。
- `HanMate.Infrastructure`：纯 `net10.0`，只引用 Core；当前提供经过领域验证的 JSON codec。
- `HanMate.Core.Tests`：只引用 Core。
- `HanMate.Infrastructure.Tests`：引用 Infrastructure 与 Core。
- `HanMate.App`：保留原 MAUI 工程并引用 Core/Infrastructure。

根 `Directory.Packages.props` 集中当前 NuGet 版本；`global.json` 锁定稳定 SDK。应用标题从模板 `HanMate.App` 对齐为 `HanMate`，ApplicationId 暂不冒充已确认发行 ID。

验证：两个 Release 测试工程合计 13/13 PASS；App 的 Windows、Android、iOS managed target Debug/Release 编译均 0 warnings、0 errors。平台运行边界见 W0-01 证据。
