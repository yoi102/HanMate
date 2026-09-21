# W1-06 · 原生分页注音阅读器

日期：2026-09-17。基础控件与阅读集成已交付，W1-06 DONE；W0-02 继续跨平台技术试验。W2-03/W2-04/W2-07 仅完成只读呈现与选择，播放/收藏等完整交付仍 IN_PROGRESS。设计调整见 ADR-036—038。

## 实现

- `HanMate.Core/Reading/ReadingDocument.cs`：对已验证 ContentDocument 建立只读投影。最多96个注音单元/页；保留完整原文、文本元素范围、unit/token/segment ID，不将视觉页变为新的语义片段。无拼音长串按字素拆为显示单元，组合字符/Emoji/CRLF保持原样；未知汉字注音与不需拼音的符号分开。
- `RubyLineLayout.cs`：基于平台实际测量的宽高换行，不拆汉字与拼音对；优先保留标点与邻字、开引号与后字。极窄宽度/过长无拼音串按字素应急换行，确保进展；硬换行与末尾空行保持。
- `HanMate.App/Controls/RubyTextView.cs`：原生Layout/Label测量和排列，只为当前页创建有限控件；布局在MAUI UI线程同步运行，无异步过期布局覆盖。暴露原文语义与显式放大/上一片段/下一片段入口，TalkBack等完整验收仍待执行。
- `Pages/ReadingPage.cs`：四类资料共用阅读器，拼音开关、100%—200%阅读字号、前后页/页码跳转、点击目标放大、上一/下一片段、来源状态和辅助译文。关闭放大页后保留原阅读页上下文。Shell保留页面时，通过Handler生命周期订阅语言变化，不能只依赖OnAppearing刷新。
- 文本/诗词点击speech segment；词语与语法按完整unit选择，避免把含逗号的例句拆成另一朗读目标。句型的literal/slot/operator以独立结构单元展示，槽位不交给朗读；所选子段只有自己的译文，没有时不借用整个unit译文。
- `ResourceLibraryPage` 将原先拼接空格的上下行预览替换为后台解析/验证/投影后的真实阅读器，取消或退出时不导航到迟到结果。正文仍只读，无数据库/schema/协议变化；已有文字安装/草稿目录不重写。

当前96单元分页是明确的产品呈现：有页码和跳转，20,000字完整保留，不以“首屏预览”冒充全文。连续滚动虚拟化不作为基础控件的硬前置，仍可后续替换。页面始终明确无匹配离线音轨，没有假播放按钮或Latin拼音TTS回退。

## 自动验证

本轮Core **102/102 PASS**，新增13例，覆盖：

- 20,000字/209页、原文及ID不变、每页不超过96单元；同一超长语义片段在放大后仍可读全部209页。
- 多种原有四类型夹具完整还原，语法和词语目标保持整个unit。
- 稳定segment选择不混入相邻文字，未知目标拒绝；未知汉字注音与符号区别。
- Emoji ZWJ、组合调号、CRLF、空行、长无拼音token、100%/200%几何及标点/极窄宽度。

```text
dotnet test HanMate.Core.Tests/HanMate.Core.Tests.csproj -c Release --no-restore
dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-windows10.0.19041.0 --no-restore
dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-android --no-restore
dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-ios --no-restore
```

Windows/Android Release与Windows上的iOS simulator managed目标均构建PASS，0 warnings / 0 errors。未新增依赖；新布局API已核对锁定MAUI 10.0.20实际XML。iOS结果不是Mac/Xcode原生构建或运行证明。

三份resx **141个键**集合、非空、无重复及格式占位符一致PASS。设备测试包的四个JSON经独立Python Draft202012 schema校验PASS。Infrastructure代码没有修改，最近一次独立测试仍为前阶段48/48；本轮未重跑该测试集。

## Android实际运行

Android16/API36、Pixel7 x86_64模拟器，Release APK通过ADB更新、保留既有数据。未用截图或文本数量反推音频审核/真实设备性能。

| 操作 | 观察 |
|---|---|
| 学习→课文→一起学习 | 真正逐字注音，标点不伪造拼音；100%字号 |
| 点击“欢迎学习中文。”中的字 | 放大到150%，显示片段2/2，只显示欢迎学习中文，不带前面的你好；后一片段到边界禁用 |
| 安装明确标为非教材的回归包 | 2条：20,000字、混排/空行；走系统选择器和真实严格安装链，显示安装2条成功 |
| 打开20,000字 | 完整209页；放大同一语义片段仍为209页，没有截断为96字 |
| 下一页、页码选择209 | 显示209/209，最后32字包含31个“中”和末尾“终”；下一页禁用 |
| 放大到200%并滚到末尾 | 实際原生拼音/汉字一起换行，末尾“终”及拼音未丢失；本次为应用阅读字号200%，不是系统字体200%验收 |
| 隐藏拼音 | 汉字重新排版，拼音行消失，仍209/209；回到原阅读页仍是原先1/209 |
| 混排与空行 | 女儿/绿色带调，CRLF空行、引号、ABC、3.14、10:30、n+组合调号、Emoji、日文均显示；长日文行可换行 |
| 保留阅读页，简中→英语→日语→简中 | 控件文案刷新、中文/注音不变；英文Reader test data.、日文对应译文出现，中文隐藏译文及留白 |
| 语法“在”表示位置 | 句型槽位/连接符分开显示，中文说明与例句使用同一个真实注音控件 |

截图：[选中片段](W1-06-android-selected-reading.png)、[20,000字最后一页200%](W1-06-android-20k-last-page-200.png)、[混排与空行](W1-06-android-mixed-reading.png)。

测试包由 `tools/build_reader_fixture.py` 生成到开发机临时目录后复制到模拟器Download；未进入应用随附目录。resourceId为`a6f8dca4-00ce-57e3-bbad-1f285bd40114`，名称“阅读器回归测试（非教材）”。模拟器因此在原30条基础上增加2条测试内容，不计入正式素材数量；原30条和语言设置保留，未清库。

## 剩余边界

Windows/iOS阅读交互、Android真机、系统字体200%、横屏/窗口极窄、完整键盘/读屏、字体缺字和性能/内存峰值NOT RUN；Windows交互工具前轮阻塞未在本轮重试。W0-02继续IN_PROGRESS。分页正确性和一次模拟器显示不等于完整跨平台Ruby验收。

此阶段没有接入正文音轨/TTS、全文播放队列、录音、收藏、编辑、资源删除保护或旧库迁移；W2-03/04/07保持IN_PROGRESS。拼音专页的6段录音与155段缺口沿用前阶段，本轮未重新下载或宣称补齐。完整176项验收保持NOT RUN。
