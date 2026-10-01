using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WorkforceManager.Business.DTOs;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// رسم تقدّم الخطة الشهرية في تبويب الإحصائيات — وضعين:
    ///   • تراكمي: مساحة المحقق التراكمي + خط "المفروض" المتقطع + خط التوقّع
    ///     المنقّط للأيام الجاية + خط الشهر اللي فات الباهت.
    ///   • يومي: عمود لكل يوم + خط المعدل اليومي المخطط.
    /// أيام الإجازة (الجمعة + العطل اليدوية) خلفية باهتة في الوضعين.
    ///
    /// **مرسوم بـ OnRender مش XAML** — 31 يوم × 4 سلاسل + تلميح عند المرور
    /// كان هيبقى مئات العناصر في الشجرة. كل الألوان DependencyProperty
    /// بـ AffectsRender ومتربطة بـ DynamicResource من الشاشة، فتبديل الثيم
    /// بيعيد الرسم لوحده.
    ///
    /// **FlowDirection = LeftToRight دايمًا** (بيتظبط في المُنشئ): الشاشة
    /// RTL، والعنصر المرسوم بيتعكس كله في RTL — النصوص كانت هتطلع معكوسة.
    /// محور الزمن شمال ← يمين، نفس ProductOutputChart القديم.
    /// </summary>
    public class PlanProgressChart : FrameworkElement
    {
        private const double PadLeft = 8, PadRight = 12, PadTop = 14, PadBottom = 28;

        private static FrameworkPropertyMetadata Render(object? def = null) =>
            new(def, FrameworkPropertyMetadataOptions.AffectsRender);

        public static readonly DependencyProperty DaysProperty = DependencyProperty.Register(
            nameof(Days), typeof(IEnumerable), typeof(PlanProgressChart), Render());
        public static readonly DependencyProperty IsCumulativeProperty = DependencyProperty.Register(
            nameof(IsCumulative), typeof(bool), typeof(PlanProgressChart), Render(true));
        public static readonly DependencyProperty DailyPlanRateProperty = DependencyProperty.Register(
            nameof(DailyPlanRate), typeof(double), typeof(PlanProgressChart), Render(0.0));
        public static readonly DependencyProperty AsOfDateProperty = DependencyProperty.Register(
            nameof(AsOfDate), typeof(DateTime), typeof(PlanProgressChart), Render(DateTime.Today));

        public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
            nameof(AccentBrush), typeof(Brush), typeof(PlanProgressChart), Render(Brushes.Goldenrod));
        public static readonly DependencyProperty AccentDeepBrushProperty = DependencyProperty.Register(
            nameof(AccentDeepBrush), typeof(Brush), typeof(PlanProgressChart), Render(Brushes.DarkGoldenrod));
        public static readonly DependencyProperty PlanBrushProperty = DependencyProperty.Register(
            nameof(PlanBrush), typeof(Brush), typeof(PlanProgressChart), Render(Brushes.DimGray));
        public static readonly DependencyProperty PreviousBrushProperty = DependencyProperty.Register(
            nameof(PreviousBrush), typeof(Brush), typeof(PlanProgressChart), Render(Brushes.Gray));
        public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
            nameof(GridBrush), typeof(Brush), typeof(PlanProgressChart), Render(Brushes.Gainsboro));
        public static readonly DependencyProperty HolidayBrushProperty = DependencyProperty.Register(
            nameof(HolidayBrush), typeof(Brush), typeof(PlanProgressChart), Render(Brushes.WhiteSmoke));
        public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
            nameof(TextBrush), typeof(Brush), typeof(PlanProgressChart), Render(Brushes.Gray));
        public static readonly DependencyProperty TextStrongBrushProperty = DependencyProperty.Register(
            nameof(TextStrongBrush), typeof(Brush), typeof(PlanProgressChart), Render(Brushes.Black));
        public static readonly DependencyProperty TooltipBackgroundProperty = DependencyProperty.Register(
            nameof(TooltipBackground), typeof(Brush), typeof(PlanProgressChart), Render(Brushes.White));

        public IEnumerable? Days { get => (IEnumerable?)GetValue(DaysProperty); set => SetValue(DaysProperty, value); }
        public bool IsCumulative { get => (bool)GetValue(IsCumulativeProperty); set => SetValue(IsCumulativeProperty, value); }
        public double DailyPlanRate { get => (double)GetValue(DailyPlanRateProperty); set => SetValue(DailyPlanRateProperty, value); }
        public DateTime AsOfDate { get => (DateTime)GetValue(AsOfDateProperty); set => SetValue(AsOfDateProperty, value); }
        public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
        public Brush AccentDeepBrush { get => (Brush)GetValue(AccentDeepBrushProperty); set => SetValue(AccentDeepBrushProperty, value); }
        public Brush PlanBrush { get => (Brush)GetValue(PlanBrushProperty); set => SetValue(PlanBrushProperty, value); }
        public Brush PreviousBrush { get => (Brush)GetValue(PreviousBrushProperty); set => SetValue(PreviousBrushProperty, value); }
        public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
        public Brush HolidayBrush { get => (Brush)GetValue(HolidayBrushProperty); set => SetValue(HolidayBrushProperty, value); }
        public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
        public Brush TextStrongBrush { get => (Brush)GetValue(TextStrongBrushProperty); set => SetValue(TextStrongBrushProperty, value); }
        public Brush TooltipBackground { get => (Brush)GetValue(TooltipBackgroundProperty); set => SetValue(TooltipBackgroundProperty, value); }

        private static readonly string[] WeekdayNames =
            { "الأحد", "الاتنين", "التلات", "الأربع", "الخميس", "الجمعة", "السبت" };

        private int _hoverIndex = -1;

        public PlanProgressChart()
        {
            FlowDirection = FlowDirection.LeftToRight;
            ClipToBounds = true;
            Cursor = Cursors.Cross;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var days = DayList();
            var index = -1;
            if (days.Count > 0)
            {
                var (plotLeft, plotWidth) = HorizontalLayout(days);
                var x = e.GetPosition(this).X - plotLeft;
                if (x >= 0 && x <= plotWidth)
                    index = Math.Clamp((int)(x / (plotWidth / days.Count)), 0, days.Count - 1);
            }
            if (index == _hoverIndex) return;
            _hoverIndex = index;
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverIndex == -1) return;
            _hoverIndex = -1;
            InvalidateVisual();
        }

        private List<MonthlyPlanStatisticsDayDto> DayList() =>
            Days?.OfType<MonthlyPlanStatisticsDayDto>().ToList() ?? new();

        private Typeface Font(FontWeight weight) =>
            new(TryFindResource("AppFont") as FontFamily ?? new FontFamily("Segoe UI"),
                FontStyles.Normal, weight, FontStretches.Normal);

        private FormattedText Text(string text, double size, Brush brush, FontWeight? weight = null) =>
            new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font(weight ?? FontWeights.Normal),
                size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        private double YMax(List<MonthlyPlanStatisticsDayDto> days)
        {
            double max = IsCumulative
                ? days.Max(d => Math.Max(Math.Max((double)d.PlanCumulative, d.Cumulative ?? 0),
                                         Math.Max((double)(d.ForecastCumulative ?? 0), d.PreviousCumulative ?? 0)))
                : Math.Max(days.Max(d => (double)d.Quantity), DailyPlanRate);
            return NiceCeiling(Math.Max(max * 1.08, 10));
        }

        /// <summary>عرض أرقام المحور الرأسي بيتحدد من أطول رقم، فالمساحة الشمال بتتظبط لوحدها</summary>
        private (double Left, double Width) HorizontalLayout(List<MonthlyPlanStatisticsDayDto> days)
        {
            var labelWidth = Text(Compact(YMax(days)), 11, TextBrush).Width;
            var left = PadLeft + labelWidth + 8;
            return (left, Math.Max(10, ActualWidth - left - PadRight));
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight)); // عشان الماوس يتلقط في المساحة الفاضية

            var days = DayList();
            if (days.Count == 0 || ActualWidth < 60 || ActualHeight < 60) return;

            var (plotLeft, plotWidth) = HorizontalLayout(days);
            var plotTop = PadTop;
            var plotHeight = Math.Max(10, ActualHeight - PadTop - PadBottom);
            var plotBottom = plotTop + plotHeight;
            var slot = plotWidth / days.Count;
            var yMax = YMax(days);

            double X(int i) => plotLeft + (i + 0.5) * slot;
            double Y(double value) => plotBottom - Math.Max(0, value) / yMax * plotHeight;

            // ── أيام الإجازة ──
            for (var i = 0; i < days.Count; i++)
                if (!days[i].IsWorkday)
                    dc.DrawRectangle(HolidayBrush, null, new Rect(plotLeft + i * slot, plotTop, slot, plotHeight));

            // ── خطوط الشبكة + أرقام المحور ──
            var gridPen = new Pen(GridBrush, 1);
            const int gridLines = 4;
            for (var g = 0; g <= gridLines; g++)
            {
                var value = yMax * g / gridLines;
                var y = Math.Round(Y(value)) + 0.5;
                dc.DrawLine(gridPen, new Point(plotLeft, y), new Point(plotLeft + plotWidth, y));
                var label = Text(Compact(value), 11, TextBrush);
                dc.DrawText(label, new Point(plotLeft - 8 - label.Width, y - label.Height / 2));
            }

            // ── النهارده ──
            var todayIndex = days.FindIndex(d => d.Date == AsOfDate.Date);
            if (todayIndex >= 0 && todayIndex < days.Count - 1)
            {
                var todayPen = new Pen(AccentDeepBrush, 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) };
                dc.DrawLine(todayPen, new Point(X(todayIndex), plotTop), new Point(X(todayIndex), plotBottom));
            }

            if (IsCumulative) RenderCumulative(dc, days, X, Y, plotBottom);
            else RenderDaily(dc, days, X, Y, slot, plotLeft, plotWidth, plotBottom);

            // ── أرقام الأيام ──
            var step = Math.Max(1, (int)Math.Ceiling(24 / slot));
            for (var i = 0; i < days.Count; i++)
            {
                var isToday = i == todayIndex;
                if (i % step != 0 && !isToday) continue;
                if (!isToday && todayIndex >= 0 && Math.Abs(i - todayIndex) < step && i % step != 0) continue;
                var label = Text(days[i].Date.Day.ToString(), isToday ? 11.5 : 11,
                    isToday ? AccentDeepBrush : TextBrush, isToday ? FontWeights.Bold : FontWeights.Normal);
                dc.DrawText(label, new Point(X(i) - label.Width / 2, plotBottom + 7));
            }

            if (_hoverIndex >= 0 && _hoverIndex < days.Count)
                RenderTooltip(dc, days, _hoverIndex, X, Y, plotTop, plotBottom, plotLeft, plotWidth);
        }

        private void RenderCumulative(DrawingContext dc, List<MonthlyPlanStatisticsDayDto> days,
            Func<int, double> X, Func<double, double> Y, double plotBottom)
        {
            // الشهر اللي فات — خط رفيع باهت ورا كل حاجة
            var previous = Points(days, d => d.PreviousCumulative, X, Y);
            if (previous.Count > 1)
                DrawPolyline(dc, previous, new Pen(PreviousBrush, 1.5) { DashStyle = new DashStyle(new double[] { 1, 2.5 }, 0) });

            // المفروض — خط متقطع
            var plan = Points(days, d => (double?)d.PlanCumulative, X, Y);
            if (days.Any(d => d.PlanCumulative > 0))
                DrawPolyline(dc, plan, new Pen(PlanBrush, 1.6) { DashStyle = new DashStyle(new double[] { 5, 4 }, 0) });

            // المحقق — مساحة + خط
            var actual = Points(days, d => d.Cumulative, X, Y);
            if (actual.Count > 0)
            {
                var area = new StreamGeometry();
                using (var ctx = area.Open())
                {
                    ctx.BeginFigure(new Point(actual[0].X, plotBottom), true, true);
                    ctx.PolyLineTo(actual, true, true);
                    ctx.LineTo(new Point(actual[^1].X, plotBottom), true, true);
                }
                area.Freeze();
                var fill = AccentBrush.Clone();
                fill.Opacity = 0.16;
                dc.DrawGeometry(fill, null, area);

                if (actual.Count > 1)
                    DrawPolyline(dc, actual, new Pen(AccentBrush, 2.6) { LineJoin = PenLineJoin.Round });
                dc.DrawEllipse(TooltipBackground, new Pen(AccentDeepBrush, 2), actual[^1], 4, 4);
            }

            // التوقّع — منقّط من آخر يوم فعلي
            var forecast = Points(days, d => (double?)d.ForecastCumulative, X, Y);
            if (forecast.Count > 1)
                DrawPolyline(dc, forecast, new Pen(AccentDeepBrush, 1.8)
                { DashStyle = new DashStyle(new double[] { 1.2, 2.2 }, 0), DashCap = PenLineCap.Round });
        }

        private void RenderDaily(DrawingContext dc, List<MonthlyPlanStatisticsDayDto> days,
            Func<int, double> X, Func<double, double> Y, double slot, double plotLeft, double plotWidth, double plotBottom)
        {
            var best = days.Where(d => !d.IsFuture && d.Quantity > 0).Select(d => d.Quantity).DefaultIfEmpty(0).Max();
            var barWidth = Math.Max(3, Math.Min(22, slot * 0.62));
            for (var i = 0; i < days.Count; i++)
            {
                var d = days[i];
                if (d.IsFuture || d.Quantity <= 0) continue;
                var top = Y(d.Quantity);
                var brush = d.Quantity == best ? AccentDeepBrush : AccentBrush;
                dc.DrawGeometry(brush, null, TopRoundedBar(X(i) - barWidth / 2, top, barWidth, plotBottom - top));
            }

            if (DailyPlanRate > 0)
            {
                var y = Y(DailyPlanRate);
                dc.DrawLine(new Pen(PlanBrush, 1.6) { DashStyle = new DashStyle(new double[] { 5, 4 }, 0) },
                    new Point(plotLeft, y), new Point(plotLeft + plotWidth, y));
            }
        }

        private void RenderTooltip(DrawingContext dc, List<MonthlyPlanStatisticsDayDto> days, int index,
            Func<int, double> X, Func<double, double> Y, double plotTop, double plotBottom, double plotLeft, double plotWidth)
        {
            var d = days[index];
            var x = X(index);
            dc.DrawLine(new Pen(TextBrush, 1) { DashStyle = DashStyles.Dot }, new Point(x, plotTop), new Point(x, plotBottom));

            var rows = new List<(string Label, string Value, Brush Swatch)>();
            if (!d.IsWorkday) rows.Add(("إجازة", "", HolidayBrush));
            if (!d.IsFuture)
            {
                rows.Add(("إنتاج اليوم", d.Quantity.ToString("N0"), AccentBrush));
                if (d.Cumulative is { } c) rows.Add(("المحقق التراكمي", c.ToString("N0"), AccentDeepBrush));
            }
            else if (d.ForecastCumulative is { } f)
                rows.Add(("التوقّع", f.ToString("N0"), AccentDeepBrush));
            if (d.PlanCumulative > 0) rows.Add(("المفروض", d.PlanCumulative.ToString("N0"), PlanBrush));
            if (IsCumulative && d.PreviousCumulative is { } p) rows.Add(("الشهر اللي فات", p.ToString("N0"), PreviousBrush));
            if (d.PresentWorkers is > 0 and var present) rows.Add(("الحاضرين", present.ToString("N0"), GridBrush));

            var header = Text(WeekdayNames[(int)d.Date.DayOfWeek], 12, TextStrongBrush, FontWeights.Bold);
            var headerDate = Text(d.Date.ToString("dd/MM"), 12, TextBrush);
            var formatted = rows.Select(r => (Label: Text(r.Label, 11.5, TextBrush),
                                              Value: Text(r.Value, 12, TextStrongBrush, FontWeights.Bold), r.Swatch)).ToList();

            const double pad = 10, rowGap = 5, swatch = 8, columnGap = 18;
            var labelWidth = Math.Max(header.Width, formatted.Select(r => r.Label.Width + swatch + 6).DefaultIfEmpty(0).Max());
            var valueWidth = Math.Max(headerDate.Width, formatted.Select(r => r.Value.Width).DefaultIfEmpty(0).Max());
            var width = pad * 2 + labelWidth + columnGap + valueWidth;
            var height = pad * 2 + header.Height + formatted.Sum(r => r.Label.Height + rowGap);

            var left = x + 14;
            if (left + width > plotLeft + plotWidth) left = x - 14 - width;
            left = Math.Max(0, left);
            var top = plotTop + 4;

            var box = new Rect(left, top, width, height);
            dc.PushOpacity(0.97);
            dc.DrawRoundedRectangle(TooltipBackground, new Pen(GridBrush, 1), box, 8, 8);
            dc.Pop();

            // التسمية على اليمين (عربي) والقيمة على الشمال — نفس اتجاه قراية الشاشة
            var right = box.Right - pad;
            var y = top + pad;
            dc.DrawText(header, new Point(right - header.Width, y));
            dc.DrawText(headerDate, new Point(box.Left + pad, y));
            y += header.Height + rowGap;
            foreach (var (label, value, brush) in formatted)
            {
                dc.DrawRoundedRectangle(brush, null, new Rect(right - swatch, y + (label.Height - swatch) / 2, swatch, swatch), 2, 2);
                dc.DrawText(label, new Point(right - swatch - 6 - label.Width, y));
                dc.DrawText(value, new Point(box.Left + pad, y));
                y += label.Height + rowGap;
            }

            // نقطة على المحقق في اليوم ده
            double? marker = IsCumulative
                ? d.Cumulative is { } cumulative ? cumulative : (double?)d.ForecastCumulative
                : d.IsFuture ? null : d.Quantity;
            if (marker is { } m)
                dc.DrawEllipse(AccentDeepBrush, new Pen(TooltipBackground, 2), new Point(x, Y(m)), 4.5, 4.5);
        }

        private static List<Point> Points(List<MonthlyPlanStatisticsDayDto> days,
            Func<MonthlyPlanStatisticsDayDto, double?> value, Func<int, double> X, Func<double, double> Y)
        {
            var points = new List<Point>();
            for (var i = 0; i < days.Count; i++)
                if (value(days[i]) is { } v) points.Add(new Point(X(i), Y(v)));
            return points;
        }

        private static void DrawPolyline(DrawingContext dc, List<Point> points, Pen pen)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(points[0], false, false);
                ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        /// <summary>عمود بحواف مدوّرة من فوق بس — القاعدة مستقيمة على المحور</summary>
        private static Geometry TopRoundedBar(double left, double top, double width, double height)
        {
            var radius = Math.Max(0, Math.Min(4, Math.Min(width / 2, height)));
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(left, top + height), true, true);
                ctx.LineTo(new Point(left, top + radius), false, false);
                ctx.ArcTo(new Point(left + radius, top), new Size(radius, radius), 0, false, SweepDirection.Clockwise, false, false);
                ctx.LineTo(new Point(left + width - radius, top), false, false);
                ctx.ArcTo(new Point(left + width, top + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, false, false);
                ctx.LineTo(new Point(left + width, top + height), false, false);
            }
            geometry.Freeze();
            return geometry;
        }

        private static double NiceCeiling(double value)
        {
            var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
            foreach (var m in new[] { 1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 })
                if (m * magnitude >= value) return m * magnitude;
            return 10 * magnitude;
        }

        /// <summary>12500 → 12.5k — المحور ضيق، والرقم الكامل في التلميح</summary>
        private static string Compact(double value) => value switch
        {
            >= 1_000_000 => (value / 1_000_000).ToString("0.#") + "M",
            >= 10_000 => (value / 1_000).ToString("0.#") + "k",
            _ => value.ToString("N0")
        };
    }
}
