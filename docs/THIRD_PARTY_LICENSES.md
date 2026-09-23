# 第三方许可核查 / Third-party licenses

核查日期：2026-09-21。核查对象：仓库当前资源，以及 GitHub `v0.1.0` 预览版采用的来源版本。本记录核对公开许可文本及本地打包配置，不代表所有内容权利已经获确认。

## HanMate 的 MIT 范围

项目所有者已选择 [MIT](../LICENSE)，适用于 HanMate 自有源码与原创文档。第三方代码、词典、课文引用、录音、模型、字体和衍生数据保留原许可。MIT 不重新许可这些资源，也不代替未知来源的授权。包含 GPL 组件的组合二进制还可能承担 GPL 分发义务；不能将整个安装包简单标为“全部 MIT”。

HanMate's original code and documentation use MIT. Third-party assets retain their own terms. A repository-level MIT license does not clear unresolved content rights or override copyleft obligations of distributed binaries.

HanMate の独自コードとオリジナル文書は MIT です。第三者素材は元の条件を維持します。MIT の追加によって、未確認の権利やバイナリのコピーレフト義務が解消することはありません。

## 已核实的来源声明

| 资源 | 实际证据 | 判断及未完成事项 |
| --- | --- | --- |
| chinese-xinhua 字典，`fe6d6c2e8baa82187f4c96bbe042e43f96c05666` | [MIT 文件](https://github.com/pwxcoo/chinese-xinhua/blob/fe6d6c2e8baa82187f4c96bbe042e43f96c05666/LICENSE)，Copyright 2018 PWXCOO；[README Copyright](https://github.com/pwxcoo/chinese-xinhua/blob/fe6d6c2e8baa82187f4c96bbe042e43f96c05666/README.md#copyright)说明数据抓取自多个网站 | **仓库声明 MIT 已确认；原始词典数据权利仍未确认。** 已补存上游 MIT 文件，不将现有 NOASSERTION 审核状态改成已批准。商店分发需取得数据权利依据，或改用权利清晰的数据。免费不自动等于获授权。 |
| audio-cmn 拼音及词语录音，`ff9ed3d0c631195bd2c06f39450f3264c7124040` | [固定版本 README](https://github.com/hugolpz/audio-cmn/blob/ff9ed3d0c631195bd2c06f39450f3264c7124040/README.md)列 Chen Wang / Yue Tan、CC BY-SA，无版本；[作者早期回复](https://github.com/hugolpz/audio-cmn/issues/1#issuecomment-46822989)提到 CC BY-SA-NC 意向 | **许可系列有声明，具体版本及历史 NC 差异未厘清。** 当前 README 仍未注明版本，不能自行改成 4.0；需作者澄清或替换录音。已有逐录音署名、来源和转换说明继续保留。未向作者发送消息。 |
| MeloTTS ONNX，`a0d5c6a264c0ef92d70d8661d8cc502d79627cd6` | [固定版本 LICENSE](https://huggingface.co/csukuangfj/vits-melo-tts-zh_en/blob/a0d5c6a264c0ef92d70d8661d8cc502d79627cd6/LICENSE)，MIT / MyShell.ai | **模型许可已确认**；本地 LICENSE 与远端文本一致。保留版权及许可。此结论不覆盖 TTS 引擎的其他组件。 |
| Kokoro v1.1-zh FP32，`914313412b607d95400bcd12446233fbd1248801` | [固定版本 LICENSE](https://huggingface.co/csukuangfj/kokoro-multi-lang-v1_1/blob/914313412b607d95400bcd12446233fbd1248801/LICENSE)，Apache 2.0；[原作者模型卡](https://huggingface.co/hexgrad/Kokoro-82M-v1.1-zh)说明 LongMaoData 许可来源 | **模型许可已确认**；本地 LICENSE 与远端文本一致。保留 Apache 许可及模型来源；不把模型作者的训练数据声明表述为独立权利审计。 |
| sherpa-onnx 1.13.8 及 eSpeak NG | [sherpa LICENSE](https://github.com/k2-fsa/sherpa-onnx/blob/v1.13.8/LICENSE)为 Apache 2.0；[TTS 构建入口](https://github.com/k2-fsa/sherpa-onnx/blob/v1.13.8/CMakeLists.txt)引入 eSpeak / Piper；[eSpeak 构建文件](https://github.com/k2-fsa/sherpa-onnx/blob/v1.13.8/cmake/espeak-ng-for-piper.cmake)固定源码并使用静态构建；[核心链接配置](https://github.com/k2-fsa/sherpa-onnx/blob/v1.13.8/sherpa-onnx/csrc/CMakeLists.txt)链接 piper_phonemize | **组合分发义务尚未完成。** 本地已附 GPL-3.0 的 eSpeak 许可，不能只看 NuGet 的 Apache 标签就断言安装包仅含宽松许可。需核对实际 Windows/Android 原生二进制、对应源码、构建/链接方式及 GPL 履约；或换用不包含该组件的实现。只切换默认声音为 Melo 不能证明排除了组件。 |
| 教育部《重編國語辭典修訂本》`2015_20260625` | [官方公众授权](https://language.moe.gov.tw/001/Upload/Files/site_content/M0001/respub/index.html)明确 CC BY-ND 3.0 TW，包括商业使用；[官方使用说明](https://language.moe.gov.tw/001/Upload/Files/site_content/M0001/respub/reviseddict_10312.odt)禁止改动词目、音读、释义和转为简化字 | **公开再分发许可有依据，但必须遵守不改作、版本和署名要求。** 本地保留官方说明。当前 Infrastructure 项目内嵌的是 xinhua，不能把此字典的许可当作 xinhua 的授权。若替换为教育部字典，应核对显示内容与原文一致。 |
| CC-CEDICT 读音索引 | [MDBG 官方页](https://www.mdbg.net/chinese/dictionary?page=cedict)明确 CC BY-SA 4.0 | **许可已确认**；保留署名、链接及对衍生索引的相同方式共享要求。它不授予对应录音的许可。 |
| Make Me a Hanzi 笔画图形派生数据，`bddc96d41bef78427ed0e034e9f7e31d71fd1b92` | [COPYING](https://github.com/skishore/makemeahanzi/blob/bddc96d41bef78427ed0e034e9f7e31d71fd1b92/COPYING)区分 dictionary 的 LGPL 与 graphics 的 Arphic Public License | **图形数据许可已确认**；本项目使用 graphics 的 medians 派生资产，保留 [Arphic 许可](../HanMate.App/Resources/Raw/Handwriting/ARPHICPL.txt)、修改说明及生成工具；并非 MIT 数据。 |
| Open Sans 随附字体 | 两个实际 TTF 的 name 表记载 Google 2010–2011、Apache License 2.0 | **随附版本许可已确认**；按实际字体元数据保留 Apache 许可，不套用其他新版字体的许可。 |

## 商店草稿与发布边界

商店状态复核（2026-09-23）：Microsoft Store 的 0.1.1 MSIX 已上传并显示 Validated，价格免费、无内购；中英日页面的 0.1.1 文案已保存，但每种语言仍缺真实 Windows 桌面截图。IARC 年龄分级草稿已将随包内容标为需要进一步分级，后续具体内容问题尚待审查；Submission options 页面虽已保存 `runFullTrust` 用途说明，概览仍显示 Incomplete，提交认证按钮禁用。Google Play 账号现可进入创建应用页面，现有 `com.companyname.hanmate.app` 包名检查显示可用；应用尚未创建。创建页要求确认开发者政策合规、接受 Play App Signing 条款并作出口合规证明，当前权利及分发义务未解决，不能据此作合规声明。现有 Android AAB 使用开发签名，正式上传签名与保数据升级仍待完成。

GitHub 的 `v0.1.0` 预览附件已在本次核查前公开。这些附件同样包含上述资源，**同样受未解决事项影响**；添加 MIT 文件不会追溯修复旧二进制的资源许可与通知文件。本文不将已有公开状态当作许可通过证据。

本轮补齐的是源码许可、来源证据和缺失的通知文件，未删除或替换功能资源，未修改已发布标签。后续商店发布前需解决 xinhua 数据权利、录音许可版本/NC 差异、GPL 组合分发，并对课文、诗词引用及其译文逐项核对来源。没有作者许可时，不能靠自动审核产生授权。

## 核查记录

远端来源文本和响应保存在本地 `artifacts/license-audit-20260921/`；上表提供可复查的固定版本链接。HTTP 获取成功只证明拿到了文件，不代表许可履约完成。自动化内容审核及听音质量审核仍是独立工作。
