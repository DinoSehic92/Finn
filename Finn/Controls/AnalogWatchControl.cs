using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;

namespace Finn.Controls
{
    /// <summary>
    /// A custom control that displays an analog watch showing the current time.
    /// </summary>
    public class AnalogWatchControl : Control
    {
        private DispatcherTimer _timer;
        private DateTime _currentTime;

        public AnalogWatchControl()
        {
            _currentTime = DateTime.Now;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) =>
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
            double centerX = bounds.Width / 2;
            double centerY = bounds.Height / 2;
            double radius = size / 2 - 6;

            // Modern face: subtle radial gradient
            var faceBrush = new RadialGradientBrush
            {
                GradientStops = new GradientStops
                {
                    new GradientStop(Color.Parse("#FAFBFC"), 0.0),
                    new GradientStop(Color.Parse("#F3F6FB"), 0.6),
                    new GradientStop(Color.Parse("#E6EAF2"), 1.0)
                },
                Center = new RelativePoint(0.35, 0.35, RelativeUnit.Relative)
            };

            // Outer ring stroke
            var ringPen = new Pen(Brushes.Gray, Math.Max(1.0, size * 0.02));

            // Draw face and rim
            context.DrawEllipse(faceBrush, ringPen, new Rect(centerX - radius, centerY - radius, radius * 2, radius * 2));

            // Draw minute ticks (subtle)
            var tickPen = new Pen(new SolidColorBrush(Color.Parse("#9AA6B2")), Math.Max(0.8, size * 0.003));
            for (int i = 0; i < 60; i++)
            {
                double angle = Math.PI * 2 * i / 60.0;
                double inner = (i % 5 == 0) ? radius - size * 0.07 : radius - size * 0.04;
                double outer = radius - size * 0.01;
                double x1 = centerX + Math.Sin(angle) * inner;
                double y1 = centerY - Math.Cos(angle) * inner;
                double x2 = centerX + Math.Sin(angle) * outer;
                double y2 = centerY - Math.Cos(angle) * outer;
                context.DrawLine(tickPen, new Point(x1, y1), new Point(x2, y2));
            }

            // Calculate hand angles
            double hourAngle = (_currentTime.Hour % 12 + _currentTime.Minute / 60.0) * 30.0;
            double minuteAngle = (_currentTime.Minute + _currentTime.Second / 60.0) * 6.0;
            double secondAngle = _currentTime.Second * 6.0;

            // Hand colors
            var handBrush = new SolidColorBrush(Color.Parse("#2D3748"));
            var secondBrush = new SolidColorBrush(Color.Parse("#E53E3E"));

            // Draw hand shadows (subtle offset)
            DrawHandWithShadow(context, centerX, centerY, radius * 0.55, hourAngle, handBrush, Math.Max(3.0, size * 0.04));
            DrawHandWithShadow(context, centerX, centerY, radius * 0.78, minuteAngle, handBrush, Math.Max(2.0, size * 0.028));
            DrawHandWithShadow(context, centerX, centerY, radius * 0.9, secondAngle, secondBrush, Math.Max(1.0, size * 0.01));

            // Modern center cap
            var capBrush = new RadialGradientBrush
            {
                GradientStops = new GradientStops
                {
                    new GradientStop(Colors.Black, 0.0),
                    new GradientStop(Color.Parse("#333333"), 0.8),
                    new GradientStop(Color.Parse("#666666"), 1.0)
                }
            };

            double capSize = Math.Max(4, size * 0.03);
            context.DrawEllipse(capBrush, null, new Rect(centerX - capSize / 2, centerY - capSize / 2, capSize, capSize));
        }


        private void DrawHandWithShadow(DrawingContext context, double cx, double cy, double length, double angleDeg, IBrush brush, double thickness)
        {
            double angleRad = Math.PI * angleDeg / 180.0;
            var end = new Point(cx + Math.Sin(angleRad) * length, cy - Math.Cos(angleRad) * length);

            // shadow line slightly offset for depth
            var shadowOffset = new Vector(0.8, 0.8);
            var shadowPen = new Pen(new SolidColorBrush(Color.Parse("#22000000")), thickness + 2);
            context.DrawLine(shadowPen, new Point(cx + shadowOffset.X, cy + shadowOffset.Y), new Point(end.X + shadowOffset.X, end.Y + shadowOffset.Y));

            var pen = new Pen(brush, thickness);
            context.DrawLine(pen, new Point(cx, cy), end);

            // small decorative counterweight for second hand
            if (thickness <= 2.0)
            {
                var tail = new Point(cx - Math.Sin(angleRad) * (length * 0.18), cy + Math.Cos(angleRad) * (length * 0.18));
                context.DrawEllipse(brush, null, new Rect(tail.X - thickness, tail.Y - thickness, thickness * 2, thickness * 2));
            }
        }
    }
}
