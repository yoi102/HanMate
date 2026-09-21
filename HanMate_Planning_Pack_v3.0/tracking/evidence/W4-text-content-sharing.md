# 文字内容包、冲突合并与独立录音分享

日期：2026-09-18。用户授权连续推进多个阶段，测试集中收尾。实现四项相连流程：严格文字内容包导出、增量导入与全图冲突映射、多选预览/系统分享、独立用户WAV导出/分享。W3-07语法分享准备完成，22/48 DONE；W4-01/02/03/08保持IN_PROGRESS，不代表首版分享/备份全部完成。

W3-07实际依赖数据模型、阅读器与文字编辑基础，已将原来包含完整语法播放的W2-07硬前置改为W1-02/W1-06/W3-01..03；整句播放仍由W2-07保持IN_PROGRESS，含音轨分享仍由W4承担。见ADR-065，未降低首版验收。

## 实现与数据边界

- StrictTextArchive共用文字资源与content路径，不解压外部目录。固定文件白名单、ZIP重复/路径/符号链接、实际展开长度、严格UTF-8/JSON重复键/schema/hash、数量/UUID/完整图范围验证。32MiB归档及展开、16MiB单JSON、1MiB manifest，content读取最多10000条，UI导出最多100条。
- 内容包只带manifest.json、contents.json、空audio/index.json，保留四类完整图、人工锁定拼音、日英译文及出处。CanShare来源声明与原始个人输入明确权利确认分开；资源限制不可绕过，本机源权利不修改。无草稿/收藏/设置/管理文件，不丢音频后冒充完整包。
- ContentPackageImportStore以源整图语义指纹保存content.v2实体映射。任一root/nested ID冲突全图新UUID，grammar及segment引用同步变换；映射复用重新核验active personal现存语义。修改/回收后的副本保留并新增；同标题不猜测合并。package身份与operation分离，规范manifest一致可重压缩，epoch防过期，正文/目标/索引/映射/收据同事务。
- ContentTransferPage统一50条分页选择和正式阅读预选入口；预览说明排除的音轨/草稿。导出重新严格读包校验后进入私有分享区，用户再打开系统分享。导入选择文件→计划计数→确认提交。
- LocalAudioStore导出已保存本人录音，验证目标和user来源、完整WAV及字节哈希，持有export租约。needsReview允许独立声音分享，关联状态不改变。ShareFileStore关闭.part后改名，失败清自己的临时文件，7天保护期与256MiB总配额；系统可能提前清缓存。原录音不删除。

## 集中自动验证：PASS

所有dotnet命令串行，未新增依赖或改变数据库/schema版本。

1. `dotnet test HanMate.Core.Tests/HanMate.Core.Tests.csproj -c Release --no-restore`：188/188。
2. `dotnet test HanMate.Infrastructure.Tests/HanMate.Infrastructure.Tests.csproj -c Release --no-restore`：134/134，新增17项。四类往返/锁定/译文/隐私白名单、完整及嵌套冲突映射、映射复用/编辑后另存、重压缩/同包ID不同语义、回收站/epoch、取消/触发器失败回滚、来源限制、8类坏包、WAV真实字节/租约释放/拒绝导入音轨、交付副本保留/失败清理。
3. Windows `net10.0-windows10.0.19041.0` Release、Android `net10.0-android -t:Rebuild` Release、Windows宿主`net10.0-ios` simulator managed编译：全部0警告/0错误。iOS不是Mac/Xcode/原生运行证据。
4. 391个简中/日/英resx键集、非空与数字占位符集合一致。翻译可调整占位符顺序，不要求语言语序相同。

开发机日志：临时目录hanmate-sharing-windows-build.log、hanmate-sharing-android-build.log、hanmate-sharing-ios-build.log。最终APK SHA256：`87fa313ec98af01891743aba6364ad7f2ee507925ceb5f0c8485280aa52b444e`。

## Android模拟器聚焦：PASS

emulator-5554，Android16/API36，1080×2400；Release覆盖安装，不清库，宿主麦克风仍关闭。未选择联系人、上传或发送文件。

- 从学习→草稿→内容包导入与分享进入，选择已有Draft-test和Grammar-smoke。预览2条，明确排除2音轨/1草稿，个人权利确认2条。未勾选时生成被拒绝；勾选后生成并重新校验成功。列表仍显示原内容NOASSERTION，源权利未被持久修改。
- 打开Android系统分享面板，显示真实`.hanpack`文件，返回取消；UI仅显示“已调用系统分享；这不表示对方已收到文件”。
- 系统选择器读取工程transfer-smoke.zip（由规划样例选取word/text/grammar/poem各一条，仅标题改为Transfer-*，来源保持testFixture/draft）。首次预览新增4、复用0、冲突另存1，提交成功；重复相同包预览新增0、复用4、冲突0，提交后仍复用4。课文学习列表能看到Transfer-text和原Draft-test。
- Draft-test详情→完整unit音频管理，原Audio-smoke-tone 1秒导入音轨没有“分享此录音”；原Emulator-silence 17.4秒user录音有此入口。确认隐私说明后系统面板显示真实`.wav`文件，取消返回；两个needsReview状态及5.4秒音频草稿保持，未重新录制或改音。
- 修复了分享列表的语法类型资源键回退，以及窄屏授权文字固定宽度问题。截图已人工查看，无语法类型裸键或授权文本截断。

截图存于开发机临时目录HanMate-sharing-smoke/content-share.png、repeat-import.png、wave-share.png；工程输入transfer-smoke.zip同目录，设备Download也保留。工程Transfer-*四条保留便于后续测试，不计正式教材。既有语法、音频、收藏与150%阅读偏好未删除。

## 未完成 / NOT RUN

含音频内容包与音轨选择、系统另存文件、资源跨版本更新、完整备份/全部替换/回收站迁移、旧包兼容和物理音频GC未实现。W4当前工作仅覆盖文字子集和独立WAV分享，相关工作包仍IN_PROGRESS。

Windows新界面交互、iOS原生、Android真机、真实接收方打开文件、IME/读屏/系统字号、手机大包容量、满磁盘/强杀窗口、六方向迁移及176项完整用例NOT RUN。真实教学音频仍6/161；本轮模拟录音分享不是真人发音或录音质量验收。
