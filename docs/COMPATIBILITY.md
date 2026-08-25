# 旧版兼容性审计与新旧版对比

## Windows API 审计

旧版没有使用 Windows 11 专属的 Mica、`DWMWA_SYSTEMBACKDROP_TYPE`、
`DWMWA_WINDOW_CORNER_PREFERENCE` 等接口。

| API / 能力 | 最低系统 | 处理方式 |
| --- | --- | --- |
| `SetWindowCompositionAttribute` | Windows 10 可用，非公开接口 | 运行时动态解析；不可用时不触发入口点异常 |
| Accent Acrylic 状态 4 | Windows 10 1803 | 版本检测；失败后退到 BlurBehind |
| Accent BlurBehind 状态 3 | Windows 10 | Acrylic 不可用时使用 |
| `DwmIsCompositionEnabled` | Windows Vista | DWM 关闭时使用 GDI 渐变 |
| `SetWindowRgn` / `CreateRoundRectRgn` | 早期 Windows 即支持 | Windows 10/11 共用的圆角裁剪兜底 |
| DPAPI `CryptProtectData` | Windows XP | 继续用于当前用户会话加密 |

最终背景链路：`Acrylic -> BlurBehind -> 半透明色板 -> GDI 渐变`。
高对比度模式直接使用 GDI 背景，避免透明材质降低可读性。

发布包为 `win-x64` 自包含单文件，不要求目标 Windows 10 预装 .NET 9。

## 新旧版本对比

| 维度 | WinForms 旧版 | Electron + React 新版 |
| --- | --- | --- |
| 进程模型 | 单进程 | 主进程、Renderer、GPU、Network，加便携启动器 |
| 私有内存量级 | 较低 | 明显较高 |
| 发布体积 | 48.1 MiB（自包含） | 83.0 MiB（便携版） |
| 动画 | 按需 Timer；文字和斜纹约 30 FPS，整窗 `Invalidate` | 合成器 `transform`，斜纹按步进移动 |
| 背景材质 | 原生 Accent + GDI 绘制 | 原生 Accent/DWM + CSS 多层玻璃材质 |
| 圆角 | Win32 Region 裁剪 | Windows 11 DWM 圆角；Windows 10 `setShape` 回退 |
| DPI / 排版 | 固定坐标，自绘调整成本高 | CSS 布局，文本与组件更易调整 |
| 可访问性 | 自绘命中区较多，语义有限 | HTML 控件、焦点态、减少动态效果支持更完整 |
| 启动 | 单进程更直接 | 便携包首次自解压更慢 |
| 维护 | 依赖少、代码直观，但 UI 迭代成本高 | 组件化和测试更完整，但依赖及升级成本更高 |
| 安全面 | 无 Web 运行时，攻击面较小 | 启用 sandbox/context isolation，但需持续升级 Electron |

结论：旧版适合作为低内存、低依赖的兼容版本；新版适合作为主版本，视觉、交互、
DPI 适配和后续 UI 迭代更有优势。
