# v1 polish 4 验收

- 分支：`fix/v1-polish-3`
- 提交：`a5edc5e`（功能修复）；版本提交见其后 release 提交
- 日期：2026-10-19
- Release：`artifacts/wiina-1.0.2-win-x64.zip`
  - SHA256 `77308C036D50554AC7530A4377E18C59B342B7F08239172CCE1548EE799A63F2`
  - 559 entries / 145,557,551 bytes
- 测试：590/590 Core tests；Release build 0 warnings / 0 errors

## 修复范围

1. **浮动 OSC 拖动恢复。** 真实帧模糊首次到达后，`OscFloatingView` 不再把
   `DragArea.Background` 设为 `null`（那会移除整个命中测试面），改为透明画刷。
   模糊层仍然可见，拖动手柄保持可命中。
2. **时间/章节预览不再漂移。** WinUI 的 `DesiredSize` 包含 `Margin`；旧逻辑在每次
   hover 时把上一次的左边距/上边距算进卡片宽高，导致预览逐次偏移。现在测量前把
   `TimePreview.Margin` 归零，使用纯内容尺寸，并把该尺寸传给缩略图定位。
3. **OSD 横向进度条改为 4 px。** `OsdText.xaml` 的 `OsdProgressHost` 由 8 px 改为
   4 px，轨道/填充圆角由 4 改为 2；100% 缩放下为 4 px 圆角横条。

## 验收清单

| # | 项目 | 结果 | 证据 |
|---|---|---|---|
| 1 | 浮动 OSC 可拖动 | ✅ | 透明命中面代码 + 用户 1.0.2 复验 |
| 2 | 视频/章节预览锚定指针且不漂移 | ✅ | `TimePreview` 内容尺寸测量修复 + 用户 1.0.2 复验 |
| 3 | OSD 进度条 100% 下 4 px 圆角 | ✅ | 1.0.2 打包版截图，音量 105% OSD |
| 4 | 真实视频模糊未回退 | ✅ | 1.0.2 包内 Win2D 组件与彩色测试片 |
| 5 | 590 Core tests | ✅ | `dotnet test ... -c Release` |

## 已知限制

- 预览缩略图仍由后台 ffmpeg 帧缓存生成，快速扫过时间轴时可能显示上一格；
  卡片定位本身已不再累积偏移。
- OSD 4 px 是 DIP；Windows 显示缩放 125%/150% 时物理像素会相应放大。
