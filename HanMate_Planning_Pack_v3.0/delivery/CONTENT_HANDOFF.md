# 内容与音源交接

提供的是开发材料及可追溯审核入口，正式教学、译文和许可终审按用户要求后置。内容及注音均保留待审标识，不产生人工审核签名。

交付的content-review目录包含：逐单元正文/拼音/日英译文清单、离线HTML及实际随附WAV、对象/音频SHA256、默认字典293批清单、AI听审句集、来源核查摘要及当前review-decisions.json。最新数量以content-audit.json为准。源码ZIP保留当前打包资源，不带任何人的录音或数据库。

默认chinese-xinhua词库292,114条，固定上游revision `fe6d6c2e8baa82187f4c96bbe042e43f96c05666`，源记录保留。许可为NOASSERTION，分发权利未核清；293批全覆盖仅是审阅分工，不是逐条已通过。导出指定批次：

```powershell
python tools/dictionary_review_batches.py --batch dictionary-batch:xinhua:0001 --output C:/HanMate-review/batch-0001.json
```

随附音频来自audio-cmn固定提交 `ff9ed3d0c631195bd2c06f39450f3264c7124040`。原声明CC BY-SA未指定版本，不补写3.0/4.0；作者、源文件与转换记录按每条保存。独立ong原录音仍缺，AI发声不冒充真人录音。没有对外发送授权询问。

新增拼音例词按“同一字形＋同一基础音节＋同一声调”与例字关联，优先显示在对应例字之后。声调相同但不同字不算配对；多音字的不同读音不强配。保留原来的其他例词。新增词的释义/翻译/注音是开发稿；无整词录音时使用明确启用的离线AI或显示无音频，不拼接单字录音。

独立审核人后续逐对象审阅，在真实证据存在时填写subjectId、当前sha256、dimension、PASS/FAIL、method=human、reviewer、date、evidence；整批词库需allEntriesReviewed和准确reviewedEntries。先保存证据再录入结论；改变正文/音频后旧哈希结论不得沿用。

```powershell
python tools/content_review_audit.py --write --check
python tools/content_review_audit.py --release
```

第一个命令是源文件/审核材料一致性检查；第二个在尚有待审项时仍应阻塞。正式音文、读音、译文与授权的真实终审不能由脚本代替。
