# 拼音单击与长按使用向导 · 2026-09-20

## 交付范围

按用户要求，在拼音页增加两步使用向导：单击拼音听教学读音，长按约半秒打开例字例词。示例使用课程中的真实 `b` 按钮和已有播放、导航服务；未更改教学音源优先级。

首次进入自动展示，可跳过、上一步、下一步或开始练习。关闭、完成或实际长按进入例词后，用本机偏好 `pinyin.gesture-guide.v1.seen` 记住状态。第一分组右侧的 `?` 可重看；切换标签临时关闭但不记为完成。遮罩显示时禁用底层拼音表，并从辅助功能树排除；离页停止本页音频。卡片受可用宽高约束，内容可滚动。中文、日文、英文文案齐备，Windows 补充右键/F10提示。

实现：`HanMate.App/Controls/PinyinGuideView.cs`、`HanMate.App/Pages/PinyinPage.cs`、三语言资源。规格同步至文档03、用户指南24。

## 验证

原始证据位于仓库 `artifacts/pinyin-guide-20260920/`。

| 项目 | 结果 | 证据与边界 |
|---|---|---|
| Windows Release 构建 | PASS | `windows-build.log`，0警告0错误 |
| Android Debug 完整包构建 | PASS | `android-build.log`，0警告0错误 |
| 三语言资源一致性 | PASS | `localization-check.json`；各615键，向导12键，无重复，占位符一致 |
| Android x64 构建与界面验证包安装 | PASS | `android-emulator-build.log`、`emulator-reduced-install.log`；仅验证包移除大型语音模型，代码和Android资源保持一致，教学录音保留 |
| 首次自动展示、两步布局 | PASS | `step1-first`记录首次展示；`final-step1`、`final-step2`截图/XML记录修正后的居中紧凑卡片 |
| 单击触发真实播放 | PASS | `final-tapped`反馈及`final-tap-audio.log`的MediaPlayer/AudioTrack播放记录；不声称真人听审 |
| 上一步、下一步、长按进入例词 | PASS | `final-previous`、`final-step2`、`final-longpress`，长按进入b的例字例词页 |
| 返回后不自动弹、问号重看 | PASS | `final-return`、`final-reopen` |
| 跳过、Android返回、开始练习关闭 | PASS | `final-skipped`、`final-back`、`final-before-done`及`final-done` |
| 冷启动保持完成状态 | PASS | `final-cold-restart`未出现向导，问号入口仍在 |
| 底层拼音按钮屏蔽 | PASS | 显示向导时底层按钮enabled=false；`final-accessibility.xml`压缩无障碍树中底层拼音按钮为0。未进行TalkBack真人验收 |
| 实体手机安装与交互 | NOT RUN | 当前 adb 未检测到此前 vivo 真机，没有更改手机资料或设置 |
| 完整包覆盖安装模拟器 | BLOCKED | `INSTALL_FAILED_INSUFFICIENT_STORAGE`；未卸载或清空应用 |

完整包身份见 `apk-identity.json`：909472755 字节，SHA256 `4E6E1540A9E0564D9DAE32A7A37C9FB9B7254825FF0328CFDFF86E3538274FFE`。这是开发签名构建，不是正式发行包。

模拟器当前使用208540691字节的界面验证包，SHA256 `1F03C4D4651C4DB72E39E6E9AE755ED39ADC0D6A1A5FEC3168823E2E11787187`。`emulator_package.py`仅从x64包排除`assets/Voices/`并重新对齐/开发签名，具体列表见`emulator-package-scope.json`，不能用于验收内置模型。完整交付包仍包含所有原有语音模型。

安装排障时仅删除了模拟器可再生成的默认字典缓存；没有删除收藏、录音或个人正文。初次尝试复用旧快速部署目录遇到旧Android资源的TextAppearance启动异常，随后安装上述匹配资源的验证包，并将整个旧覆盖目录移动到`files/.guide-old-fastdeploy-20260920`，未恢复旧程序集。最终包启动/交互期间`final-crash.log`无新增崩溃。模拟器旧程序集额外备份在`files/.pinyin-guide-backup-20260920`；后续不要恢复这些旧覆盖文件。

Windows本轮仅构建通过，未执行Windows界面/右键/F10验收；iOS、真机、字体缩放与三语言逐页视觉验收NOT RUN。

本轮不扩写为整体设备、读屏或教学听审完成，不改变45/48及正式发布后置门禁。
