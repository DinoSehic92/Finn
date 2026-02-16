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
            double centerX = bounds.Width / 2;
            double centerY = bounds.Height / 2;
            double radius = size / 2 - 5;

            // Draw watch face
            context.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 2), new Rect(centerX - radius, centerY - radius, radius * 2, radius * 2));

            // Draw hour marks
            for (int i = 0; i < 12; i++)
            {
                double angle = Math.PI * 2 * i / 12;
                double x1 = centerX + Math.Sin(angle) * (radius - 10);
                double y1 = centerY - Math.Cos(angle) * (radius - 10);
                double x2 = centerX + Math.Sin(angle) * radius;
                double y2 = centerY - Math.Cos(angle) * radius;
                context.DrawLine(new Pen(Brushes.Black, 2), new Point(x1, y1), new Point(x2, y2));
            }

            // Draw hands
            DrawHand(context, centerX, centerY, radius * 0.5, _currentTime.Hour % 12 * 30 + _currentTime.Minute / 2, Brushes.Black, 4); // Hour
            DrawHand(context, centerX, centerY, radius * 0.7, _currentTime.Minute * 6, Brushes.Black, 2); // Minute
            DrawHand(context, centerX, centerY, radius * 0.8, _currentTime.Second * 6, Brushes.Red, 1); // Second

            // Draw center
            context.DrawEllipse(Brushes.Black, null, new Rect(centerX - 4, centerY - 4, 8, 8));
        }

        private void DrawHand(DrawingContext context, double cx, double cy, double length, double angleDeg, IBrush brush, double thickness)
        {
            double angleRad = Math.PI * angleDeg / 180.0;
            double x = cx + Math.Sin(angleRad) * length;
            double y = cy - Math.Cos(angleRad) * length;
            context.DrawLine(new Pen(brush, thickness), new Point(cx, cy), new Point(x, y));
        }
    }
}
