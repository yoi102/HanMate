# 开发素材生成

阶段 A 辅助工具：

- `dotnet run --project tools/HanMate.VoiceProbe/HanMate.VoiceProbe.csproj -c Release -- --soak`：固定工程中文/数字、48元素短块，连续20分钟真实合成和30次取消恢复，逐轮记录耗时/内存，报告位于TEMP/HanMate-voice-probe/soak.json。只验证原生合成与信号，不代表20分钟设备播放或听审。运行期间不要向同一输出目录重新构建；需要另一个探针时指定独立 `--output`。
- `dotnet run --project tools/HanMate.VoiceProbe/HanMate.VoiceProbe.csproj -c Release -- --review-samples HanMate_Planning_Pack_v3.0/tracking/review/ai-listening-corpus.csv`：用最终生产预检/合成器生成默认067和对比011的听审WAV、逐文件哈希；没有自动听审结论。仅传入公开工程句，不使用私人正文。
- `python tools/build_voice_review_inventory.py`：离线生成63教学项和394段录音的来源审核清单，核验实际文件哈希，不自动提高人工审核状态。
- `python tools/android_acceptance_ui.py snapshot` 或 `tap Voice.Preview` / `hold Pinyin.Item.initial.m`：用实时ADB UI树定位唯一控件，记录私有TEMP截图/XML/操作日志。设备可用 `--serial` 指定；不清库，不改权限，不自动确认删除。操作成功不等于验收通过。
- 从规划包目录运行 `python tools/verify_execution_ledger.py`：检查逐平台证据台账；不替代平台执行，也不把原176项批量改成PASS。

`python tools/build_voice_catalog.py` 是显式开发机联网任务：固定Hugging Face模型提交，下载到私有TEMP并生成AI语音包的大小/SHA256目录。普通App构建不下载模型。不要把TEMP中的模型放入源码或安装包。`dotnet run --project tools/HanMate.VoiceProbe/HanMate.VoiceProbe.csproj -c Release` 使用生产下载校验器和生产合成器，在TEMP安装并用两个音色合成固定工程中文，生成WAV与report.json；无用户正文或麦克风数据。

从仓库根目录运行。应用及普通 .NET 构建无需 Python 或联网，生成结果已经随源码保存。

`dotnet run --project tools/HanMate.VoiceProbe/HanMate.VoiceProbe.csproj -c Release -- --audit` 逐一验证174音色非静音PCM输出、中文/日期/数字输入、不支持输入的原生调用前拒绝，以及实际中途取消和连续30次取消后恢复。报告在TEMP/HanMate-voice-probe/audit.json；这是原生合成与信号验证，不代替听审。首次无缓存时会明确使用工具下载模型，不访问用户内容。

`dotnet run --project tools/HanMate.PerformanceProbe/HanMate.PerformanceProbe.csproj -c Release` 使用独立TEMP库，测量1k/5k/20k字注音与分页、10k/100k同词头压力查询，每项预热1次、采样30次并保存原始耗时和P50/P95/max。不要与其他构建/压力任务并行测量；结果是当前Windows宿主CPU/SQLite数据，不是Android/iOS帧率或可听首音延迟。

- `python tools/build_annotation_lexicon.py`：从固定SHA256的Unicode17缓存生成字音TSV、完整许可证及source manifest，默认无网络。55,328候选中当前解析器接受55,301条，27条特殊形式显式报告；另有24条原创上下文种子，均需复核。此库独立于查询字典；不可把未知/待复核提升为教材确认。

- `python tools/build_starter_catalog.py`：从规划包原样例生成确定性、不同身份的 20 条学习草稿和 3 条字典草稿。更改生成结果后需同步 `BundledResourceCatalog` 的固定哈希，并运行 Infrastructure 测试。
- `python tools/build_pinyin_course.py`：读取 `pinyin-examples.tsv`、`pinyin-definitions.tsv`、`expand_pinyin_course.py`、`content/pinyin-tone-{characters,words}.tsv` 与已缓存录音，离线生成课程、PCM16 WAV、署名与缺口清单。需要构建期 `soundfile==0.14.0` 和 `imageio-ffmpeg`（提供FFmpeg解码器）。
- 只有显式 `--fetch` 才刷新 Commons 元数据，只有 `--download` 才下载缺失录音。下载串行且有间隔；429 后遵循 Retry-After 的全局冷却，不连续运行重试来规避限制。
- `--download-limit 12` 限制本次Commons新增下载请求（0—50，默认12）；缓存始终离线处理。
- `--teaching-audio` 显式取得独立来源 audio-cmn 的缺失MP3，固定提交 `ff9ed3d0c631195bd2c06f39450f3264c7124040`，每份校验Git blob SHA1；串行请求，错误即停。已缓存时普通生成无需联网。来源树、原README、原MP3及使用清单在 `resource-audit/audio-cmn`；最终WAV的SHA256及原MP3 SHA256保存在课程中。
- 2026-09-20随附63教学项、417例字/例词、394录音；原录音直接示范62/63，独立`ong`录音缺失时不以`dong`代替；启用离线AI可按明确音素合成ong。声母显式绑定bo/po/mo/fo等教学读音；多字词使用完整词语录音，不拼接音节。所有内容与音文匹配仍待独立审校。
- audio-cmn README标明Chen Wang（音节）及Yue Tan（SWAC词语）录音、Hugo Lopez整理，声明CC BY-SA但没有版本号。保留原声明及固定来源链接，不虚构3.0/4.0或终审；公开发行前须解决许可版本与正式审核门禁。

Commons 原文件/元数据存于 `resource-audit`。Android/iOS/Windows 只携带 `HanMate.App/Resources/Raw/Pinyin` 的生成文件；不携带 Python、下载器或整个研究缓存。新增音频后运行 Core 测试核对课程绑定、WAV、哈希、时长；重新构建 App 才会带上新增录音。

来源核查与许可边界见规划包 `tracking/evidence/RESOURCE_SOURCES-20260917.md`。所有新增教学例字/译文和音文匹配尚待人工审校。

`python tools/build_reader_fixture.py <temporary-path>/reader-smoke.zip` 生成标明“非教材”的20,000字与混排/诗行测试包；需构建期regex。它只用于设备回归，不进入随附目录或正式内容统计。输出目录由调用者显式指定，使用实际资源安装入口验证。

`python tools/build_playback_fixture.py <temporary-path>/playback-smoke.zip` 生成5条独立播放测试内容及10条音频绑定，覆盖词语、语法、课文、诗词和整篇音轨。合成音只用于验证播放、切换与停止，不作为教学发音或随附教材；输出到显式指定的临时路径，再通过应用内容包导入入口验证。

`python tools/build_backup_fixture.py <temporary-path>/backup-smoke.zip` 生成独立的5条备份测试内容、10条合成音轨、2个收藏夹和5个收藏成员，设置为日语/150%。仅用于正常备份导入入口的增量回归，不包含真实个人资料，不计教材或正式录音。

`dotnet run --project tools/HanMate.MediaProbe/HanMate.MediaProbe.csproj -c Release` 在独立TEMP库使用生产Windows解码器导入MP3/M4A，验证截断、预取消、草稿/临时文件清理及规范化音频备份；生成报告、`migration.hanbackup`和`expected.json`。夹具是1秒440Hz合成信号，不是教材或麦克风录音。

将工程备份通过Android应用增量导入（保留本机设置），再由应用另存备份后，可执行 `dotnet run --project tools/HanMate.MediaProbe/HanMate.MediaProbe.csproj -c Release -- verify-return <returned-backup> <expected.json>`。它只在新的TEMP库恢复并比对正文/人工校正、收藏、两个WAV哈希及默认音轨；不会替换真实数据库。返回备份可能包含真实个人资料，只放私有TEMP，不入仓库。

## 字典和当前审核范围

`python tools/build_dictionary_pinyin.py`以pypinyin 0.55.0构建89,034条展示读音，由应用独立C#注音器读取，不需要运行时Python。默认词库构建使用`build_xinhua_dictionary.py`；默认292,114词与两个Catalog ZIP中的小型字典草稿不是同一数据集合。

`python tools/content_review_audit.py --write --check`当前核验1444审核对象的一致性，含独立默认词库的1个版本/权利对象及293批内容/拼音对象，覆盖292,114条。扫描核验实际GZip和SQLite哈希，以固定词频/字头/UUID顺序分批，最后一批114条；全部待审，不把覆盖数当成通过数。报告的`dictionaryCoverage`单列全批审核/未全批审核数量；`--release`和`release_preflight.py`共同拒绝未审或权利未明的默认库。

`python tools/dictionary_review_batches.py --batch dictionary-batch:xinhua:0001 --output <temporary-path>/batch-0001.json`导出该批1000条完整源记录、逐条哈希及审核对象哈希，便于按高频优先审校。批次审核决策除现有真实审核人/证据字段外，必须声明`allEntriesReviewed: true`和准确`reviewedEntries`；抽查记录不能批准整批。批次文件只生成材料，不产生审核签名或修改任何词条。任何格式PASS均不等于语言或许可终审。

2026-09-20错音修复：pa1/pa4/chi1/chi4在有匹配录音时回退录音，覆盖词内音节；前三者换用独立整字录音。`pinyin_tone_coverage.py`按准确声母/韵母/整体音节补齐分组，236/252组合同时有例字词，余下缺口见`resource-audit/audio-cmn/tone-coverage.json`。普通构建仍不联网；新增释义注音为待审草稿。

## 内部交付与例字配词（2026-09-20）

`pinyin-character-words.tsv`补充42个原创开发例词，同字、同拼音/声调与例字配对。当前251/256处已匹配，界面按例字及匹配词相邻显示；42个新增词无整词录音，不用单字音频代替。释义注音及译文仍为待审稿。

`build_internal_delivery.ps1 -OutputDirectory <全新目录> -Python <Python路径>`顺序构建Windows/Android并输出内部交付；不运行应用测试、不安装、不改发行身份、不发布。`package_internal_delivery.py verify --output <交付目录>`逐个核验交付文件及源码/Windows ZIP全部条目。具体说明见 `HanMate_Planning_Pack_v3.0/delivery/README.md`。正式后置条件由 `release_preflight.py` 的 `deferred_release_gates` 独立阻塞。
