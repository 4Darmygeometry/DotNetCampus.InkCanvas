using System.Collections.Generic;

using DotNetCampus.Inking.LogoExport;

namespace DotNetCampus.Inking;

/// <summary>
/// 为 <see cref="InkCanvas"/> 和 <see cref="SkiaStroke"/> 提供"笔迹 → Logo 源码"扩展方法。
/// 该扩展使任何使用 DotNetCampus.AvaloniaInkCanvas 库的应用都能直接调用，无需反射，
/// 完全兼容 AOT/裁剪发布。
/// </summary>
public static class InkCanvasLogoExtensions
{
    /// <summary>
    /// 把 InkCanvas 上所有笔迹（多笔 SkiaStroke）转成标准 Logo 源码。
    /// 默认模式：指数平滑 + RDP 抽稀 + LT/RT 相对转角 + FD 合并（kImi 黄金压缩规则）。
    /// </summary>
    /// <param name="inkCanvas">InkCanvas 实例（取其 <see cref="InkCanvas.Strokes"/>）</param>
    /// <param name="flipY">是否翻转 Y 轴（屏幕 Y 向下 → Logo Y 向上），默认 true</param>
    /// <param name="scale">坐标缩放系数，1.0 表示不缩放</param>
    /// <param name="minAngleDeg">最小转角阈值（度），过滤抖动；推荐 0.5</param>
    /// <param name="minStepPx">最小步长阈值（像素），过滤抖动；推荐 0.5</param>
    /// <param name="smoothAlpha">指数平滑因子 α；&lt;= 0 或 &gt;= 1 表示不平滑；推荐 0.5</param>
    /// <param name="rdpEpsilon">道格拉斯-普克抽稀阈值 ε（像素），&lt;= 0 表示不抽稀；推荐 0.3</param>
    /// <param name="enableFdMerge">是否合并连续无转角的 FD</param>
    /// <param name="originShiftX">X 方向原点平移量（屏幕坐标）</param>
    /// <param name="originShiftY">Y 方向原点平移量（屏幕坐标）</param>
    /// <param name="mode">导出模式（<see cref="LogoExportMode"/>）</param>
    /// <returns>Logo 源码字符串，可直接传给 AOTLogoSharp 解析</returns>
    public static string ToLogoSource(
        this InkCanvas inkCanvas,
        bool flipY = true,
        double scale = 1.0,
        double minAngleDeg = InkToLogoConverter.MinAngleDeg,
        double minStepPx = InkToLogoConverter.MinStepPx,
        double smoothAlpha = InkToLogoConverter.DefaultSmoothAlpha,
        double rdpEpsilon = InkToLogoConverter.DefaultRdpEpsilon,
        bool enableFdMerge = true,
        double originShiftX = 0.0,
        double originShiftY = 0.0,
        LogoExportMode mode = LogoExportMode.Optimized)
    {
        return InkToLogoConverter.Convert(
            inkCanvas.Strokes,
            flipY, scale, minAngleDeg, minStepPx,
            smoothAlpha, rdpEpsilon, enableFdMerge,
            originShiftX, originShiftY, mode);
    }

    /// <summary>
    /// 把单笔 SkiaStroke 转成标准 Logo 源码。
    /// </summary>
    /// <param name="skiaStroke">单笔 SkiaStroke；其 <see cref="SkiaStroke.PointList"/> 按书写时间排序</param>
    /// <param name="flipY">是否翻转 Y 轴</param>
    /// <param name="scale">坐标缩放系数</param>
    /// <param name="minAngleDeg">最小转角阈值（度）</param>
    /// <param name="minStepPx">最小步长阈值（像素）</param>
    /// <param name="smoothAlpha">指数平滑因子 α</param>
    /// <param name="rdpEpsilon">道格拉斯-普克抽稀阈值 ε（像素）</param>
    /// <param name="enableFdMerge">是否合并连续无转角的 FD</param>
    /// <param name="originShiftX">X 方向原点平移量</param>
    /// <param name="originShiftY">Y 方向原点平移量</param>
    /// <param name="mode">导出模式（<see cref="LogoExportMode"/>）</param>
    /// <returns>Logo 源码字符串</returns>
    public static string ToLogoSource(
        this SkiaStroke skiaStroke,
        bool flipY = true,
        double scale = 1.0,
        double minAngleDeg = InkToLogoConverter.MinAngleDeg,
        double minStepPx = InkToLogoConverter.MinStepPx,
        double smoothAlpha = InkToLogoConverter.DefaultSmoothAlpha,
        double rdpEpsilon = InkToLogoConverter.DefaultRdpEpsilon,
        bool enableFdMerge = true,
        double originShiftX = 0.0,
        double originShiftY = 0.0,
        LogoExportMode mode = LogoExportMode.Optimized)
    {
        return InkToLogoConverter.ConvertStroke(
            skiaStroke, flipY, scale, minAngleDeg, minStepPx,
            smoothAlpha, rdpEpsilon, enableFdMerge,
            originShiftX, originShiftY, mode);
    }

    /// <summary>
    /// 把笔迹列表转成 Logo 源码（直接传入 <see cref="IReadOnlyList{SkiaStroke}"/>）。
    /// </summary>
    public static string ToLogoSource(
        this IReadOnlyList<SkiaStroke> strokes,
        bool flipY = true,
        double scale = 1.0,
        double minAngleDeg = InkToLogoConverter.MinAngleDeg,
        double minStepPx = InkToLogoConverter.MinStepPx,
        double smoothAlpha = InkToLogoConverter.DefaultSmoothAlpha,
        double rdpEpsilon = InkToLogoConverter.DefaultRdpEpsilon,
        bool enableFdMerge = true,
        double originShiftX = 0.0,
        double originShiftY = 0.0,
        LogoExportMode mode = LogoExportMode.Optimized)
    {
        return InkToLogoConverter.Convert(
            strokes, flipY, scale, minAngleDeg, minStepPx,
            smoothAlpha, rdpEpsilon, enableFdMerge,
            originShiftX, originShiftY, mode);
    }
}
