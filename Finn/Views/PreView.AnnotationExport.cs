using Finn.ViewModels;
using Finn.Model;
using Finn.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using iText.IO.Image;
using iText.Kernel.Pdf;
using MuPDFCore;
using MuPDFCore.MuPDFRenderer;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using PdfSharp.Drawing;

namespace Finn.Views;

public partial class PreView
{
    #region Annotation Save / Copy

    /// <summary>
    /// Renders the current PDF page with ink strokes composited on top.
    /// Returns the PNG-encoded data, or null if nothing can be rendered.
    /// </summary>
    private SKData? RenderAnnotatedPage(double renderZoom = 2.0, int page = -1)
    {
        if (pwr?.MainPreviewFile == null || pwr.Pagecount <= 0) return null;
        if (page < 0) page = pwr.CurrentPage1;
        if (page < 0 || page >= pwr.Pagecount) return null;

        using var ms = new MemoryStream();
        pwr.MainPreviewFile.WriteImage(page, renderZoom, PixelFormats.RGBA,
            ms, RasterOutputFileTypes.PNG, true);
        ms.Position = 0;
        using var pageBitmap = SKBitmap.Decode(ms);
        if (pageBitmap == null) return null;

        using var surface = SKSurface.Create(new SKImageInfo(pageBitmap.Width, pageBitmap.Height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(pageBitmap, 0, 0);

        // Composite diff overlay between PDF page and annotations
        if (pwr.DiffOverlayActive)
        {
            var diffPath = pwr.GetDiffImagePath(page);
            if (diffPath != null && File.Exists(diffPath))
            {
                using var diffBitmap = SKBitmap.Decode(diffPath);
                if (diffBitmap != null)
                {
                    using var diffPaint = new SKPaint
                    {
                        Color = SKColors.White.WithAlpha((byte)(MuPDFRenderer.DiffOverlayOpacity * 255))
                    };
                    var destRect = new SKRect(0, 0, pageBitmap.Width, pageBitmap.Height);
                    canvas.DrawBitmap(diffBitmap, destRect, diffPaint);
                }
            }
        }

        DrawAnnotationsToCanvas(canvas, page, renderZoom);

        using var image = surface.Snapshot();
        return image.Encode(SKEncodedImageFormat.Png, 100);
    }

    /// <summary>
    /// Draws all annotation elements (strokes, shapes, text, measurements)
    /// onto the given SkiaSharp canvas at the specified zoom level.
    /// </summary>
    private void DrawAnnotationsToCanvas(SKCanvas canvas, int page, double renderZoom)
    {
        var strokes = MuPDFRenderer.GetStrokes(page);
        foreach (var stroke in strokes)
        {
            if (stroke.Points.Count < 2) continue;

            using var paint = new SKPaint
            {
                Color = stroke.Opacity < 1.0
                    ? new SKColor(stroke.Color.R, stroke.Color.G, stroke.Color.B, (byte)(stroke.Opacity * 255))
                    : new SKColor(stroke.Color.R, stroke.Color.G, stroke.Color.B, stroke.Color.A),
                StrokeWidth = (float)(stroke.Width * renderZoom),
                Style = SKPaintStyle.Stroke,
                StrokeCap = stroke.IsHighlighter ? SKStrokeCap.Square : SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
                PathEffect = CreateDashEffect(stroke.DashPattern, (float)(stroke.Width * renderZoom))
            };

            var pts = stroke.Points;
            using var path = new SKPath();
            float z = (float)renderZoom;

            if (pts.Count == 2)
            {
                path.MoveTo((float)(pts[0].X * z), (float)(pts[0].Y * z));
                path.LineTo((float)(pts[1].X * z), (float)(pts[1].Y * z));
            }
            else if (stroke.IsPolyline)
            {
                float cr = (float)(stroke.CornerRadius * z);
                bool closed = stroke.IsClosed && pts.Count >= 3;
                // Remove duplicated closing point if present
                int n = pts.Count;
                if (closed && n >= 3)
                {
                    var f = pts[0]; var l = pts[n - 1];
                    if (Math.Abs(f.X - l.X) < 0.5 && Math.Abs(f.Y - l.Y) < 0.5)
                        n--;
                }

                // Pre-transform to screen space
                var sp = new SKPoint[n];
                for (int i = 0; i < n; i++)
                    sp[i] = new SKPoint((float)(pts[i].X * z), (float)(pts[i].Y * z));

                if (cr > 0.5f && n >= 3)
                    BuildRoundedPolylinePath(path, sp, n, cr, closed);
                else
                {
                    path.MoveTo(sp[0]);
                    for (int i = 1; i < n; i++)
                        path.LineTo(sp[i]);
                    if (closed) path.Close();
                }

                // Fill closed polylines with a translucent tint (matches in-app alpha 40)
                if (closed)
                {
                    using var closedFill = new SKPaint
                    {
                        Color = new SKColor(stroke.Color.R, stroke.Color.G, stroke.Color.B, 40),
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true
                    };
                    canvas.DrawPath(path, closedFill);
                }
            }
            else
            {
                path.MoveTo((float)(pts[0].X * z), (float)(pts[0].Y * z));
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var pm1 = pts[Math.Max(i - 1, 0)];
                    var pi  = pts[i];
                    var pi1 = pts[i + 1];
                    var pi2 = pts[Math.Min(i + 2, pts.Count - 1)];

                    float cp1x = (float)((pi.X + (pi1.X - pm1.X) / 6.0) * z);
                    float cp1y = (float)((pi.Y + (pi1.Y - pm1.Y) / 6.0) * z);
                    float cp2x = (float)((pi1.X - (pi2.X - pi.X) / 6.0) * z);
                    float cp2y = (float)((pi1.Y - (pi2.Y - pi.Y) / 6.0) * z);
                    float ex   = (float)(pi1.X * z);
                    float ey   = (float)(pi1.Y * z);

                    path.CubicTo(cp1x, cp1y, cp2x, cp2y, ex, ey);
                }
            }
            canvas.DrawPath(path, paint);
            paint.PathEffect?.Dispose();
        }

        // Render shapes
        var shapes = MuPDFRenderer.GetShapes(page);
        foreach (var shape in shapes)
        {
            using var paint = new SKPaint
            {
                Color = shape.Opacity < 1.0
                    ? new SKColor(shape.Color.R, shape.Color.G, shape.Color.B, (byte)(shape.Opacity * 255))
                    : new SKColor(shape.Color.R, shape.Color.G, shape.Color.B, shape.Color.A),
                StrokeWidth = (float)(shape.StrokeWidth * renderZoom),
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
                PathEffect = CreateDashEffect(shape.DashPattern, (float)(shape.StrokeWidth * renderZoom))
            };

            SKPaint? fillPaint = null;
            if (shape.IsFilled)
            {
                fillPaint = new SKPaint
                {
                    Color = new SKColor(shape.Color.R, shape.Color.G, shape.Color.B, 80),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
            }

            float sx = (float)(shape.Start.X * renderZoom);
            float sy = (float)(shape.Start.Y * renderZoom);
            float ex = (float)(shape.End.X * renderZoom);
            float ey = (float)(shape.End.Y * renderZoom);

            switch (shape.ShapeType)
            {
                case InlineAnnotationTool.Line:
                    canvas.DrawLine(sx, sy, ex, ey, paint);
                    break;

                case InlineAnnotationTool.Arrow:
                {
                    // Shorten line to arrowhead base to prevent round-cap protrusion
                    float adx = ex - sx, ady = ey - sy;
                    float alen = MathF.Sqrt(adx * adx + ady * ady);
                    if (alen > 1)
                    {
                        float headLen = MathF.Min(8f * (float)renderZoom, alen * 0.4f);
                        float shortenX = adx / alen * headLen;
                        float shortenY = ady / alen * headLen;
                        canvas.DrawLine(sx, sy, ex - shortenX, ey - shortenY, paint);
                    }
                    RenderSkiaArrowhead(canvas, paint, sx, sy, ex, ey, renderZoom);
                    break;
                }

                case InlineAnnotationTool.Rectangle:
                {
                    float cr = (float)(shape.CornerRadius * renderZoom);
                    var rect = new SKRect(Math.Min(sx, ex), Math.Min(sy, ey),
                                          Math.Max(sx, ex), Math.Max(sy, ey));
                    if (cr > 0.5f)
                    {
                        if (fillPaint != null)
                            canvas.DrawRoundRect(rect, cr, cr, fillPaint);
                        canvas.DrawRoundRect(rect, cr, cr, paint);
                    }
                    else
                    {
                        if (fillPaint != null)
                            canvas.DrawRect(rect, fillPaint);
                        canvas.DrawRect(rect, paint);
                    }
                    break;
                }

                case InlineAnnotationTool.Ellipse:
                {
                    var ovalRect = new SKRect(Math.Min(sx, ex), Math.Min(sy, ey),
                                              Math.Max(sx, ex), Math.Max(sy, ey));
                    if (fillPaint != null)
                        canvas.DrawOval(ovalRect, fillPaint);
                    canvas.DrawOval(ovalRect, paint);
                    break;
                }

                case InlineAnnotationTool.RevisionCloud:
                {
                    using var cloudPath = RenderSkiaCloudPath(sx, sy, ex, ey, renderZoom);
                    if (fillPaint != null)
                        canvas.DrawPath(cloudPath, fillPaint);
                    canvas.DrawPath(cloudPath, paint);
                    break;
                }

                case InlineAnnotationTool.Dot:
                {
                    float r = (float)(shape.StrokeWidth * renderZoom);
                    byte dotAlpha = shape.Opacity < 1.0 ? (byte)(shape.Opacity * 255) : (byte)255;
                    using var dotPaint = new SKPaint
                    {
                        Color = new SKColor(shape.Color.R, shape.Color.G, shape.Color.B, dotAlpha),
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true
                    };
                    canvas.DrawCircle(sx, sy, r, dotPaint);
                    canvas.DrawCircle(sx, sy, r, paint);
                    break;
                }
            }
            paint.PathEffect?.Dispose();
            fillPaint?.Dispose();
        }

        // Render text annotations with frame
        var texts = MuPDFRenderer.GetTexts(page);
        foreach (var t in texts)
        {
            byte alpha = t.Opacity < 1.0 ? (byte)(t.Opacity * 255) : (byte)255;

            // Sticky notes: render as a folded-corner icon (matches in-app appearance)
            if (t.IsStickyNote)
            {
                RenderSkiaStickyNoteIcon(canvas, t, renderZoom);
                continue;
            }

            // Labels: simple centred text with pill background (fixed font size, no textbox frame)
            if (t.IsLabel)
            {
                RenderSkiaLabel(canvas, t, renderZoom);
                continue;
            }

            // Render arrow line for ArrowText annotations
            if (t.ArrowOrigin.HasValue)
            {
                float ax = (float)(t.ArrowOrigin.Value.X * renderZoom);
                float ay = (float)(t.ArrowOrigin.Value.Y * renderZoom);
                // Defer arrow drawing until after frame is measured (need box rect)
            }

            var typeface = !string.IsNullOrEmpty(t.FontFamily)
                ? (SKTypeface.FromFamilyName(t.FontFamily) ?? SKTypeface.Default)
                : SKTypeface.Default;
            using var font = new SKFont(typeface, (float)(t.FontSize * renderZoom));
            using var textPaint = new SKPaint { Color = new SKColor(t.Color.R, t.Color.G, t.Color.B, alpha), IsAntialias = true };
            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
            using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)(1.2 * renderZoom), IsAntialias = true };

            float tx = (float)(t.Position.X * renderZoom);
            float lineHeight = (float)(t.FontSize * renderZoom * 1.3);
            float ty = (float)(t.Position.Y * renderZoom) + (float)(t.FontSize * renderZoom);

            // Word-wrap when MaxWidth is set, otherwise split on explicit newlines
            List<string> textLines;
            if (t.MaxWidth > 0)
            {
                float maxWidthPx = (float)(t.MaxWidth * renderZoom);
                textLines = WrapTextLines(t.Text, maxWidthPx, font);
            }
            else
            {
                textLines = new List<string>(t.Text.Split('\n'));
            }

            // Measure total extent for frame
            float frameMinX = float.MaxValue, frameMinY = float.MaxValue;
            float frameMaxX = float.MinValue, frameMaxY = float.MinValue;
            var lineInfos = new List<(string text, float y, SKRect bounds)>();
            float curY = ty;
            foreach (var line in textLines)
            {
                if (line.Length > 0)
                {
                    font.MeasureText(line, out var tb);
                    lineInfos.Add((line, curY, tb));
                    frameMinX = Math.Min(frameMinX, tx + tb.Left);
                    frameMinY = Math.Min(frameMinY, curY + tb.Top);
                    frameMaxX = Math.Max(frameMaxX, tx + tb.Left + tb.Width);
                    frameMaxY = Math.Max(frameMaxY, curY + tb.Top + tb.Height);
                }
                curY += lineHeight;
            }

            if (lineInfos.Count > 0)
            {
                // Stamp-style frame: padding matches the in-app renderer.
                float s   = (float)renderZoom;
                float pad = 5 * s;
                float accentW = 4 * s;
                float radius  = 4 * s;
                var textArea = new SKRect(frameMinX - pad, frameMinY - pad,
                                          frameMaxX + pad, frameMaxY + pad);
                var fullArea = new SKRect(textArea.Left - accentW, textArea.Top,
                                          textArea.Right, textArea.Bottom);
                var frameRRect = new SKRoundRect(fullArea, radius, radius);

                // Subtle drop shadow
                bgPaint.Color = new SKColor(0, 0, 0, 25);
                canvas.DrawRoundRect(new SKRoundRect(
                    new SKRect(fullArea.Left + s, fullArea.Top + s,
                               fullArea.Right + s, fullArea.Bottom + 2 * s), radius, radius), bgPaint);

                // White background
                bgPaint.Color = new SKColor(255, 255, 255, 245);
                canvas.DrawRoundRect(frameRRect, bgPaint);

                // Subtle gray border
                framePaint.Color = new SKColor(0, 0, 0, 30);
                canvas.DrawRoundRect(frameRRect, framePaint);

                // Colored left accent bar
                var accentRRect = new SKRoundRect();
                accentRRect.SetRectRadii(
                    new SKRect(fullArea.Left, fullArea.Top,
                               fullArea.Left + accentW, fullArea.Bottom),
                    [new SKPoint(radius, radius), new SKPoint(0, 0),
                     new SKPoint(0, 0), new SKPoint(radius, radius)]);
                bgPaint.Color = new SKColor(t.Color.R, t.Color.G, t.Color.B, 210);
                canvas.DrawRoundRect(accentRRect, bgPaint);

                // Arrow connecting to the closest side center of the frame
                if (t.ArrowOrigin.HasValue)
                {
                    float arrowTipX = (float)(t.ArrowOrigin.Value.X * renderZoom);
                    float arrowTipY = (float)(t.ArrowOrigin.Value.Y * renderZoom);
                    var conn = ClosestSideCenterF(fullArea, arrowTipX, arrowTipY);
                    using var arrowLinePaint = new SKPaint
                    {
                        Color = new SKColor(t.Color.R, t.Color.G, t.Color.B, alpha),
                        StrokeWidth = 1.2f * (float)renderZoom,
                        Style = SKPaintStyle.Stroke,
                        StrokeCap = SKStrokeCap.Round,
                        IsAntialias = true
                    };
                    // Shorten line to arrowhead base to prevent round-cap protrusion
                    float aadx = arrowTipX - conn.x, aady = arrowTipY - conn.y;
                    float aalen = MathF.Sqrt(aadx * aadx + aady * aady);
                    if (aalen > 1)
                    {
                        float headLen = MathF.Min(8f * (float)renderZoom, aalen * 0.4f);
                        float shX = aadx / aalen * headLen;
                        float shY = aady / aalen * headLen;
                        canvas.DrawLine(conn.x, conn.y, arrowTipX - shX, arrowTipY - shY, arrowLinePaint);
                    }
                    RenderSkiaArrowhead(canvas, arrowLinePaint, conn.x, conn.y, arrowTipX, arrowTipY, renderZoom);
                }

                foreach (var (text, y, _) in lineInfos)
                    canvas.DrawText(text, tx, y, font, textPaint);
            }
        }

        // Render measurements
        var measurements = MuPDFRenderer.GetMeasurements(page);
        foreach (var m in measurements)
        {
            using var mPaint = new SKPaint
            {
                Color = new SKColor(m.Color.R, m.Color.G, m.Color.B, 220),
                StrokeWidth = (float)(1.5 * renderZoom),
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = true
            };

            var pts = m.Points;
            if (pts.Count >= 2)
            {
                float x0 = (float)(pts[0].X * renderZoom), y0 = (float)(pts[0].Y * renderZoom);
                float x1 = (float)(pts[1].X * renderZoom), y1 = (float)(pts[1].Y * renderZoom);
                canvas.DrawLine(x0, y0, x1, y1, mPaint);

                // End-marks: perpendicular ticks at both endpoints
                float emLen = (float)(6 * renderZoom);
                float ddx = x1 - x0, ddy = y1 - y0;
                float dlen = MathF.Sqrt(ddx * ddx + ddy * ddy);
                if (dlen > 1)
                {
                    float nx = -ddy / dlen * emLen, ny = ddx / dlen * emLen;
                    using var emPaint = new SKPaint { Color = mPaint.Color, StrokeWidth = mPaint.StrokeWidth, Style = SKPaintStyle.Stroke, IsAntialias = true };
                    canvas.DrawLine(x0 - nx, y0 - ny, x0 + nx, y0 + ny, emPaint);
                    canvas.DrawLine(x1 - nx, y1 - ny, x1 + nx, y1 + ny, emPaint);
                }

                // Arrowheads at both endpoints
                RenderSkiaMeasureArrowhead(canvas, mPaint.Color, x0, y0, x1, y1, renderZoom);
                RenderSkiaMeasureArrowhead(canvas, mPaint.Color, x1, y1, x0, y0, renderZoom);
            }

            // Draw label
            var labelPos = m.GetLabelPosition();
            float lx = (float)(labelPos.X * renderZoom), ly = (float)(labelPos.Y * renderZoom);
            using var labelFont = new SKFont(SKTypeface.Default, (float)(10 * renderZoom));
            using var labelPaint = new SKPaint { Color = new SKColor(m.Color.R, m.Color.G, m.Color.B), IsAntialias = true };
            using var labelBg = new SKPaint { Color = new SKColor(255, 255, 255, 200), Style = SKPaintStyle.Fill, IsAntialias = true };
            string label = m.GetLabel();
            float labelWidth = labelFont.MeasureText(label, out var labelBounds);
            canvas.DrawRoundRect(lx + labelBounds.Left - 3, ly + labelBounds.Top - 2,
                labelBounds.Width + 6, labelBounds.Height + 4, 3, 3, labelBg);
            canvas.DrawText(label, lx, ly, labelFont, labelPaint);
        }
    }

    /// <summary>
    private bool PageHasAnnotations(int page)
    {
        return MuPDFRenderer.GetStrokes(page).Count > 0
            || MuPDFRenderer.GetShapes(page).Count > 0
            || MuPDFRenderer.GetTexts(page).Count > 0
            || MuPDFRenderer.GetMeasurements(page).Count > 0;
    }

    private static void RenderSkiaStickyNoteIcon(SKCanvas canvas, TextAnnotation t, double renderZoom)
    {
        float x = (float)(t.Position.X * renderZoom);
        float y = (float)(t.Position.Y * renderZoom);
        float sz = (float)(13.0 * renderZoom);
        byte alpha = t.Opacity < 1.0 ? (byte)(t.Opacity * 255) : (byte)255;
        var color = new SKColor(t.Color.R, t.Color.G, t.Color.B, alpha);
        float fold = sz * 0.28f;

        using var bgPaint  = new SKPaint { Style = SKPaintStyle.Fill,   IsAntialias = true };
        using var brdPaint = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = MathF.Max(0.8f, sz * 0.05f) };

        // Body
        using var body = new SKPath();
        body.MoveTo(x, y);
        body.LineTo(x + sz - fold, y);
        body.LineTo(x + sz, y + fold);
        body.LineTo(x + sz, y + sz);
        body.LineTo(x, y + sz);
        body.Close();
        bgPaint.Color = new SKColor(color.Red, color.Green, color.Blue, 45);
        canvas.DrawPath(body, bgPaint);
        brdPaint.Color = new SKColor(color.Red, color.Green, color.Blue, 200);
        canvas.DrawPath(body, brdPaint);

        // Fold triangle
        using var foldPath = new SKPath();
        foldPath.MoveTo(x + sz - fold, y);
        foldPath.LineTo(x + sz, y + fold);
        foldPath.LineTo(x + sz - fold, y + fold);
        foldPath.Close();
        bgPaint.Color = new SKColor(color.Red, color.Green, color.Blue, 110);
        canvas.DrawPath(foldPath, bgPaint);
        brdPaint.Color = new SKColor(color.Red, color.Green, color.Blue, 180);
        canvas.DrawPath(foldPath, brdPaint);

        // Suggested-text lines
        using var linesPaint = new SKPaint
        {
            Color = new SKColor(color.Red, color.Green, color.Blue, 140),
            StrokeWidth = MathF.Max(0.8f, sz * 0.07f),
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };
        float lx = x + sz * 0.14f;
        float lw = sz * 0.52f;
        float ly = y + sz * 0.38f;
        float ls = sz * 0.18f;
        canvas.DrawLine(lx, ly,           lx + lw,          ly,           linesPaint);
        canvas.DrawLine(lx, ly + ls,      lx + lw,          ly + ls,      linesPaint);
        canvas.DrawLine(lx, ly + ls * 2f, lx + lw * 0.65f,  ly + ls * 2f, linesPaint);
    }

    private static void RenderSkiaArrowhead(SKCanvas canvas, SKPaint paint,
                                            float fx, float fy, float tx, float ty,
                                            double renderZoom)
    {
        double dx = tx - fx;
        double dy = ty - fy;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;

        double headLen = Math.Min(8 * renderZoom, len * 0.4);
        double headAngle = Math.PI / 8;
        double angle = Math.Atan2(dy, dx);

        using var arrowPaint = new SKPaint
        {
            Color = paint.Color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        using var path = new SKPath();
        float lx = (float)(tx - headLen * Math.Cos(angle - headAngle));
        float ly = (float)(ty - headLen * Math.Sin(angle - headAngle));
        float rx = (float)(tx - headLen * Math.Cos(angle + headAngle));
        float ry = (float)(ty - headLen * Math.Sin(angle + headAngle));
        path.MoveTo(lx, ly);
        path.LineTo(tx, ty);
        path.LineTo(rx, ry);
        path.Close();
        canvas.DrawPath(path, arrowPaint);
    }

    /// <summary>
    /// Draws a measurement-style arrowhead (smaller, slightly wider angle) for Skia export.
    /// Matches the in-app DrawMeasureArrowhead.
    /// </summary>
    private static void RenderSkiaMeasureArrowhead(SKCanvas canvas, SKColor color,
                                                    float tipX, float tipY,
                                                    float fromX, float fromY,
                                                    double renderZoom)
    {
        double dx = tipX - fromX;
        double dy = tipY - fromY;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        double headLen = Math.Min(6 * renderZoom, len * 0.3);
        double angle = Math.Atan2(dy, dx);
        const double half = Math.PI / 7;
        float p1x = (float)(tipX - headLen * Math.Cos(angle - half));
        float p1y = (float)(tipY - headLen * Math.Sin(angle - half));
        float p2x = (float)(tipX - headLen * Math.Cos(angle + half));
        float p2y = (float)(tipY - headLen * Math.Sin(angle + half));
        using var arrowPaint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var path = new SKPath();
        path.MoveTo(p1x, p1y);
        path.LineTo(tipX, tipY);
        path.LineTo(p2x, p2y);
        path.Close();
        canvas.DrawPath(path, arrowPaint);
    }

    /// <summary>Returns the center of the SKRect side closest to the given point.</summary>
    private static (float x, float y) ClosestSideCenterF(SKRect rect, float px, float py)
    {
        (float x, float y)[] candidates =
        [
            (rect.MidX, rect.Top),
            (rect.MidX, rect.Bottom),
            (rect.Left, rect.MidY),
            (rect.Right, rect.MidY)
        ];
        var best = candidates[0];
        float bestDist = float.MaxValue;
        foreach (var c in candidates)
        {
            float dx = c.x - px, dy = c.y - py;
            float d = dx * dx + dy * dy;
            if (d < bestDist) { bestDist = d; best = c; }
        }
        return best;
    }

    /// <summary>
    /// Creates a SkiaSharp cloud path (revision cloud) for export rendering.
    /// </summary>
    private static SKPath RenderSkiaCloudPath(float sx, float sy, float ex, float ey, double renderZoom)
    {
        float x1 = Math.Min(sx, ex), y1 = Math.Min(sy, ey);
        float x2 = Math.Max(sx, ex), y2 = Math.Max(sy, ey);
        float arcRadius = (float)(8 * renderZoom);
        if (arcRadius < 4) arcRadius = 4;

        var edgePoints = new List<(float x, float y)>();
        void AddEdge(float fx, float fy, float tx, float ty)
        {
            float dx = tx - fx, dy = ty - fy;
            float edgeLen = MathF.Sqrt(dx * dx + dy * dy);
            int segments = Math.Max(1, (int)(edgeLen / (arcRadius * 1.6f)));
            for (int i = 0; i < segments; i++)
            {
                float t = (float)i / segments;
                edgePoints.Add((fx + dx * t, fy + dy * t));
            }
        }
        AddEdge(x1, y1, x2, y1);
        AddEdge(x2, y1, x2, y2);
        AddEdge(x2, y2, x1, y2);
        AddEdge(x1, y2, x1, y1);

        var path = new SKPath();
        if (edgePoints.Count < 2)
        {
            path.AddRect(new SKRect(x1, y1, x2, y2));
            return path;
        }

        path.MoveTo(edgePoints[0].x, edgePoints[0].y);
        for (int i = 0; i < edgePoints.Count; i++)
        {
            var (cx, cy) = edgePoints[i];
            var (nx, ny) = edgePoints[(i + 1) % edgePoints.Count];
            float mx = (cx + nx) / 2, my = (cy + ny) / 2;
            float edx = nx - cx, edy = ny - cy;
            float elen = MathF.Sqrt(edx * edx + edy * edy);
            if (elen < 0.5f) { path.LineTo(nx, ny); continue; }
            float perpX = edy / elen, perpY = -edx / elen;
            float bulge = arcRadius * 0.6f;
            path.QuadTo(mx + perpX * bulge, my + perpY * bulge, nx, ny);
        }
        path.Close();
        return path;
    }

    /// <summary>
    /// Creates an SKPathEffect for dash patterns matching the in-app Avalonia renderer.
    /// Dash intervals in Avalonia DashStyle are in stroke-width multiples; SkiaSharp
    /// PathEffect.CreateDash expects pixel values, so we multiply by strokeWidth.
    /// </summary>
    private static SKPathEffect? CreateDashEffect(LineDashPattern pattern, float strokeWidth)
    {
        float[]? intervals = pattern switch
        {
            LineDashPattern.Dashed => [4 * strokeWidth, 3 * strokeWidth],
            LineDashPattern.Dotted => [1 * strokeWidth, 2 * strokeWidth],
            LineDashPattern.DashDot => [4 * strokeWidth, 2 * strokeWidth, 1 * strokeWidth, 2 * strokeWidth],
            _ => null
        };
        return intervals != null ? SKPathEffect.CreateDash(intervals, 0) : null;
    }

    /// <summary>
    /// Renders a label-style text annotation: centred text with a subtle pill background.
    /// Font size scales with renderZoom so the label stays proportional in the export.
    /// </summary>
    private static void RenderSkiaLabel(SKCanvas canvas, TextAnnotation t, double renderZoom)
    {
        float fontSize = (float)(t.FontSize * renderZoom);
        byte alpha = t.Opacity < 1.0 ? (byte)(t.Opacity * 255) : (byte)255;
        var color = new SKColor(t.Color.R, t.Color.G, t.Color.B, alpha);

        var typeface = !string.IsNullOrEmpty(t.FontFamily)
            ? (SKTypeface.FromFamilyName(t.FontFamily) ?? SKTypeface.Default)
            : SKTypeface.Default;
        using var font = new SKFont(typeface, fontSize);
        float textWidth = font.MeasureText(t.Text, out var textBounds);

        float cx = (float)(t.Position.X * renderZoom);
        float cy = (float)(t.Position.Y * renderZoom);
        float x = cx - textWidth * 0.5f;
        float y = cy + fontSize;
        float s = (float)renderZoom;

        // Pill background
        using var bgPaint = new SKPaint { Color = new SKColor(255, 255, 255, 200), Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawRoundRect(x + textBounds.Left - 3 * s, y + textBounds.Top - 2 * s,
            textBounds.Width + 6 * s, textBounds.Height + 4 * s, 3 * s, 3 * s, bgPaint);

        using var textPaint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(t.Text, x, y, font, textPaint);
    }

    /// <summary>
    /// Builds a SkiaSharp path for a polyline with rounded corners using quadratic
    /// Bézier curves at each vertex. Matches the in-app Avalonia renderer's
    /// ComputeArcRadius + QuadraticBezierTo logic.
    /// </summary>
    private static void BuildRoundedPolylinePath(SKPath path, SKPoint[] pts, int n, float cr, bool closed)
    {
        static float Dist(SKPoint a, SKPoint b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        static float ArcRadius(SKPoint prev, SKPoint curr, SKPoint next, float maxR)
        {
            float dIn = Dist(prev, curr);
            float dOut = Dist(curr, next);
            float halfMin = MathF.Min(dIn, dOut) * 0.45f;
            return MathF.Min(maxR, halfMin);
        }

        if (closed && n >= 3)
        {
            // Start at arc-start of vertex 0
            var prev0 = pts[(n - 1) % n];
            var curr0 = pts[0];
            var next0 = pts[1];
            float r0 = ArcRadius(prev0, curr0, next0, cr);
            if (r0 >= 0.5f)
            {
                float dIn = Dist(prev0, curr0);
                float dOut = Dist(curr0, next0);
                var arcStart = new SKPoint(curr0.X - (curr0.X - prev0.X) / dIn * r0,
                                           curr0.Y - (curr0.Y - prev0.Y) / dIn * r0);
                path.MoveTo(arcStart);
                var arcEnd = new SKPoint(curr0.X + (next0.X - curr0.X) / dOut * r0,
                                         curr0.Y + (next0.Y - curr0.Y) / dOut * r0);
                path.QuadTo(curr0, arcEnd);
            }
            else
            {
                path.MoveTo(curr0);
            }

            for (int i = 1; i < n; i++)
            {
                var prev = pts[(i - 1 + n) % n];
                var curr = pts[i % n];
                var next = pts[(i + 1) % n];
                float r = ArcRadius(prev, curr, next, cr);
                if (r >= 0.5f)
                {
                    float dIn = Dist(prev, curr);
                    float dOut = Dist(curr, next);
                    var arcStart = new SKPoint(curr.X - (curr.X - prev.X) / dIn * r,
                                               curr.Y - (curr.Y - prev.Y) / dIn * r);
                    path.LineTo(arcStart);
                    var arcEnd = new SKPoint(curr.X + (next.X - curr.X) / dOut * r,
                                             curr.Y + (next.Y - curr.Y) / dOut * r);
                    path.QuadTo(curr, arcEnd);
                }
                else
                {
                    path.LineTo(curr);
                }
            }
            path.Close();
        }
        else
        {
            // Open polyline: first and last points are endpoints, round interior vertices
            path.MoveTo(pts[0]);
            for (int i = 1; i < n - 1; i++)
            {
                var prev = pts[i - 1];
                var curr = pts[i];
                var next = pts[i + 1];
                float r = ArcRadius(prev, curr, next, cr);
                if (r >= 0.5f)
                {
                    float dIn = Dist(prev, curr);
                    float dOut = Dist(curr, next);
                    var arcStart = new SKPoint(curr.X - (curr.X - prev.X) / dIn * r,
                                               curr.Y - (curr.Y - prev.Y) / dIn * r);
                    path.LineTo(arcStart);
                    var arcEnd = new SKPoint(curr.X + (next.X - curr.X) / dOut * r,
                                             curr.Y + (next.Y - curr.Y) / dOut * r);
                    path.QuadTo(curr, arcEnd);
                }
                else
                {
                    path.LineTo(curr);
                }
            }
            path.LineTo(pts[n - 1]);
        }
    }

    private async void OnAnnotateCopy(object sender, RoutedEventArgs e)
    {
        using var data = RenderAnnotatedPage();
        if (data == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null) return;

        // Write to a temp file and place it on the clipboard as a file drop.
        // This is the most reliable way to paste images into Word, Teams, etc.
        string tempPath = Path.Combine(Path.GetTempPath(), "finn_annotation.png");
        using (var fs = File.Create(tempPath))
            data.SaveTo(fs);

        var storageFile = await topLevel.StorageProvider
            .TryGetFileFromPathAsync(new Uri("file:///" + tempPath.Replace('\\', '/')));
        if (storageFile == null) return;

        await topLevel.Clipboard.SetFilesAsync(new[] { storageFile });
        pwr.StatusMessage = "Annotation copied to clipboard";
    }

    private void OnAnnotateExportReview(object sender, RoutedEventArgs e)
    {
        if (pwr.CurrentFile == null && !pwr.WhiteboardMode) return;
        ReviewNameBox.Text = "";
        ReviewExportStatus.Text = "";
        ReviewExportCanvas.IsVisible = true;
        ReviewNameBox.Focus();
    }

    private void OnReviewNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OnReviewExportConfirm(sender!, e); e.Handled = true; }
        else if (e.Key == Key.Escape) { OnReviewExportCancel(sender!, e); e.Handled = true; }
    }

    private void OnReviewExportCancel(object sender, RoutedEventArgs e)
    {
        ReviewExportCanvas.IsVisible = false;
    }

    private async void OnReviewExportConfirm(object sender, RoutedEventArgs e)
    {
        string reviewName = ReviewNameBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(reviewName))
            reviewName = "review";

        ReviewExportStatus.Text = "Exporting...";

        try
        {
            string exportPath = await Task.Run(() => ExportReviewPdf(reviewName));
            ReviewExportCanvas.IsVisible = false;

            if (exportPath != null && pwr.CurrentFile != null)
            {
                pwr.CurrentFile.AddVersion(exportPath, "REVIEW");
                ctx.MarkDirty();
            }

            pwr.StatusMessage = $"Review exported: {Path.GetFileName(exportPath ?? "")}";
        }
        catch (Exception ex)
        {
            ReviewExportStatus.Text = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Exports a review PDF using PDFSharp vector export.
    /// </summary>
    private string ExportReviewPdf(string reviewName)
    {
        var integrity = MuPDFRenderer.ValidateAndRepairAnnotations();
        if ((integrity.FixedCount > 0 || integrity.RemovedCount > 0) && pwr != null)
            pwr.StatusMessage = $"Annotations normalized ({integrity.FixedCount} fixed, {integrity.RemovedCount} removed)";

        const double renderZoom = 2.0;

        // Determine review output folder
        string baseFolder = ctx?.CurrentProject?.ReviewFolder;
        if (string.IsNullOrWhiteSpace(baseFolder))
            baseFolder = Path.Combine(MainViewModel.SavePath, "Reviews");

        string subFolder = $"{DateTime.Now:yyyy-MM-dd}_{SanitizeFileName(reviewName)}";
        string outputDir = Path.Combine(baseFolder, subFolder);
        Directory.CreateDirectory(outputDir);

        string? sourcePath = pwr.CurrentFile?.Sökväg;
        string sourceName = sourcePath != null
            ? Path.GetFileNameWithoutExtension(sourcePath)
            : "whiteboard";
        string outputPath = Path.Combine(outputDir, $"{sourceName}.pdf");

        // Whiteboard or missing source → image-based fallback
        if (sourcePath == null || !File.Exists(sourcePath))
            return ExportReviewPdfImageBased(outputPath, renderZoom);

        return ExportReviewPdfSharpOnly(outputPath, sourcePath);
    }

    /// <summary>
    /// PDFSharp-only export: copies the source PDF and draws ALL annotations
    /// using PDFSharp XGraphics as static page content. No iText7 involved.
    /// Exceptions propagate to the UI error label for easy debugging.
    /// </summary>
    private string ExportReviewPdfSharpOnly(string outputPath, string sourcePath)
    {
        // PDFSharp 6 core build cannot find system fonts without this opt-in
        PdfSharp.Fonts.GlobalFontSettings.UseWindowsFontsUnderWindows = true;

        // Import source pages into a NEW document so PDFSharp fully owns each
        // page's resource dictionary. In Modify+Append mode PDFSharp fails to
        // merge ExtGState entries into the existing resources, silently dropping
        // all alpha/opacity values.
        using var srcDoc = PdfSharp.Pdf.IO.PdfReader.Open(
            sourcePath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        using var outDoc = new PdfSharp.Pdf.PdfDocument();

        int pageCount = Math.Min(srcDoc.PageCount, pwr.Pagecount);
        for (int i = 0; i < pageCount; i++)
        {
            // AddPage imports the full page (content + resources)
            var page = outDoc.AddPage(srcDoc.Pages[i]);

            if (!PageHasAnnotations(i)) continue;

            // XGraphics in explicit block so it finalises before we add annotations
            using (var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
            {
                DrawStrokesWithPdfSharp(gfx, i);
                DrawShapesWithPdfSharp(gfx, i);
                DrawTextsWithPdfSharp(gfx, i);
                DrawMeasurementsWithPdfSharp(gfx, i);
            }

            // Sticky notes as native PDF annotations (expandable in viewers)
            AddPdfSharpStickyNotes(outDoc, page, i);
        }

        outDoc.Save(outputPath);
        return outputPath;
    }

    private void DrawStrokesWithPdfSharp(XGraphics gfx, int page)
    {
        var strokes = MuPDFRenderer.GetStrokes(page);
        foreach (var stroke in strokes)
        {
            if (stroke.Points.Count < 2) continue;

            byte alpha = stroke.Opacity < 1.0 ? (byte)(stroke.Opacity * 255) : stroke.Color.A;
            var pen = new XPen(XColor.FromArgb(alpha, stroke.Color.R, stroke.Color.G, stroke.Color.B), stroke.Width)
            {
                LineCap = stroke.IsHighlighter ? XLineCap.Square : XLineCap.Round,
                LineJoin = XLineJoin.Round
            };

            var pts = stroke.Points;

            if (pts.Count == 2 || stroke.IsPolyline)
            {
                for (int j = 0; j < pts.Count - 1; j++)
                    gfx.DrawLine(pen, pts[j].X, pts[j].Y, pts[j + 1].X, pts[j + 1].Y);
            }
            else
            {
                // Catmull-Rom → Bézier spline
                var path = new XGraphicsPath();
                path.StartFigure();
                for (int j = 0; j < pts.Count - 1; j++)
                {
                    var pm1 = pts[Math.Max(j - 1, 0)];
                    var pi  = pts[j];
                    var pi1 = pts[j + 1];
                    var pi2 = pts[Math.Min(j + 2, pts.Count - 1)];

                    double cp1x = pi.X + (pi1.X - pm1.X) / 6.0;
                    double cp1y = pi.Y + (pi1.Y - pm1.Y) / 6.0;
                    double cp2x = pi1.X - (pi2.X - pi.X) / 6.0;
                    double cp2y = pi1.Y - (pi2.Y - pi.Y) / 6.0;

                    path.AddBezier(pi.X, pi.Y, cp1x, cp1y, cp2x, cp2y, pi1.X, pi1.Y);
                }
                gfx.DrawPath(pen, path);
            }
        }
    }

    private void DrawShapesWithPdfSharp(XGraphics gfx, int page)
    {
        var shapes = MuPDFRenderer.GetShapes(page);
        foreach (var shape in shapes)
        {
            double opFactor = shape.Opacity < 1.0 ? shape.Opacity : 1.0;
            byte alpha = (byte)(255 * opFactor);
            if (shape.Opacity >= 1.0) alpha = shape.Color.A;
            var color = XColor.FromArgb(alpha, shape.Color.R, shape.Color.G, shape.Color.B);
            var pen = new XPen(color, shape.StrokeWidth) { LineCap = XLineCap.Round, LineJoin = XLineJoin.Round };
            XBrush? fill = shape.IsFilled
                ? new XSolidBrush(XColor.FromArgb((byte)(80 * opFactor), shape.Color.R, shape.Color.G, shape.Color.B))
                : null;

            double sx = shape.Start.X, sy = shape.Start.Y;
            double ex = shape.End.X,   ey = shape.End.Y;
            double left = Math.Min(sx, ex), top = Math.Min(sy, ey);
            double w = Math.Abs(ex - sx), h = Math.Abs(ey - sy);

            switch (shape.ShapeType)
            {
                case InlineAnnotationTool.Line:
                    gfx.DrawLine(pen, sx, sy, ex, ey);
                    break;

                case InlineAnnotationTool.Arrow:
                {
                    // Shorten line to arrowhead base to prevent round-cap protrusion
                    double adx = ex - sx, ady = ey - sy;
                    double alen = Math.Sqrt(adx * adx + ady * ady);
                    if (alen > 1)
                    {
                        double headLen = Math.Min(8.0, alen * 0.4);
                        double shX = adx / alen * headLen;
                        double shY = ady / alen * headLen;
                        gfx.DrawLine(pen, sx, sy, ex - shX, ey - shY);
                    }
                    DrawArrowheadPdfSharp(gfx, sx, sy, ex, ey, color);
                    break;
                }

                case InlineAnnotationTool.Rectangle:
                    if (fill != null) gfx.DrawRectangle(fill, left, top, w, h);
                    gfx.DrawRectangle(pen, left, top, w, h);
                    break;

                case InlineAnnotationTool.Ellipse:
                    if (fill != null) gfx.DrawEllipse(fill, left, top, w, h);
                    gfx.DrawEllipse(pen, left, top, w, h);
                    break;

                case InlineAnnotationTool.RevisionCloud:
                    DrawCloudWithPdfSharp(gfx, pen, fill, sx, sy, ex, ey);
                    break;
            }
        }
    }

    private void DrawTextsWithPdfSharp(XGraphics gfx, int page)
    {
        var texts = MuPDFRenderer.GetTexts(page);
        foreach (var t in texts)
        {
            // Sticky notes are added as native PDF annotations separately
            if (t.IsStickyNote) continue;

            byte alpha = t.Opacity < 1.0 ? (byte)(t.Opacity * 255) : (byte)255;
            var annColor = XColor.FromArgb(alpha, t.Color.R, t.Color.G, t.Color.B);

            string fontName = !string.IsNullOrEmpty(t.FontFamily) ? t.FontFamily : "Arial";
            XFont font;
            try   { font = new XFont(fontName, t.FontSize, XFontStyleEx.Regular); }
            catch { font = new XFont("Arial",  t.FontSize, XFontStyleEx.Regular); }

            // Strip \r so carriage-return doesn't render as a missing-glyph box
            string[] lines = t.Text.Replace("\r", "").Split('\n');
            double lineHeight = t.FontSize * 1.3;
            double maxW = 0;
            foreach (var line in lines)
                if (line.Length > 0)
                    maxW = Math.Max(maxW, gfx.MeasureString(line, font).Width);
            maxW = Math.Max(maxW, t.FontSize * 2);
            double totalH = lines.Length * lineHeight;

            // Vertical offset: in Skia the baseline sits at Position.Y + FontSize
            // and the visible text top is ~FontSize*0.2 below Position.Y.
            // PDFSharp TopLeft places the cell top at Position.Y, so shift down
            // to match the in-app / Skia rendered position.
            double yOff = t.FontSize * 0.2;

            const double pad = 2, accentW = 4, r = 4;
            double fx = t.Position.X - pad - accentW;
            double fy = t.Position.Y - pad + yOff;
            double fw = maxW + pad * 2 + accentW;
            double fh = totalH + pad * 2;
            var frameRect = new XRect(fx, fy, fw, fh);
            var corner = new XSize(r, r);

            // Shadow
            gfx.DrawRoundedRectangle(
                new XSolidBrush(XColor.FromArgb(20, 0, 0, 0)),
                new XRect(fx + 1, fy + 2, fw, fh), corner);
            // White body
            gfx.DrawRoundedRectangle(
                new XSolidBrush(XColor.FromArgb(245, 255, 255, 255)),
                frameRect, corner);
            // Colored accent bar
            var accentColor = XColor.FromArgb((byte)(210 * alpha / 255), t.Color.R, t.Color.G, t.Color.B);
            gfx.DrawRectangle(new XSolidBrush(accentColor),
                new XRect(fx, fy + r, accentW, fh - r * 2));
            // Border
            gfx.DrawRoundedRectangle(
                new XPen(XColor.FromArgb(40, 0, 0, 0), 0.8), frameRect, corner);

            // Arrow for ArrowText — Euclidean distance to side centres (matches Skia)
            if (t.ArrowOrigin.HasValue)
            {
                double ax = t.ArrowOrigin.Value.X, ay = t.ArrowOrigin.Value.Y;
                double midX = fx + fw / 2, midY = fy + fh / 2;
                (double cx, double cy)[] sides =
                [
                    (midX, fy),          // top
                    (midX, fy + fh),      // bottom
                    (fx, midY),           // left
                    (fx + fw, midY)       // right
                ];
                double bestDist = double.MaxValue;
                double attX = midX, attY = fy;
                foreach (var (cx, cy) in sides)
                {
                    double d = (cx - ax) * (cx - ax) + (cy - ay) * (cy - ay);
                    if (d < bestDist) { bestDist = d; attX = cx; attY = cy; }
                }
                // Shorten line to arrowhead base to prevent round-cap protrusion
                var arrowPen = new XPen(annColor, 1.2) { LineCap = XLineCap.Round };
                double aadx = ax - attX, aady = ay - attY;
                double aalen = Math.Sqrt(aadx * aadx + aady * aady);
                if (aalen > 1)
                {
                    double headLen = Math.Min(8.0, aalen * 0.4);
                    double shX = aadx / aalen * headLen;
                    double shY = aady / aalen * headLen;
                    gfx.DrawLine(arrowPen, attX, attY, ax - shX, ay - shY);
                }
                DrawArrowheadPdfSharp(gfx, attX, attY, ax, ay, annColor);
            }

            // Text lines (shifted by yOff to match Skia)
            var textBrush = new XSolidBrush(annColor);
            double tx = t.Position.X;
            double ty = t.Position.Y + yOff;
            foreach (var line in lines)
            {
                if (line.Length > 0)
                    gfx.DrawString(line, font, textBrush, tx, ty, XStringFormats.TopLeft);
                ty += lineHeight;
            }
        }
    }

    /// <summary>
    /// Adds native PDF text annotations (sticky notes) for the given page.
    /// These are expandable comment icons in Acrobat / Bluebeam / etc.
    /// Called after XGraphics is disposed so annotation coordinates use
    /// PDF native space (Y-up from bottom-left).
    /// </summary>
    private void AddPdfSharpStickyNotes(
        PdfSharp.Pdf.PdfDocument outDoc,
        PdfSharp.Pdf.PdfPage page, int pageIndex)
    {
        var texts = MuPDFRenderer.GetTexts(pageIndex);
        double pageH = page.Height.Point;

        foreach (var t in texts)
        {
            if (!t.IsStickyNote) continue;

            // Convert from XGraphics Y-down to PDF native Y-up
            double pdfX = t.Position.X;
            double pdfY = pageH - t.Position.Y;

            var annot = new PdfSharp.Pdf.Annotations.PdfTextAnnotation(outDoc);
            annot.Contents = t.Text;
            annot.Icon = PdfSharp.Pdf.Annotations.PdfTextAnnotationIcon.Comment;
            annot.Color = XColor.FromArgb(255, t.Color.R, t.Color.G, t.Color.B);
            annot.Open = false;
            annot.Flags = PdfSharp.Pdf.Annotations.PdfAnnotationFlags.Print;
            if (t.Opacity < 1.0)
                annot.Opacity = t.Opacity;
            annot.Rectangle = new PdfSharp.Pdf.PdfRectangle(
                new XPoint(pdfX - 12, pdfY - 12), new XPoint(pdfX + 12, pdfY + 12));

            page.Annotations.Add(annot);
        }
    }

    private void DrawMeasurementsWithPdfSharp(XGraphics gfx, int page)
    {
        var measurements = MuPDFRenderer.GetMeasurements(page);
        foreach (var m in measurements)
        {
            var color = XColor.FromArgb(220, m.Color.R, m.Color.G, m.Color.B);
            var pen = new XPen(color, 1.5)
            {
                LineCap = XLineCap.Round
            };

            var pts = m.Points;
            if (pts.Count >= 2)
            {
                double x0 = pts[0].X, y0 = pts[0].Y;
                double x1 = pts[1].X, y1 = pts[1].Y;
                gfx.DrawLine(pen, x0, y0, x1, y1);

                // End-marks: perpendicular ticks
                double emLen = 6;
                double ddx = x1 - x0, ddy = y1 - y0;
                double dlen = Math.Sqrt(ddx * ddx + ddy * ddy);
                if (dlen > 1)
                {
                    double nx = -ddy / dlen * emLen, ny = ddx / dlen * emLen;
                    var emPen = new XPen(color, 1.5) { LineCap = XLineCap.Round };
                    gfx.DrawLine(emPen, x0 - nx, y0 - ny, x0 + nx, y0 + ny);
                    gfx.DrawLine(emPen, x1 - nx, y1 - ny, x1 + nx, y1 + ny);
                }

                // Arrowheads at both endpoints
                DrawMeasureArrowheadPdfSharp(gfx, x0, y0, x1, y1, color);
                DrawMeasureArrowheadPdfSharp(gfx, x1, y1, x0, y0, color);
            }

            // Label
            var labelPos = m.GetLabelPosition();
            string label = m.GetLabel();
            var labelFont = new XFont("Arial", 10, XFontStyleEx.Regular);
            var labelSize = gfx.MeasureString(label, labelFont);
            double lx = labelPos.X - labelSize.Width / 2;
            double ly = labelPos.Y - labelSize.Height / 2;
            gfx.DrawRoundedRectangle(
                new XSolidBrush(XColor.FromArgb(200, 255, 255, 255)),
                new XRect(lx - 3, ly - 2, labelSize.Width + 6, labelSize.Height + 4),
                new XSize(3, 3));
            gfx.DrawString(label, labelFont, new XSolidBrush(XColor.FromArgb(255, m.Color.R, m.Color.G, m.Color.B)),
                labelPos.X, labelPos.Y, XStringFormats.Center);
        }
    }

    private static void DrawArrowheadPdfSharp(
        XGraphics gfx, double fromX, double fromY, double tipX, double tipY, XColor color)
    {
        double dx = tipX - fromX, dy = tipY - fromY;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        double headLen = Math.Min(8.0, len * 0.4);
        const double ang = Math.PI / 8;
        double angle = Math.Atan2(dy, dx);
        var path = new XGraphicsPath();
        path.AddPolygon([
            new XPoint(tipX - headLen * Math.Cos(angle - ang),
                       tipY - headLen * Math.Sin(angle - ang)),
            new XPoint(tipX, tipY),
            new XPoint(tipX - headLen * Math.Cos(angle + ang),
                       tipY - headLen * Math.Sin(angle + ang))
        ]);
        gfx.DrawPath(new XSolidBrush(color), path);
    }

    /// <summary>
    /// Draws a measurement-style arrowhead (smaller, slightly wider angle) for PDFSharp export.
    /// Matches the in-app DrawMeasureArrowhead.
    /// </summary>
    private static void DrawMeasureArrowheadPdfSharp(
        XGraphics gfx, double tipX, double tipY, double fromX, double fromY, XColor color)
    {
        double dx = tipX - fromX, dy = tipY - fromY;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        double headLen = Math.Min(6.0, len * 0.3);
        const double half = Math.PI / 7;
        double angle = Math.Atan2(dy, dx);
        var path = new XGraphicsPath();
        path.AddPolygon([
            new XPoint(tipX - headLen * Math.Cos(angle - half),
                       tipY - headLen * Math.Sin(angle - half)),
            new XPoint(tipX, tipY),
            new XPoint(tipX - headLen * Math.Cos(angle + half),
                       tipY - headLen * Math.Sin(angle + half))
        ]);
        gfx.DrawPath(new XSolidBrush(color), path);
    }

    private static void DrawCloudWithPdfSharp(
        XGraphics gfx, XPen pen, XBrush? fill,
        double sx, double sy, double ex, double ey)
    {
        double x1 = Math.Min(sx, ex), y1 = Math.Min(sy, ey);
        double x2 = Math.Max(sx, ex), y2 = Math.Max(sy, ey);
        double arcRadius = 8;

        var edgePoints = new List<(double x, double y)>();
        void AddEdge(double fx, double fy, double tx, double ty)
        {
            double dx = tx - fx, dy = ty - fy;
            double edgeLen = Math.Sqrt(dx * dx + dy * dy);
            int segments = Math.Max(1, (int)(edgeLen / (arcRadius * 1.6)));
            for (int i = 0; i < segments; i++)
            {
                double t = (double)i / segments;
                edgePoints.Add((fx + dx * t, fy + dy * t));
            }
        }
        AddEdge(x1, y1, x2, y1);
        AddEdge(x2, y1, x2, y2);
        AddEdge(x2, y2, x1, y2);
        AddEdge(x1, y2, x1, y1);

        if (edgePoints.Count < 2)
        {
            if (fill != null) gfx.DrawRectangle(fill, x1, y1, x2 - x1, y2 - y1);
            gfx.DrawRectangle(pen, x1, y1, x2 - x1, y2 - y1);
            return;
        }

        // Build a single connected figure using Bézier curves for the cloud bulges
        var path = new XGraphicsPath();
        path.StartFigure();
        // Seed the path at the first point so all subsequent curves connect
        var first = edgePoints[0];
        path.AddLine(first.x, first.y, first.x, first.y);
        for (int i = 0; i < edgePoints.Count; i++)
        {
            var (cx, cy) = edgePoints[i];
            var (nx, ny) = edgePoints[(i + 1) % edgePoints.Count];
            double mx = (cx + nx) / 2, my = (cy + ny) / 2;
            double edx = nx - cx, edy = ny - cy;
            double elen = Math.Sqrt(edx * edx + edy * edy);
            if (elen < 0.5)
            {
                path.AddLine(cx, cy, nx, ny);
                continue;
            }
            double perpX = edy / elen, perpY = -edx / elen;
            double bulge = arcRadius * 0.6;
            double qcx = mx + perpX * bulge, qcy = my + perpY * bulge;
            double c1x = cx + 2.0 / 3.0 * (qcx - cx), c1y = cy + 2.0 / 3.0 * (qcy - cy);
            double c2x = nx + 2.0 / 3.0 * (qcx - nx), c2y = ny + 2.0 / 3.0 * (qcy - ny);
            path.AddBezier(cx, cy, c1x, c1y, c2x, c2y, nx, ny);
        }
        path.CloseFigure();
        if (fill != null) gfx.DrawPath(fill, path);
        gfx.DrawPath(pen, path);
    }

    /// <summary>
    /// Fallback export for whiteboard mode: renders each page as a full image.
    /// </summary>
    private string ExportReviewPdfImageBased(string outputPath, double renderZoom)
    {
        using var pdfWriter = new PdfWriter(outputPath);
        using var pdfDoc = new PdfDocument(pdfWriter);
        using var document = new iText.Layout.Document(pdfDoc);
        document.SetMargins(0, 0, 0, 0);

        int pageCount = pwr.Pagecount;
        for (int i = 0; i < pageCount; i++)
        {
            using var pageData = RenderAnnotatedPage(renderZoom, i);
            if (pageData == null) continue;

            byte[] pngBytes = pageData.ToArray();
            var imgData = ImageDataFactory.Create(pngBytes);
            var img = new iText.Layout.Element.Image(imgData);

            float pdfW = img.GetImageWidth()  / (float)renderZoom;
            float pdfH = img.GetImageHeight() / (float)renderZoom;

            pdfDoc.AddNewPage(new iText.Kernel.Geom.PageSize(pdfW, pdfH));
            img.SetFixedPosition(i + 1, 0, 0);
            img.SetWidth(pdfW);
            img.SetHeight(pdfH);
            document.Add(img);
        }

        return outputPath;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 60 ? name[..60] : name;
    }

    /// <summary>
    /// Word-wraps text into lines that fit within <paramref name="maxWidthPx"/>.
    /// Matches the in-app renderer's WrapTextLines logic.
    /// </summary>
    private static List<string> WrapTextLines(string text, float maxWidthPx, SKFont font)
    {
        var result = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            if (string.IsNullOrEmpty(paragraph)) { result.Add(""); continue; }
            var words = paragraph.Split(' ');
            string currentLine = "";
            foreach (var word in words)
            {
                string test = currentLine.Length == 0 ? word : currentLine + " " + word;
                float width = font.MeasureText(test);
                if (width <= maxWidthPx || currentLine.Length == 0)
                    currentLine = test;
                else
                {
                    result.Add(currentLine);
                    currentLine = word;
                }
            }
            if (currentLine.Length > 0)
                result.Add(currentLine);
        }
        if (result.Count == 0) result.Add("");
        return result;
    }

    #endregion
}
