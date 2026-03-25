using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;

namespace Finn.Controls
{
    /// <summary>
    /// A custom control that displays an analog clock showing the current time.
    /// Adapts to the current theme via the inherited Foreground property.
    /// </summary>
    public class AnalogWatchControl : Control
    {
        private readonly DispatcherTimer _timer;
        private DateTime _currentTime;

        /// <summary>
        /// Foreground brush used for hands, ticks, and numerals.
        /// Defaults to inheriting from the parent control's foreground.
        /// </summary>
        public static readonly StyledProperty<IBrush> ForegroundProperty =
            TextBlock.ForegroundProperty.AddOwner<AnalogWatchControl>();

        public IBrush Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        /// <summary>
        /// Optional brush for the clock face fill. When set, the face is
        /// painted with this brush at low opacity to stand out from the background.
        /// </summary>
        public static readonly StyledProperty<IBrush?> FaceBrushProperty =
            AvaloniaProperty.Register<AnalogWatchControl, IBrush?>(nameof(FaceBrush));

        public IBrush? FaceBrush
        {
            get => GetValue(FaceBrushProperty);
            set => SetValue(FaceBrushProperty, value);
        }

        public AnalogWatchControl()
        {
            _currentTime = DateTime.Now;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) =>
            {
                _currentTime = DateTime.Now;
                InvalidateVisual();
            };
            _timer.Start();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var bounds = Bounds;
            double size = Math.Min(bounds.Width, bounds.Height);
            if (size <= 0) return;
            double cx = bounds.Width / 2;
            double cy = bounds.Height / 2;
            double radius = size / 2 - 4;

            // Use the inherited foreground for theme adaptation
            var fg = Foreground ?? Brushes.Black;
            var fgColor = fg is SolidColorBrush scb ? scb.Color : Colors.Black;

            // Face fill — uses FaceBrush when set, otherwise subtle foreground outline
            var faceRect = new Rect(cx - radius, cy - radius, radius * 2, radius * 2);
            if (FaceBrush is ISolidColorBrush fb)
            {
                var fc = fb.Color;
                var fillBrush = new SolidColorBrush(Color.FromArgb(25, fc.R, fc.G, fc.B));
                var rimPen = new Pen(new SolidColorBrush(Color.FromArgb(50, fc.R, fc.G, fc.B)), 1.5);
                context.DrawEllipse(fillBrush, rimPen, faceRect);
            }
            else
            {
                var facePen = new Pen(new SolidColorBrush(Color.FromArgb(30, fgColor.R, fgColor.G, fgColor.B)), 1.5);
                context.DrawEllipse(null, facePen, faceRect);
            }

            // Hour numerals
            var numeralBrush = new SolidColorBrush(Color.FromArgb(120, fgColor.R, fgColor.G, fgColor.B));
            double numeralRadius = radius - size * 0.09;
            double fontSize = Math.Max(9, size * 0.065);
            var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Normal);

            for (int h = 1; h <= 12; h++)
            {
                double angle = Math.PI * 2 * h / 12.0;
                double nx = cx + Math.Sin(angle) * numeralRadius;
                double ny = cy - Math.Cos(angle) * numeralRadius;
                var text = new FormattedText(h.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, fontSize, numeralBrush);
                context.DrawText(text, new Point(nx - text.Width / 2, ny - text.Height / 2));
            }

            // Minute ticks
            var tickBrush = new SolidColorBrush(Color.FromArgb(60, fgColor.R, fgColor.G, fgColor.B));
            var minorPen = new Pen(tickBrush, Math.Max(0.5, size * 0.003));
            var majorPen = new Pen(new SolidColorBrush(Color.FromArgb(100, fgColor.R, fgColor.G, fgColor.B)), Math.Max(1.0, size * 0.006));
            for (int i = 0; i < 60; i++)
            {
                double angle = Math.PI * 2 * i / 60.0;
                bool isHour = i % 5 == 0;
                double inner = isHour ? radius - size * 0.055 : radius - size * 0.03;
                double outer = radius - size * 0.005;
                context.DrawLine(isHour ? majorPen : minorPen,
                    new Point(cx + Math.Sin(angle) * inner, cy - Math.Cos(angle) * inner),
                    new Point(cx + Math.Sin(angle) * outer, cy - Math.Cos(angle) * outer));
            }

            // Hand angles (smooth)
            double hourAngle = (_currentTime.Hour % 12 + _currentTime.Minute / 60.0) * 30.0;
            double minuteAngle = (_currentTime.Minute + _currentTime.Second / 60.0) * 6.0;
            double secondAngle = _currentTime.Second * 6.0;

            // Draw hands
            var handBrush = new SolidColorBrush(Color.FromArgb(200, fgColor.R, fgColor.G, fgColor.B));
            DrawHand(context, cx, cy, radius * 0.50, hourAngle, handBrush, Math.Max(2.5, size * 0.025));
            DrawHand(context, cx, cy, radius * 0.72, minuteAngle, handBrush, Math.Max(1.5, size * 0.016));

            var secondBrush = new SolidColorBrush(Color.Parse("#E05050"));
            DrawHand(context, cx, cy, radius * 0.85, secondAngle, secondBrush, Math.Max(0.8, size * 0.006));

            // Center dot
            double capR = Math.Max(2.5, size * 0.018);
            context.DrawEllipse(handBrush, null, new Rect(cx - capR, cy - capR, capR * 2, capR * 2));
        }

        private static void DrawHand(DrawingContext context, double cx, double cy,
            double length, double angleDeg, IBrush brush, double thickness)
        {
            double rad = Math.PI * angleDeg / 180.0;
            var end = new Point(cx + Math.Sin(rad) * length, cy - Math.Cos(rad) * length);
            var pen = new Pen(brush, thickness, lineCap: PenLineCap.Round);
            context.DrawLine(pen, new Point(cx, cy), end);
        }
    }
}
