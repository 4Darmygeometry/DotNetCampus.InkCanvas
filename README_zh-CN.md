# DotNetCampus.InkCanvas

书写笔迹画板

这个项目起源于： https://github.com/AvaloniaUI/Avalonia/issues/1477

![](./docs/images/Image1.png)

### Avalonia InkCanvas

### 快速开始

1. 安装 NuGet 包 [`DotNetCampus.AvaloniaInkCanvas`](https://www.nuget.org/packages/DotNetCampus.AvaloniaInkCanvas)

   ```xml
     <ItemGroup>
       <PackageReference Include="DotNetCampus.AvaloniaInkCanvas" Version="1.0.0" />
     </ItemGroup>
   ```

2. 在 XAML 中使用 InkCanvas 控件

   ```xml
   xmlns:inking="using:DotNetCampus.Inking"
   
   <inking:InkCanvas x:Name="InkCanvas"/>
   ```

3. 在代码中使用 InkCanvas 控件，切换不同的输入模式

   ```csharp
        // 切换书写模式
        InkCanvas.EditingMode = InkCanvasEditingMode.Ink;
        // 切换橡皮擦模式
        InkCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
   ```

### FAQ

**Q:** 这个库支持 AOT 编译吗？

**A:** 支持。这个库经过测试，确认可以在 AOT 环境下正常工作。

**Q:** 这个库可以在 Linux 环境下使用吗？

**A:** 可以。这个库基于 Avalonia 和 SkiaSharp 构建，这些都是支持 Linux 的跨平台框架。

**Q:** 我是否能直接用这个库制作出一个高性能笔迹白板应用？

**A:** 不可以。受限于 Avalonia 的渲染性能，目前这个库还不能用来制作高性能的笔迹白板应用。如果你需要一个高性能的笔迹白板应用，建议在 Windows 平台上添加 WPF 加速层，使用 WPF 绘制笔迹以提升性能；在 Linux 平台上使用 X11 原生绘制以提升性能。相关讨论请参考 <https://github.com/AvaloniaUI/Avalonia/discussions/18702> 和 <https://www.cnblogs.com/lindexi/p/18835048>

### 进阶用法

#### 切换笔迹渲染器

当前内置了以下笔迹渲染器：

- `SimpleInkRender`: 最简单的笔迹渲染器，适合大部分场景，速度快，逻辑简单。但在某些输入情况下，笔迹可能出现锯齿等问题
- `WpfForSkiaInkStrokeRenderer`: 使用 WPF 的笔迹渲染算法的渲染器，适合对笔迹质量要求较高的场景，但性能相对较低。其实现代码源于 WPF 开源仓库，逻辑复杂

切换笔迹渲染器示例：

```csharp
 AvaloniaSkiaInkCanvasSettings settings = InkCanvas.SkiaInkCanvas.Settings;
 // 使用 WPF 的笔迹渲染算法的渲染器
 settings.InkStrokeRenderer = new WpfForSkiaInkStrokeRenderer();
 // 使用默认简单的笔迹渲染器
 settings.InkStrokeRenderer = null;
```

注： 使用 `WpfForSkiaInkStrokeRenderer` 仅使用 WPF 开源仓库中的笔迹渲染算法代码，不依赖 WPF 框架本身

#### 处理笔迹收集事件

```csharp
        InkCanvas.StrokeCollected += (o, args) =>
        {
            var addedStroke = args.SkiaStroke;
        };
```

#### 处理笔迹擦除事件

```csharp
        InkCanvas.StrokeErased += (o, args) =>
        {
            foreach (ErasedSkiaStroke erasedSkiaStroke in args.ErasingSkiaStrokeList)
            {
                if (erasedSkiaStroke.IsErased)
                {
                    // 被擦掉的笔迹
                    IReadOnlyList<SkiaStroke> newStrokes = erasedSkiaStroke.NewStrokeList;

                    // 一段笔迹可以被擦掉成多段笔迹。但也可能被完全擦掉，变成 0 段笔迹
                    foreach (var skiaStroke in newStrokes)
                    {
                        
                    }
                }
                else
                {
                    // 没有被擦掉的笔迹，保持原样
                    SkiaStroke originalStroke = erasedSkiaStroke.OriginStroke;
                }
            }
        };
```

#### 控制橡皮擦属性

通过 AvaloniaSkiaInkCanvasSettings 进行控制，例如：

```csharp
        AvaloniaSkiaInkCanvasSettings settings = InkCanvas.SkiaInkCanvas.Settings;
        settings.EraserSize = new Size(100, 200);
```

#### 如何自定义橡皮擦界面

1. 编写一个继承自 Control 的橡皮擦控件，且让其实现 IEraserView 接口
2. 将该橡皮擦控件的创建委托赋值给 InkCanvas.AvaloniaSkiaInkCanvas.Settings 的 EraserViewCreator 属性

示例代码如下：

```csharp
internal class CustomEraserView : Control, IEraserView
{
    ...
}

        var settings = InkCanvas.AvaloniaSkiaInkCanvas.Settings;
        settings.EraserViewCreator = new DelegateEraserViewCreator(() => new CustomEraserView());
```

注： 不能在使用过程中动态更换 EraserViewCreator 属性，仅支持在初始化时设置该属性。应当在任何橡皮擦视图创建之前设置该属性

#### 将笔迹导出为 SVG 图片

演示将每一笔笔迹导出为单独的 SVG 图片：

```csharp
    private void SaveStrokeAsSvgButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var saveFolder = Path.Join(AppContext.BaseDirectory, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}");
        Directory.CreateDirectory(saveFolder);

        using var skPaint = new SKPaint();
        skPaint.IsAntialias = true;
        skPaint.Style = SKPaintStyle.Fill;

        for (var i = 0; i < InkCanvas.Strokes.Count; i++)
        {
            var saveSvgFile = Path.Join(saveFolder, $"{i}.svg");
            using var fileStream = File.Create(saveSvgFile);

            var stroke = InkCanvas.Strokes[i];

            var bounds = InkCanvas.Bounds.ToSKRect();
            using var skCanvas = SKSvgCanvas.Create(bounds, fileStream);

            skPaint.Color = stroke.Color;
            skCanvas.DrawPath(stroke.Path, skPaint);
        }
    }
```

### 4Darmygeometry 分支新增的公开 API

> 4Darmygeometry 分支保持与上游 `DotNetCampus.AvaloniaInkCanvas` **100% API 兼容**，
> 同时把若干私有 API 提升为 public，使得调用方在做笔迹分析 / Logo 语言回放时
> **无需使用反射**（反射会破坏 AOT / 裁剪发布）。

#### `SkiaStroke.PointList` — 直接读取笔尖原始点列（零反射）

`SkiaStroke.Path` 是 Skia 渲染用的**外轮廓多边形**，看起来像线段，实际上是围绕中心线的封闭多边形，
直接连 `Path.Points` 会画出空心、变形的笔迹。

真实的、按书写时间排序的笔尖点列保存在 `SkiaStroke.PointList`（`IReadOnlyList<InkStylusPoint>`）。
4Darmygeometry 分支已将此属性提升为 public：

```csharp
using DotNetCampus.Inking;
using DotNetCampus.Inking.Primitive;

InkCanvas.StrokeCollected += (o, args) =>
{
    var stroke = args.SkiaStroke; // SkiaStroke
    var pts    = stroke.PointList; // IReadOnlyList<InkStylusPoint> — public，AOT/裁剪友好

    foreach (var p in pts)
    {
        // p.X, p.Y, p.Pressure, p.Timestamp, ...
    }
};
```

#### 将笔迹转换为 Logo 语言源码（PCLogo / MSWLogo / AOTLogoSharp 1.2.1+）

本分支内置 `DotNetCampus.Inking.LogoExport.InkToLogoConverter` 静态类，以及 `InkCanvas.ToLogoSource(...)` 扩展方法。
输出的是**标准 Logo 源码**，可直接交给任何 Logo 解释器（PCLogo、MSWLogo、
[AOTLogoSharp](https://www.nuget.org/packages/AOTLogoSharp.Drawing) 1.2.1+）由海龟回放。

支持三种导出模式（通过 `LogoExportMode` 选择）：

| 模式 | 说明 |
|---|---|
| `Optimized` | 指数平滑 + RDP 抽稀 + LT/RT 相对转角 + FD 合并（默认，输出最小） |
| `AbsoluteCoordinates` | 纯 `SETXY` 绝对坐标，不平滑、不 RDP、不算角度（调试用） |
| `RawRelativeAngles` | 原始点列 + LT/RT + FD 合并，不平滑、不 RDP（调试用） |

**每一笔的前两点始终原样保留**（它们决定了 `SETH` 初始朝向），所以优化过程**不会改变笔画整体方向**。

```csharp
using DotNetCampus.Inking;
using DotNetCampus.Inking.LogoExport;

// 1) 计算所有笔迹的包围盒，用中心点作为 Logo 坐标原点 (0,0)
var (minX, minY, maxX, maxY) = InkToLogoConverter.GetBoundingBox(InkCanvas.Strokes);
double cx = (minX + maxX) / 2.0;
double cy = (minY + maxY) / 2.0;

// 2) 转 Logo 源码
string logo = InkCanvas.ToLogoSource(
    flipY:         true,            // 屏幕 Y 向下 → Logo Y 向上
    originShiftX:  cx,
    originShiftY:  cy,
    mode:          LogoExportMode.Optimized);

// 3) 用 AOTLogoSharp 1.2.1+（或 PCLogo / MSWLogo）回放
File.WriteAllText("handwriting.logo", logo, System.Text.Encoding.UTF8);
```

`SkiaStroke`、`IReadOnlyList<SkiaStroke>`、`InkCanvas` 三者都暴露了同名的 `ToLogoSource(...)` 扩展方法，
签名一致，可以按需使用。

转换器遵循的 Logo 方言规范：

| 命令 | 含义 |
|---|---|
| `SETH θ` | 设定绝对朝向；`0°` = 正北，顺时针增加；方向 = `(sin θ, cos θ)` |
| `LT a` | 逆时针旋转 `a` 度 |
| `RT a` | 顺时针旋转 `a` 度 |
| `FD d` | 沿当前朝向前进 `d` 单位 |
| `SETXY x y` | 跳到绝对坐标 `(x, y)` |
| `PU` / `PD` | 抬笔 / 落笔 |
| `HOME` | 回到 `(0, 0)`，朝向 0° |
| `CS` | 清屏 |

# 开源社区

如果你希望参与贡献，欢迎 [Pull Request](https://github.com/dotnet-campus/DotNetCampus.InkCanvas/pulls)，或给我们 [报告 Bug](https://github.com/dotnet-campus/DotNetCampus.InkCanvas/issues/new)

# 授权协议

[![](https://img.shields.io/badge/License-MIT-blue?style=flat-square)](./LICENSE)