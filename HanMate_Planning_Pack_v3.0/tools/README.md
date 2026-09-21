# 文档和工程夹具校验

在规划包根目录运行；Windows 可用 `py -3` 或实际 Python 路径替换 `python`，建议加 `-X utf8`。依赖仅安装在开发用虚拟环境，不进入 MAUI 安装包：

```sh
python -m pip install -r tools/requirements-validated.txt
python -X utf8 tools/build_bundle.py --write
python -X utf8 tools/verify_bundle.py --write-report
python -X utf8 tools/build_bundle.py --write
python -X utf8 tools/build_bundle.py --check
```

当前脚本检查：本地Markdown链接/代码围栏、10份Schema结构、24条样例字音/范围/引用、四种ZIP容器的文件哈希与计数、22需求/176用例映射、48任务/64文案、21表SQLite约束、构造坏包拒绝、语法/字典/隐私负例、原v1包校验和内存内容转换。

复审新增：任务依赖存在性/环检查、状态与证据路径、派生文件一致性，以及严格 Unicode/JSON 深度和分享未选音频负例。176 个应用用例不由本工具执行；这里只核对清单和部分工程夹具。

`backlog.csv` 是任务表真源，`acceptance-cases.csv` 是验收预期和汇总状态真源；专题正文在 docs 编辑。build_bundle 仅重生成第 21 章任务表、第 29 章、汇总版和 FILE_MANIFEST，不改其他正文、状态、Schema 或 ZIP 样例。汇总链接到实际校验报告，不嵌入报告正文，避免生成循环。报告写入后会改变文件字节，故最后再生成并只读核对清单。单独核对已有包时用 `build_bundle.py --check`，不要先重生成来掩盖意外变更。

`--write-report` 从实际运行时生成 validated-environment.json；requirements-validated.txt 记录本次验证环境的精确依赖，requirements.txt 是允许范围。文件清单覆盖归档，但排除自身、Python 的 __pycache__ 和 .pyc。

应用任务允许 TODO/IN_PROGRESS/REVIEW/DONE/BLOCKED；REVIEW/DONE/BLOCKED 的 evidence 填规划包相对证据文件路径，DONE 还要求全部前置 DONE。应用用例允许 PASS/FAIL/NOT RUN/BLOCKED，PASS/FAIL/BLOCKED 须填写 evidence 路径，并在 TEST_MATRIX 记录各平台实际结果；只有所列平台都满足才汇总为 PASS。文件存在性不等于验证过其真实性，不用占位空报告制造完成。

旧协议读取复用archive/v2_baseline中归档的旧验证器与规则，而不是修改老manifest版本号就跳过哈希验证。实际数据库升级和平台代码没有实现，不能把此测试当成已经可安全升级用户库。

工具使用Python jsonschema/regex/SQLite，只在开发阶段运行；应用仍用.NET MAUI，不在手机中嵌入Python。验证器是有上限的样例检查工具，不是流式生产导入器、安全审计或音频解码器。

真实执行环境写在validated-environment.json；应用运行环境是另一份tracking/environment-lock.template.json，后者不能用工具环境填充。
