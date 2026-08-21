# Handoff 历史归档

这里**不是当前项目文档入口**，只保存已经发生过的排错现场和审计证据。

## 当前文档

请直接看仓库根目录：

- `README.md`
- `ARCHITECTURE_MASTER.md`
- `BUG_KNOWLEDGE_BASE.md`
- `API.md`
- `DEVELOPMENT_GUIDE.md`
- `PROJECT_STATUS.md`

## 保留内容

只保留以下类型：

- `BUGFIX_*.md`：真实修复现场与验证证据
- `BUG_POSTMORTEM_*.md`：事故/回归复盘
- `FULL_CODE_AUDIT_*.md`：历史全量审计
- `NAVIGATION_RACE_*.md`：关键竞态排查证据

这些文件记录“当时发生了什么、为什么这样修、哪些方案失败”，不能覆盖当前代码事实。

## 删除原则

阶段性交接、旧项目状态、旧路线图、旧测试表、里程碑说明不再作为独立文档长期维护；当前状态统一写入根目录 `PROJECT_STATUS.md`，当前规则统一写入根目录 `ARCHITECTURE_MASTER.md`。
