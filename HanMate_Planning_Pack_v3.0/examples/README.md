# 开发验证样例

共有24条ContentDocument草稿，其中3条grammar，另有字典词条与学习包词条。它们只用于数据/范围/引用/包结构验证；正文、拼音、译文、教学难度均未正式终审，**不含任何录音文件**。

| 文件 | 作用 |
|---|---|
| contents.json | 当前协议2的集合，含个人和资源内容 |
| grammar-content.json | 一条完整语法数据示例 |
| dictionary-entry.json | word+definition字典条目示例 |
| learning-resource.json / dictionary-resource.json | 两个可安装资源的描述和entry映射 |
| resources.json | 备份登记、启停/优先级/撤下状态 |
| sample-content.hanpack | 24条资料的个人分享包，无资源拥有权 |
| sample-backup.hanbackup | 24条内容、2个资源登记、收藏/设置，无音频 |
| sample-learning.hanresource | 学习资源安装包：3条语法、1词 |
| sample-dictionary.handict | 字典安装包：3个词条 |
| ui-copy.csv | 64条简中/日/英文案初稿，待母语审校 |
| pinyin/segmentation/search-cases.json | 保留旧算法行为样例 |

四个包均为真实ZIP容器，内部清单可校验，但当前没有MAUI应用实现来直接导入。不要把无音频样例的验证通过描述成“真实录音迁移已完成”。

样例来源标识只说明当前工程草稿由规划编写；不代表教材、第三方词库或录音的授权证明。正式发布数据须独立管理来源许可和审核状态。
