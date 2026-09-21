# 字典收藏与词头播放 · 2026-09-20

实现：词头旁的SVG星形图标切换空心/实心；点击词头文字播放，不显示独立播放按钮。使用原生按钮提供键盘及本地化读屏标签，保持既有单会话播放和离页停止。

默认字典及随附拼音条目仅保存来源、稳定ID和标题引用；收藏页提供“字典收藏”入口及移除操作。普通内容仍使用现有收藏夹。收藏不会复制释义或音频。collections v2独立携带引用，v1兼容及安全替换规则见ADR-107。

## 验证

- PASS：84项Infrastructure相关自动测试（73项BackupTests含安全恢复、8项LearningFavoritesDraftTests、3项DefaultDictionaryTests）。包含新增10项收藏持久化、两次备份迁移、不恢复设置时仍恢复引用、v1合并/替换、安全副本、事务失败回滚、陈旧预览和恶意引用拒绝。
- PASS：Windows Release构建，0警告/0错误；空心/实心星图标已生成各缩放PNG。
- PASS：Android Release构建，0警告/0错误；已核对开发签名APK包含两种星形资源。
- PASS：简中/日/英各593键，键集及占位符一致。
- NOT RUN：设备/UI调试、点击和实际试听、新包安装；遵循用户“先不调试”。不把代码/构建结果写成新UI验收通过。

日志与TRX：仓库artifacts/dictionary-favorites-20260920/。Windows/Android产物使用全新TEMP/HanMate-dictionary-favorites-20260920/；未覆盖D8交付归档。

45/48、原176聚合NOT RUN、82条执行记录及HM-D019 OPEN均不变；正式审核/签名/发布继续后置。
