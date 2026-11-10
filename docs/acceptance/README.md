# 阶段验收

本目录保存 **版本验收页**（`v1.1.0`～`v1.3.11`，发布记录）与验收模板。

## 版本验收（当前）

- 1.1.0：[v1.1.0](v1.1.0.md) · 1.1.1：[v1.1.1](v1.1.1.md) ·
  1.1.2：[v1.1.2](v1.1.2.md) · 1.2.0：[v1.2.0](v1.2.0.md) ·
  1.2.1：[v1.2.1](v1.2.1.md) · 1.2.2：[v1.2.2](v1.2.2.md) ·
  1.2.3：[v1.2.3](v1.2.3.md) · 1.2.4：[v1.2.4](v1.2.4.md) · 1.2.5：[v1.2.5](v1.2.5.md) ·
  1.2.6：[v1.2.6](v1.2.6.md) · 1.2.7：[v1.2.7](v1.2.7.md) · 1.3.0：[v1.3.0](v1.3.0.md) · 1.3.1：[v1.3.1](v1.3.1.md) · 1.3.2：[v1.3.2](v1.3.2.md) · 1.3.3：[v1.3.3](v1.3.3.md) · 1.3.4：[v1.3.4](v1.3.4.md) · 1.3.5：[v1.3.5](v1.3.5.md) · 1.3.6：[v1.3.6](v1.3.6.md) · 1.3.7：[v1.3.7](v1.3.7.md) · 1.3.8：[v1.3.8](v1.3.8.md) · 1.3.9：[v1.3.9](v1.3.9.md) · 1.3.10：[v1.3.10](v1.3.10.md) · 1.3.11：[v1.3.11](v1.3.11.md)

## 历史阶段验收（已归档）

Stage 0–11 与 v1 修复轮（`v1-polish` 1–4）已于 **2026-11-08**（基线 commit `a440803`）归档到
[`docs/archive/acceptance/`](../archive/acceptance/)，原因是它们是 v1 发布前的过程记录，
结论已被后续版本验收页取代；继续放在主树会被误读为当前结论。为保持可发现性，链接如下：

- Stage 0：[stage-0](../archive/acceptance/stage-0.md) · [stage-1](../archive/acceptance/stage-1.md) ·
  [stage-2](../archive/acceptance/stage-2.md) · [stage-3](../archive/acceptance/stage-3.md) ·
  [stage-4](../archive/acceptance/stage-4.md) · [stage-5](../archive/acceptance/stage-5.md) ·
  [stage-6](../archive/acceptance/stage-6.md) · [stage-7](../archive/acceptance/stage-7.md) ·
  [stage-8](../archive/acceptance/stage-8.md)
- Stage 11：[stage-11](../archive/acceptance/stage-11.md)
- 修复轮次：[v1-polish](../archive/acceptance/v1-polish.md) · [v1-polish2](../archive/acceptance/v1-polish2.md) ·
  [v1-polish3](../archive/acceptance/v1-polish3.md) · [v1-polish4](../archive/acceptance/v1-polish4.md)

> 归档页引用的截图仍留在本目录 [`assets/`](assets)（未随页面移动），归档页内的相对链接已按新深度重算。
> 归档原则、替代关系与旧路径 stub 见 [`docs/archive/README.md`](../archive/README.md)。

> 历史验收页保留当时结论；与后续版本冲突处（如 1.2.0 的“24 px 吸附 / 三档玻璃模式”）
> 已就地标注校正，当前状态以最新版本页为准。

## 模板

```markdown
# Stage N — <名称> 验收

- 分支：`stage/N-<slug>`
- 提交：`<sha>`
- 日期：
- 环境：Windows 11 <build> / .NET 10.0.x / libmpv <version> / GPU <name+driver>

## 交付范围

- [ ] ...
- [ ] ...

## 验收清单

| # | 项目 | 结果 | 证据 |
|---|---|---|---|
| 1 | ... | ✅ / ❌ / n/a | 截图/录屏/命令 |

## 运行方式

```powershell
...
```

## 已知问题 / 偏离规格

- ...

## 许可证检查

- [ ] 无新增不兼容依赖
- [ ] 无 IINA/SF Symbols 资产
- [ ] NOTICE/许可证已更新（如需要）

## 下一步

- ...
```

> 验收通过后由维护者确认，再提交 PR；PR 描述引用本文件。

