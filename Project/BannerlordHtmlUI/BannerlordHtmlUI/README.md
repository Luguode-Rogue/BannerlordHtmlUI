
## 规范入口

当前架构、API、线程契约与回归清单见仓库根目录：[DEVELOPMENT_GUIDE.md](../../DEVELOPMENT_GUIDE.md)。
Surface 设计见 [docs/SURFACES.md](docs/SURFACES.md)。历史文档已归档至 [docs/过期文档/](docs/过期文档/)。

## v0.42 logging behavior

Normal runtime no longer emits per-tick Window Tracking log lines. Window state transitions remain observable without flooding the log.
