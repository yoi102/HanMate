# 整体剩余工作与文档核查 · 2026-09-20

## 范围和结论

本轮按用户要求检查整体未完成项并修改文档。核对当前代码、元数据、backlog、执行台账、已有测试/构建日志及上一轮UI结果，运行只读内容审核与最终APK发布预检；没有修改应用代码、重新构建/运行应用测试、操作设备或改变真实用户资料。

48工作包为35 DONE、10 IN_PROGRESS、3 TODO。iOS暂缓，仍有本机界面、内容质量、审核覆盖和设备验收工作，不能写“除iOS全部完成”。原176聚合保持NOT RUN，不否定已记录的局部PASS；局部PASS也不等于原176完整通过。

## 核查依据

| 源文件或已有证据 | 当前发现 |
|---|---|
| HanMate.App/Pages/SettingsPage.xaml；STAGE-D4-20260919.md | 长按钮仍原布局，HM-D017无后续关闭证据 |
| HanMate.App/Pages/DictionaryEntryPage.cs | TogglePinyin保留列表；展开长释义仍Render重建；词头可播放，例句RubyTextView未接点击播放；两条流程不可混称 |
| HanMate.Infrastructure/Dictionary/DictionaryDetail.cs；Pinyin/DictionaryDisplayPinyin.cs | 视图补音保留原读音；源例句优先，75句/70词补充；不代表全库语义或读音正确 |
| HanMate.Infrastructure/Dictionary/XINHUA-NOTICE.json | 默认292,114条，44,523来源读音、244,976自动、2,615缺完整解析；NOASSERTION，用户选择本机使用 |
| tools/content_review_audit.py的collect | 两Catalog ZIP及拼音课程、展示注音和辅助例句在审核范围；xinhua.sqlite.gz全库没有逐条审核覆盖或独立默认库权利门禁。登记R04，不把767对象当完整全库覆盖 |
| HanMate.App/Audio/PinyinSpeechService.cs、AiVoiceService.cs、LocalAudioBackend.cs | 教学/可解析字典词头支持明确拼音；普通全文AI仍使用正文；系统TTS亦不接受人工拼音控制 |
| HanMate.Infrastructure/Packages/SafetyRecoveryStore.cs、OrphanAudioStore.cs；BackupPage；阶段B证据 | 恢复向导/媒体物理清理已实现，设备验收仍未完成，旧“待开发”不再有效 |
| STAGE-D2/D3/D4、HANDWRITING、XINHUA、PINYIN-VOICE、DICTIONARY-DETAIL证据 | 有Windows UI通路和多轮聚焦结果，缺最新页面全UI/真机/读屏/完整迁移；不把旧构建结果推广到最新全应用 |
| TEMP/HanMate-dictionary-scroll-ui/verification.json及对应XML/PNG、构建日志 | 上一轮显隐双向保位已验；补录HM-DD003及证据，未冒充本轮再执行 |

默认库逐条内容覆盖和整体权利门禁为不同工作：先以版本/哈希绑定整体来源审核，再分批登记内容审校范围。抽检不能转成全库已审。新增字典例句点读、默认库收藏和普通全文明确读音控制只是当前能力边界，未擅自当成用户本轮要求新增的功能。

## 本轮只读检查

- PASS：content_review_audit.py --check，派生文件与实际资源相符；767对象/227内容/456单元/224音频/63教学项/174音色，767对象待审，decisionIssues=0，releaseReady=false。
- 审核提示：227 CONTENT_DRAFT、224 LICENSE_VERSION_UNRESOLVED、22 READING_RECHECK、1 DEMO_MISSING。READING_RECHECK是候选筛查而非已确认22处错误；DEMO_MISSING为原录音ong，不否定启用AI的ong合成路径。
- PASS：最终APK权限、禁用系统备份、分享范围；源码权限、锁文件存在性及574键三语言一致。锁文件检查不是重新restore或完整工具链锁定。
- BLOCKED：发布预检5组，分别为release_identity、windows_publisher、work_packages、shipped_content_and_audio_review、native_and_store_evidence。预检阻塞是实际结果，不为得到绿色退出而改状态。
- NOT RUN：本轮应用单元测试、构建、Windows/Android UI、真机/iOS、独立内容/声音审校。最近实际构建仍为显隐保位轮Windows/Android零警告错误。

原始只读报告在TEMP/HanMate-overall-audit-20260920/preflight.json；小型[机器摘要](OVERALL-AUDIT-verification-20260920.json)保留数量、检查结果和产物/代码哈希。默认字典规模不并入227学习内容数。没有Git元数据，不提供虚构commit或diff。

## 文档处理

统一STATUS、HANDOFF、ISSUES、COMPLETION_PLAN当前结论，旧阶段原记录保留在折叠历史中；新增REMAINING_WORK作为当前待办入口。README、TEST_MATRIX、字典专题、ADR、审核说明及发布指南同步校正。追加1补充用例和上一轮1执行记录后为49补充场景/62执行行；backlog及176聚合状态不变。

下一阶段D5：设置200%按钮、字典长释义展示/展开保位、新版两平台UI；之后补高频内容和默认库审核范围，再继续真机/迁移/发行。具体退出条件见COMPLETION_PLAN，不能只写泛化的“继续验收”。
