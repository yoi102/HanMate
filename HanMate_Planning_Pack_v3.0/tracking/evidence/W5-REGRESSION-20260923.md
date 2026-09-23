# W5 聚焦回归增量 · 2026-09-23

HM-D019 已按用户要求以 `CLOSED_BY_REQUEST` 结案，原因与残余风险见 [结案决定](HM-D019-CLOSURE-20260923.md)。本记录只补充非 iOS 范围的实际执行结果，不把一次平台操作扩写为原 176 用例完整通过。

## 前序完整包与自动回归

- Core 测试 TRX：393/393 PASS；Infrastructure 测试 TRX：378/378 PASS。原始报告位于 `%TEMP%\HanMate-regression-20260923\`。内容审核 Python 测试 10/10、拼音测试 3/3 PASS；审核测试的旧对象数量断言按当前资源修正，未降低审核门禁。
- Windows x64 与 Android x64 Release 构建均为 0 警告、0 错误。Windows 可执行文件 SHA256 为 `2CCCAE13EB3DF4A22418E040DE65E36268A6F758C411DC5F4F769F7A2C486BB9`。Android 完整开发签名 Release APK SHA256 为 `7E460EEBF4D4FB0869EBA8B54F2520B4C05AAC8651BD9C6445C8E58C1449137D`；从隔离模拟器拉取的已安装包哈希相同。不是商店签名或正式发行验收。
- Windows 独立临时库的字典服务探针：首次准备 1515.4 ms；首次“八”查询 151.0 ms、精确取词 6.9 ms、详情投影 236.7 ms；第二次“八”查询 43.9 ms、投影 1.1 ms。原始冷／热输出在同一 `%TEMP%\HanMate-regression-20260923\dictionary-probe-*.txt`。它不测 Android 或页面呈现时间。

## Android 实际界面

设备为隔离 API 36、4 GB 模拟器 `emulator-5554`。完整 APK 安装后的系统字号 200% 截图显示英文 `Dictionary` 标题、`Pinyin ✓` 与操作菜单完整可见；此前标题被截成省略号，因此改用三语言简短拼音状态标签。英文、日文、中文、再英文切换时，已打开字典页标题、拼音按钮和释义本地化更新；随后又核对页脚：日文出现“意味のピンインは自動注音です…”及“補足の学習例文を含みます…”，英文出现对应英文说明。原始截图与 UI XML 在 `%TEMP%\HanMate-accessibility-20260923\header-final\`、`final-check\`；日文页脚快照 `final-check\1790144142979105500.xml`，英文页脚快照 `header-final\1790143958219383400.xml`。

恢复系统字号至原来的 `1.0` 后，应用重新回到拼音首页。再次搜索 `ri4` 并进入“日”字典，滚动到释义中段，打开“View original text”再返回。前后首 5 个可见 `Dictionary.Passage` 稳定 ID 和屏幕 bounds 完全一致；原始快照为 `final-check\1790144298214327200.xml` 与 `final-check\1790144343636301300.xml`。模拟器语言恢复英语，系统字号读回 `1.0`。未操作连接的 vivo 真机资料。

另在 200% 字号下实际打开英文学习、收藏和设置首页：学习四类入口完整可见且页面可继续滚动；收藏夹选择与空状态可见；设置页语言选择、资源/字典/语音入口可见，较长入口在卡片内换行。UI XML 和截图在 `%TEMP%\HanMate-accessibility-20260923\w5-multipage\`。这是三个首页的视觉/可达性聚焦检查，不代表其子流程或读屏通过；检查后再次将模拟器系统字号恢复为 `1.0`。

用完整 APK 运行 `release_preflight.py` 得到 5 项 `BLOCKED`：`release_identity`、`work_packages`、`deferred_release_gates`、`shipped_content_and_audio_review`、`native_and_store_evidence`；`windows_publisher` 现为 `PASS`，不能沿用旧的 6 项说法。报告在 `%TEMP%\HanMate-accessibility-20260923\release-preflight-final.json`。`content_review_audit.py --check` 通过清单一致性检查，但 1,394 个审核对象全部 pending，默认词库 292,114 条分 293 批，0 条终审，`releaseReady=false`。

## 继续执行：性能、故障恢复与读屏焦点

- Windows .NET 10 Release 独立探针以 30 次/场景测量服务层：10,000 条同词头查询的五种输入 P95 为 7.83–57.43 ms；100,000 条同词头查询最慢 P95 为 575.38 ms。原始报告 `%TEMP%\HanMate-performance\20260923-064046\report.json`。这没有测量 Android 参考手机的页面响应、交互就绪或滚动帧率；HM-T127 仅部分覆盖。
- 完整开发签名包在隔离 API 36、4 GB 模拟器运行 30 次 `force-stop` 后启动，未清应用数据。ActivityManager `TotalTime` P50 1670 ms、P95 1836.4 ms；启动后进程 PSS 中位 319696 KiB、最高 324012 KiB。原始数据 `%TEMP%\HanMate-w5-20260923\android-launch.json`；`TotalTime` 不等于首页可交互时间，也不是实体手机内存结论。
- 临时数据库/宿主进程故障探针顺序复跑：替换恢复的 `after-safety`、`after-clear`、`before-commit`、`after-commit` 和真实 SQLite `SQLITE_FULL` 5/5 PASS；Stage B 的恢复/媒体清理故障 7/7 PASS（含清理元数据后、删除后强杀）。原始报告 `%TEMP%\HanMate-recovery-probe-0602957ae4264939b86dc61619ef4094\report.json` 和 `%TEMP%\HanMate-stage-b-crash-90e50803882d4ef196ad910db096ef43\report.json`。这是隔离宿主夹具，不是设备卷满或设备恢复向导验收。两探针首次并行运行时因共用 `obj` 出现构建文件锁；顺序重跑全部通过。
- 隔离模拟器实际启用 TalkBack 后，键盘 TAB 能依次到达拼音 `b/p/m/f` 的有名称按钮。旧完整包的“日”字典逐段朗读按钮前出现无名称、`clickable/focusable` 的 RecyclerView 行容器；原始快照 `%TEMP%\HanMate-w5-20260923\talkback-candidate\tab-02.xml` 等。仅将 MAUI `BlockView` 排除无障碍树未消除此焦点，因此改为在 Android 行加载后关闭原生行容器的焦点与点击属性，保留子按钮；无效的跨平台排除标记已撤回。
- 字典行焦点修复包的完整 Android x64 Release APK SHA256 为 `951E665830D27515D675525C593254AEFA5BA69A3096C473D94A85F63C19B51D`。覆盖安装到同一隔离模拟器、保留数据后，TalkBack 启用状态下 TAB 从“日”词头、收藏，连续进入有名称的释义朗读按钮；前 10 个停靠点不再出现空白行。按 Enter 展开长释义后继续逐段 TAB，后续 8 个停靠点也都落在有名称按钮。快照 `%TEMP%\HanMate-w5-20260923\talkback-final\tab-00.xml` 至 `tab-09.xml`，后续 8 次焦点名称长度均大于零。字典标题/释义静态文本仍在 UI 树中；尚未完成 TalkBack 手势、真人听读、全页面/语言和真机读屏验收。Android x64 与 Windows x64 Release 构建均 0 警告、0 错误。第一次重编 Android 在 `bundletool` 默认 1 GB Java 堆下报 `OutOfMemoryError`，改以 `JavaMaximumHeapSize=3G` 重编通过；应用编译与复验通过。测试后已恢复模拟器无障碍服务 `null`、启用值 `0`、系统字号 `1.0`，并撤销临时通知权限。

### 多页原生列表焦点追加回归

随后在同一模拟器发现相同的空白焦点还出现在语法详情、课文详情、词语/语法学习列表：旧包逐项 TAB 时每个朗读按钮或卡片前均有无名称、可点击的 RecyclerView 行容器。语法与课文原始快照分别在 `%TEMP%\HanMate-w5-20260923\talkback-grammar-baseline\`、`talkback-lesson-baseline\`。三个详情页复用已验证的 Android 原生行焦点处理；带独立操作控件的学习、收藏及字典收藏卡片使用同一处理。需要整行选中的资源安装预览与搜索结果未改。

当前完整 Android x64 Release APK SHA256 为 `44F0638F000A87984F999A970F8828D548A0065C643321EA5F9CDA89B8AC698E`。TalkBack 启用后的键盘逐项结果：词语列表从筛选直接进入 7 个有名称的词卡；语法列表直接进入 3 个有名称的卡片；课文列表直接进入 4 个有名称的卡片；普通收藏列表直接进入有名称的《一天的生活》及菜单；字典收藏列表直接进入有名称的“日”及菜单。对应空白行焦点不再出现。课文/语法详情与字典详情的共用修复在上一候选 APK 分别核对了题名/朗读按钮及前 3、4、6 个有名称的焦点；原始快照在 `%TEMP%\HanMate-w5-20260923\talkback-lesson-final\`、`talkback-grammar-final\`、`talkback-dictionary-shared-final\`。当前 APK 的三个列表页面卡片仍可点击进入详情；词卡单击显示“Preparing or reading. Tap the text again to stop.”，长按进入字典，进程 PID 保持不变。测试收藏与字典收藏均已移除，默认收藏计数回到 0。当前 Android x64 与 Windows x64 Release 构建均 0 警告、0 错误。

这些是模拟器上 TalkBack 启用时的键盘焦点与点击/长按局部检查；RecyclerView 自身可能作为列表容器获得一次焦点，TalkBack 触摸手势、朗读内容、所有语言和页面、真机与 Windows Narrator 仍未全面验收。最后已关闭临时 TalkBack 服务并撤销通知权限。

用户确认本轮没有可用 Android 真机；实体手机的查询、内存、音频路由和读屏均保持 `NOT RUN`。

### Windows 原生窗口补充检查

从工作区 Windows x64 Release 可执行文件启动 PID 59644，使用 `tools/windows_acceptance_ui.ps1` 的进程路径、窗口归属和控件 ID 校验取得拼音首页 UIA 快照与窗口截图，原始文件在 `%TEMP%\HanMate-w5-windows-20260923\`。日语首页按钮与文字在截图中可见。对 `Pinyin.Guide.Open` 先焦点再 Enter、随后 UIA Invoke 均返回，但后续截图及 UIA 树未显示引导遮罩或跳过控件；一次受保护的鼠标点击因前台窗口归属不符被脚本拒绝，没有发送点击。无法证明引导实际打开，`HM-T007` 的 Tab/Enter/Esc 弹窗及焦点返回仍为 `NOT RUN`，不能把控件存在或 Invoke 返回记为通过；此现象也未单凭 UIA 结果确认为应用缺陷。最终截图/树为 `1790152045180.png` / `.json`。

启动前备份了两个可能的 Windows 应用 Data 目录；本次只浏览拼音首页，没有修改资料。受控进程已通过 `CloseMainWindow` 正常退出，退出前后两个原数据库 SHA256 分别维持 `9D3A9577881E0AA22431ED68DB367F975FC1123322668527484544358685FE5F` 和 `BDE06B60D4A4D1C7E2C570C825010CBE094F9090BC88F87C155DE9214ECD7595`。未操作含个人音轨的旧目录。

## 工作包边界

W5-07 的 Android 非破坏性资源路径也已聚焦执行：从设置进入学习资源管理，查看随附学习草稿版本 `1.0.5`、141 条、启用/优先级 10；打开条目管理，从“在学校”进入原阅读页并返回；在隔离模拟器上禁用该随附资源，详情状态变为 `Disabled` / `Enable resource`，随即重新启用，状态恢复 `Enabled` / `Disable resource`、优先级 10。原始 UI XML/截图在 `%TEMP%\HanMate-accessibility-20260923\w5-resource\`，禁用/恢复分别为 `1790145158639000400.xml`、`1790145171394964500.xml`。没有卸载、撤下、安装或修改连接的真机资料；因此 HM-T145 只记 `partial PASS`。

在字典行焦点修复 APK（SHA256 `951E665830D27515D675525C593254AEFA5BA69A3096C473D94A85F63C19B51D`）的同一隔离模拟器继续执行随附学习资源卸载/重装：卸载确认框明确显示保留 0 条引用/改动条目、删除 141 条未引用条目，并声明收藏、用户音频和草稿保留；确认后详情为 `Version 1.0.5 · 0 entries · 0 hidden`、`Uninstalled / missing · Priority 10`。`force-stop` 后重新启动应用，资源列表仍显示 0 条且未自动重装。随后在详情页点击 `Restore bundled resource`，等待安装完成，详情恢复 `141 entries · 0 hidden`、`Enabled · Priority 10`，`Manage resource entries` 可再打开。

随后用隔离资料将《在学校》加入默认收藏，重新打开选择页核对勾选且收藏列表为 1 项。再次卸载同一资源时，确认框改为“Keep 1 referenced or modified entries and delete 140 unreferenced entries”；卸载后收藏列表仍为 1 项，从收藏打开《在学校》可见标题及首段“我每天早上去学校。”。再次显式恢复随附资源后详情为 141 条、启用、优先级 10，收藏仍为 1 项而未重复。最后取消本次创建的测试收藏，收藏夹计数回到 0。这里验证了一个收藏引用保留和同版重装；用户录音、草稿、撤下条目、跨版本升级、外部文件包与 Windows 端仍未验。HM-T152 记部分覆盖，W5-07 不因此结项。


本次支持 W5-01 的 200% 字典标题布局、Windows 服务层与模拟器启动测量、TalkBack 局部焦点，支持 W5-05 的自动回归、字典返回和隔离故障探针，支持 W5-07 的字典三语页面刷新。W5-01 仍缺参考手机 UI/内存与音频路由、完整 TalkBack/Narrator；W5-05 仍缺按平台逐条执行适用 P0 和设备恢复/清理故障矩阵；W5-07 仍缺本构建上的资源升级卸载、分享与双向迁移完整平台回归。原 176 聚合保持 `NOT RUN`，工作包保持 45/48。正式内容终审、素材权利、发行签名和商店门禁独立阻塞。

规划包 `verify_bundle.py --write-report` 的 16 组契约/夹具检查 PASS；`build_bundle.py --check` 核对 257 个文件哈希及完整集合 PASS；最新 `verify_execution_ledger.py` 检查 105 条分范围执行行 PASS，原 176 聚合状态未改变；`git diff --check` PASS。校验依赖按规划包锁定版本安装在 `%TEMP%\HanMate-validation-deps-20260923`，未改变应用依赖。

## Android 分享文件与 Android→Windows 工程内容迁移

- 在隔离模拟器中从系统文件选择器导入仓库自带 `examples/sample-content.hanpack`，SHA256 `7858A11A2ECE1206109FA121A00DD0172876FA2AF5132ED793549049A5E15BB5`。导入前预览为新增 24、复用 0、冲突 0、音轨 0；确认后页面显示 `Imported: 24 added, 0 reused`。该文件明确是未审工程样例，包含词语 19、语法 3、课文 1、诗词 1；不含音频。原始 UI XML/截图在 `%TEMP%\HanMate-w5-20260923\share-fixture\`。
- 从已导入的工程内容分别选中词语“中国” `1e4ca243-aa8a-5268-ab8c-90fafe2e3b86`、诗词“读书小句（原创测试）” `2383fe34-9063-525d-be5d-4ac059347a31`、课文“一起学习” `9fc65df4-78d4-5d16-b5ea-4b3d3a40751e`、语法“在”表示位置 `f7d967d2-9c45-5eeb-b8d4-19d4939dc60d`。预览显示 4 条、0 音轨、0 草稿、0 受限项。生成并经系统文件选择器保存的包在 `%TEMP%\HanMate-w5-20260923\share-fixture\android-four-kind.hanpack.zip`，SHA256 `76CF9120928CB2F60FE58947A7D3DBC258EE4D819B871B1E10A1898377944DD5`。ZIP 内恰有四条指定 ID、四类各一条；manifest 计数、文件集合、字节数与 SHA256 均核对通过，不含收藏、设置或资源。与原工程包同 ID 内容逐字段比较，除导入时生成的 `createdAtUtc`/`updatedAtUtc` 外一致。
- 将上述 **Android 实际导出** 包交给 Windows .NET 10 Release 独立服务宿主 `HanMate.MediaProbe verify-w5-android-content`，在全新 `%TEMP%\HanMate-d2-probe-d8061ff5f7544a7aa44044038bcd4c05\` 数据库导入：4 条新增，四类文档 JSON 完全一致；再次导入为 0 新增、4 复用、0 冲突。探针 DLL SHA256 `89CE2B94A238349540B84F5D115CD624BDDD17019CE0CC24A51FA2C1A9E96068`，原始结果在该目录 `verification.json`。这证明 Android 导出→Windows 服务导入，不代表 Windows 原生界面或含音轨迁移通过。
- 本次还发现 Android 原“保存文件”使用 `application/zip`，系统选择器把建议的 `.hanpack` 名称自动改为 `.hanpack.zip`。`NativeFileSaver` 已让原生扩展名使用 `application/octet-stream`，显式 `.zip` 仍用 `application/zip`，WAV 仍用 `audio/wav`。修复后的完整 Android x64 Release 开发签名包 SHA256 `09ED5EEEAAC557ECA52DB38C26D3065489AC28D80649FBFFECDCFB68A606BE46`，隔离模拟器覆盖安装后拉取的 base.apk 哈希相同。旧工程语法条目仍可被选中；同一条目经“保存文件”实际落为 `.hanpack`，经“另存兼容 ZIP”落为 `.hanpack.zip`，两文件 SHA256 同为 `1044D78EAF6EC50B77EBE1E5A5365A1C669B0019D3655DE852B56D48E2C98508`。再从系统选择器导入原生 `.hanpack`，预览为 0 新增、1 复用，确认后页面也显示 `Imported: 0 added, 1 reused`。原始 UI XML/截图在 `%TEMP%\HanMate-w5-20260923\save-fix\`。Windows 和 Android Release 构建均 0 警告、0 错误。

以上是工程包、Android 模拟器与 Windows 独立服务宿主的分范围证据；四类单条独立分享、音轨选择、Windows 原生 UI 双向迁移、真机、完整适用 P0 均未完成。W5-07、W5-05 仍为 `IN_PROGRESS`，工作包保持 45/48；正式内容/权利及发布门禁不因工程包成功而放行。

## 隔离资源安装与跨版本升级

从资源安装页试装仓库原样例 `sample-learning.hanresource` 时，确认页正确显示 1.0.0、4 条，但安装返回 `RESOURCE_CONTENT_CONFLICT`。此前在同一隔离模拟器导入的个人工程样例占用了包内图谱 ID；安装器拒绝覆盖，不能把此结果当作资源更新失败。原始界面快照位于 `%TEMP%\HanMate-w5-20260923\resource-upgrade\`，首次冲突的末张为 `1790154784711986600.xml`。

改用 `tools/make_w5_resource_upgrade_fixture.py` 从规划样例派生独立资源 UUID `96b2e57f-5fc2-5903-9ca5-39ce8b4c752e`。1.0.0 含 4 条，1.0.1 保留原 4 条稳定条目 ID 并新增“中国”，共 5 条；包只写入 `%TEMP%`，不读写应用数据。Android 实际安装的两个包 SHA256 分别为 `80F56E98C077B8B04F297D075C3DB9A653C23B02C831755AEF04560B59ACC006`、`7FFCBDDEA22589A013F9E60FBCBE62501DA5E405DA52017EE21F3C5C5149D3BA`。完整 Android x64 Release 开发签名 APK 仍为 `09ED5EEEAAC557ECA52DB38C26D3065489AC28D80649FBFFECDCFB68A606BE46`，没有重编或清除模拟器数据。

先用 `HanMate.MediaProbe verify-w5-resource-upgrade` 在全新 Windows 临时库调用正式 `TextResourceInstaller.PlanAsync/InstallAsync`：1.0.0 安装 4 条，按正式 `ResourceStateStore` 接口撤下 `entry-1`；1.0.1 预览为新增 1／不变 4／更改 0／移除 0、可执行。安装后为 5 条、1 条仍撤下，“中国”可用。探针 DLL SHA256 `7E0E024984BE60B2DA3ED6DF5D8B5487FF71266363A4210214BD374DC34574E3`，原始 `verification.json` 位于 `%TEMP%\HanMate-w5-resource-upgrade-40ca1c332bfa483fa490532c7ef254ea\`。相关 Infrastructure 66/66 测试通过。

同两包随后推入隔离 `emulator-5554` 的 Downloads。安装 1.0.0 后资源详情为 `4 entries · 0 hidden`；在条目页撤下“在”表示位置后显示 `Restore entry` 和隐藏状态。`force-stop` 再启动，资源列表仍为 `1.0.0 · 4 entries · 1 hidden`。从该资源详情选择 1.0.1，逐条预览显示 4 条 `Unchanged`、1 条 `Added · 中国`；确认页为 1.0.0 → 1.0.1、新增 1、其余 4 条不变。安装后详情为 `1.0.1 · 5 entries · 1 hidden`、启用且优先级仍为 100。再次打开条目页，原“在”保持隐藏，新增“中国”为 `Available`。对应快照：重启后列表 `1790155332426649200.xml`、逐条更新预览 `1790155424290564800.xml`、安装确认 `1790155439181815600.xml`、最终详情 `1790155467741776500.xml`、最终条目页 `1790155490098234600.xml`；同目录有对应截图和 `actions.jsonl`。

随后重复从系统选择器安装同一 1.0.1 包，页面显示 `This package is already installed (5 entries).`，快照 `1790155749294178300.xml`。再选择旧版 1.0.0，安装规划阶段拒绝 `RESOURCE_DOWNGRADE_REJECTED`，快照 `1790155774869459800.xml`；返回详情仍为 `1.0.1 · 5 entries · 1 hidden`、优先级 100，快照 `1790155785159110800.xml`。同包重复安装为 HM-T142 的 Android 局部证据；未逐项核对收据和音轨。

这为 HM-T141/142/146/147 的 Android 局部路径及 HM-T147 的 Windows 服务层提供分范围证据。尚未逐项核对更新前后个人副本、用户音轨、草稿和四类撤下条目；未测 Windows 原生资源 UI、实体手机及完整 P0。W5-01/05/07、原 176 项聚合和 45/48 均不变。

### 含工程测试音轨的升级、卸载和重装

同一生成器追加独立资源的 1.0.2：原 5 条保留，首条完整文字单元绑定一秒 8 kHz／mono／PCM16 的 440 Hz **合成测试音**，标签明确为 `Synthetic engineering tone, not speech`。它只用于验证包与播放通路，不是汉语录音、教学音准或可发布素材。1.0.2 包 SHA256 `643027EA4BBF2BA5B6F4EA57B1BF383FDB76262508EA621EA4B9BF7C0B5FE67C`，实际 WAV SHA256 `AF4B80247F61D0823E9219E3AA2270E81EBD2B553A5411E1471AF82857FFE6F4`；生成文件位于 `%TEMP%\HanMate-w5-20260923\resource-audio\packages\`。前述两个 1.0.0/1.0.1 已安装包的 SHA256 保持上文记录；这次生成器复跑输出的前两包 ZIP 时间戳不同，不将其新哈希冒充 Android 先前安装身份。

Windows 全新临时库使用 `HanMate.MediaProbe verify-w5-resource-upgrade` 从 1.0.0→1.0.1→1.0.2 逐次调用正式安装器。末版仍 5 条、1 条撤下；标准音轨为 `Eligible` 且 `Preferred`，WAV 元数据为 1000 ms／8000 Hz／单声道，实际打开的安装音频 SHA256 与包内 WAV 完全一致。探针 DLL SHA256 `01F0A81DDA7D2FC05E042C367DF4F2BFB86304729030EE3514EB034E5C6AC875`，原始结果 `%TEMP%\HanMate-w5-resource-upgrade-cbf2cca4ed924c28bef21085ed628912\verification.json`；相关资源媒体／升级／管理测试 39/39 PASS。

隔离 Android 模拟器上选择 1.0.2 后，差异页列出 5 条 `Changed`、0 条新增或移除（新增发布音轨使包内各条参与版本切换）；确认并安装后详情为 `1.0.2 · 5 entries · 1 hidden`，原优先级 100 不变。从已撤下“在”条目的管理页仍可进入只读正文的音轨页，显示合成测试音 `1.0s`、`Standard track · Default and applicable`。点 `Play applicable track` 后状态先为 `Playing…`、随后为 `Playback finished`。差异预览、确认、安装后详情、音轨列表、播放中和结束快照依次为 `%TEMP%\HanMate-w5-20260923\resource-audio\1790156252086771900.xml`、`1790156264457000800.xml`、`1790156287109152500.xml`、`1790156369989106700.xml`、`1790156385722562800.xml`、`1790156397277095300.xml`，同目录有截图。

在该隔离资源详情执行卸载，确认页明确预览“保留 5 条引用或已修改条目、删除 0 条未引用条目”，并注明音频文件不立即回收；卸载后资源登记仍为 `1.0.2 · 0 entries · 1 hidden`。`force-stop` 后重新启动，资源列表仍显示 0 条，未自动重装。从详情选择原 1.0.2 文件并确认，页面显示 `Installed 5 entries`；返回详情为 `1.0.2 · 5 entries · 1 hidden`、优先级 100，撤下条目仍隐藏。再次从该条进入音轨页，合成测试音仍为 `Standard track · Default and applicable`。卸载预览、卸载后、重启后、重装结果、最终详情及音轨快照分别为同目录的 `1790156462425210900.xml`、`1790156476872788200.xml`、`1790156507700338200.xml`、`1790156588127653400.xml`、`1790156602551960600.xml`、`1790156649442897600.xml`。

又将同一合成 WAV 从包中取出，作为**个人导入音轨**附到已撤下“在”条目的完整单元。保存后音轨页并列显示 `Imported audio · Confirmed applicable` 与 `Synthetic engineering tone, not speech · Standard track · Default and applicable`；个人导入音轨未设为默认。随后再次卸载 1.0.2，从“Retained old content”进入该条，两轨仍在；重选原包并确认安装后，页面显示 `Installed 5 entries.`，详情为 `1.0.2 · 5 entries · 1 hidden`、启用及优先级 100。再次从隐藏条目进入音轨页，两轨仍并列且适用，个人轨依然非默认，标准轨依然默认。保存后、卸载后及重装后的原始快照分别在 `%TEMP%\HanMate-w5-20260923\resource-personal-audio\1790156916642940200.xml`、`1790157088903126200.xml`、`1790157364803949900.xml`；重装确认和详情为 `1790157304627164700.xml`、`1790157314802678600.xml`，同目录有截图和操作记录。

在同版重装后只对原先撤下的“在”执行 `Restore entry`，管理页该条立即由 `Entry hidden` 变为 `Available`，按钮改为 `Withdraw entry`，其他可见条目仍是 `Available`；详情随之变为 `1.0.2 · 5 entries · 0 hidden`。再入该条音轨页，个人导入轨与默认标准轨仍并列且适用。恢复前后管理页、详情和音轨快照为同目录的 `1790157655274468700.xml`、`1790157666812532300.xml`、`1790157680646821800.xml`、`1790157731325215100.xml`。这是 HM-T152 “仅恢复选中 entry” 的 Android 局部证据；未逐条核对学习/搜索索引。

Android 结果证明当前完整开发签名包的工程标准轨与个人导入轨，在该隔离资源卸载及同版重装后均能保持绑定和适用性；Android 私有数据库不可由 `run-as` 读取，未在 Android 上逐字节复核音频。Windows 的精确 WAV 哈希不替代真机听审、蓝牙／耳机路由或 Android 安装文件哈希。个人导入的是合成测试音，**并非麦克风录制的用户语音**；截至此阶段，草稿、个人正文副本及多资源冲突仍未验。HM-T147/152 仍只记局部覆盖，W5-01/05/07 和 45/48 保持不变。

### 个人语法副本与源资源隔离

在同一隔离模拟器，从 retained 的工程语法条目“在”进入 `Copy to personal library and edit`，将副本标题改为 `“在”表示位置_W5` 并保存草稿；注音审阅页显示 16 个汉字仍待审，确认保留待审状态后正式保存到个人库，**没有把自动拼音冒充人工校正**。新副本在学习语法列表中与原题目并列，从副本详情进入音轨页时明确显示 `This target has no tracks yet.`，说明没有将源资源音轨误绑定到副本。随后仅对副本完整单元另行导入同一合成 WAV，保存后该副本的音轨页显示 `Imported audio · Default and applicable`。编辑草稿、审阅警告、保存后阅读页、列表、原空音轨和副本新音轨快照分别为 `%TEMP%\HanMate-w5-20260923\resource-personal-copy\1790158039763972900.xml`、`1790158085104878000.xml`、`1790158098807249400.xml`、`1790158126296861400.xml`、`1790158231415699600.xml`、`1790158314039997000.xml`。

再次卸载源工程资源，确认页为保留 5 条引用或修改条目、删除 0 条；资源详情成为 `1.0.2 · 0 entries · 0 hidden`，学习语法总数由 10 降为 7，仍列出带 `_W5` 标记的个人副本。重新打开副本音轨页，个人导入轨仍为 `Default and applicable`。显式重装原 1.0.2 包后页面显示 `Installed 5 entries.`，语法列表回到 10 条且个人副本仍单列，副本音轨仍为默认适用。对应卸载确认、卸载后详情、卸载后列表与音轨、重装结果、重装后列表与音轨快照为同目录的 `1790158466249442700.xml`、`1790158481079338300.xml`、`1790158511370912200.xml`、`1790158614168790700.xml`、`1790158751376676200.xml`、`1790158775852217800.xml`、`1790158872414604500.xml`。

这为 HM-T149 的 Android 工程副本、独立音轨及源包卸载/同版重装提供**局部**证据；没有修改副本正文或人工拼音，没有执行源包更高版本更新，也没有验证麦克风用户录音、Windows 原生 UI、真机或完整 P0。期间 ADB 新出现其他设备；本序列所有 UI 助手调用均固定 `emulator-5554`，后续原始 ADB 操作也显式指定该模拟器，未操作其他设备。W5-01/05/07 和 45/48 不变。
