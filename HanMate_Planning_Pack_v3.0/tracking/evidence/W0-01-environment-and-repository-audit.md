# W0-01 · 环境与已有仓库审计

执行时间：2026-09-14。结果：PASS（审计任务完成；平台运行门禁另列 NOT RUN/BLOCKED）。

## 已有工程

- 工作区没有 `.git` 元数据，未执行初始化、清理或分支操作。
- 解决方案 `HanMate.slnx` 原含 `HanMate.App`，应用是 .NET MAUI 10 默认计数器模板。
- 保留现有应用目录；新增 Core、Infrastructure 和两个测试工程，没有重建 `HanMate.App`。
- 业务数据、旧数据库和已发布包：未发现。规划目录的 archive 和 examples 只是夹具。

## 锁定环境

- 新增根 `global.json`，锁定稳定 .NET SDK 10.0.401，禁用预览；修正此前默认选中 11.0 Preview 的漂移风险。
- MAUI Windows workload 10.0.20/10.0.100；Android 36.1.69；iOS 26.5.10315。
- Android SDK platform 36、build-tools 36.0.0、ADB 36.0.0；Microsoft OpenJDK 21.0.8 LTS。
- Windows SDK 10.0.26100.0；本机 Windows 11 10.0.26220 x64。
- 完整机器记录见 `tracking/environment-lock.json`。

## 实际命令与结果

```text
dotnet build HanMate.App/HanMate.App.csproj -c Debug -f net10.0-windows10.0.19041.0 --no-restore
PASS, 0 warnings, 0 errors

dotnet build HanMate.App/HanMate.App.csproj -c Debug -f net10.0-android --no-restore
PASS, 0 warnings, 0 errors

dotnet build HanMate.App/HanMate.App.csproj -c Debug -f net10.0-ios --no-restore
PASS, iossimulator-x64 managed build, 0 warnings, 0 errors

dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-windows10.0.19041.0
PASS, 0 warnings, 0 errors

dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-android
PASS, signed APK produced, 0 warnings, 0 errors

dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-ios
PASS, iossimulator-x64 managed build, 0 warnings, 0 errors
```

## 未运行与阻塞边界

- Android：ADB 可用但没有连接设备；安装、启动、权限、录音、TTS 和文件交互 NOT RUN。
- iOS：未验证 paired Mac、macOS、Xcode、签名、打包、模拟器或真机；Windows 产出 DLL 不能写成 iOS 平台通过。
- Windows：编译通过，尚未启动 UI 或执行平台验收。
- `ApplicationId=com.companyname.hanmate.app` 仍是模板占位，不作为发行身份。

审计完成允许纯逻辑开发继续；W0-02—W0-06 仍须分别完成，三平台发布门未关闭。
