# W1-03 · SQLite 与首版迁移框架

执行时间：2026-09-15。结果：PASS（新库 v2 与版本化仓储）；真实 v1→v2 用户库迁移仍属于 W4-09。

## 依赖与平台资产

- 选择 `Microsoft.Data.Sqlite 10.0.12`，集中锁定在 `Directory.Packages.props`。
- 传递依赖为 SQLitePCLRaw 2.1.12。最初试用 10.0.0 时 NuGet 报出 2.1.11 高危漏洞，已升级并消除警告。
- `dotnet list ... package --vulnerable --include-transitive` 对 Infrastructure 与 App 均报告无已知漏洞。
- Windows、Android、iOS managed target 的 Release 编译均通过；Android 链接日志包含 `SQLitePCLRaw.lib.e_sqlite3.android`。设备实际打开数据库仍随平台验收保持 NOT RUN。

## 新库与迁移安全

- 将目标21表 DDL作为 Infrastructure 嵌入式新库 migration；事务、`foreign_keys` 和 `user_version` 由迁移器控制。
- 新空库在单个 `BEGIN IMMEDIATE` 事务中建立，外键检查成功后才提交并写 `user_version=2`。
- 注入中途 SQL 错误时，已创建表和版本号全部回滚。
- `user_version=0` 但已有用户表的未知库拒绝覆盖。
- `user_version=1` 明确返回需要迁移并保持旧表原值，不把新建 DDL 当升级脚本。
- `user_version=2` 会核对关键表存在，较新或未注册版本拒绝打开。
- 每次连接执行并核对 `PRAGMA foreign_keys=ON`，悬空收藏外键实际返回 SQLite constraint 错误。

## 仓储行为

- `SqliteContentDocumentStore`：写入前领域验证；ContentDocument JSON 真源和查询列同事务保存；创建 revision=1，更新使用 expected revision 并自增；重启可读取。
- `VersionedLocalStateStore`：草稿和设置使用实际 `row_revision` 乐观锁，重启读取保持最终 JSON。
- `FavoriteMembershipStore`：最终收藏夹集合与 `membership_revision` 在一个事务中更新；不改变正文 `row_revision`。
- `ImportOperationStore`：package 身份收据与本次 operation 收据同事务提交；同一 package 可有多个 operationId，重复 operationId 的数据变更全部回滚；数据回调失败不留下 receipt。

## 验证

```text
dotnet test HanMate.Core.Tests/HanMate.Core.Tests.csproj -c Release --no-restore
PASS 33/33

dotnet test HanMate.Infrastructure.Tests/HanMate.Infrastructure.Tests.csproj -c Release --no-restore
PASS 10/10

dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-windows10.0.19041.0 --no-restore
PASS, 0 warnings, 0 errors

dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-android --no-restore
PASS, 0 warnings, 0 errors

dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-ios --no-restore
PASS, iossimulator-x64 managed build, 0 warnings, 0 errors
```

Infrastructure 的10项测试包含7项 SQLite/仓储测试和原有3个实际夹具/codec检查。平台安装、启动和真实文件目录权限没有执行，不能由这里改写应用用例状态。
