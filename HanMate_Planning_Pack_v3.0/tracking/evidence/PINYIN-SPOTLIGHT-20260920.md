# 拼音四步目标高亮引导 · 2026-09-20

## 用户修订与实现

用户要求替换弹窗式向导：除了目标之外其余界面整体灰暗，以箭头、文字提示引导；前两步在拼音页单击、长按，后两步在例字页单击、长按例字。

`PinyinGuideView` 改为窗口遮罩，保留真实目标处的镂空，白色描边和箭头指向目标；没有复制示范按钮。用户进一步要求背景60%透明，采用`#66000000`黑色遮罩（40%不透明度），并显式清除BoxView隐式样式的实色背景。提示文字附紧凑底色，避免与透出的拼音文字重叠。Android覆盖应用导航区，并让镂空处手势交给原控件。提示位置跟随窗口/目标位置变化；提示内容可滚动，非目标区域阻挡误触。

`PinyinGuideSession` 将四步串联：单击b播放后进入第二步，长按b进入例字页继续第三步；单击八播放后进入第四步，长按八打开字典释义并完成。保留已有教学录音及字典查询路径。错误手势不越级，允许跳过或返回关闭；第一分组问号可重新开始。采用独立本机v2完成标记，使看过旧版两步向导的人仍能看到新指引。

相关实现：`HanMate.App/Controls/PinyinGuideView.cs`、`PinyinGuideSession.cs`、`HanMate.App/Pages/PinyinPage.cs`、`PinyinExamplesPage.cs`及三语言资源。规格03、用户指南24同步修订。

## 验证范围

原始证据位于 `artifacts/pinyin-spotlight-20260920/`。本轮真机没有连接；不把模拟器结果当作实体手机或正式发布验收。模拟器因存储限制继续使用排除大型语音模型的专用界面验证包，教学录音保留，应用代码与Android资源来自本轮构建。完整交付包仍应保留原有模型。

| 检查 | 状态 | 证据 |
|---|---|---|
| 三语言键、占位符一致性 | PASS | `localization-check.json`，12个向导文案键 |
| Windows Release、Android Debug完整包 | PASS | `windows-build.log`、`android-build.log`，均0警告0错误 |
| 模拟器验证包安装 | PASS | `emulator-install.log`；源自完整包，排除大型模型和非x64原生库，不用于模型验收 |
| 四个真实手势依次推进 | PASS | `tour-result.log`、`final-step1`—`final-step4`及`final-dictionary`截图/XML；最后实际打开八的字典释义 |
| 遮罩60%透明度 | PASS | `transparency-check.json`：原白色255的边缘像素变为153，即60%透出；目标保持原色 |
| 返回、重看、误触屏蔽、跳过、冷启动状态 | PASS | `exit-result.log`；非目标拼音/底部导航点击保持当前步骤，退出后普通拼音按钮恢复，重启不再自动展示 |
| 实体手机安装与四步交互 | NOT RUN | adb仅检测到模拟器 |

完整开发APK为909819789字节，SHA256 `1E035CE2BC1C36D04F852A9D5FF43357F76EE218C325B9C502ACC61D0521E9CA`；模拟器专用包208883629字节，SHA256 `6F5165E1E0A27C913C281B05C2D5194608D6AC3DCE3D803A43FD8A4E4E008B99`。机器可读身份见`apk-identity.json`，排除项见`emulator-package-scope.json`。手机尚未安装本版。

实测修复了原生窗口覆盖层没有MAUI父布局时的尺寸同步，以及例字页OnAppearing早于Handler就绪时启动向导导致的异常。最终采用页面/目标Loaded及尺寸就绪后显示；最终构建启动和四步流程无新增崩溃，见`final-crash.log`。排障时尝试的临时程序集覆盖文件已移除，最终验证直接使用安装包。Windows本轮只有编译证据，未验窗口缩放/右键/F10；TalkBack、真机、iOS与正式听审NOT RUN。

整体工作包计数及正式审校、iOS、发布门禁不因本轮UI修改变化。
