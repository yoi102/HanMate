# W1-05 / W2-01 · 随附草稿与拼音练习

日期：2026-09-17。W1-05 DONE；W0-02、W0-04、W1-06、W2-01 IN_PROGRESS。这是开发预览实现，不是正式素材/三平台发行验收。

## 实现

- 63 项拼音表：21 声母、y/w 两个拼写辅助、6 单韵母、9 复/特殊韵母、9 鼻韵母、16 整体认读音节。161 个独立例字均为 schema v2 ContentDocument，稳定 ID 和 unit/token/拼音文本元素范围闭合；没有用轻声填四声缺项。
- 原生四声详情页、短词 Ruby 上拼音下汉字；只对目标声母/韵母范围标红并加下划线，ü 在 yu/yue/yun 的省点拼写有说明。日/英辅助译文随设置，中文隐藏译文行。Android 长按 500 ms，拖动取消；“例字”按钮始终可用，Windows 另有右键。宽屏、读屏、200% 字号和长文仍需后续验收。
- 6 段有逐文件授权的离线 PCM16 WAV，包含 ba 的四声与 pa 的二/四声。原始 Ogg SHA1 与 Commons 元数据一致；每段保留原始/转换 SHA256、作者、具体许可、来源、时长和改动说明。来源页面可见署名；缺音频保持禁用，不用静音或英文字母 TTS 补齐。
- `PlaybackCoordinator` 对新请求先取消/等待旧会话，相同目标再点停止、owner 隔离、迟到完成不覆盖当前状态。原生 backend 校验文件 SHA256 后播放；完成/取消/超时均释放 Stop+Dispose，清理失败后禁止继续重叠播放。页面退出、Shell 导航/切 Tab、窗口后台和销毁均请求停止。
- Plugin.Maui.Audio **4.0.0** 集中版本管理；读取实际安装包 XML 核对 CreateAsyncPlayer(Stream)、PlayAsync(CancellationToken)、Stop/Dispose，未把预览 5.x 当稳定版。
- `BundledResourceCatalog` 只信任嵌入且哈希固定的两个包，复用严格安装校验与 SQLite 事务。20 条学习草稿（15 词、3 语法、1 文、1 诗）和 3 条字典草稿。既有资源停用/缺失/卸载不自动恢复；外部输入不能抢占随附资源 ID。学习按类型仅查询 learning 资源，设置中的管理列表保留两种资源。

## 自动验证

Core **89/89**、Infrastructure **48/48**，合计 **137/137 PASS**。新增覆盖课程引用/高亮、每个实际 WAV 哈希/PCM/时长、会话替换/取消/owner/清理失败、实际 SQLite 装载/重启/过滤、已停用记录、外部随附身份冒用。

独立 Python Draft202012 schema 校验 **161/161 PASS**；三份 resx **118 个键**的集合/非空/格式占位符一致；6 个原始 Ogg 的 SHA1 与 Commons API 元数据一致。音频不静音与容器验证不等于人工试听或教学审批。

```text
dotnet test HanMate.Core.Tests/HanMate.Core.Tests.csproj -c Release --no-restore
dotnet test HanMate.Infrastructure.Tests/HanMate.Infrastructure.Tests.csproj -c Release --no-restore
dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-android --no-restore
dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-windows10.0.19041.0 --no-restore
dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-ios --no-restore
```

构建及最终 UI 回归结果在下方登记；普通 Release 配置用于开发验证，不能自动提升 draft 为正式发行素材。

## Android 实际观察

Android 16 / API 36、Pixel 7 x86_64 模拟器 emulator-5554，通过 ADB 更新 Release APK 并保留已有数据。

- 拼音页显示“63 个项目 · 6 段离线录音 · 开发预览”。点击 b 后从播放状态到“播放结束”；日志出现 raw decoder、44,000 Hz、单声道输出，AudioTrack 完成 37,400 frames，NuPlayer reset complete。证明执行了原生播放与释放；未声称人工监听音质。
- 长按 b 650 ms 打开四声页，不补发 click；返回后再次点击 b 可播放。例字分别为八/拔/把/爸，截图中 b 为红色下划线，调号和 a 未被误标。按钮与汉字区域可触发例字音轨，快速切第三/第四声后返回，后续点播仍可正常完成。
- m 缺录音时打开实际例字页并显示 Recording not available；英文为 mother/hemp 等译文，中文没有辅助译文行。四声未选字与已选字但缺录音是两个独立状态。
- 学习页实际装载随附草稿，词语分类出现 16 条（随附 15 + 上阶段外部学习包 1），未混入随附/外部两个字典的 6 条。课文为“一起学习”1 条，打开后中文拼音与日译正确显示。

截图：[Android 四声目标高亮](W2-01-android-pinyin-tones.png)。完整来源与 155 段音频缺口见 [资源实查](RESOURCE_SOURCES-20260917.md)。

## 修复后最终回归

Windows/Android Release 和 iOS managed simulator 目标最终构建全部 PASS，0 warnings / 0 errors。Android 最终 APK 已安装并运行；Windows 新页面交互沿用前轮工具阻塞未重试，iOS 仅 managed 编译。

- 修复保留的四声页在切 Tab 改语言后不刷新的问题：同一页日语→英语→中文实际切换通过，分别出现日译“八”、英文 eight、中文无译文行；标题/说明一起刷新。
- 修复 Shell 切 Tab 不可靠触发已 Push 页面退出回调的问题：播放 bā 后立即切设置，日志在约200ms内出现 pause、reset、notifyResetComplete；不继续后台占用播放器。窗口后台事件已接入，但本轮未另做来电/耳机切换/真实录音验证。
- 从 b 按下并拖动650ms后仅滚动拼音列表，没有误开例字页。
- 最终学习分类：词语16（随附15+外部1）、课文1、语法6（随附3+外部3）、诗词1；字典6条不混入学习分类。诗词预览保留两行中文，简中无译文行。
- force-stop 后重新启动：语言仍简中，管理列表“正在显示第1–30条”，随附23条与前轮外部7条均保留，没有重复安装。截图：[重启后的资源列表](W1-05-android-starter-reopen.png)。

以上为聚焦 PASS；全63项逐个听辨、200%字号、读屏、iOS/Windows手势和真实麦克风试验均 NOT RUN。

## 未完成

W2-01 尚缺多数示范音频、正式声母教学读法及全平台手势/键盘/焦点验收；W0-04 尚缺真实麦克风录音、权限/TTS/输出切换；W0-02/W1-06 尚缺长文混排/200%/性能与读屏。Windows 新交互沿用 W1-09 的工具阻塞，iOS 运行和 Android 真机 NOT RUN。没有把这些任务标 DONE。

内容仍为草稿，日英译文、读音及音文一致性待人工审校。拼音课程暂为不可变应用资产，未接入资源卸载/用户改音/通用音轨绑定；外部音频资源包仍整包拒绝。正文阅读器、查词、录音、资源管理生命周期和完整迁移继续按 backlog 推进。176 项完整验收未由本次聚焦测试替代。
