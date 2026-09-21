# W2-05 / W2-09 离线查词与来源查询

日期：2026-09-17。本阶段实现查词，不改变正式教材/音频审校状态。源码复用同级HanMate.App/Core/Infrastructure，无Git元数据。

## 已实现

- 汉字精确、前缀、包含；连续/分音节/隔音符拼音、带调字符、组合标记、数字声调0/5、ü/v/u:。普通u不变成ü，声调与边界显式约束严格匹配。本阶段不返回放宽声调的近似项。
- 个人word、启用且存在的学习word和字典word联合检索；SQL排除removed、retained、missing及grammar/text/poem。查询字典不修改独立自动注音引擎。
- 匹配等级先于来源优先级，再用已有frequencyRank（无则空）、元素长度、来源/内容ID稳定排序。同contentId多个alias取最佳行；跨来源同名内容不合并。
- 参数化SQL、LIKE转义；全部候选完成语义匹配后去重分页，50条初页、真实总数及稳定游标。epoch不符返回SEARCH_CURSOR_STALE，页面重新查，不拼接过期结果。
- WordSearchIndex由内容保存及包安装共用；个人保存与索引同事务，失败回滚正文/修订/epoch。缺失或不支持版本索引从正文事务重建；触发器故障注入验证没有半成品发布。
- 原生底部输入框、防抖250ms、Enter立即查询、清空取消、来源选择、上/下页；结果进入原生阅读器，返回保留查询/来源/页码。语言切换刷新译文，中文隐藏辅助行。Shell导航和generation检查拒绝迟到结果与导航。

## 自动化与构建

| 验证 | 结果 |
|---|---|
| Core Release | PASS：140/140，新增38项查询语义用例 |
| Infrastructure Release | PASS：63/63，新增15项实际SQLite查询、fixture、事务及基准用例 |
| Windows Release | PASS：0警告/0错误 |
| Android Release | PASS：0警告/0错误，APK覆盖安装保留数据 |
| iOS simulator managed目标，Windows宿主 | PASS：0警告/0错误；非Mac/Xcode原生运行 |
| 三语言resx | PASS：149键，键集、非空与格式占位符一致 |
| 61词设备测试包 | PASS：manifest/resource/contents/audio四JSON独立Python jsonschema验证；实际严格.NET安装通过 |

回归覆盖禁用、撤下、恢复、缺失状态、跨来源同词、来源优先级不压过精确结果、多alias长度排序、超过100条无重复分页、旧cursor拒绝、取消、SQL字面转义、未确认读音、重建及保存失败回滚。初版SQL空格/转义错误已修复；随附读音未确认的错误测试预期改为保留真实状态，并另加确认读音fixture测试，没有提升素材审校状态。

```text
dotnet test HanMate.Core.Tests/HanMate.Core.Tests.csproj -c Release --no-restore
dotnet test HanMate.Infrastructure.Tests/HanMate.Infrastructure.Tests.csproj -c Release --no-restore
dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-windows10.0.19041.0 --no-restore
dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-android --no-restore
dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-ios --no-restore
```

## 实测性能与查询计划

Windows开发机、.NET10 Release、真实Microsoft.Data.Sqlite、临时数据库。10,000条合成已确认word，刻意重复“中国”词头，使命中查询排名全部10k候选。这不是正式词典或10k包安装/跨实体图验证。预热后11种查询各5次，计入打开连接、过滤、排名和初页正文读取，不含UI防抖/渲染。

| 查询 | 最新中位数ms | 最新最大ms |
|---|---:|---:|
| 中国 | 56.47 | 65.03 |
| 中 | 60.98 | 67.70 |
| 国 | 58.66 | 63.16 |
| zhongguo | 63.29 | 67.36 |
| zhong1guo2 | 53.32 | 69.58 |
| zhōngguó | 51.09 | 55.02 |
| zhong | 49.97 | 52.33 |
| guo2 | 50.31 | 51.62 |
| nu: | 6.41 | 7.05 |
| xi'an | 5.92 | 6.08 |
| %（非法纯符号） | 0.53 | 0.55 |

最新55次P95 **67.36ms**、最大69.58ms；前一次完整回归P95 **122.34ms**、最大137.25ms。两次低于本机200ms目标，受JIT/并发测试/环境影响，不保证全平台性能。基准保存在OfflineSearchStoreTests.TenThousandWordSqliteBenchmark。

`EXPLAIN QUERY PLAN SELECT content_id FROM search_index WHERE pinyin_joined LIKE '%zhong%';` 返回 `SCAN search_index USING COVERING INDEX idx_search_pinyin`。包含路径确实扫描，未宣称精确/前缀已独立使用B-tree范围加速。手机10k/100k容量仍NOT RUN。

## Android聚焦运行

Android16/API36、emulator-5554，Release实际操作。正常系统选择器安装61条“搜索回归测试（非教材）”；原32条保留，现93条，随附目录保持23条。

| 操作 | 观察 |
|---|---|
| 搜索页进入资源安装并返回 | 61词安装成功，来源选择立即出现测试字典 |
| ni3hao3 | 55条你好，首屏1—50，下一页51—55；末页下一页禁用 |
| 第二页打开你好并返回 | 原生注音/例句正常，返回仍51—55、查询保留 |
| 选择随附学习草稿，再选测试字典 | 前者未确认读音不命中，后者55条、重置第一页 |
| nu: er / nu | 前者女儿、nǚ ér一条，后者空态 |
| xian / xi an | 前者先与西安两条，后者仅西安 |
| Enter | 留在列表，不自动打开第一条 |
| 最终APK再次覆盖安装、重启后输入nihao | 简中设置保留、测试字典仍在，返回55条 |
| 点击输入框清空按钮 | 输入和结果一起清空，显示输入提示，分页按钮禁用 |
| 简中→英文→日文→简中 | 查询/来源保留，英文Xi’an、日文西安辅助译文显示；中文隐藏辅助行 |

已查看截图：[第二页与总数](W2-search-android-page2.png)、[英文译文](W2-search-android-english.png)。不是仅根据UI树推断。

脚本tools/build_search_fixture.py只输出到显式临时路径，不改随附目录。resourceId `20a2c017-c411-5bcc-aabb-740925b877eb`；61个工程词语用于55条分页和边界测试。source保留draft/testFixture，confirmed只是回归输入条件，不是教材审校声明。

## 剩余门禁

Windows/iOS新搜索交互、Android真机、手机10k性能、100k容量、完整键盘/读屏、中文IME组合输入及通常软键盘布局NOT RUN。模拟器只观察到浮动输入工具条，未计为常规全键盘适配通过；show_ime_with_hard_keyboard恢复原值0。Windows工具前轮阻塞未重试。

随附词头尚未全部confirmed，可按汉字查词但没有完整可发布拼音字典，页面明确说明；没有为增加命中而改needsReview。W2-08启停/排序/卸载页面、受引用保护、正文音频、编辑录音、分享和迁移仍待实现。拼音录音沿用6段/155缺口，本轮无新增下载。176项完整应用验收保持NOT RUN、R10继续IN PROGRESS；W2-05/W2-09基础工作包DONE不替代这些门禁。
