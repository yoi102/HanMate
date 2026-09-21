# 拼音练习界面与音源对接 · 2026-09-19

## 交付

- 拼音首页只保留分类和拼音按钮。移除顶部标题/提示/统计/工具及独立例字按钮；教学目录导入与逐文件署名移至设置。
- 声母点击显式播放bo1、po1、mo1、fo1、de1、te1、ne1、le1等常见教学音节，不再选首个例字ba/pa/ma/fa。长按进入例字/例词页。
- 例词按实际一至四声分组，每声可有多个完整拼音例词；保留目标拼音红色下划线。没有独立播放按钮、顶部说明或停止按钮，点词直接播放，播放行高亮；同目标再点停止、切换或离页取消旧声音。
- 长按例字/例词打开搜索使用的ReadingPage。完全同文字和读音的已安装词条优先；缺少匹配时用随附原创草稿解释，只读、不写入数据库。例词全有拼音；草稿释义正文尚未注音，阅读器按既有未知读音规则显示问号，未伪造已确认注音。
- 19个新增整词包括波浪、播放、爸爸、苹果、妈妈、麻木、马车、佛教等。共180个独立例字/例词，均绑定可用录音；新增释义覆盖180条。外部教学catalog schema与用户数据不改。

## 音频及来源

- 随附200个单声道24kHz PCM16 WAV。21声母及y/w使用明确教学读音，62/63教学项有直接示范。独立ong未取得适合来源录音，点击明确提示缺音，不能拿dong冒充；冬/懂/动例字仍可点读。
- 固定来源audio-cmn提交`ff9ed3d0c631195bd2c06f39450f3264c7124040`。音节作者Chen Wang；整词作者Yue Tan，来源SWAC；Hugo Lopez整理转换。原README、Git树、使用清单、原MP3在`tools/resource-audit/audio-cmn`。
- 原README声明CC BY-SA但未注明版本。课程和NOTICE逐文件保留作者、原文件和许可声明链接、原MP3及WAV SHA256、转换说明。每份源文件校验Git blob SHA1；构建期转换，不拼接/剪造发音，App运行不联网、不使用系统TTS读拉丁字符。
- 所有教学选择、译文、释义、音文匹配仍是待审草稿，尚未完成独立听审和许可版本确认；不冒充正式教学审核或发行放行。

## 验证

| 检查 | 结果 | 证据/边界 |
|---|---|---|
| Core Release | PASS | 208/208；生成来源字段和死代码清理后PinyinCourseTests再次10/10，覆盖200文件哈希/PCM/非静音/时长、21声母映射、完整例词音频与释义、整词非高亮音节修改后拒绝旧录音 |
| Infrastructure Release | PASS | 238/238，现有教学导入、媒体、备份相关回归保持通过 |
| Windows Release | PASS | 最终0警告0错误；包含右键、触屏Holding与F10代码 |
| Android Release Rebuild | PASS | 最终0警告0错误，保留数据`adb install -r`成功 |
| Android API36实际交互 | PASS | 主界面b/p/m/f点读、b/m长按进入例词；波浪与妈妈整词播放，原生NuPlayer `audio/raw`、state5及应用UID确认；单字八与整词波浪长按打开解释；滚动不误触长按，离页/连续切换无错误弹窗 |
| Android显示与设置 | PASS | 实际查看日语和中文截图，红色目标/完整拼音/轻声显示；中文译文与空白隐藏；设置的导入/音源署名入口可达。恢复日语，原150%阅读字号保留 |
| Windows UI | BLOCKED | computer-use两次窗口定位均错误识别为OneDrive.App.exe，报window id no longer belongs；没有用旧坐标或其他UI注入绕过，不把构建当交互通过 |
| iOS | NOT RUN | 按用户要求暂缓 |
| 本地发布预检 | PASS/BLOCKED | 492键三语言一致，最终APK权限/自动备份/分享范围通过；发行身份/签名/原生及正式审核门禁仍BLOCKED |

Android最终APK：`HanMate.App/bin/Release/net10.0-android/com.companyname.hanmate.app-Signed.apk`，SHA256=`ad1c300d3b1edb070a10ccf4fc58d32d4a773151053e7fab0cab1b30fd589e8c`。截图与预检JSON在开发机私有`%TEMP%/HanMate-pinyin-20260919`。未清库、未录麦克风、未外发；日志只有既往2026-09-18崩溃记录，本次PID17194未见新增崩溃。

本次是用户要求的拼音体验调整，DONE仍35/48；不关闭全平台、正式素材与发行门禁。
