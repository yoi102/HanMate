# 实体手机语音修复 · 2026-09-20

用户重新明确授权实体手机调试与覆盖安装。本记录补充此前语音和启动证据，不改写历史构建结论；45/48、176聚合NOT RUN及82条执行记录保持不变。

## 修复与设备

vivo V2408A，Android应用 `com.companyname.hanmate.app`。最终安装本轮v4 Debug APK，安装结果Success，未卸载、未清除学习资料。旧快速部署程序集覆盖新APK，已将私有目录 `files/.__override__` 移为 `files/.speech-fastdeploy-backup-20260920` 留存，不恢复旧程序集。旧模型缓存保留。

- 系统TTS：支持设备实际返回的zh-Hans/zh-Hant/zh普通话标签；仍过滤需网络或未安装音色。Android音色标识加入语言标签，解决同名zh冲突，并兼容旧偏好。vivo引擎实际选择zh-Hans。
- Kokoro：旧INT8在该手机产生非有限采样。替换为同系列v1.1 FP32，保留原voices.bin与speaker ID，成功验证新包后迁移已有音色。Kokoro语言明确设为zh。
- 性能：进程内缓存已校验文件状态，变化/缺失时重验；首次及安装仍完整SHA256校验。最多缓存一个原生引擎及一个字符词典，原生调用串行；切模型/明确音素表时重建，后台/窗口销毁停止后释放。
- 字典：点击时懒加载播放协调器，避免页面初次出现时Handler尚未就绪导致无法点读；补常见引号、斜线和破折号的停顿规范化。不支持输入仍明确报错。
- 私有诊断文件限64KiB，只记录音源、字符数量、耗时、采样有效性和取消状态，不记录正文或文档ID。教学录音优先规则保留。

## 模型及构建身份

FP32源：`csukuangfj/kokoro-multi-lang-v1_1`，revision `914313412b607d95400bcd12446233fbd1248801`，`model.onnx` 325631784字节，SHA256 `acc4adc175b9d9986106cd20060329673ad5a2e12ef3c557d2d3745b694f8b38`，与固定源LFS哈希一致。辅助资源保持原固定来源。新包374文件、414330311字节；固定语音包容量上限512MiB。源模型未修改，正式许可与发行审查仍后置。

APK：`HanMate.App/bin/device-speech/android/com.companyname.hanmate.app-Signed.apk`，880234303字节，SHA256 `a9098c6635430142305c748bc047b2f2bf0a93a01d50306ec3da2049c4a7122e`。这是开发签名Debug包，不是正式发行包。

## 实际验证

| 项目 | 结果与范围 |
|---|---|
| Core受影响测试 | PASS，40项 |
| Infrastructure语音包测试 | PASS，27项，含文件损坏/删除重验、替换保留音色 |
| Android Debug v4 / Windows Release | PASS，各0警告0错误 |
| Windows生产FP32合成探针 | PASS，音色3、11、58及58复用，共4次有效非静音PCM；不计真人试听 |
| MeloTTS真机试听 | PASS，用户确认“听到了，清楚” |
| vivo系统TTS真机试听 | PASS，用户确认“听到了，清楚” |
| Kokoro中文女声与男声58 | PASS，用户确认“男女声都听到了，清楚” |
| 字典词头、释义、例句 | PASS（聚焦），真实词条“一个”；三音源均有段落请求与完成日志 |
| 同目标再点、换目标 | PASS（聚焦），例句播放取消约20ms完成清理；换目标先旧请求cancelled=True结束，再启动新请求 |

短句性能仅代表本机固定样本：Kokoro冷引擎加载约1.3秒，10字合成约1.7—1.9秒；复用后2字词头约418ms、8字例句约1.2秒，后续27字段落约4.4秒。Melo冷引擎约2.4秒，10字合成约1.1秒。首次模型解包另有等待，不承诺秒开或长段落无等待。原生合成期间取消仍需等待当前调用返回。

原始材料：仓库 `artifacts/device-speech-20260920/` 下 `core-tests.log`、`voice-tests-final.log`、`android-debug-v4.log`、`windows-build.log`、`fp32-windows-probe.log`、`install-v4.log`、`speech-diagnostics-final.log`、`apk-manifest.json` 与UI快照。手机同时有用户操作，交互判定依据明确的请求/取消顺序，不把所有后续点击视为受控测试。

NOT RUN：全部100中文音色、全字典输入、教学音准终审、全量回归、断网场景、完整路由/压力、Windows本轮UI及iOS。Melo中文男声来源仍缺，R14 OPEN；本次Kokoro男女声确认不关闭它。
