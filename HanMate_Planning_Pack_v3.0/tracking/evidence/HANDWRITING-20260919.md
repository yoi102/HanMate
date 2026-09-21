# 单字手写搜索与字词释义 · 2026-09-19

## 实现

搜索顶部文字/手写切换；上半屏候选与相关例词，下半屏单字田字格。每次抬笔离线匹配，撤销/清空重新识别。模式切换与释义返回保留会话笔迹；页面离开取消旧请求，数据epoch变化拒绝旧条目。

候选字/例词点击进入现有ReadingPage。已安装条目优先，当前可见教学例词只读补充；同词同音已有字典条目时不重复显示教学备用条目，字典不同内容ID仍保留。缺少精确释义的字进入明确缺失状态页，不伪造内容、不写数据库。拼音例词高亮保留红色/粗体且无下划线。

识别数据是Make Me a Hanzi固定提交bddc96d41bef78427ed0e034e9f7e31d71fd1b92的9,574字笔画中线，2,441,435字节；原版Arphic Public License及修改/来源说明打包并可在界面查看。构建脚本校验上游SHA256，无运行时下载或笔迹上传。按标准笔顺模板比较，不是任意连笔神经模型；字形覆盖不代表词典释义覆盖。

## 实际验证

- PASS：8/8识别测试，独立绘制一/十/人/口/大/中、缩放平移、逐笔前缀、撤销空态、取消及输入上限；15/15现有OfflineSearchStore测试。
- PASS：Windows与Android最终Release零警告错误。Windows输出到TEMP/HanMate-handwriting-windows-final，避免重启正在使用的工作区窗口。Android使用install -r覆盖安装，不清应用数据。
- PASS：Android API36 emulator-5554，田字格先横再竖，首候选一→十；点击十进入shí及“数目，九加一。”解释，点击麻木进入既有阅读页；返回笔迹保留。最终包确认备用麻木不再重复。
- PASS：最终Android撤销2→1笔；点击一打开缺失释义页；清空后无候选、撤销/清空禁用。
- PASS：文字查询nihao→手写→文字，输入与结果保持；中文→英文返回搜索仍保留查询，中日英静态按钮和说明已随语言刷新。该流程在最终备用去重修复前的BC12构建执行；之后只变更教学备用结果去重。
- PASS：551个三语言键一致；最终APK权限/禁止系统备份/限定分享目录检查通过。整体发行预检仍BLOCKED（5项既有身份、发行者、工作包、正式内容审核及平台证据门禁）。
- Windows已观察手写页和候选显示；脚本鼠标输入被窗口归属保护拒绝，完整Windows手写交互NOT RUN，不把构建当UI通过。iOS暂缓。真机、任意笔迹识别率、读屏和手写页系统200%验收NOT RUN。

## 私有原始证据

原始截图和UI树只放TEMP，不收入文档包：HanMate-handwriting-ui/actions.jsonl及对应png/xml。最终包：1789818152001309100逐笔/候选/无重复；1789818170276052600麻木阅读页；1789818180738092600撤销；1789818185797249100缺失释义；1789818208877020400十释义；1789818219203229600清空。此前BC12：1789818066462626500模式切换保留nihao；1789818090673945000英文刷新且保留查询。中日英空查询文案亦见1789817701066693500、1789817744483560700、1789817864177284800。

测试日志TEMP/HanMate-handwriting-tests.log及HanMate-handwriting-search-tests.log；TRX位于HanMate-handwriting-tests。最终构建日志HanMate-handwriting-android-final.log及HanMate-handwriting-windows-final.log；预检HanMate-handwriting-preflight.json。

## 边界与交接

本轮不新增、替换或删除真实正文/字典/媒体/AI模型。Android恢复中文、系统100%，阅读150%保持，最终停在手写空白页；Windows现有窗口和笔迹未强制重启。总体35/48及原176聚合NOT RUN不变。下一阶段仍为D5设置200%长按钮和剩余平台验收；搜索语言刷新HM-D018已有Android聚焦复验，Windows仍待验。

## 最终构建SHA256

- androidApk: `383c0d100045c517f9e6b0f614acb57569be065228a0aceff801e3d81bd16c8d`
- windowsDll: `84073f53fba6af838ad1c46d4bb4a819f35f39f91efd5febf65a963a75679bc0`
- coreTestsDll: `b07d3dbc718926dd720b0342adb05eaf440b24643a764c5d32b8e077f6936965`
- infrastructureTestsDll: `94cd6fd6d3654963e9476b678147c1db8bdc73d2d9ae44ce81caa7b1fd68fbb7`
