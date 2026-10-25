# Wiina 官网

GitHub Pages 静态站，纯 HTML/CSS/JS，无构建步骤、无外部 CDN。

## 本地预览

```powershell
python -m http.server 8080 --directory site
# 打开 http://localhost:8080
```

## 部署

仓库已包含 `.github/workflows/pages.yml`：

1. GitHub → Settings → Pages → **Build and deployment** → Source 选 **GitHub Actions**。
2. 推送到 `main` 且 `site/` 有变更时自动构建发布；也可在 Actions 里手动 `workflow_dispatch`。
3. 站点地址：<https://rarespecies233.github.io/wiina/>

## 维护

- 截图位于 `assets/`，以 WebP 提供；更新界面后替换同名文件即可（`hero`、`osc`、`osc-thumb`、`settings`、`settings-search`、`keybindings`、`quality`、`welcome`、`stats`）。
- 页面支持深浅色切换：默认跟随系统，导航栏按钮可手动切换并记忆；`?theme=light` / `?theme=dark` 便于截图与分享。
- 下载按钮指向 `releases/latest` 与固定资产名；发新版本时同步修改 `index.html` 中的版本号与两个资产文件名，并更新 `assets/og.png` 的文案。
- 页面文案与 README 保持同一口径；事实性数字（测试数、设置行数）以当版验收页为准。

## 视觉规范

样式是 Fluent 2 / WinUI 3 的静态复刻，设计令牌直接对齐应用本体
（`src/Wiina.App/Themes/Tokens.xaml`），改动时请成对确认：

| 页面令牌 | 对应应用令牌 | 说明 |
| --- | --- | --- |
| `--bg` / `--bg-soft` | `AppBackgroundBrush` | 深色 `#090a0c`、浅色 `#f6f6f7` |
| `--bg-elev` / `--card` | `PanelBrush` / `CardBrush` | 面板与卡片表面 |
| `--stroke*` | `PanelBorderBrush` / `CardBorderBrush` | 1 px 发丝描边，三档强度 |
| `--accent*` | `AccentBrush` + 品牌图标金色 | 全站唯一重点色，只用于小面积 |
| `--radius` / `--radius-sm` | `PanelCornerRadius` / `CardCornerRadius` | 8 / 6 px，与应用同档 |

- **截图一律直角**：所有 `<img>` 都不加 `border-radius`（`styles.css` 里有一条兜底规则），
  卡片、按钮、面板才用 8 px 圆角。新增插画或截图时不要补圆角。
- 重点色集中在 `:root` / `html[data-theme="light"]` 的 `--accent*`、`--btn-*`、`--grad-*` 上，
  想改成系统强调色只需换这几行，其它样式不含硬编码颜色。
- 交互细节：`prefers-reduced-motion` 关闭全部动画，`prefers-contrast: more` 加深描边与正文色，
  键盘焦点统一 2 px 描边；新增组件请复用顶部令牌，不要另写颜色。
