# 阶段验收

每个阶段一个文件：`stage-N.md`；v1 修复轮次为 `v1-polish*.md`。当前记录：

- Stage 0–8：`stage-0.md` … `stage-8.md`
- Stage 11：`stage-11.md`
- 修复轮次：[v1-polish](v1-polish.md) · [v1-polish2](v1-polish2.md) ·
  [v1-polish3](v1-polish3.md) · [v1-polish4](v1-polish4.md)
- 1.1.0：[v1.1.0](v1.1.0.md) · 1.1.1：[v1.1.1](v1.1.1.md) ·
  1.1.2：[v1.1.2](v1.1.2.md) · 1.2.0：[v1.2.0](v1.2.0.md)

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
