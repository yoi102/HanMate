# W1-04 · 五入口与即时本地化

执行时间：2026-09-15。结果：PASS（Windows 聚焦运行验证）；Android/iOS 仅完成 managed target 编译，完整三平台应用用例仍为 NOT RUN。

## 实现

- `HanMate.App/AppShell.xaml` 建立 `pinyin`、`learn`、`search`、`favorites`、`settings` 五个稳定路由，并由 DI 注入对应页面。
- `HanMate.App/Localization/AppResources*.resx` 提供简中、日、英三套资源。三套各 64 键，键集合、非空值和内容均与 `examples/ui-copy.csv` 对应列一致。
- `LocalizationService` 根据系统语言映射首次语言，将明确选择写入 SQLite `user_settings` 后再切换 Culture，并通过 `INotifyPropertyChanged` 刷新已打开页面和 Shell。
- `LearningPage` 的教学正文固定为 `nǐ hǎo / 你好`；辅助译文使用领域语言策略。简中时节点隐藏，日语缺失时不会回退英语。
- `App` 在加载应用资源字典后才解析 Shell 和页面，避免 DI 在 `InitializeComponent` 之前构造使用 `{StaticResource ...}` 的页面。
- 删除默认 `MainPage` 计数器页面与事件代码。

## Windows 聚焦运行验证

以 Debug Windows unpackaged 产物启动，进程持续运行且 `Responding=True`，窗口标题为 `HanMate`。通过 Windows UI Automation 实际选择 Tab 和按钮，观察到：

| 操作 | 实际结果 |
|---|---|
| 初始英文界面 | `Pinyin / Learn / Search / Favorites / Settings` 五入口均可识别 |
| 切换日语 | 已打开设置页立即变为 `ピンイン / 学習 / 検索 / お気に入り / 設定`，显示 `保存しました` |
| 打开日语学习页 | `nǐ hǎo / 你好 / こんにちは` |
| 切换简中并打开学习页 | `拼音 / 学习 / 搜索 / 收藏 / 设置`；保留 `nǐ hǎo / 你好`，辅助译文节点不存在 |
| 切换英文并打开学习页 | `Pinyin / Learn / Search / Favorites / Settings`；`nǐ hǎo / 你好 / Hello` |
| 保存日语、关闭并重启 | 首屏恢复日语入口和 `読み上げ`，证明明确语言选择持久化 |

测试完成后将本机测试设置恢复为原来的英语。

## 验证命令与结果

```text
dotnet test HanMate.Core.Tests/HanMate.Core.Tests.csproj -c Release --no-restore
PASS 40/40

dotnet test HanMate.Infrastructure.Tests/HanMate.Infrastructure.Tests.csproj -c Release --no-restore
PASS 10/10

dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-windows10.0.19041.0 --no-restore
PASS, 0 warnings, 0 errors

dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-android --no-restore -t:Rebuild
PASS, 0 warnings, 0 errors

dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-ios --no-restore
PASS, iossimulator-x64 managed build, 0 warnings, 0 errors
```

Android 重复增量构建曾触发 SDK 内部 `XAGNM7009`（缺少 Arm64 native codegen state）；执行目标 clean 后完整 `Rebuild` 通过，未改代码或降低构建条件。

Windows 启动期间先后发现并修复两项 XAML 启动异常：带点资源键的索引器 Binding 路径无法解析，以及页面在应用资源字典加载前由 DI 构造导致 `StaticResource Headline` 不可见。修复后的两次启动均稳定运行；临时未处理异常日志代码已删除。

## 边界

- Windows 本次只覆盖 W1-04 的五入口、即时三语言、固定教学正文、中文隐藏译文和语言重启保持，不代表 176 项应用用例已经完整执行。
- Android 没有连接设备；iOS 没有 Mac/Xcode/签名/模拟器或真机证据。两端运行继续为 NOT RUN。
- 正式日英文案仍需母语审校，App ID、图标、签名和商店资料仍未锁定。
