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

- 截图位于 `assets/`，以 WebP 提供；更新界面后替换同名文件即可。
- 下载按钮指向 `releases/latest` 与固定资产名；发新版本时同步修改 `index.html` 中的版本号与两个资产文件名，并更新 `assets/og.png` 的文案。
- 页面文案与 README 保持同一口径；事实性数字（测试数、设置行数）以当版验收页为准。
