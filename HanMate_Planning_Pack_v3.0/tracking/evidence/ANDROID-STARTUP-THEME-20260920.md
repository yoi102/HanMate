# Android 启动主题修复 · 2026-09-20

用户报告启动时 `Java.Lang.IllegalArgumentException: This component requires that you specify a valid TextAppearance attribute`，随后明确授权仅在模拟器覆盖安装并验证启动。

## 定位与修复

- 读取既有手机和模拟器日志，均定位到 `ThemeEnforcement.checkTextAppearance → NavigationBarView → BottomNavigationView → PlatformInterop.createNavigationBar → ShellItemRenderer.OnCreateView`。这是创建底部导航时的运行时异常，不能以此前编译成功判定启动正常。
- 原清单仅 Activity 使用 `Maui.SplashTheme`，Application 未指定主题。为 Application 添加 `@style/Maui.MainTheme.NoActionBar`，补齐应用上下文的 Material 样式；Activity 继续使用 MAUI 启动主题。
- 核查固定 MAUI 10.0.20 / Material 1.12.0.5 的实际资源与 MAUI 对应版本源码；打包资源中主题链为 `Maui.MainTheme.NoActionBar → Maui.MainTheme → Maui.MainTheme.Base → Theme.MaterialComponents.DayNight`。未关闭 Material 主题校验或修改依赖。

## 验证

- PASS：Android Debug 构建，0 警告 / 0 错误。命令：`dotnet build HanMate.App/HanMate.App.csproj -f net10.0-android -c Debug --no-restore -p:EmbedAssembliesIntoApk=true -v:minimal`。
- PASS：`emulator-5554` 使用 `adb install -r` 覆盖安装，未清除数据。冷启动 `Status: ok`，进程 PID 15057；首页“声母”和拼音按钮显示，底部“拼音/学习/搜索/收藏/设置”五标签均在 UI 树与截图中确认。
- PASS：本次启动进程日志未出现 TextAppearance / ThemeEnforcement / FATAL / UNHANDLED 异常，截图采集时进程仍存活。
- NOT RUN：实体手机修复包安装/启动、音频/录音、其他页面及全量回归、Release 构建、Windows/iOS。本轮仅 Android 清单改动，未重复无关测试。

原始日志、截图、UI 树及 APK 摘要位于 `artifacts/android-theme-20260920/`。历史崩溃日志只保留匹配主题异常的片段。当前工作包仍 45/48，原 176 聚合 NOT RUN、82 条执行记录与 HM-D019 保持。
