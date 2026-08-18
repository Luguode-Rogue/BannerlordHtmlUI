# Handoff 历史归档

`Handoff/` 保存阶段性交接、审计、Bug Postmortem、里程碑和历史决策。它是**证据与历史上下文仓库**，不是当前规范的唯一入口。

## 当前工作先看

1. `../Project/BannerlordHtmlUI/BannerlordHtmlUI/docs/README.md`
2. `../Project/BannerlordHtmlUI/BannerlordHtmlUI/docs/PROJECT_STATUS.md`
3. `../Project/BannerlordHtmlUI/BannerlordHtmlUI/docs/ARCHITECTURE_MASTER.md`
4. `../Project/BannerlordHtmlUI/BannerlordHtmlUI/docs/BUG_KNOWLEDGE_BASE.md`
5. `../Project/BannerlordHtmlUI/BannerlordHtmlUI/docs/TESTING_AND_REGRESSION.md`

## 历史资料

本目录中的：

- `PROJECT_HANDOFF_*.md`
- `PROJECT_STATUS.md`
- `FULL_CODE_AUDIT_*.md`
- `BUG_POSTMORTEM_*.md`
- `BUGFIX_*.md`
- `M*_*.md`
- `DECISIONS.md`
- `KNOWN_ISSUES.md`
- `ROADMAP.md`
- `REGRESSION_TESTS.md`

均保留作为历史上下文。**不要为了整理文档删除 Bug 复盘或失败方案。**

## 当前状态冲突说明

旧交接文件可能反映不同日期、不同版本和不同分支的状态。例如某些历史文件中的版本号与当前 `dev` 文档目标并不一致。

遇到冲突时，以 `Project/BannerlordHtmlUI/BannerlordHtmlUI/docs/PROJECT_STATUS.md`、当前代码和最近一次实机验证为准；旧 Handoff 只用于追溯“为什么曾经这样设计/修复”。

## 文档治理

未来新的修复继续写入完整 Bug/Postmortem；同时在 `docs/BUG_KNOWLEDGE_BASE.md` 增加检索入口。不要只更新摘要而丢失原始排查过程。
