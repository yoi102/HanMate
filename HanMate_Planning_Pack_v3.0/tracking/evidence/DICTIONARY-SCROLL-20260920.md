# 字典拼音显隐保位 · 2026-09-20

这是上一轮已执行修复的补归档，本次文档核查没有重新操作设备。

DictionaryEntryPage.TogglePinyin只更新稳定Block的Pinyin属性，以编译绑定刷新对应行；不重建ItemsSource、Header或Footer。修复原Render重建列表导致切换回到顶部的问题。此修改不改变内容、例句和数据哈希。

Windows/Android Release均PASS，零警告错误。Android最终APK已覆盖安装。API36 emulator-5554在展开的“日”长释义下方执行隐藏→显示→隐藏，三份XML的首个可见释义块一致且词头播放按钮不在视口；截图也检查显示结果。行高改变会重新排版，不代表逐像素锁定。长释义展开/收起仍是另一个待处理路径。

私有TEMP/HanMate-dictionary-scroll-ui中的1789833352134840500、1789833374698197000、1789833396485312400为对应XML/PNG；verification.json为比对结果。构建日志TEMP/HanMate-dictionary-scroll-{windows,android}.log，Windows输出TEMP/HanMate-dictionary-scroll-windows/HanMate.App.exe。构建和源文件哈希补录于OVERALL-AUDIT-verification-20260920.json；APK已与本次实际文件重新核对一致。

NOT RUN：本轮Windows UI、iOS、真机、大字号、读屏、全部回归。未为这个局部表现变更新建镜像实现的单元测试；记录的是实际UI聚焦结果，不沿用上一轮44测试为本次重跑结果。
