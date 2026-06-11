using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using DotNetCampus.Inking.Primitive;

namespace DotNetCampus.Inking.LogoExport;

/// <summary>
/// 矢量墨迹 → Logo 源码 转换器（标准 Logo 语言，遵循 PCLogo / MSWLogo / AOTLogoSharp 1.2.1+ 规范）。
///
/// <para>
/// 把 InkCanvas 上一笔笔迹（多笔 <see cref="SkiaStroke"/>）按时间顺序转成 Logo 源码。
/// 该转换器在 <c>DotNetCampus.AvaloniaInkCanvas</c> 库内部，不依赖任何业务项目，
/// 任何使用 <c>InkCanvas</c> / <c>SkiaStroke</c> 的应用都可直接调用。
/// </para>
///
/// <para><b>坐标系约定</b>：</para>
/// <list type="bullet">
///   <item>屏幕坐标：Y 向下，原点在左上角（与 InkCanvas 画布一致）</item>
///   <item>Logo 坐标：Y 向上，原点在画布中心（AOTLogoSharp Turtle 默认）</item>
/// </list>
///
/// <para>
/// 为保证回放不形变，转换时默认执行 <c>flipY = true</c>（Y 轴翻转），
/// 并把"笔迹区域中心"对齐到世界坐标 (0,0)：
/// 先计算整段笔迹的 bounding box 中心点 (cx, cy)，所有点写入 Logo 之前先减掉 (cx, cy)。
/// 回放控件把世界 (0,0) 映射回 InkCanvas 中心即可。
/// </para>
///
/// <para><b>支持三种导出模式</b>（分别对应 <see cref="LogoExportMode"/>）：</para>
/// <list type="bullet">
///   <item><b><see cref="LogoExportMode.Optimized"/></b>：指数平滑 + RDP 抽稀 +
///         LT/RT 相对转角 + FD 合并（kImi 黄金压缩规则，常规发布用）</item>
///   <item><b><see cref="LogoExportMode.AbsoluteCoordinates"/></b>：纯 SETXY 绝对坐标连线，
///         不优化、不算角度（用于排查"RT/LT 角度错"还是"RDP/平滑丢细节"导致变形）</item>
///   <item><b><see cref="LogoExportMode.RawRelativeAngles"/></b>：保留原始点列 + LT/RT 相对转角 +
///         FD 合并（**不**平滑、**不**RDP，用于隔离排查"平滑/RDP 丢细节"vs"RT/LT 角度算错"）</item>
/// </list>
///
/// <para><b>Logo 语言规范</b>（PCLogo / MSWLogo / AOTLogoSharp 1.2.1+）：</para>
/// <list type="bullet">
///   <item><c>SETH θ</c>：0° = 正北，顺时针增加；θ → 方向 (sin θ, cos θ)</item>
///   <item><c>LT angle</c>：逆时针旋转 angle 度</item>
///   <item><c>RT angle</c>：顺时针旋转 angle 度</item>
///   <item><c>FD dist</c>：向当前朝向前进 dist</item>
///   <item><c>PU</c> / <c>PD</c>：抬笔 / 落笔</item>
///   <item><c>SETXY x y</c>：跳到绝对坐标 (x, y)</item>
///   <item><c>HOME</c>：回到 (0, 0)，朝向 0°</item>
///   <item><c>CS</c>：清屏</item>
/// </list>
/// </summary>
public static class InkToLogoConverter
{
    /// <summary>
    /// 最小转角（度），低于此角度视为抖动，不输出 RT/LT
    /// </summary>
    public const double MinAngleDeg = 0.5;

    /// <summary>
    /// 最小步长（像素），低于此距离视为抖动，不输出 FD
    /// </summary>
    public const double MinStepPx = 0.5;

    /// <summary>
    /// 默认指数平滑因子 α：S_t = α * Y_t + (1-α) * S_{t-1}
    /// 0.5 比 0.3 更接近原始点 → 保留曲线细节（避免 RDP 后形状变形）
    /// </summary>
    public const double DefaultSmoothAlpha = 0.5;

    /// <summary>
    /// 默认道格拉斯-普克抽稀阈值 ε（屏幕像素）。
    /// 0.3 比 1.0 更小 → 保留更多曲线拐点，避免把"撇/捺/弯钩"等曲线压扁成折线。
    /// </summary>
    public const double DefaultRdpEpsilon = 0.3;

    /// <summary>
    /// 平滑后两点的最小距离平方（像素²），低于此值视为不动点直接丢弃
    /// 防止连续多帧都落在同一点上造成 FD 指令 0
    /// </summary>
    private const double MinSmoothedStepSq = 1e-6;

    /// <summary>
    /// 合并后 FD 的最小长度（像素），低于此长度视为抖动不输出
    /// </summary>
    private const double MinMergedFdPx = 0.5;

    /// <summary>
    /// 笔尖 X 坐标（屏幕坐标，未做 Y 翻转）
    /// </summary>
    public readonly struct RawPoint
    {
        public RawPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }
    }

    /// <summary>
    /// 把单个笔触（<see cref="SkiaStroke"/>）转成 Logo 源码。
    /// 笔触为空（无点）时返回空字符串。
    /// </summary>
    /// <param name="skiaStroke">单笔 SkiaStroke；其 <see cref="SkiaStroke.PointList"/> 按书写时间排序</param>
    /// <param name="flipY">是否翻转 Y 轴（屏幕 Y 向下 → Logo Y 向上），默认 true</param>
    /// <param name="scale">坐标缩放系数，1.0 表示不缩放</param>
    /// <param name="minAngleDeg">最小转角阈值（度），过滤抖动</param>
    /// <param name="minStepPx">最小步长阈值（像素），过滤抖动</param>
    /// <param name="smoothAlpha">指数平滑因子 α；&lt;= 0 或 &gt;= 1 表示不平滑</param>
    /// <param name="rdpEpsilon">道格拉斯-普克抽稀阈值 ε（像素），&lt;= 0 表示不抽稀</param>
    /// <param name="enableFdMerge">是否合并连续无转角的 FD（kImi 黄金压缩规则）</param>
    /// <param name="originShiftX">X 方向原点平移量（屏幕坐标），写入前从所有 X 减去</param>
    /// <param name="originShiftY">Y 方向原点平移量（屏幕坐标），写入前从所有 Y 减去</param>
    /// <param name="mode">导出模式（<see cref="LogoExportMode"/>）</param>
    /// <returns>Logo 源码字符串（已包含该笔的 PU / SETXY / PD / SETH / LT / RT / FD）</returns>
    public static string ConvertStroke(
        SkiaStroke skiaStroke,
        bool flipY = true,
        double scale = 1.0,
        double minAngleDeg = MinAngleDeg,
        double minStepPx = MinStepPx,
        double smoothAlpha = DefaultSmoothAlpha,
        double rdpEpsilon = DefaultRdpEpsilon,
        bool enableFdMerge = true,
        double originShiftX = 0.0,
        double originShiftY = 0.0,
        LogoExportMode mode = LogoExportMode.Optimized)
    {
        if (skiaStroke == null) return string.Empty;
        var raw = skiaStroke.PointList;
        if (raw == null || raw.Count == 0) return string.Empty;

        var src = ToRawPoints(raw);
        return ConvertCore(
            src,
            flipY,
            scale,
            minAngleDeg,
            minStepPx,
            smoothAlpha,
            rdpEpsilon,
            enableFdMerge,
            originShiftX,
            originShiftY,
            mode,
            strokeIndex: 0,
            totalStrokes: 1,
            originalCount: src.Count);
    }

    /// <summary>
    /// 把多笔笔迹（<see cref="IReadOnlyList{SkiaStroke}"/>）转成 Logo 源码。
    /// 笔迹区域的中心点会被自动减掉（与 <paramref name="originShiftX"/>/<paramref name="originShiftY"/>
    /// 共同作用），保证"笔迹中心"对齐到 Logo 世界坐标 (0,0)，回放时不形变。
    /// </summary>
    /// <param name="strokes">多笔 SkiaStroke 列表（按时间顺序）</param>
    /// <param name="flipY">是否翻转 Y 轴</param>
    /// <param name="scale">坐标缩放系数</param>
    /// <param name="minAngleDeg">最小转角阈值（度）</param>
    /// <param name="minStepPx">最小步长阈值（像素）</param>
    /// <param name="smoothAlpha">指数平滑因子 α</param>
    /// <param name="rdpEpsilon">道格拉斯-普克抽稀阈值 ε（像素）</param>
    /// <param name="enableFdMerge">是否合并连续无转角的 FD</param>
    /// <param name="originShiftX">X 方向原点平移量（屏幕坐标）</param>
    /// <param name="originShiftY">Y 方向原点平移量（屏幕坐标）</param>
    /// <param name="mode">导出模式（<see cref="LogoExportMode"/>）</param>
    /// <returns>Logo 源码字符串</returns>
    public static string Convert(
        IReadOnlyList<SkiaStroke> strokes,
        bool flipY = true,
        double scale = 1.0,
        double minAngleDeg = MinAngleDeg,
        double minStepPx = MinStepPx,
        double smoothAlpha = DefaultSmoothAlpha,
        double rdpEpsilon = DefaultRdpEpsilon,
        bool enableFdMerge = true,
        double originShiftX = 0.0,
        double originShiftY = 0.0,
        LogoExportMode mode = LogoExportMode.Optimized)
    {
        if (strokes == null || strokes.Count == 0)
        {
            return "; (空笔迹：无笔触)\nHOME\n";
        }

        var sb = new StringBuilder();
        sb.AppendLine("; ===== 由 InkToLogoConverter 生成的 Logo 源 =====");
        sb.AppendLine($"; 笔触数: {strokes.Count}");
        sb.AppendLine($"; 模式: {DescribeMode(mode, smoothAlpha, rdpEpsilon)}");
        if (mode != LogoExportMode.AbsoluteCoordinates)
        {
            sb.AppendLine($"; 角度算法: 最近三点法（叉积/点积求相对转角）");
            sb.AppendLine($"; 指数平滑 α: {(mode == LogoExportMode.RawRelativeAngles ? "关闭（保留原始点列）" : (IsValidAlpha(smoothAlpha) ? smoothAlpha.ToString("0.##", CultureInfo.InvariantCulture) : "关闭"))}");
            sb.AppendLine($"; RDP 抽稀 ε: {(mode == LogoExportMode.RawRelativeAngles ? "关闭（保留原始点列）" : (rdpEpsilon > 0 ? rdpEpsilon.ToString("0.##", CultureInfo.InvariantCulture) + " px" : "关闭"))}");
            sb.AppendLine($"; FD 合并: {(enableFdMerge ? "开启" : "关闭")}");
        }
        sb.AppendLine("CS");
        sb.AppendLine("PU");
        sb.AppendLine("HOME");

        for (int s = 0; s < strokes.Count; s++)
        {
            var stroke = strokes[s];
            if (stroke == null) continue;
            var raw = stroke.PointList;
            if (raw == null || raw.Count == 0) continue;

            var src = ToRawPoints(raw);
            var strokeLogo = ConvertCore(
                src,
                flipY,
                scale,
                minAngleDeg,
                minStepPx,
                smoothAlpha,
                rdpEpsilon,
                enableFdMerge,
                originShiftX,
                originShiftY,
                mode,
                strokeIndex: s,
                totalStrokes: strokes.Count,
                originalCount: src.Count);
            sb.Append(strokeLogo);
        }

        sb.AppendLine("; ===== 结束 =====");
        return sb.ToString();
    }

    /// <summary>
    /// 计算多笔笔迹的 bounding box（屏幕坐标）。
    /// </summary>
    /// <param name="strokes">笔迹列表</param>
    /// <returns>(minX, minY, maxX, maxY)，无笔迹时所有分量均为 0</returns>
    public static (double MinX, double MinY, double MaxX, double MaxY) GetBoundingBox(IReadOnlyList<SkiaStroke> strokes)
    {
        if (strokes == null || strokes.Count == 0)
        {
            return (0.0, 0.0, 0.0, 0.0);
        }

        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;

        foreach (var stroke in strokes)
        {
            if (stroke == null) continue;
            var pts = stroke.PointList;
            if (pts == null) continue;
            foreach (var p in pts)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
        }

        if (double.IsPositiveInfinity(minX))
        {
            return (0.0, 0.0, 0.0, 0.0);
        }
        return (minX, minY, maxX, maxY);
    }

    /// <summary>
    /// 把 <see cref="IReadOnlyList{InkStylusPoint}"/> 转换成 <see cref="RawPoint"/> 列表
    /// （仅取 X/Y，丢弃 Pressure 等冗余信息，便于后续算法处理）。
    /// </summary>
    private static List<RawPoint> ToRawPoints(IReadOnlyList<InkStylusPoint> source)
    {
        var list = new List<RawPoint>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            var p = source[i];
            list.Add(new RawPoint(p.X, p.Y));
        }
        return list;
    }

    private static string ConvertCore(
        List<RawPoint> source,
        bool flipY,
        double scale,
        double minAngleDeg,
        double minStepPx,
        double smoothAlpha,
        double rdpEpsilon,
        bool enableFdMerge,
        double originShiftX,
        double originShiftY,
        LogoExportMode mode,
        int strokeIndex,
        int totalStrokes,
        int originalCount)
    {
        if (source == null || source.Count == 0) return string.Empty;

        var sb = new StringBuilder();

        // 调试模式（useAbsoluteCoords=true 或 useRawPoints=true）跳过平滑，原样保留每个点
        var workPoints = (mode == LogoExportMode.AbsoluteCoordinates || mode == LogoExportMode.RawRelativeAngles)
            ? ClonePoints(source)
            : (IsValidAlpha(smoothAlpha)
                ? ExponentialSmooth(source, smoothAlpha)
                : ClonePoints(source));

        // 道格拉斯-普克抽稀：把"近似共线"的中间点直接扔掉
        if (mode != LogoExportMode.AbsoluteCoordinates && mode != LogoExportMode.RawRelativeAngles
            && rdpEpsilon > 0 && workPoints.Count > 2)
        {
            workPoints = RamerDouglasPeucker(workPoints, rdpEpsilon);
        }

        // 笔起点：写入 Logo 前先减掉 originShift
        var first = workPoints[0];
        var startX = (first.X - originShiftX) * scale;
        var startYRaw = first.Y - originShiftY;
        var startY = flipY ? -startYRaw * scale : startYRaw * scale;
        sb.AppendLine($"; --- 笔 {strokeIndex + 1}/{totalStrokes}（原始 {originalCount} 点 / 优化后 {workPoints.Count} 点）---");
        sb.AppendLine($"SETXY {Fmt(startX)} {Fmt(startY)}");
        sb.AppendLine("PD");

        // 单点 / 空点：直接 PU
        if (workPoints.Count < 2)
        {
            sb.AppendLine("PU");
            return sb.ToString();
        }

        // 调试模式：纯 SETXY 绝对坐标连线
        if (mode == LogoExportMode.AbsoluteCoordinates)
        {
            for (int i = 1; i < workPoints.Count; i++)
            {
                var p = workPoints[i];
                var sx = (p.X - originShiftX) * scale;
                var syRaw = p.Y - originShiftY;
                var sy = flipY ? -syRaw * scale : syRaw * scale;
                sb.AppendLine($"SETXY {Fmt(sx)} {Fmt(sy)}");
            }
            sb.AppendLine("PU");
            return sb.ToString();
        }

        // 生成原始 Logo 指令列表（暂存，最后做 FD 合并）
        var rawCommands = new List<(string Cmd, double Value)>();

        // 3.1) 首段：使用绝对朝向（SETH）
        var p0 = workPoints[0];
        var p1 = workPoints[1];
        var (vx0, vy0, dist0) = DiffWithScale(p0, p1, scale, flipY);
        if (dist0 >= minStepPx)
        {
            // 标准 Logo 规范：SETH 0 = 正北，顺时针增加
            // 方向向量 (vx, vy) 在世界坐标 (Y 向上) 下的换算公式：
            //   SETH θ → 方向 (sin θ, cos θ)
            //   反向换算：θ = atan2(vx, vy) * 180 / π
            double headingDeg = Math.Atan2(vx0, vy0) * 180.0 / Math.PI;
            rawCommands.Add(("SETH", headingDeg));
            rawCommands.Add(("FD", dist0));
        }

        // 3.2) 后续段：使用最近三点法（v1 = B - A，v2 = C - B）
        for (int i = 2; i < workPoints.Count; i++)
        {
            var A = workPoints[i - 2];
            var B = workPoints[i - 1];
            var C = workPoints[i];

            var (v1x, v1y, _) = DiffWithScale(A, B, scale, flipY);
            var (v2x, v2y, dist) = DiffWithScale(B, C, scale, flipY);

            if (dist < minStepPx) continue;

            double cross = v1x * v2y - v1y * v2x;
            double dot   = v1x * v2x + v1y * v2y;
            double angleRad = Math.Atan2(Math.Abs(cross), dot);
            double angleDeg = angleRad * 180.0 / Math.PI;

            // cross 是世界坐标（Y 向上）叉积：cross>0 为逆时针=LT，cross<0 为顺时针=RT。
            // DiffWithScale 已应用 flipY；不翻转时屏幕 Y 向下，符号相反。
            bool isLeft = flipY ? (cross > 0) : (cross < 0);

            if (angleDeg > minAngleDeg)
            {
                rawCommands.Add(isLeft ? ("LT", angleDeg) : ("RT", angleDeg));
            }

            rawCommands.Add(("FD", dist));
        }

        // 4) FD 合并：连续无转角的 FD 累加为单条 FD
        var finalCommands = enableFdMerge ? MergeFdOnly(rawCommands) : rawCommands;

        // 5) 输出
        foreach (var cmd in finalCommands)
        {
            sb.AppendLine($"{cmd.Cmd} {Fmt(cmd.Value)}");
        }

        sb.AppendLine("PU");
        return sb.ToString();
    }

    /// <summary>
    /// 单变量指数平滑：S_t = α * Y_t + (1-α) * S_{t-1}
    /// 用于滤除手写笔尖的高频抖动，使后续角度/距离计算更稳定。
    ///
    /// <para>
    /// <b>首两点保留策略</b>：每一笔的<b>前两点</b>（决定 SETH 初始朝向的向量）<b>始终原样保留</b>，
    /// 仅从第 3 个点开始应用指数平滑。否则平滑会把首段方向也"拉偏"，导致 SETH 与原始笔迹角度不一致，
    /// 回放时整笔偏转。
    /// </para>
    /// </summary>
    public static List<RawPoint> ExponentialSmooth(List<RawPoint> points, double alpha)
    {
        if (points == null || points.Count == 0) return new List<RawPoint>();
        if (!IsValidAlpha(alpha)) return ClonePoints(points);
        if (points.Count == 1) return ClonePoints(points);

        var smoothed = new List<RawPoint>(points.Count);
        // 第 1 个点：笔尖起点，原样保留（决定 SETH 起点）
        smoothed.Add(new RawPoint(points[0].X, points[0].Y));
        // 第 2 个点：首段向量终点，原样保留（决定 SETH 角度）
        smoothed.Add(new RawPoint(points[1].X, points[1].Y));

        // 从第 3 个点开始才做平滑；起点处 sx/sy 初始化为原始第 2 点的值，
        // 这样第 3 个点的平滑是基于原始第 2 点 → 不污染前两点的方向
        double sx = points[1].X;
        double sy = points[1].Y;

        for (int i = 2; i < points.Count; i++)
        {
            sx = alpha * points[i].X + (1.0 - alpha) * sx;
            sy = alpha * points[i].Y + (1.0 - alpha) * sy;

            // 距离过滤：平滑后若几乎没移动，直接丢弃
            var last = smoothed[smoothed.Count - 1];
            double dx = sx - last.X;
            double dy = sy - last.Y;
            if (dx * dx + dy * dy <= MinSmoothedStepSq) continue;

            smoothed.Add(new RawPoint(sx, sy));
        }

        return smoothed;
    }

    /// <summary>
    /// 道格拉斯-普克 (Ramer-Douglas-Peucker) 路径抽稀算法。
    /// 给定有序点列与像素阈值 ε，递归地把"偏离首尾连线不超过 ε"的中间点全部丢弃。
    ///
    /// <para>
    /// <b>首两点保留策略</b>：每一笔的<b>前两点</b>（决定 SETH 初始朝向的向量）<b>始终强制保留</b>。
    /// 抽稀只在 <c>[1, last]</c> 与 <c>[0, 1]</c> 之外的范围递归，
    /// 因此首段（point 0 → point 1）的方向永远不会被 RDP 改写。
    /// </para>
    /// </summary>
    public static List<RawPoint> RamerDouglasPeucker(List<RawPoint> points, double epsilon)
    {
        if (points == null || points.Count < 3 || epsilon <= 0)
            return points == null ? new List<RawPoint>() : ClonePoints(points);

        bool[] keep = new bool[points.Count];
        keep[0] = true;                                  // 起点（笔尖起点）
        keep[points.Count - 1] = true;                   // 终点
        if (points.Count >= 2) keep[1] = true;           // 第二点（首段向量终点，决定 SETH 角度）

        // 抽稀范围 [1, last]：把首段 [0, 1] 排除在 RDP 之外，确保首段方向永远不被改写
        RdpRecursive(points, 1, points.Count - 1, epsilon, keep);

        var result = new List<RawPoint>();
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i]) result.Add(points[i]);
        }
        return result;
    }

    private static void RdpRecursive(List<RawPoint> points, int first, int last, double epsilon, bool[] keep)
    {
        if (last <= first + 1) return;

        double maxDist = 0;
        int maxIdx = first;
        double ax = points[first].X;
        double ay = points[first].Y;
        double bx = points[last].X;
        double by = points[last].Y;
        double dx = bx - ax;
        double dy = by - ay;
        double segLenSq = dx * dx + dy * dy;

        for (int i = first + 1; i < last; i++)
        {
            double d = PointToSegmentDistance(points[i].X, points[i].Y, ax, ay, bx, by, segLenSq);
            if (d > maxDist)
            {
                maxDist = d;
                maxIdx = i;
            }
        }

        if (maxDist > epsilon)
        {
            keep[maxIdx] = true;
            RdpRecursive(points, first, maxIdx, epsilon, keep);
            RdpRecursive(points, maxIdx, last, epsilon, keep);
        }
    }

    /// <summary>
    /// 点到线段的距离（像素）；线段退化（首尾重合）时回退为点到点距离
    /// </summary>
    private static double PointToSegmentDistance(
        double px, double py,
        double ax, double ay,
        double bx, double by,
        double segLenSq)
    {
        if (segLenSq < 1e-12)
        {
            double dx = px - ax;
            double dy = py - ay;
            return Math.Sqrt(dx * dx + dy * dy);
        }
        // 投影参数 t = ((P-A)·(B-A)) / |B-A|²，clamp 到 [0,1]
        double t = ((px - ax) * (bx - ax) + (py - ay) * (by - ay)) / segLenSq;
        if (t < 0) t = 0;
        else if (t > 1) t = 1;
        double cx = ax + t * (bx - ax);
        double cy = ay + t * (by - ay);
        double ddx = px - cx;
        double ddy = py - cy;
        return Math.Sqrt(ddx * ddx + ddy * ddy);
    }

    /// <summary>
    /// FD 合并：把连续无转角的 FD 累加为单条 FD。
    /// 角度（LT/RT/SETH）原样保留，避免相对角度合并造成的方向漂移。
    /// </summary>
    public static List<(string Cmd, double Value)> MergeFdOnly(List<(string Cmd, double Value)> raw)
    {
        if (raw == null || raw.Count == 0) return new List<(string, double)>();

        var opt = new List<(string Cmd, double Value)>(raw.Count);
        double pendingFd = 0.0;
        bool hasPending = false;

        foreach (var (cmd, val) in raw)
        {
            if (cmd == "FD" || cmd == "BK")
            {
                pendingFd += val;
                hasPending = true;
            }
            else
            {
                if (hasPending)
                {
                    if (Math.Abs(pendingFd) >= MinMergedFdPx)
                    {
                        opt.Add(("FD", pendingFd));
                    }
                    pendingFd = 0.0;
                    hasPending = false;
                }
                opt.Add((cmd, val));
            }
        }

        if (hasPending && Math.Abs(pendingFd) >= MinMergedFdPx)
        {
            opt.Add(("FD", pendingFd));
        }

        return opt;
    }

    private static List<RawPoint> ClonePoints(List<RawPoint> source)
    {
        var list = new List<RawPoint>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            list.Add(source[i]);
        }
        return list;
    }

    private static bool IsValidAlpha(double alpha)
    {
        return alpha > 0.0 && alpha < 1.0;
    }

    private static string DescribeMode(LogoExportMode mode, double smoothAlpha, double rdpEpsilon)
    {
        return mode switch
        {
            LogoExportMode.AbsoluteCoordinates => "SETXY 绝对坐标调试（不优化、不算角度）",
            LogoExportMode.RawRelativeAngles => "RT/LT 相对转角 + FD 合并（**不**平滑、**不**RDP）",
            _ => "RT/LT 相对转角 + 平滑 + RDP + FD 合并"
        };
    }

    /// <summary>
    /// 计算两点差向量（已应用 scale + Y 翻转），同时返回欧氏距离
    /// </summary>
    private static (double vx, double vy, double dist) DiffWithScale(RawPoint from, RawPoint to, double scale, bool flipY)
    {
        double ax = from.X * scale;
        double ay = flipY ? -from.Y * scale : from.Y * scale;
        double bx = to.X * scale;
        double by = flipY ? -to.Y * scale : to.Y * scale;

        double vx = bx - ax;
        double vy = by - ay;
        double dist = Math.Sqrt(vx * vx + vy * vy);
        return (vx, vy, dist);
    }

    private static string Fmt(double v)
    {
        // 保留 2 位小数，去掉多余 0
        return v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
