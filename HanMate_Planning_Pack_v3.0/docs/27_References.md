# 27 · 参考资料与核查边界

> HanMate 文档包 v3.0 · 2026-09-14 · 状态：开发基线提案，非已实现报告。
> 正式名称：HanMate / 汉语小伴；R01—R22 为需求基线，其余明确标注的规则为可调整默认设计。

## 1. 使用原则

上一版记录的核查日期：2026-09-14；本次文档复审未重新联网核查，以下为待实施时复核的参考索引。技术参考优先使用官方文档、项目维护者仓库与规范原文。需求、容量、交互、协议和阶段安排是本项目设计，不是引用资料规定的通用标准。动态页面与 main 分支后续可能变化，M0 应记录实际采用的版本或 commit。

本包没有编译第三方依赖，没有执行三平台真机验证，也没有完成素材法律审核。列出某个仓库不表示它的全部文件都可以不经核查分发。引用只支持相应技术说明，不支持测试通过或授权结论。

## 2. 来源索引

### S01 · Microsoft：.NET MAUI 支持策略

`https://dotnet.microsoft.com/en-us/platform/support/policy/maui`

核查受支持 MAUI 版本及支持周期；开工与上架时再次核对。

### S02 · Microsoft Learn：支持平台

`https://learn.microsoft.com/en-us/dotnet/maui/supported-platforms?view=net-maui-10.0`

平台、开发工具和 iOS Mac 环境；不要混用页面不同版本的最低系统要求。

### S03 · Microsoft Learn：Localization

`https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/localization?view=net-maui-10.0`

resx 与平台本地化；即时切换仍需应用实现。

### S04 · Microsoft Learn：Text-to-Speech

`https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/device-media/text-to-speech?view=net-maui-10.0`

系统语音、选项、取消与队列；不保证人工注音控制或任意设备离线。

### S05 · Microsoft Learn：FilePicker

`https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/file-picker?view=net-maui-10.0`

通过流读取文件，不假定 FullPath 永久物理路径。

### S06 · Community Toolkit：FileSaver

`https://learn.microsoft.com/en-us/dotnet/communitytoolkit/maui/essentials/file-saver`

系统文件保存候选，取消/权限要实测。

### S07 · Microsoft Learn：Share

`https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/data/share?view=net-maui-10.0`

系统分享；只开放本次导出文件，不暴露整个应用目录。

### S08 · Community Toolkit：TouchBehavior

`https://learn.microsoft.com/en-us/dotnet/communitytoolkit/maui/behaviors/touch-behavior`

长按候选与绑定行为；滚动冲突需验证。

### S09 · Microsoft Learn：Local databases

`https://learn.microsoft.com/en-us/dotnet/maui/data-cloud/database-sqlite?view=net-maui-10.0`

SQLite 本地持久化候选。

### S10 · Plugin.Maui.Audio 官方仓库

`https://github.com/jfversluis/Plugin.Maui.Audio`

播放/录音候选；具体版本和平台格式未在本包内验证。

### S10r · Plugin.Maui.Audio 录音说明

`https://github.com/jfversluis/Plugin.Maui.Audio/blob/main/docs/audio-recorder.md`

录音接口与平台实现参考。

### S11 · SQLite：Transactions

`https://www.sqlite.org/lang_transaction.html`

数据库事务边界，不覆盖外部文件。

### S12 · SQLite：CREATE INDEX

`https://www.sqlite.org/lang_createindex.html`

唯一索引和 NULL 行为。

### S13 · SQLite：Foreign Keys

`https://www.sqlite.org/foreignkeys.html`

外键开启、关联约束与复合外键。

### S14a · pinyin-data 维护者仓库

`https://github.com/mozillazg/pinyin-data`

单字读音及上游来源；采用前逐文件核查许可。

### S14b · phrase-pinyin-data 维护者仓库

`https://github.com/mozillazg/phrase-pinyin-data`

词组字音数据参考。

### S14c · python-pinyin 维护者仓库

`https://github.com/mozillazg/python-pinyin`

理解词组/候选能力，不要求移动端运行 Python。

### S15 · Microsoft Learn：StringInfo

`https://learn.microsoft.com/en-us/dotnet/api/system.globalization.stringinfo?view=net-10.0`

文本元素及 UTF-16 起点映射。

### S16 · Android：Storage Access Framework

`https://developer.android.com/training/data-storage/shared/documents-files`

文档选择与 URI 授权边界。

### S17 · Apple：App privacy details

`https://developer.apple.com/app-store/app-privacy-details/`

上架隐私披露；实际行为和 SDK 需独立核对。

### S18 · Google Play：Data safety

`https://support.google.com/googleplay/android-developer/answer/10787469`

数据安全说明；不是本项目已完成合规的证明。

### S19 · 《汉语拼音方案》原文转录

`https://zh.wikisource.org/wiki/汉语拼音方案`

用于核对基本拼写、声调和儿化；转录页面不替代教学素材终审。

### S20 · Microsoft Learn：App lifecycle

`https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/app-lifecycle?view=net-maui-10.0`

生命周期事件；实际中断恢复按平台测试。

### S21 · Microsoft Learn：Custom layouts

`https://learn.microsoft.com/en-us/dotnet/maui/user-interface/layouts/custom?view=net-maui-10.0`

自定义测量和布局方案参考。

### S22 · JSON Schema：Draft 2020-12

`https://json-schema.org/draft/2020-12`

数据形状验证；跨引用、范围和文件校验仍需业务验证器。


## 本次需求修订的来源边界

v3.0 的正式命名、语法、四类内容增删、字典管理和分享来自用户本轮确认。资源协议、删除语义、快照保护、版本迁移和容量为本项目默认设计，不是外部标准规定。

技术来源列表沿用上一版归档记录，本次是需求与契约修订，没有重新联网核查全部网址或最新 SDK/插件版本。实施 M0 必须针对实际构建环境重新锁定和验证；不能把旧核查日期转换成当前真机测试证据。
