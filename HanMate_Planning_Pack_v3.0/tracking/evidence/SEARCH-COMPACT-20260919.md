# 搜索布局、下滑加载及默认词典 · 2026-09-19

## 实现

手机标题同行居中显示“文字 / 手写”。手写区去掉常规提示，田字格增大；底部改为保留可访问名称的撤销、清空、来源图标。候选纵向排列，每行左侧字和拼音，右侧例词自动换行且无拼音；字词均进入原释义页。

文字搜索移除上一页/下一页，每批50条稳定游标，剩余8项时追加，按contentId去重并保留滚动偏移。查询/来源变化重置，旧请求取消，epoch变化重查，切换模式保留已加载结果。

按此前用户选择内置教育部《重編國語辭典修訂本》2015_20260625共163,920条。原18字段完整保留；简体仅用于检索，显示繁体原文；不作为大陆普通话标准或新华字典。2,478条非标准拼音格式没有拼音索引，仍可汉字查询。数据独立只读缓存，不导入个人数据库；启停设置与原词库不纳入个人备份。来源页包含完整使用说明、署名、版本和许可，条目只读。

## 最新偏好与来源调查

用户最新反馈不喜欢繁体。中文维基词典数据允许按CC-BY-SA/GFDL条件再利用，可进一步整理简体、筛选普通话、去重并抽查释义；尚未下载/接入或通过质量验收。chinese-xinhua自述抓取网站、无商业目的和侵权删除，不能推定原文获得再分发授权。当前教育部词库尚未替换。

- 官方数据：https://language.moe.gov.tw/001/Upload/Files/site_content/M0001/respub/download/dict_revised_2015_20260625.zip
- 授权说明：https://language.moe.gov.tw/001/Upload/Files/site_content/M0001/respub/index.html
- 中文维基词典数据：https://kaikki.org/zhwiktionary/
- 抓取库声明：https://github.com/pwxcoo/chinese-xinhua#copyright

## 验证

- PASS：DefaultDictionaryTests 3项及OfflineSearchStore 15项，共18/18；覆盖简繁/拼音、原文、阅读文档、启停、过期游标、分页去重、不写个人条目及丁的多音顺序。
- PASS：163,920条18字段与官方表逐项比对，SQLite完整性、确定性重建通过；哈希见Infrastructure/Dictionary/NOTICE.json。没有新增运行时依赖。
- PASS：Windows和Android最新Release均0警告0错误。Windows输出TEMP/HanMate-search-compact-windows，未重启现有窗口；Android覆盖安装未清数据。
- PASS：Android API36查询nihao，提交后1—50/56，下滑追加到1—56/56，继续滚动保持56；手写往返仍56条且同一滚动位置。无搜索分页按钮。
- PASS：Android手写一；无常规提示，田字格范围[32,1058][1049,2054]；切换居中。首行左一/yī，右一晌、一似、一堂、一揆、一從分两行无拼音。点击一晌及一分别打开对应释义，返回保留笔迹；截图已目视检查。
- PASS：最终APK权限、禁止系统备份、限定分享目录和三语言键一致性预检。整体发行仍BLOCKED：身份、发行者、工作包、正式内容审核及平台证据5个既有门禁。
- NOT RUN：最新Windows原生UI、iOS、真机、读屏、新布局系统200%、加载失败实际UI重试、字典启停控件实际交互。单测不代替平台验收。

## 证据与后续

原始资料仅留TEMP/HanMate-search-compact-ui：1789820033625362400首次50条；1789820144109523800追加56条；1789820230792343700模式往返；1789820183649225800候选布局；1789820195564890700一晌释义；1789820219701600900一释义。

TEMP/HanMate-search-compact-tests.log、HanMate-search-compact-windows.log、HanMate-search-compact-android.log、HanMate-search-compact-preflight.json保留原始日志。源数据/复现产物在TEMP/HanMate-moe-dictionary。Android阅读150%保持，系统字号未改；Windows窗口、用户资料及AI模型保留。

总体35/48、原176聚合NOT RUN不变。下一步优先评估简体默认词库，再继续D5设置大字号及平台验收；当前繁体方案尚不满足最新简体偏好。
