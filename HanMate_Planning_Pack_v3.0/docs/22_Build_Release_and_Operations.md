# 22 · 构建、发布与维护手册

> HanMate 文档包 v3.0 · 2026-09-14 · 状态：开发基线提案，非已实现报告。
> 正式名称：HanMate / 汉语小伴；R01—R22 为需求基线，其余明确标注的规则为可调整默认设计。

## 本轮内部开发交付（ADR-106）

2026-09-20用户确认正式审校、发行签名和商店发布后置；应用测试暂缓。使用仓库 `tools/build_internal_delivery.ps1 -OutputDirectory <全新目录> -Python <Python路径>` 顺序locked restore和Windows/Android Release构建，再组装/验证开发产物、源码快照和哈希。实际锁定版本见 `tracking/environment-lock.json`，安装/复现、隐私与恢复详见 [内部交付指南](../delivery/README.md)。正式发布条件继续按 `tracking/deferred-release-gates.json` 检查，预检当前BLOCKED。下文正式发布步骤仍保留为后续要求。

## 1. 环境锁定

实际版本记录到 `tracking/environment-lock.template.json` 的工作副本；模板中的 null 不是已经验证的版本。记录 .NET SDK、MAUI workload、NuGet、Android SDK/JDK、Xcode/macOS、Windows SDK、各目标 TFM、最低系统版本与构建命令。

开工时核对当前官方 MAUI 支持策略和目标平台要求，锁定受支持稳定版本；不混用 .NET SDK 版本、MAUI workload 版本和 NuGet 版本。[S01 / S02](27_References.md) iOS 的 Mac/Xcode 构建条件必须落实，不能仅凭 Windows 编译其他目标声明 iOS 完成。

## 2. 仓库文件

`global.json` 记录实际选定 SDK；`Directory.Packages.props` 集中依赖版本；必要的 NuGet lock 文件随仓库提交。代码、非私密内容源、测试基准、DDL 迁移和文档版本化。证书私钥、商店密码、用户录音及个人备份不提交 Git。

建议分支短生命周期，小范围 PR；数据格式变化必须附 ADR 和迁移/兼容用例。不把生成的整个 bin/obj、系统缓存提交入库。中文资源 UTF-8，保持稳定换行策略但不自动改写用户正文样本。

## 3. 启动检查命令

以下是环境确认命令，不是本次已运行 MAUI 的证明：

```bash
dotnet --info
dotnet workload list
dotnet restore HanMate.slnx
dotnet test tests/HanMate.Core.Tests/HanMate.Core.Tests.csproj -c Release
dotnet test tests/HanMate.Infrastructure.Tests/HanMate.Infrastructure.Tests.csproj -c Release
```

项目尚未创建时不能运行后两个项目路径。AI 应先读取真实 `.csproj` 和环境，再填写可复制的实际构建命令。针对平台使用锁定目标的 `dotnet build/publish -f <实际TFM>`；`<实际TFM>` 是说明占位，不要原样执行。

## 4. CI 分层

纯 Core/Infrastructure 测试不依赖 UI，先在合适 runner 执行；Windows/Android 构建和 iOS Mac 构建分作业；签名材料通过 CI secret 注入。Pull request 不输出机密，不向不可信分支提供发行凭据。

CI 先校验资源键、schema、内容范围和许可字段，再编译与测试。未安装麦克风/语音包的 runner 无法完成真实录音验收，应标记未覆盖而非 Mock 替代全部门槛。平台测试和人工内容审核保留独立状态。

## 5. 发布前清单

确认版本号、数据 schema、目录内容版本、迁移路径、清单容量限制及错误文案；执行全新安装、已有数据升级、恢复旧备份、卸载后从外部备份恢复。检查离线资源确实进安装包，Release 裁剪/AOT 不破坏反射序列化或依赖。

商店截图只展示真实功能；隐私披露按实际系统语音、第三方 SDK 与数据流填写，不照抄“完全不收集”模板。Apple 与 Google 的平台披露要求应在上架前重核。[S17 / S18](27_References.md)

## 6. 版本回退与用户数据

回退应用二进制不代表旧程序能读取新数据库；必须定义当前 schema 的最低读写版本。升级前自动安全备份和外部备份说明不可省略。遇到迁移失败保留旧数据与诊断，禁止启动时删库重建。

内容目录可独立修复，但不得覆盖用户改音版本。撤回错误素材时保留相关收藏/音轨所需快照或迁移映射；不能移除 ID 后让用户录音永久失联。

## 7. 发布证据

归档 commit、构建环境锁、签名产物哈希、测试矩阵、内容审核清单、依赖许可、已知限制及安装/恢复步骤。发布包签名由真实账号持有人配置；本规划不提供或假定已有证书、App ID、商店账号。


## 8. 正式名称与新增发布门禁

产品名 HanMate，中文显示汉语小伴；日/英品牌仍为 HanMate。图标、Bundle ID/Application ID、商店描述、隐私页品牌统一。没有用户真实发布配置时不编造反向域名，不因改中文名就更改签名身份。

发布内容清单需四类素材及至少一套基础查询字典，所有许可证与真实音频可追溯。安装包预置资源和外部资源使用同一格式验证，可信 bundled 来源由应用打包清单建立，不受外部文件自称影响。

增加资源更新回滚、撤下状态跨升级、v1包兼容、语法收藏与分享回归。不能把“以后可下载”写成当前商店宣传的在线下载服务；目前只有本地包管理，后续上线需更新隐私与网络行为声明。
