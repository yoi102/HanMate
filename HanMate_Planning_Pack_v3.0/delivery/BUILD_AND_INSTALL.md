# 构建、校验与安装

## 从源码快照复现

在Windows解压源码ZIP，保留全部目录结构。使用.NET SDK 10.0.401、MAUI Windows 10.0.20、Android工作负载36.1.69、Android SDK36/build-tools36.0.0、JDK21。实际环境及NuGet版本/内容哈希分别在environment-lock.json、Directory.Packages.props和各项目packages.lock.json中。

需要Python3；普通构建直接使用已随源码归档的资源，不需要下载词库、录音或模型。重建整套开发交付：

```powershell
./tools/build_internal_delivery.ps1 -OutputDirectory C:/HanMate-builds/internal-next -Python python
```

输出目录必须不存在。脚本执行locked restore、Windows Release publish、Android Release build、权限/备份/分享范围检查、内部包生成与哈希核对，顺序运行。它不安装应用、不操作现有数据库、不执行应用测试、不发布到外部服务。构建日志在输出的build目录。

若只构建应用，先 `dotnet restore HanMate.slnx --locked-mode`，再分别运行：

```powershell
dotnet publish HanMate.App/HanMate.App.csproj -c Release -f net10.0-windows10.0.19041.0 --no-restore -p:RuntimeIdentifierOverride=win-x64 -p:PublishDir=C:/HanMate-builds/windows/
dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-android --no-restore
```

避免在正在运行的应用目录覆盖构建。NuGet与SDK/workload安装可能联网；不是运行应用时上传用户数据。资源生成脚本和原始稿随源码保存，但完整上游音源缓存未打包；重生成资源需要单独准备其构建依赖/来源缓存，已有WAV与course.json可直接编译。

校验交付目录：

```powershell
python tools/package_internal_delivery.py verify --output C:/HanMate-builds/internal-next/delivery
```

## 安装边界

Windows解压运行；保留旧目录便于回退二进制。当前运行条件沿用开发环境，没有宣称MSIX签名或干净系统安装通过。应用仍使用原身份与本机数据目录，不在启动时清库。

Android必须先核对已有安装的包名、版本、签名证书和备份。需要安装时，由设备所有者允许正常安装确认。`INSTALL_FAILED_UPDATE_INCOMPATIBLE`或系统拒绝时停止，保留旧安装；不要卸载、改Application ID或清除数据以绕过。开发签名不等于正式上架签名，源码快照不附带私钥，另一台机器产生的开发证书可能不同。

测试本轮按用户要求后置。新的构建成功不自动继承D7真机/UI测试结论，也不能称为已完成升级安装验收。正式发行时再完成身份、签名、净机安装/升级、适用P0和商店审核。
