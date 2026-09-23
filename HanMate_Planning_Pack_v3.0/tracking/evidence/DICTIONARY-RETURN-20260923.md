# 字典返回保位与长释义聚焦回归 · 2026-09-23

本轮修复 `DictionaryEntryPage` 返回可见时的无条件 `Render()`：同一界面语言沿用已有 `CollectionView` 行、Header 和 Footer，只刷新收藏状态并清除上一页的朗读提示；在隐藏期间切换了界面语言时仍重建本地化内容。没有改变词典原文、释义、读音或收藏引用。

验证范围：Windows x64 Release 与 Android x64 Release 构建均为 0 警告、0 错误。Android 使用全新隔离的 API 36、4 GB RAM 模拟器 `emulator-5554`，安装由本轮 Release 构建裁出非 x64 库与 `assets/Voices/` 后重新签名的 UI 专用包（SHA256 `392F035147EEEFF4A00E5408B7B425DEB59D6589475B09CB2A57881AEBF1653F`）。该包保留字典与界面，但不用于声音验收；未触碰已连接的实体手机或旧模拟器资料。

- PASS：词典“日”的长释义页面执行 3 轮展开、两次拼音显隐、滚动与收起；进程 PID 始终为 2097。结束后近 5000 行 logcat 未见本应用新 `SIGSEGV`、Fatal signal、FATAL EXCEPTION 或 ANR。
- PASS：同一页停在释义中段，打开“查看原文”并返回；前后首 5 个可见释义块的稳定 ID 与屏幕边界完全一致，未回到页首。原始 UI XML/截图分别保存在 `%TEMP%\HanMate-dictionary-20260923\return-ui\1790140229354229000` 与 `...\1790140300129006300`。
- PASS：本轮 Windows Release 程序（PID 16236，启动路径与工作区 Release 输出一致）以日语界面搜索 `ri4`，打开“日”，展开长释义并滚动到中段，进入“原文をすべて表示”再返回。前后首 5 个可见释义块的稳定 ID、Y 坐标与高度完全一致，展开按钮仍为“長い説明を折りたたむ ⌃”。UIA 快照在 `%TEMP%\HanMate-dictionary-20260923\windows-ui\1790140655437.json` 和 `1790140746215.json`，返回后的截图为 `1790140790512.png`。为准确选择操作菜单中同名的列表项和文本，Windows UI 脚本增加了可选的精确控件类型筛选；动作仍校验窗口与进程归属。
- 工具边界：首次 2 GB 隔离模拟器运行 2/5 轮后 `uiautomator dump` 超时，应用进程仍在、模拟器内存与 swap 耗尽；改用 4 GB 后 3/3 轮通过。不能把首次工具超时判作应用崩溃，也不能凭本次未复现关闭 HM-D019。

原始动作与快照在 `%TEMP%\HanMate-dictionary-20260923\stress-4gb-rerun\`、`return-ui\`、`windows-ui\`，未入库。本次覆盖 Android x64 隔离模拟器与 Windows x64 的这一长词条返回路径；真机、短词条、大字号/读屏、正式声音与完整 176 用例仍需独立验收。HM-D019 原生崩溃根因未知，保持 OPEN；45/48 内部工作包与正式发行门禁均不因该聚焦回归改变。
