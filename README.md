<p align="center"><img src="docs/assets/hanmate-icon.svg" width="112" height="112" alt="HanMate 图标" /></p>

# HanMate · 汉语小伴

**简体中文** · [English](README.en.md) · [日本語](README.ja.md)

**[下载 0.1.3 预览版](https://github.com/yoi102/HanMate/releases/tag/v0.1.3)** · [版本说明](docs/releases/0.1.3.md) · [隐私政策](PRIVACY.md)

免费，无内购。GitHub 已提供 Windows / Android 预览包；Microsoft Store 与 Google Play 尚未上架。

面向中文学习的离线应用，基于 **.NET 10 / .NET MAUI**，将拼音、词语、课文、语法、诗词、字典和朗读放在一起。界面支持简体中文、日语和英语，教学中文与拼音保持原样。

当前主要开发和验证平台为 **Windows 与 Android**。工程保留 iOS / Mac Catalyst 目标，但尚不作为已验证的平台交付。

## 功能

| 入口 | 可以做什么 |
| --- | --- |
| 拼音 | 学习声母、韵母、整体认读音节及声调，通过例字、例词和匹配录音练习发音。 |
| 学习 | 按类别学习词语，阅读课文、语法和诗词；显示拼音，在非中文界面显示已有辅助译文。 |
| 搜索 | 查询本地字典，查看释义、拼音和已有例句，支持手写输入与词条收藏。 |
| 收藏 | 使用收藏夹整理内容，直接打开与学习模块相同的详情页。 |
| 设置 | 调整语言、阅读显示和朗读声音，管理内容资源、导入导出与备份恢复。 |

### 学习与阅读

- **词语**：包含常用、称呼、招呼、数字、重量、数量词、长度、调味料、厨房用具、生活用品等类别；数字等内容按学习顺序排列。
- **词语点读**：单击播放，再次点击可停止；长按打开已有释义，显示已收录的用法和例句。
- **课文与诗词**：标题、作者信息及正文支持拼音；点击标题、作者或正文片段可朗读，也可独立播放全文。
- **语法**：集中展示语法说明、句型和例句。
- **返回与导航**：收藏数据未变化时保留列表；底部标签重复点击或长按可返回该标签首页。

支持个人内容编辑、拼音校正、录音及内容分享。内容包、字典资源与个人收藏独立管理；删除收藏不会删除正文或音频。

局域网分享：在词语、课文、语法或诗词详情的“更多 → 分享”发送单项；也可在各模块列表的“更多”中选择多个项目，或在词语类别页选择多个类别。发送端输入两位房间号（如 07）并创建房间，接收端从学习首页的“通过 Wi-Fi 接收”输入相同号码后开始查找；页面会持续搜索，找到后自动加入。若广播搜索不到，可输入发送方页面显示的局域网 IP 再查找；同号房间有多个时需选择发送设备。发送端勾选接收设备后发送，并逐台查看接收与导入结果。双方需处于同一局域网，传输包最多 32 MiB。词语会附带来源类别信息，接收端可选择目标类别。可选择附带本人录音及首选录音设置；语音包文件和合成音频不会发送，接收端的朗读引擎设置也不会改变。两位号码只是查找标签，发送前应核对设备；受分享限制的预置内容不能导出。

四类内容的列表页把“添加内容”和“选择要分享的学习内容”放在右上角“更多”中。选择模式可跨翻页勾选最多 100 项，点悬浮分享图标后直接为已选内容创建房间；详情页的分享仍只发送当前项目。

### 朗读

在“设置 → 朗读声音”选择可用声音：

- **MeloTTS**：内置中文女声，设备本地合成。
- **Kokoro**：内置中文男声、女声，设备本地合成。
- **系统语音**：使用设备已安装的语音，支持情况由操作系统决定。
- **已有录音**：在适用的教学内容中优先使用匹配录音。

离线 AI 朗读会将装饰性符号转换为停顿分隔符，避免因释义中的特殊符号而拒绝整段播放；显示和保存的原文不变。模型首次加载仍可能有等待时间，合成效果与速度受设备、文本和声音选择影响。

## 获取源码

语音模型和压缩字典通过 **Git LFS** 保存。请先安装 [Git](https://git-scm.com/downloads) 与 [Git LFS](https://git-lfs.com/)，再执行：

```powershell
git lfs install
git clone https://github.com/yoi102/HanMate.git
cd HanMate
git lfs pull
git lfs ls-files
```

首次拉取需要下载数百 MB 的资源。建议使用 Git 克隆；网页下载的源码 ZIP 不保证包含实际 LFS 文件。若模型只是很小的文本指针文件，请先执行 `git lfs pull`，再构建应用。

## 开发环境

- Windows 开发机与 [.NET SDK 10.0.401](https://dotnet.microsoft.com/download/dotnet/10.0)，版本策略见 [global.json](global.json)。
- .NET MAUI 工作负载；可使用支持该 SDK 的 Visual Studio，或 .NET CLI。
- Windows 构建需要 Windows SDK；Android 构建需要对应的 Android SDK 与 JDK，可由 Visual Studio 的 MAUI 开发环境安装。
- Python 仅用于部分素材生成和审计工具，普通应用构建不需要运行这些工具。

```powershell
dotnet --version
dotnet workload install maui
```

NuGet 版本集中在 [Directory.Packages.props](Directory.Packages.props)，各项目附带依赖锁文件。首次恢复需要网络；内置模型由 Git LFS 提供，普通构建不会另行下载模型。

## 构建与测试

以下命令从仓库根目录运行，按目标平台分别恢复，避免构建暂未验证的平台。

### Windows

```powershell
dotnet restore HanMate.App/HanMate.App.csproj --locked-mode -p:TargetFrameworks=net10.0-windows10.0.19041.0
dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-windows10.0.19041.0 -p:TargetFrameworks=net10.0-windows10.0.19041.0 --no-restore
```

当前采用非打包 Windows 应用。调试时可在 Visual Studio 中选择 `HanMate.App` 和 Windows 目标运行。

### Android

```powershell
dotnet restore HanMate.App/HanMate.App.csproj --locked-mode -p:TargetFrameworks=net10.0-android
dotnet build HanMate.App/HanMate.App.csproj -c Debug -f net10.0-android -p:TargetFrameworks=net10.0-android -p:EmbedAssembliesIntoApk=true --no-restore
```

APK 位于 `HanMate.App/bin/Debug/net10.0-android/`。内置语音模型使安装包较大，首次构建和安装需要一定时间。Debug APK 使用开发签名，不是商店发行包。

### 自动测试

```powershell
dotnet test HanMate.Core.Tests/HanMate.Core.Tests.csproj -p:RestoreLockedMode=true
dotnet test HanMate.Infrastructure.Tests/HanMate.Infrastructure.Tests.csproj -p:RestoreLockedMode=true
```

自动测试覆盖核心规则、注音、音频输入处理、收藏、内容资源及数据恢复等逻辑；不代替设备上的界面、麦克风、朗读听审和无障碍验证。

## 工程结构

| 路径 | 用途 |
| --- | --- |
| [HanMate.App](HanMate.App) | MAUI 页面、交互、平台适配与音频服务。 |
| [HanMate.Core](HanMate.Core) | 内容模型、注音、阅读、音频与业务规则。 |
| [HanMate.Infrastructure](HanMate.Infrastructure) | SQLite、字典、资源包、收藏、导入导出与恢复。 |
| [HanMate.Core.Tests](HanMate.Core.Tests) | 核心逻辑测试。 |
| [HanMate.Infrastructure.Tests](HanMate.Infrastructure.Tests) | 持久化、资源和恢复等测试。 |
| [tools](tools/README.md) | 素材生成、资源审计及开发验证工具。 |
| [HanMate_Planning_Pack_v3.0](HanMate_Planning_Pack_v3.0/README.md) | 产品规格、架构、状态、验证证据和交付文档。 |

## 数据与隐私

正文、收藏、录音和设置保存在本机；离线模型在设备上合成，不上传朗读正文。可下载语音资源只在用户主动操作时访问指定模型源。系统语音的行为取决于设备和系统语音服务。

分享与备份文件可能包含个人正文或录音，请自行保管。开发时不要将个人数据库、备份、录音、凭据或签名密钥提交到仓库。构建产物和本地验证报告存放于被 Git 忽略的 `artifacts/` 等目录。

## 当前状态与资料

本项目处于持续开发阶段。2026-09-21 的收藏详情跳转改动已通过 16 项相关自动测试、Windows Release 和 Android Debug 构建；本次改动尚未完成设备交互验证。构建通过不代表完整产品验收或正式发行。

- [开发状态](HanMate_Planning_Pack_v3.0/tracking/STATUS.md)
- [交接说明](HanMate_Planning_Pack_v3.0/tracking/HANDOFF.md)
- [剩余工作](HanMate_Planning_Pack_v3.0/tracking/REMAINING_WORK.md)
- [内部交付说明](HanMate_Planning_Pack_v3.0/delivery/README.md)
- [用户指南与常见问题](HanMate_Planning_Pack_v3.0/docs/24_User_Guide_and_FAQ.md)

详细文档保留历史阶段记录，具体结果请结合记录日期和对应验证范围阅读。教学内容审校、音源听审、资源再分发权利、正式签名及商店发布仍有独立待办。

## 许可证与第三方资源

HanMate 自有源码与原创文档采用 [MIT 许可证](LICENSE)。第三方代码、字典、录音、模型、字体和衍生数据不因此改为 MIT，仍遵循各自的许可与署名要求；来源许可未明确的素材也不因此获得再分发授权。详见[第三方许可核查](docs/THIRD_PARTY_LICENSES.md)。

- [语音模型及运行库说明](HanMate.App/Resources/Raw/Voices/NOTICE.txt)
- [拼音录音说明](HanMate.App/Resources/Raw/Pinyin/NOTICE.txt)
- [词语录音说明](HanMate.App/Resources/Raw/WordAudio/NOTICE.txt)
- [字典来源说明](HanMate.Infrastructure/Dictionary/NOTICE.json)
- [默认字典来源及审核状态](HanMate.Infrastructure/Dictionary/XINHUA-NOTICE.json)
- [手写资源说明](HanMate.App/Resources/Raw/Handwriting/NOTICE.json)

各资源目录保留上游许可证。工程集成与自动校验不等于内容、发音质量或再分发许可已完成最终审核。
