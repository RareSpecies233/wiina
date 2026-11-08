# 文档归档（docs/archive）

本目录保存**已完成使命的历史文档**。归档不是删除：这些文件仍然可读、可引用、可追溯，只是**不再代表当前状态**。

- 归档日期：2026-11-08
- 基线 commit：`a440803`（`docs: add 1.3.11 fix plan, WinUI 3 audit and docs archive audit`）
- 归档依据：[`docs/reports/docs-audit-1.3.11.md`](../reports/docs-audit-1.3.11.md)、[`docs/reports/fix-plan-1.3.11.md`](../reports/fix-plan-1.3.11.md) §13/§16

## 归档原则

1. **不是删除，是降级**：历史文档保留原样，便于追溯当时的判断、证据与决策；只在目录上移出主树。
2. **不再当现状读**：归档文件里的版本号、性能数字、结论都停留在写作当时；与当前文档冲突时，一律以当前文档为准。
3. **当前文档不归档**：`v1.x` 版本验收页是发布记录，全部保留在 [`docs/acceptance/`](../acceptance/README.md)；architecture ADR 记录“为什么这样设计”，保留在 [`docs/architecture/`](../architecture/0001-video-pipeline.md)；当前 reference/release 文档保留并随版本更新。
4. **旧路径留 stub**：被大量历史页/PR/issue 引用的三个文件在原位保留一页“已归档”重定向，避免旧链接 404（见下方“旧路径 stub”）。
5. **链接已重写**：主树内所有指向归档文件的相对链接都已改为指向本目录；归档文件内部的相对链接也按新深度重算。

## 目录结构

```
docs/archive/
  README.md                  # 本文件
  planning/PLAN.md           # v1 开工前的范围/阶段草案
  acceptance/                # Stage 0-11 与 v1 修复轮（v1-polish 1-4）
  reports/                   # 一次性分析报告（1.2.7 / 1.3.0 / 1.3.1 / 1.3.7 / 1.3.8）
```

## 归档文件与当前替代

### planning

| 归档文件 | 原路径 | 为什么归档 | 当前替代 |
|---|---|---|---|
| [PLAN.md](planning/PLAN.md) | `docs/PLAN.md` | 头部自述已是历史文档，v1 开工前的范围/阶段草案 | [release/notes.md](../release/notes.md)、[acceptance/README.md](../acceptance/README.md) 与各版本验收页 |

### acceptance（阶段验收与 v1 修复轮）

| 归档文件 | 原路径 | 为什么归档 | 当前替代 |
|---|---|---|---|
| [stage-0.md](acceptance/stage-0.md) | `docs/acceptance/stage-0.md` | Stage 0 基线验证，一次性 | 各 `v1.x` 版本验收页；当前状态看最新版本页 |
| [stage-1.md](acceptance/stage-1.md) | `docs/acceptance/stage-1.md` | 主窗口静态骨架 | 同上 |
| [stage-2.md](acceptance/stage-2.md) | `docs/acceptance/stage-2.md` | OSC 实装 | 同上 |
| [stage-3.md](acceptance/stage-3.md) | `docs/acceptance/stage-3.md` | 播放内核接入 | 同上 |
| [stage-4.md](acceptance/stage-4.md) | `docs/acceptance/stage-4.md` | 侧栏与快速设置 | 同上 |
| [stage-5.md](acceptance/stage-5.md) | `docs/acceptance/stage-5.md` | OSD / 缩略图预览 | 同上 |
| [stage-6.md](acceptance/stage-6.md) | `docs/acceptance/stage-6.md` | 模式与多窗口 | 同上 |
| [stage-7.md](acceptance/stage-7.md) | `docs/acceptance/stage-7.md` | 设置 / 菜单 / 欢迎 | 同上 |
| [stage-8.md](acceptance/stage-8.md) | `docs/acceptance/stage-8.md` | 滤镜 / 均衡器 | 同上 |
| [stage-11.md](acceptance/stage-11.md) | `docs/acceptance/stage-11.md` | 打磨与发布（v1 前） | 同上 |
| [v1-polish.md](acceptance/v1-polish.md) | `docs/acceptance/v1-polish.md` | v1 修复轮 1 | `v1.1.0`～`v1.3.x` 版本验收页 |
| [v1-polish2.md](acceptance/v1-polish2.md) | `docs/acceptance/v1-polish2.md` | v1 修复轮 2 | 同上 |
| [v1-polish3.md](acceptance/v1-polish3.md) | `docs/acceptance/v1-polish3.md` | v1 修复轮 3 | 同上 |
| [v1-polish4.md](acceptance/v1-polish4.md) | `docs/acceptance/v1-polish4.md` | v1 修复轮 4 | 同上 |

> Stage 页引用的截图仍留在 [`docs/acceptance/assets/`](../acceptance/assets)（未随页面移动），归档页内的相对链接已改为 `../../acceptance/assets/…`。

### reports（一次性分析报告）

| 归档文件 | 原路径 | 为什么归档 | 当前替代 |
|---|---|---|---|
| [hang-analysis-1.2.7.md](reports/hang-analysis-1.2.7.md) | `docs/reports/hang-analysis-1.2.7.md` | 针对 1.2.7 的挂起/崩溃取证；1.3.2 起有设备丢失恢复，1.3.10 又加看门狗 | [release/known-issues.md](../release/known-issues.md) 的 Shutdown/Hang 段、[fix-plan-1.3.10.md](../reports/fix-plan-1.3.10.md) §6 |
| [known-issues-1.3.1.md](reports/known-issues-1.3.1.md) | `docs/reports/known-issues-1.3.1.md` | v1.3.1 的 30 条决策快照，后续版本已逐条处理 | [release/known-issues.md](../release/known-issues.md) |
| [performance-vs-potplayer.md](reports/performance-vs-potplayer.md) | `docs/reports/performance-vs-potplayer.md` | 目标版本 v1.2.7，数据已被 1.3.10/1.3.11 改动（4 Hz 毛玻璃等）覆盖 | [docs/performance.md](../performance.md) |
| [ui-freeze-analysis.md](reports/ui-freeze-analysis.md) | `docs/reports/ui-freeze-analysis.md` | v1.3.0 工作树的冻结取证，R1/R2/D1-D7 多数已落地 | [release/known-issues.md](../release/known-issues.md)、[fix-plan-1.3.10.md](../reports/fix-plan-1.3.10.md) §6 |
| [thumbnails-optimization-1.3.7.md](reports/thumbnails-optimization-1.3.7.md) | `docs/reports/thumbnails-optimization-1.3.7.md` | v1.3.7 可行性报告，1.3.9 已实施 A/B/C | [reference/thumbnails.md](../reference/thumbnails.md) |
| [thumbnails-v1.3.8-plan.md](reports/thumbnails-v1.3.8-plan.md) | `docs/reports/thumbnails-v1.3.8-plan.md` | v1.3.8 方案，1.3.9 已实施；剩余项进 backlog | [reference/thumbnails.md](../reference/thumbnails.md) §实施状态 |

## 旧路径 stub（保留旧链接可用）

| 旧路径 | 内容 | 新位置 |
|---|---|---|
| [`docs/PLAN.md`](../PLAN.md) | 已归档说明页 | [planning/PLAN.md](planning/PLAN.md) |
| [`docs/reports/known-issues-1.3.1.md`](../reports/known-issues-1.3.1.md) | 已归档说明页 | [reports/known-issues-1.3.1.md](reports/known-issues-1.3.1.md) |
| [`docs/reports/hang-analysis-1.2.7.md`](../reports/hang-analysis-1.2.7.md) | 已归档说明页 | [reports/hang-analysis-1.2.7.md](reports/hang-analysis-1.2.7.md) |

其余 18 个文件不做 stub：它们主要由 `docs/acceptance/README.md` 与本仓库历史页引用，链接已直接改到本目录。

## 未归档（有意保留）

- **所有 `v1.x` 版本验收页**（`docs/acceptance/v1.*.md`）：发布记录与历史证据，不是“过时”。
- **architecture ADR**：记录设计取舍的原因，长期有效。
- **`docs/reports/fix-plan-1.3.10.md`**：1.3.11 的直接上游依据，本轮保留；1.3.11 收尾后可再归档。
- **1.3.11 三份报告**（`fix-plan-1.3.11.md`、`winui3-audit-1.3.11.md`、`docs-audit-1.3.11.md`）：当前轮交付物。

## 使用建议

- 引用归档文件时，请同时说明“这是历史结论”并链接替代文档。
- 新增归档文件时：更新本页表格、`git mv` 到对应子目录、在原路径决定是否留 stub、重写主树链接，然后跑一次链接检查（仓库内 `artifacts/_linkcheck.py` 仅覆盖根 README 与 site，docs 全树的检查需要额外脚本，勿提交临时脚本）。
