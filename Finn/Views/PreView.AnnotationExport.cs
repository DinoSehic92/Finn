using Finn.ViewModels;
using Finn.Model;
using Finn.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using iText.IO.Image;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Canvas;
using MuPDFCore;
using MuPDFCore.MuPDFRenderer;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

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
                IsAntialias = true
            };

            var pts = stroke.Points;
            var path = new SKPath();
            path.MoveTo((float)(pts[0].X * renderZoom), (float)(pts[0].Y * renderZoom));

            if (pts.Count == 2)
            {
                path.LineTo((float)(pts[1].X * renderZoom), (float)(pts[1].Y * renderZoom));
            }
            else if (stroke.IsPolyline)
            {
                // Straight line segments for polylines
                for (int i = 1; i < pts.Count; i++)
                    path.LineTo((float)(pts[i].X * renderZoom), (float)(pts[i].Y * renderZoom));
            }
            else
            {
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var pm1 = pts[Math.Max(i - 1, 0)];
                    var pi  = pts[i];
                    var pi1 = pts[i + 1];
                    var pi2 = pts[Math.Min(i + 2, pts.Count - 1)];

                    float cp1x = (float)((pi.X + (pi1.X - pm1.X) / 6.0) * renderZoom);
                    float cp1y = (float)((pi.Y + (pi1.Y - pm1.Y) / 6.0) * renderZoom);
                    float cp2x = (float)((pi1.X - (pi2.X - pi.X) / 6.0) * renderZoom);
                    float cp2y = (float)((pi1.Y - (pi2.Y - pi.Y) / 6.0) * renderZoom);
                    float ex   = (float)(pi1.X * renderZoom);
                    float ey   = (float)(pi1.Y * renderZoom);

                    path.CubicTo(cp1x, cp1y, cp2x, cp2y, ex, ey);
                }
            }
            canvas.DrawPath(path, paint);
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
                IsAntialias = true
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
                    canvas.DrawLine(sx, sy, ex, ey, paint);
                    RenderSkiaArrowhead(canvas, paint, sx, sy, ex, ey, renderZoom);
                    break;

                case InlineAnnotationTool.Rectangle:
                    if (fillPaint != null)
                        canvas.DrawRect(Math.Min(sx, ex), Math.Min(sy, ey),
                                        Math.Abs(ex - sx), Math.Abs(ey - sy), fillPaint);
                    canvas.DrawRect(Math.Min(sx, ex), Math.Min(sy, ey),
                                    Math.Abs(ex - sx), Math.Abs(ey - sy), paint);
                    break;

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
                    var cloudPath = RenderSkiaCloudPath(sx, sy, ex, ey, renderZoom);
                    if (fillPaint != null)
                        canvas.DrawPath(cloudPath, fillPaint);
                    canvas.DrawPath(cloudPath, paint);
                    break;
                }
            }
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

            // Measure total extent for frame
            float frameMinX = float.MaxValue, frameMinY = float.MaxValue;
            float frameMaxX = float.MinValue, frameMaxY = float.MinValue;
            var lineInfos = new List<(string text, float y, SKRect bounds)>();
            float curY = ty;
            foreach (var line in t.Text.Split('\n'))
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
                // Stamp-style frame: all dimensions scaled by renderZoom so the
                // exported result is proportionally identical to the in-app view.
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
                    canvas.DrawLine(arrowTipX, arrowTipY, conn.x, conn.y, arrowLinePaint);
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
                PathEffect = SKPathEffect.CreateDash([8f * (float)renderZoom, 6f * (float)renderZoom], 0),
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
    /// Renders annotations only (no PDF background) onto a transparent surface.
    /// Used by the searchable PDF export to overlay annotations on original pages.
    /// </summary>
    private SKData? RenderAnnotationsOnly(int page, double renderZoom, int pixelWidth, int pixelHeight)
    {
        using var surface = SKSurface.Create(new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface == null) return null;
        surface.Canvas.Clear(SKColors.Transparent);
        DrawAnnotationsToCanvas(surface.Canvas, page, renderZoom);
        using var image = surface.Snapshot();
        return image.Encode(SKEncodedImageFormat.Png, 100);
    }

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
        var body = new SKPath();
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
        var foldPath = new SKPath();
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

        var path = new SKPath();
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

    private void OnAnnotateSave(object sender, RoutedEventArgs e)
    {
        using var data = RenderAnnotatedPage();
        if (data == null) return;

        int page = pwr.CurrentPage1;
        Directory.CreateDirectory(Path.Combine(MainViewModel.SavePath, "Annotations"));
        string filePath = Path.Combine(MainViewModel.SavePath, "Annotations",
            $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_page{page + 1}.png");
        using var fs = File.OpenWrite(filePath);
        data.SaveTo(fs);
        pwr.StatusMessage = $"Saved to {Path.GetFileName(filePath)}";
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

        var dataObject = new DataObject();
        dataObject.Set(DataFormats.Files, new[] { storageFile });
        await topLevel.Clipboard.SetDataObjectAsync(dataObject);
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
        bool nativeMode = ExportModeNative.IsChecked == true;

        ReviewExportStatus.Text = "Exporting...";

        try
        {
            string exportPath = await Task.Run(() => ExportReviewPdf(reviewName, nativeMode));
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
    /// Exports a review PDF in either native (editable) or rendered (pixel-perfect) mode.
    /// </summary>
    private string ExportReviewPdf(string reviewName, bool nativeMode)
    {
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

        if (nativeMode)
            return ExportReviewPdfNative(outputPath, sourcePath, renderZoom);
        else
            return ExportReviewPdfRendered(outputPath, sourcePath, renderZoom);
    }

    /// <summary>
    /// Native export: uses PDF annotations for maximum editability in other viewers.
    /// Revision clouds and measurements (no native equivalent) are stamped as overlay.
    /// </summary>
    private string ExportReviewPdfNative(string outputPath, string sourcePath, double renderZoom)
    {
        using var reader = new PdfReader(sourcePath);
        using var writer = new PdfWriter(outputPath);
        using var pdfDoc = new PdfDocument(reader, writer);

        int pageCount = pdfDoc.GetNumberOfPages();
        for (int i = 0; i < pageCount; i++)
        {
            if (!PageHasAnnotations(i)) continue;

            var pdfPage = pdfDoc.GetPage(i + 1);
            var mediaBox = pdfPage.GetMediaBox();
            float pageWidth = mediaBox.GetWidth();
            float pageHeight = mediaBox.GetHeight();

            AddNativeAnnotationsToPage(pdfPage, i, pageHeight);

            // Overlay only for types without native PDF equivalents
            if (PageHasNativeOverlayAnnotations(i))
            {
                int pixW = (int)Math.Ceiling(pageWidth * renderZoom);
                int pixH = (int)Math.Ceiling(pageHeight * renderZoom);
                using var overlayData = RenderNativeOverlayOnly(i, renderZoom, pixW, pixH);
                if (overlayData != null)
                {
                    byte[] pngBytes = overlayData.ToArray();
                    var imageData = ImageDataFactory.Create(pngBytes);
                    var xObject = new iText.Kernel.Pdf.Xobject.PdfImageXObject(imageData);
                    var pdfCanvas = new PdfCanvas(pdfPage);
                    pdfCanvas.AddXObjectWithTransformationMatrix(xObject,
                        pageWidth, 0, 0, pageHeight,
                        mediaBox.GetLeft(), mediaBox.GetBottom());
                }
            }
        }

        return outputPath;
    }

    /// <summary>
    /// Rendered export: stamps a full Skia-rendered image of all annotations
    /// on each page. Pixel-perfect match to in-app appearance.
    /// </summary>
    private string ExportReviewPdfRendered(string outputPath, string sourcePath, double renderZoom)
    {
        using var reader = new PdfReader(sourcePath);
        using var writer = new PdfWriter(outputPath);
        using var pdfDoc = new PdfDocument(reader, writer);

        int pageCount = pdfDoc.GetNumberOfPages();
        for (int i = 0; i < pageCount; i++)
        {
            if (!PageHasAnnotations(i)) continue;

            var pdfPage = pdfDoc.GetPage(i + 1);
            var mediaBox = pdfPage.GetMediaBox();
            float pageWidth = mediaBox.GetWidth();
            float pageHeight = mediaBox.GetHeight();

            int pixW = (int)Math.Ceiling(pageWidth * renderZoom);
            int pixH = (int)Math.Ceiling(pageHeight * renderZoom);
            using var overlayData = RenderAnnotationsOnly(i, renderZoom, pixW, pixH);
            if (overlayData != null)
            {
                byte[] pngBytes = overlayData.ToArray();
                var imageData = ImageDataFactory.Create(pngBytes);
                var xObject = new iText.Kernel.Pdf.Xobject.PdfImageXObject(imageData);
                var pdfCanvas = new PdfCanvas(pdfPage);
                pdfCanvas.AddXObjectWithTransformationMatrix(xObject,
                    pageWidth, 0, 0, pageHeight,
                    mediaBox.GetLeft(), mediaBox.GetBottom());
            }
        }

        return outputPath;
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

    /// <summary>
    /// Adds native PDF annotations for all supported types so they remain
    /// editable in other PDF viewers (Acrobat, Acroplot, etc.).
    /// Only revision clouds and measurements have no native equivalent.
    /// </summary>
    private void AddNativeAnnotationsToPage(iText.Kernel.Pdf.PdfPage pdfPage, int page, float pageHeight)
    {
        // Ink strokes → PdfInkAnnotation
        var strokes = MuPDFRenderer.GetStrokes(page);
        foreach (var stroke in strokes)
        {
            if (stroke.Points.Count < 2) continue;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            var pdfPoints = new List<float>();
            foreach (var pt in stroke.Points)
            {
                float px = (float)pt.X;
                float py = pageHeight - (float)pt.Y;
                pdfPoints.Add(px);
                pdfPoints.Add(py);
                minX = Math.Min(minX, px); minY = Math.Min(minY, py);
                maxX = Math.Max(maxX, px); maxY = Math.Max(maxY, py);
            }

            float pad = (float)stroke.Width + 2;
            var rect = new iText.Kernel.Geom.Rectangle(minX - pad, minY - pad,
                (maxX - minX) + pad * 2, (maxY - minY) + pad * 2);

            var inkList = new iText.Kernel.Pdf.PdfArray();
            var pointsArr = new iText.Kernel.Pdf.PdfArray();
            foreach (float v in pdfPoints) pointsArr.Add(new iText.Kernel.Pdf.PdfNumber(v));
            inkList.Add(pointsArr);

            var annot = new PdfInkAnnotation(rect, inkList);
            annot.SetColor(new iText.Kernel.Colors.DeviceRgb(stroke.Color.R, stroke.Color.G, stroke.Color.B));
            if (stroke.Opacity < 1.0)
                annot.Put(iText.Kernel.Pdf.PdfName.CA, new iText.Kernel.Pdf.PdfNumber(stroke.Opacity));

            var bs = new iText.Kernel.Pdf.PdfDictionary();
            bs.Put(iText.Kernel.Pdf.PdfName.W, new iText.Kernel.Pdf.PdfNumber(stroke.Width));
            bs.Put(iText.Kernel.Pdf.PdfName.S, iText.Kernel.Pdf.PdfName.S);
            annot.Put(new iText.Kernel.Pdf.PdfName("BS"), bs);

            annot.SetFlags(iText.Kernel.Pdf.Annot.PdfAnnotation.PRINT);
            pdfPage.AddAnnotation(annot);
        }

        // Shapes → native PDF annotations
        // RevisionCloud has no native equivalent and goes to overlay.
        var shapes = MuPDFRenderer.GetShapes(page);
        foreach (var shape in shapes)
        {
            if (shape.ShapeType == InlineAnnotationTool.RevisionCloud) continue;

            float sx = (float)shape.Start.X;
            float sy = pageHeight - (float)shape.Start.Y;
            float ex = (float)shape.End.X;
            float ey = pageHeight - (float)shape.End.Y;

            float left = Math.Min(sx, ex), bottom = Math.Min(sy, ey);
            float right = Math.Max(sx, ex), top = Math.Max(sy, ey);
            float pad = (float)shape.StrokeWidth + 2;

            var color = new iText.Kernel.Colors.DeviceRgb(shape.Color.R, shape.Color.G, shape.Color.B);

            PdfAnnotation annot;
            switch (shape.ShapeType)
            {
                case InlineAnnotationTool.Rectangle:
                {
                    var rect = new iText.Kernel.Geom.Rectangle(left - pad, bottom - pad,
                        (right - left) + pad * 2, (top - bottom) + pad * 2);
                    var sq = new PdfSquareAnnotation(rect);
                    sq.SetColor(color);
                    if (shape.IsFilled)
                    {
                        sq.Put(new iText.Kernel.Pdf.PdfName("IC"),
                            new iText.Kernel.Pdf.PdfArray(new float[]
                                { shape.Color.R / 255f, shape.Color.G / 255f, shape.Color.B / 255f }));
                        // /ca = non-stroke (fill) opacity; matches in-app alpha 80/255
                        sq.Put(new iText.Kernel.Pdf.PdfName("ca"),
                            new iText.Kernel.Pdf.PdfNumber(80.0 / 255.0));
                    }
                    annot = sq;
                    break;
                }
                case InlineAnnotationTool.Ellipse:
                {
                    var rect = new iText.Kernel.Geom.Rectangle(left - pad, bottom - pad,
                        (right - left) + pad * 2, (top - bottom) + pad * 2);
                    var circ = new PdfCircleAnnotation(rect);
                    circ.SetColor(color);
                    if (shape.IsFilled)
                    {
                        circ.Put(new iText.Kernel.Pdf.PdfName("IC"),
                            new iText.Kernel.Pdf.PdfArray(new float[]
                                { shape.Color.R / 255f, shape.Color.G / 255f, shape.Color.B / 255f }));
                        circ.Put(new iText.Kernel.Pdf.PdfName("ca"),
                            new iText.Kernel.Pdf.PdfNumber(80.0 / 255.0));
                    }
                    annot = circ;
                    break;
                }
                case InlineAnnotationTool.Line:
                {
                    var rect = new iText.Kernel.Geom.Rectangle(left - pad, bottom - pad,
                        (right - left) + pad * 2, (top - bottom) + pad * 2);
                    var line = new PdfLineAnnotation(rect,
                        new float[] { sx, sy, ex, ey });
                    line.SetColor(color);
                    annot = line;
                    break;
                }
                case InlineAnnotationTool.Arrow:
                {
                    var rect = new iText.Kernel.Geom.Rectangle(left - pad, bottom - pad,
                        (right - left) + pad * 2, (top - bottom) + pad * 2);
                    var line = new PdfLineAnnotation(rect,
                        new float[] { sx, sy, ex, ey });
                    line.SetColor(color);
                    // /LE [/None /OpenArrow] = arrowhead at the endpoint
                    var le = new iText.Kernel.Pdf.PdfArray();
                    le.Add(iText.Kernel.Pdf.PdfName.None);
                    le.Add(new iText.Kernel.Pdf.PdfName("OpenArrow"));
                    line.Put(new iText.Kernel.Pdf.PdfName("LE"), le);
                    annot = line;
                    break;
                }
                default:
                    continue;
            }

            if (shape.Opacity < 1.0)
                annot.Put(iText.Kernel.Pdf.PdfName.CA, new iText.Kernel.Pdf.PdfNumber(shape.Opacity));

            var shapeBs = new iText.Kernel.Pdf.PdfDictionary();
            shapeBs.Put(iText.Kernel.Pdf.PdfName.W, new iText.Kernel.Pdf.PdfNumber(shape.StrokeWidth));
            shapeBs.Put(iText.Kernel.Pdf.PdfName.S, iText.Kernel.Pdf.PdfName.S);
            annot.Put(new iText.Kernel.Pdf.PdfName("BS"), shapeBs);

            annot.SetFlags(iText.Kernel.Pdf.Annot.PdfAnnotation.PRINT);
            pdfPage.AddAnnotation(annot);
        }

        // Text annotations:
        //   Sticky notes → PdfTextAnnotation  (/Subtype /Text)  — icon, popup on click.
        //   Plain text   → PdfFreeTextAnnotation                — visible text box.
        //   Arrow text   → PdfFreeTextAnnotation /IT /FreeTextCallout — callout box.
        var texts = MuPDFRenderer.GetTexts(page);
        if (texts.Count > 0)
        {
            var helvetica = iText.Kernel.Font.PdfFontFactory.CreateFont(
                iText.IO.Font.Constants.StandardFonts.HELVETICA);

            foreach (var t in texts)
            {
                float tpx = (float)t.Position.X;
                float tpy = pageHeight - (float)t.Position.Y;
                float fontSize = (float)t.FontSize;
                var color = new iText.Kernel.Colors.DeviceRgb(t.Color.R, t.Color.G, t.Color.B);

                // ── Sticky notes → native comment icon (unchanged) ───────────────────
                if (t.IsStickyNote)
                {
                    var noteRect = new iText.Kernel.Geom.Rectangle(tpx - 9, tpy - 9, 18, 18);
                    var note = new iText.Kernel.Pdf.Annot.PdfTextAnnotation(noteRect);
                    note.SetIconName(new iText.Kernel.Pdf.PdfName("Comment"));
                    note.SetColor(color);
                    note.SetContents(new iText.Kernel.Pdf.PdfString(t.Text));
                    note.SetOpen(false);
                    if (t.Opacity < 1.0)
                        note.Put(iText.Kernel.Pdf.PdfName.CA,
                            new iText.Kernel.Pdf.PdfNumber(t.Opacity));
                    var popupRect = new iText.Kernel.Geom.Rectangle(tpx + 20, tpy - 10, 220, 100);
                    var popup = new iText.Kernel.Pdf.Annot.PdfPopupAnnotation(popupRect);
                    popup.SetContents(new iText.Kernel.Pdf.PdfString(t.Text));
                    note.SetPopup(popup);
                    popup.SetParent(note);
                    note.SetFlags(iText.Kernel.Pdf.Annot.PdfAnnotation.PRINT);
                    pdfPage.AddAnnotation(popup);
                    pdfPage.AddAnnotation(note);
                    continue;
                }

                // Measure text to compute annotation rect
                string[] textLines = t.Text.Split('\n');
                float lineHeight = fontSize * 1.3f;
                float maxW = 0;
                foreach (var ln in textLines)
                    maxW = Math.Max(maxW, helvetica.GetWidth(ln, fontSize));
                maxW = Math.Max(maxW, fontSize * 2);
                float totalH = textLines.Length * lineHeight;
                float tpad = 5;
                float boxL = tpx - tpad;
                float boxB = tpy - totalH - tpad;
                float boxW = maxW + tpad * 2;
                float boxH = totalH + tpad * 2;

                // /DA default appearance: Helvetica at the annotation font size and colour
                float dr = t.Color.R / 255f, dg = t.Color.G / 255f, db = t.Color.B / 255f;
                string da = FormattableString.Invariant(
                    $"/Helv {fontSize:F1} Tf {dr:F3} {dg:F3} {db:F3} rg");

                iText.Kernel.Pdf.Annot.PdfFreeTextAnnotation fta;

                if (!t.ArrowOrigin.HasValue)
                {
                    // ── Plain text → visible FreeText box ───────────────────────────
                    fta = new iText.Kernel.Pdf.Annot.PdfFreeTextAnnotation(
                        new iText.Kernel.Geom.Rectangle(boxL, boxB, boxW, boxH),
                        new iText.Kernel.Pdf.PdfString(t.Text));
                }
                else
                {
                    // ── Arrow text → FreeText callout ────────────────────────────────
                    float ax = (float)t.ArrowOrigin.Value.X;
                    float ay = pageHeight - (float)t.ArrowOrigin.Value.Y;

                    // Outer rect must encompass both the text box and the arrow tip
                    float fullL = Math.Min(boxL, ax) - tpad;
                    float fullB = Math.Min(boxB, ay) - tpad;
                    float fullR = Math.Max(boxL + boxW, ax) + tpad;
                    float fullT = Math.Max(boxB + boxH, ay) + tpad;

                    fta = new iText.Kernel.Pdf.Annot.PdfFreeTextAnnotation(
                        new iText.Kernel.Geom.Rectangle(fullL, fullB, fullR - fullL, fullT - fullB),
                        new iText.Kernel.Pdf.PdfString(t.Text));
                    fta.Put(new iText.Kernel.Pdf.PdfName("IT"),
                        new iText.Kernel.Pdf.PdfName("FreeTextCallout"));

                    // CL: [arrowTipX, arrowTipY, attachX, attachY] in PDF user space
                    float midX = boxL + boxW / 2f, midY = boxB + boxH / 2f;
                    float dL = Math.Abs(ax - boxL),       dR = Math.Abs(ax - (boxL + boxW));
                    float dB = Math.Abs(ay - boxB),       dT = Math.Abs(ay - (boxB + boxH));
                    float minD = Math.Min(Math.Min(dL, dR), Math.Min(dB, dT));
                    float attX = (minD == dL) ? boxL : (minD == dR) ? boxL + boxW : midX;
                    float attY = (minD == dB) ? boxB : (minD == dT) ? boxB + boxH : midY;
                    fta.Put(new iText.Kernel.Pdf.PdfName("CL"),
                        new iText.Kernel.Pdf.PdfArray(new float[] { ax, ay, attX, attY }));

                    // OpenArrow at the tip, nothing at the text-box end
                    var le = new iText.Kernel.Pdf.PdfArray();
                    le.Add(new iText.Kernel.Pdf.PdfName("OpenArrow"));
                    le.Add(iText.Kernel.Pdf.PdfName.None);
                    fta.Put(new iText.Kernel.Pdf.PdfName("LE"), le);

                    // RD: insets from outer rect to the inner text-rendering rect
                    fta.Put(new iText.Kernel.Pdf.PdfName("RD"),
                        new iText.Kernel.Pdf.PdfArray(new float[] {
                            boxL - fullL, boxB - fullB,
                            fullR - (boxL + boxW), fullT - (boxB + boxH) }));
                }

                // Properties shared by plain and callout FreeText annotations
                fta.Put(new iText.Kernel.Pdf.PdfName("DA"),
                    new iText.Kernel.Pdf.PdfString(da));
                fta.Put(new iText.Kernel.Pdf.PdfName("Q"),
                    new iText.Kernel.Pdf.PdfNumber(0));  // left-align
                fta.SetColor(color);
                var fbs = new iText.Kernel.Pdf.PdfDictionary();
                fbs.Put(iText.Kernel.Pdf.PdfName.W, new iText.Kernel.Pdf.PdfNumber(1.2));
                fbs.Put(iText.Kernel.Pdf.PdfName.S, iText.Kernel.Pdf.PdfName.S);
                fta.Put(new iText.Kernel.Pdf.PdfName("BS"), fbs);
                fta.Put(new iText.Kernel.Pdf.PdfName("IC"),
                    new iText.Kernel.Pdf.PdfArray(new float[] { 1f, 1f, 1f }));  // white fill
                if (t.Opacity < 1.0)
                    fta.Put(iText.Kernel.Pdf.PdfName.CA,
                        new iText.Kernel.Pdf.PdfNumber(t.Opacity));
                fta.SetFlags(iText.Kernel.Pdf.Annot.PdfAnnotation.PRINT);
                pdfPage.AddAnnotation(fta);
            }
        }
    }

    /// <summary>
    /// Returns true if the page has annotation types that have no native PDF
    /// equivalent (revision clouds and measurements only).
    /// </summary>
    private bool PageHasNativeOverlayAnnotations(int page)
    {
        if (MuPDFRenderer.GetMeasurements(page).Count > 0) return true;
        foreach (var shape in MuPDFRenderer.GetShapes(page))
        {
            if (shape.ShapeType == InlineAnnotationTool.RevisionCloud)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Draws revision clouds and measurements onto the given SkiaSharp canvas.
    /// These are the only annotation types without native PDF equivalents.
    /// </summary>
    private void DrawNativeOverlay(SKCanvas canvas, int page, double renderZoom)
    {
        // Revision clouds
        var shapes = MuPDFRenderer.GetShapes(page);
        foreach (var shape in shapes)
        {
            if (shape.ShapeType != InlineAnnotationTool.RevisionCloud) continue;

            using var paint = new SKPaint
            {
                Color = shape.Opacity < 1.0
                    ? new SKColor(shape.Color.R, shape.Color.G, shape.Color.B, (byte)(shape.Opacity * 255))
                    : new SKColor(shape.Color.R, shape.Color.G, shape.Color.B, shape.Color.A),
                StrokeWidth = (float)(shape.StrokeWidth * renderZoom),
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true
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

            var cloudPath = RenderSkiaCloudPath(sx, sy, ex, ey, renderZoom);
            if (fillPaint != null)
                canvas.DrawPath(cloudPath, fillPaint);
            canvas.DrawPath(cloudPath, paint);
            fillPaint?.Dispose();
        }

        // Measurements
        var measurements = MuPDFRenderer.GetMeasurements(page);
        foreach (var m in measurements)
        {
            using var mPaint = new SKPaint
            {
                Color = new SKColor(m.Color.R, m.Color.G, m.Color.B, 220),
                StrokeWidth = (float)(1.5 * renderZoom),
                Style = SKPaintStyle.Stroke,
                PathEffect = SKPathEffect.CreateDash([8f * (float)renderZoom, 6f * (float)renderZoom], 0),
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = true
            };

            var pts = m.Points;
            if (pts.Count >= 2)
            {
                float x0 = (float)(pts[0].X * renderZoom), y0 = (float)(pts[0].Y * renderZoom);
                float x1 = (float)(pts[1].X * renderZoom), y1 = (float)(pts[1].Y * renderZoom);
                canvas.DrawLine(x0, y0, x1, y1, mPaint);

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
            }

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
    /// Renders only native-overlay annotations (revision clouds + measurements)
    /// onto a transparent surface for the native export path.
    /// </summary>
    private SKData? RenderNativeOverlayOnly(int page, double renderZoom, int pixelWidth, int pixelHeight)
    {
        using var surface = SKSurface.Create(new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface == null) return null;
        surface.Canvas.Clear(SKColors.Transparent);
        DrawNativeOverlay(surface.Canvas, page, renderZoom);
        using var image = surface.Snapshot();
        return image.Encode(SKEncodedImageFormat.Png, 100);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 60 ? name[..60] : name;
    }

    #endregion
}