# Wiina 文档过时审计与归档方案（1.3.11）

> 范围：`docs/` 全树，基线 `feat/v1.3.10` @ `11df58c`。
> 方法：按最后提交日期、头部版本号、内容是否仍被当前流程引用三项判断；只读审计，未移动文件。
> 结论：21 个历史/一次性文档建议归档；12 个当前文档保留但需更新版本号/链接。

---

## 0. 结论

- **历史计划/阶段验收/一次性分析**（19 个）已经完成使命，继续放在主树会让读者把旧结论当现状；建议 `git mv` 到 `docs/archive/`。
- **所有 `v1.x` 版本验收页保留**（发布记录，`docs/acceptance/README.md` 索引）；它们是历史证据，不是“过时”。
- **当前 reference/architecture/release 文档保留**，但 `installer.md`、`performance.md`、`licensing/AUDIT.md`、`plugins/README.md`、`reference/settings-layout.md` 的版本号/标题需要更新。
- 归档会改 30+ 处链接；建议移动后跑一次链接检查，并在旧路径保留一页“已归档”stub（避免外部/历史 PR 链接 404）。

---

## 1. 建议归档（19 个）

### 1.1 计划与阶段验收（16 个）

| 文件 | 最后提交 | 为什么过时 | 现在的替代 |
|---|---|---|---|
| `docs/PLAN.md` | 2026-10-25 | 头部自述“**历史文档，不再是当前计划**”；v1 开工前的范围/阶段草案 | `README.md`、`docs/release/notes.md`、各版本验收页 |
| `docs/acceptance/stage-0.md` | 2026-10-18 | Stage 0 基线验证，一次性 | `docs/acceptance/v1.0.x` 之后的版本页；当前状态看 `v1.3.11` |
| `docs/acceptance/stage-1.md` | 2026-10-18 | 主窗口静态骨架 | 同上 |
| `docs/acceptance/stage-2.md` | 2026-10-18 | OSC 实装 | 同上 |
| `docs/acceptance/stage-3.md` | 2026-10-18 | 播放内核接入 | 同上 |
| `docs/acceptance/stage-4.md` | 2026-10-18 | 侧栏与快速设置 | 同上 |
| `docs/acceptance/stage-5.md` | 2026-10-18 | OSD/缩略图预览 | 同上 |
| `docs/acceptance/stage-6.md` | 2026-10-18 | 模式与多窗口 | 同上 |
| `docs/acceptance/stage-7.md` | 2026-10-18 | 设置/菜单/欢迎 | 同上 |
| `docs/acceptance/stage-8.md` | 2026-10-18 | 滤镜/均衡器 | 同上 |
| `docs/acceptance/stage-11.md` | 2026-10-19 | 打磨与发布（v1 前） | 同上 |
| `docs/acceptance/v1-polish.md` | 2026-10-19 | v1 修复轮 1 | `v1.1.0`/`v1.2.x`/`v1.3.x` 版本页 |
| `docs/acceptance/v1-polish2.md` | 2026-10-19 | v1 修复轮 2 | 同上 |
| `docs/acceptance/v1-polish3.md` | 2026-10-19 | v1 修复轮 3 | 同上 |
| `docs/acceptance/v1-polish4.md` | 2026-10-24 | v1 修复轮 4 | 同上 |
| `docs/acceptance/README.md` 的 Stage/v1-polish 段落 | 2026-11-07 | 索引结构本身当前，但需把 Stage/v1-polish 标为“已归档” | 保留索引，改链接/说明 |

### 1.2 一次性分析报告（6 个）

| 文件 | 最后提交 | 为什么过时 | 现在的替代 |
|---|---|---|---|
| `docs/reports/hang-analysis-1.2.7.md` | 2026-10-24 | 针对 v1.2.7 的挂起/崩溃；1.3.2 已实现设备丢失恢复，1.3.10 又加看门狗/恢复链修正 | `docs/release/known-issues.md` 的 Shutdown/Hang 段、`fix-plan-1.3.10.md` §6 |
| `docs/reports/known-issues-1.3.1.md` | 2026-10-25 | v1.3.1 的 30 条决策快照，后续版本已逐条处理 | `docs/release/known-issues.md`（当前版本） |
| `docs/reports/performance-vs-potplayer.md` | 2026-10-24 | 目标 v1.2.7；8Hz 毛玻璃、预生成等数据已被 1.3.10/1.3.11 改动 | `docs/performance.md`（更新到 1.3.11） |
| `docs/reports/ui-freeze-analysis.md` | 2026-10-24 | v1.3.0 工作树；R1/R2/D1-D7 多数已落地 | `known-issues.md`、`fix-plan-1.3.10.md` §6 |
| `docs/reports/thumbnails-optimization-1.3.7.md` | 2026-10-25 | v1.3.7 可行性报告；1.3.9 已实施 A/B/C | `docs/reference/thumbnails.md` |
| `docs/reports/thumbnails-v1.3.8-plan.md` | 2026-10-25 | v1.3.8 方案；1.3.9 已实施，剩余 20ms 轮询/`vf=scale` 仍在 backlog | `docs/reference/thumbnails.md` §实施状态 |

> 备注：`fix-plan-1.3.10.md` 是本轮的直接上游，保留在 `docs/reports/`（1.3.11 完成后可再归档）。

---

## 2. 保留但需要更新的文档（12 个）

| 文件 | 问题 | 处理 |
|---|---|---|
| `README.md` | 当前版本/测试数/键位数已更新到 1.3.10；1.3.11 后需再更新 | 版本号、测试数、键位数、文档地图 |
| `docs/installer.md` | 示例路径写 `artifacts/wiina-1.3.9-win-x64/` | 改成 `<version>` 或 1.3.11 |
| `docs/performance.md` | 标题 v1.3.9、测量数据旧 | 更新到 1.3.11（含 4Hz 毛玻璃、看门狗）或标“历史测量” |
| `docs/licensing/AUDIT.md` | 标题 v1.3.4 | 更新到 1.3.11 或标“审计基线” |
| `docs/plugins/README.md` | 写 v1.3.4；插件仍未实现 | 更新版本，保持“未实现”结论 |
| `docs/reference/settings-layout.md` | H1 仍写“1.3.9”，正文已 1.3.10 | 改 H1 为 1.3.11，补新设置行 |
| `docs/reference/iina-shortcuts.md` | 已有 1.3.10 更新说明；1.3.11 若加 `Ctrl+Alt+M` 需同步 | 按键表/说明 |
| `docs/reference/thumbnails.md` | 1.3.9 实施记录；1.3.11 修复“生成期间黑卡”后需补 | 悬停状态机/空卡行为 |
| `docs/release/notes.md` | 当前 1.3.10；1.3.11 需新增段 | 版本段 |
| `docs/release/known-issues.md` | 当前 1.3.10；1.3.11 需更新标题/新限制 | 版本段 |
| `docs/release/releasing.md` | 示例用 1.3.9 | 改 `<version>` 或 1.3.11 |
| `CONTRIBUTING.md` | 链接 `docs/PLAN.md`（将归档） | 改到 `docs/archive/planning/PLAN.md` 或移除 |

---

## 3. 归档目录结构

```
docs/archive/
  README.md                      # 归档说明、索引、为什么保留
  planning/
    PLAN.md
  acceptance/
    stage-0.md ... stage-8.md
    stage-11.md
    v1-polish.md
    v1-polish2.md
    v1-polish3.md
    v1-polish4.md
  reports/
    hang-analysis-1.2.7.md
    known-issues-1.3.1.md
    performance-vs-potplayer.md
    ui-freeze-analysis.md
    thumbnails-optimization-1.3.7.md
    thumbnails-v1.3.8-plan.md
```

`docs/archive/README.md` 内容：

- 归档原则：不是删除，是“不再代表当前状态”的历史证据；
- 当前替代文档的链接；
- 归档日期与基线 commit；
- 旧路径 stub 列表。

---

## 4. 链接重写计划

### 4.1 需要改的引用（审计结果）

- `CONTRIBUTING.md:6` → `docs/PLAN.md`
- `docs/acceptance/README.md:3-8` → stage/v1-polish 链接与说明
- `docs/reference/README.md:14-17,31` → 旧报告/PLAN
- `docs/reference/thumbnails.md:289-291` → v1.3.8 方案/v1.3.7 报告
- `docs/release/notes.md:167,226,536,599,603,760,768` → 旧报告
- `docs/release/known-issues.md:3,83,644` → known-issues-1.3.1/hang-analysis
- 历史验收页 `v1.2.4/5/7`、`v1.3.0-1.3.9` 内的 `../PLAN.md`、`../reports/*` 链接
- `docs/PLAN.md` 自身的内部链接（移动后相对路径变化）

### 4.2 策略

1. `git mv` 到 `docs/archive/...`（保留历史）。
2. 全局替换路径（脚本 + 人工 review）；相对路径按所在目录重算。
3. 对**被外部/PR 引用的旧路径**保留 stub：
   ```md
   # 已归档
   本文已移至 [docs/archive/...](...)。保留此页仅用于旧链接。
   ```
   stub 只用于 `PLAN.md`、`known-issues-1.3.1.md`、`hang-analysis-1.2.7.md` 这类被大量历史页引用的文件；stage/v1-polish 可直接改链接，不做 stub。
4. 跑 `artifacts/_linkcheck.py`（或等价脚本）检查所有 md 相对链接；修复 404。
5. `docs/acceptance/README.md` 增加“历史阶段验收（已归档）”小节，保留可发现性。

---

## 5. 风险

| 风险 | 说明 | 缓解 |
|---|---|---|
| 历史 PR/外部链接 404 | 旧路径被 README/issue/PR 引用 | 关键文件保留 stub |
| 相对链接批量替换出错 | 不同目录的 `../` 层级不同 | 脚本按文件目录重算 + linkcheck |
| 误把版本验收页当“过时”归档 | 发布记录需要保留 | 仅归档 stage/v1-polish 与一次性报告 |
| 归档后读者找不到 | 旧结论仍有参考价值 | `docs/archive/README.md` + 当前文档链接 |
| 文档与代码再次漂移 | 1.3.11 改动多 | 验收页/notes/known-issues 同批更新 |

---

## 6. 建议执行顺序

1. 先创建 `docs/archive/` 与 `README.md`。
2. `git mv` 19 个文件；保留 3 个 stub。
3. 脚本重写引用，人工 review `CONTRIBUTING.md`、`docs/acceptance/README.md`、`docs/reference/README.md`。
4. 跑 linkcheck；修 404。
5. 更新 `README.md` 文档地图，把“历史计划/阶段验收”指到 archive。
6. 在 `docs/acceptance/v1.3.11.md` 记录归档清单与链接检查结果。
