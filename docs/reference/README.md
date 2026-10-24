# 输入规格：IINA macOS UI 复刻规格

本目录存放本项目的输入设计规格。

## 文件

- `IINA-macOS-UI-复刻规格.md` — 由维护者提供的规格（1230 行），基于
  [`iina/iina`](https://github.com/iina/iina) `develop` @ `5c15f81568b1f61f85219302843018e3c43c8d3b`（2026-10-16）。
- `settings-layout.md` — 当前设置界面的完整快照（11 页 / 126 行 / 39 个分节；1.2.7 为 12 页 / 125 行，1.3.0
  把工具页并入高级并调整缩略图行），含设置可打开的全部窗口与快速设置侧栏；修改设置结构后需同步更新。
- `iina-shortcuts.md` — Wiina / mpv / IINA 默认键位对照与兼容性说明（基于 IINA 默认配置推导，不复制代码）。
- `../reports/` — 两份维护者分析报告：`hang-analysis-1.2.7.md`（冻结/崩溃取证与修复建议）和
  `performance-vs-potplayer.md`（画质选项与 PotPlayer 的逐项开销分析）。
- `iina-thumbnails.md` — IINA 时间轴缩略图的实现调研与 Wiina 现状/差距分析；1.2.7 已按方案落地（预生成 + 磁盘缓存）。

## 来源与许可证

- IINA 以 **GPL-3.0** 发布。该规格是从 IINA 源码/资源推导出的**行为描述**，不是源码副本；
  为合规起见，本项目（GPL-3.0-only）将其作为 GPL-3.0 文档随仓库分发。
- 规格中的 `iina/Xxx.swift:123` 行号是**指向 IINA 源码的证据标注**，不是本项目文件。
- 本项目不复制 IINA 的 Swift/AppKit 源码、`.xcassets` 图片、SF Symbols 或应用图标。
- “IINA” 名称与图标归其作者所有；本项目与 IINA 无隶属关系。

## 使用方式

实现时以规格的**层级、顺序、状态迁移、触发条件、避让规则**为准；像素/时长按 Windows Fluent 规范换算（见
[实施计划 §4](../PLAN.md)）。规格未覆盖的细节，回到 IINA 源码确认，不臆造。

## 维护

- 规格是输入，不在本仓库内直接改写；如发现与上游不一致，在 [架构决策记录](../architecture/)
  中记录并以实现为准。
- 上游基线升级时，在本文件“基线”小节记录差异。
