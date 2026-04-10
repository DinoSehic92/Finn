using MuPDFCore;
using MuPDFCore.StructuredText;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Finn.Services
{
    public static class TextDiffDiagnostics
    {
        private record struct ZoneWord(string Raw, string Norm, double Y);

        public static string RunDiagnostics(string pdfPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== TextDiff Stripping Diagnostics ===");
            sb.AppendLine($"File: {pdfPath}");
            sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            using var ctx = new MuPDFContext();
            using var doc = new MuPDFDocument(ctx, pdfPath);
            int pageCount = doc.Pages.Count;
            sb.AppendLine($"Page count: {pageCount}");

            var pageBounds = new (double MinY, double MaxY)[pageCount];
            for (int i = 0; i < pageCount; i++)
            {
                var b = doc.Pages[i].Bounds;
                pageBounds[i] = (Math.Min(b.Y0, b.Y1), Math.Max(b.Y0, b.Y1));
                if (i < 5 || i == pageCount - 1)
                    sb.AppendLine($"  Page {i}: bounds Y=[{b.Y0:F1}, {b.Y1:F1}] size={b.Width:F1}x{b.Height:F1}");
            }
            sb.AppendLine();

            // Extract all words per page
            var allPageWords = new List<ZoneWord>[pageCount];
            for (int i = 0; i < pageCount; i++)
            {
                allPageWords[i] = [];
                using var disposable = doc.GetStructuredTextPage(i);
                var page = (MuPDFStructuredTextPage)disposable;
                foreach (var block in page)
                    foreach (var line in block)
                    {
                        var chars = line.Characters;
                        if (chars == null || chars.Length == 0) continue;
                        int wordStart = -1;
                        double minY = double.MaxValue;
                        double maxY = double.MinValue;
                        var wc = new StringBuilder();
                        for (int ci = 0; ci < chars.Length; ci++)
                        {
                            var ch = chars[ci];
                            if (char.IsWhiteSpace(ch.Character, 0))
                            {
                                if (wordStart >= 0)
                                {
                                    string raw = wc.ToString();
                                    allPageWords[i].Add(new ZoneWord(raw, Normalize(raw), (minY + maxY) / 2));
                                    wc.Clear(); wordStart = -1;
                                    minY = double.MaxValue;
                                    maxY = double.MinValue;
                                }
                            }
                            else
                            {
                                if (wordStart < 0) wordStart = ci;
                                wc.Append(ch.Character);
                                var q = ch.BoundingQuad;
                                double y0 = Math.Min(Math.Min(q.UpperLeft.Y, q.LowerLeft.Y), Math.Min(q.UpperRight.Y, q.LowerRight.Y));
                                double y1 = Math.Max(Math.Max(q.UpperLeft.Y, q.LowerLeft.Y), Math.Max(q.UpperRight.Y, q.LowerRight.Y));
                                if (y0 < minY) minY = y0;
                                if (y1 > maxY) maxY = y1;
                            }
                        }
                        if (wordStart >= 0)
                        {
                            string raw = wc.ToString();
                            allPageWords[i].Add(new ZoneWord(raw, Normalize(raw), (minY + maxY) / 2));
                        }
                    }
            }

            // Build zones
            const double zoneFraction = 0.18;
            int threshold = Math.Max(2, (int)(pageCount * 0.5));
            sb.AppendLine($"Threshold: {threshold} pages (50% of {pageCount})");
            sb.AppendLine();

            var headerZones = new List<ZoneWord>[pageCount];
            var footerZones = new List<ZoneWord>[pageCount];

            for (int p = 0; p < pageCount; p++)
            {
                double pMinY = pageBounds[p].MinY;
                double pMaxY = pageBounds[p].MaxY;
                double range = pMaxY - pMinY;
                double hCut = pMinY + range * zoneFraction;
                double fCut = pMaxY - range * zoneFraction;

                headerZones[p] = [];
                footerZones[p] = [];
                foreach (var w in allPageWords[p])
                {
                    if (w.Y <= hCut) headerZones[p].Add(w);
                    if (w.Y >= fCut) footerZones[p].Add(w);
                }
                headerZones[p].Sort((a, b) =>
                {
                    double bandA = Math.Round(a.Y / 3.0);
                    double bandB = Math.Round(b.Y / 3.0);
                    int cmp = bandA.CompareTo(bandB);
                    return cmp != 0 ? cmp : string.Compare(a.Norm, b.Norm, StringComparison.OrdinalIgnoreCase);
                });
                footerZones[p].Sort((a, b) =>
                {
                    double bandA = Math.Round(a.Y / 3.0);
                    double bandB = Math.Round(b.Y / 3.0);
                    int cmp = bandA.CompareTo(bandB);
                    return cmp != 0 ? cmp : string.Compare(a.Norm, b.Norm, StringComparison.OrdinalIgnoreCase);
                });
            }

            // Dump zones for key pages
            DumpZones(sb, "HEADER", headerZones, pageCount);
            DumpZones(sb, "FOOTER", footerZones, pageCount);

            // Fingerprint — separate start/end dictionaries (matches TextDiffService)
            var hStartFPs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var hEndFPs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var fStartFPs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var fEndFPs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int p = 0; p < pageCount; p++)
            {
                AddFPStart(headerZones[p], hStartFPs);
                AddFPEnd(headerZones[p], hEndFPs);
                AddFPStart(footerZones[p], fStartFPs);
                AddFPEnd(footerZones[p], fEndFPs);
            }

            string? hFromStart = FindBest(hStartFPs, threshold);
            string? hFromEnd = FindBest(hEndFPs, threshold);
            string? fFromStart = FindBest(fStartFPs, threshold);
            string? fFromEnd = FindBest(fEndFPs, threshold);

            // Dedup
            if (hFromStart != null && hFromEnd != null)
            {
                if (hFromStart.Contains(hFromEnd, StringComparison.OrdinalIgnoreCase)) hFromEnd = null;
                else if (hFromEnd.Contains(hFromStart, StringComparison.OrdinalIgnoreCase)) hFromStart = null;
            }
            if (fFromStart != null && fFromEnd != null)
            {
                if (fFromStart.Contains(fFromEnd, StringComparison.OrdinalIgnoreCase)) fFromEnd = null;
                else if (fFromEnd.Contains(fFromStart, StringComparison.OrdinalIgnoreCase)) fFromStart = null;
            }

            sb.AppendLine("=== FINGERPRINT RESULTS ===");
            sb.AppendLine($"Header start-FPs above threshold: {CountAbove(hStartFPs, threshold)}");
            sb.AppendLine($"Header end-FPs above threshold: {CountAbove(hEndFPs, threshold)}");
            sb.AppendLine($"Footer start-FPs above threshold: {CountAbove(fStartFPs, threshold)}");
            sb.AppendLine($"Footer end-FPs above threshold: {CountAbove(fEndFPs, threshold)}");
            sb.AppendLine();

            sb.AppendLine("Top 5 HEADER from-start:");
            foreach (var item in TopN(hStartFPs, 5))
                sb.AppendLine($"  count={item.Count} len={item.FP.Length} {(item.Count >= threshold ? "PASS" : "FAIL")} \"{Trunc(item.FP, 80)}\"");
            sb.AppendLine("Top 5 HEADER from-end:");
            foreach (var item in TopN(hEndFPs, 5))
                sb.AppendLine($"  count={item.Count} len={item.FP.Length} {(item.Count >= threshold ? "PASS" : "FAIL")} \"{Trunc(item.FP, 80)}\"");
            sb.AppendLine("Top 5 FOOTER from-end:");
            foreach (var item in TopN(fEndFPs, 5))
                sb.AppendLine($"  count={item.Count} len={item.FP.Length} {(item.Count >= threshold ? "PASS" : "FAIL")} \"{Trunc(item.FP, 80)}\"");
            sb.AppendLine();

            sb.AppendLine($"Header from-start: {(hFromStart == null ? "(none)" : $"len={hFromStart.Length} count={hStartFPs.GetValueOrDefault(hFromStart)} \"{Trunc(hFromStart, 100)}\"")}");
            sb.AppendLine($"Header from-end:   {(hFromEnd == null ? "(none/deduped)" : $"len={hFromEnd.Length} count={hEndFPs.GetValueOrDefault(hFromEnd)} \"{Trunc(hFromEnd, 100)}\"")}");
            sb.AppendLine($"Footer from-start: {(fFromStart == null ? "(none/deduped)" : $"len={fFromStart.Length} count={fStartFPs.GetValueOrDefault(fFromStart)} \"{Trunc(fFromStart, 100)}\"")}");
            sb.AppendLine($"Footer from-end:   {(fFromEnd == null ? "(none)" : $"len={fFromEnd.Length} count={fEndFPs.GetValueOrDefault(fFromEnd)} \"{Trunc(fFromEnd, 100)}\"")}");
            sb.AppendLine();

            // Per-page strip test
            sb.AppendLine("=== PER-PAGE STRIPPING TEST ===");
            int hOk = 0, hFail = 0, fOk = 0, fFail = 0;
            bool anyH = hFromStart != null || hFromEnd != null;
            bool anyF = fFromStart != null || fFromEnd != null;
            for (int p = 0; p < pageCount; p++)
            {
                bool hStrip = false;
                if (anyH && headerZones[p].Count > 0)
                {
                    if (hFromStart != null && (TestAccum(headerZones[p], hFromStart, false) || TestAccum(headerZones[p], hFromStart, true)))
                        hStrip = true;
                    if (hFromEnd != null && (TestAccum(headerZones[p], hFromEnd, true) || TestAccum(headerZones[p], hFromEnd, false)))
                        hStrip = true;
                }

                bool fStrip = false;
                if (anyF && footerZones[p].Count > 0)
                {
                    if (fFromEnd != null && (TestAccum(footerZones[p], fFromEnd, true) || TestAccum(footerZones[p], fFromEnd, false)))
                        fStrip = true;
                    if (fFromStart != null && (TestAccum(footerZones[p], fFromStart, false) || TestAccum(footerZones[p], fFromStart, true)))
                        fStrip = true;
                }

                if (hStrip) hOk++; else if (anyH) hFail++;
                if (fStrip) fOk++; else if (anyF) fFail++;

                bool problem = (anyH && !hStrip) || (anyF && !fStrip);
                string tag = (hStrip ? "H:ok" : anyH ? "H:FAIL" : "H:n/a")
                           + " " + (fStrip ? "F:ok" : anyF ? "F:FAIL" : "F:n/a");
                sb.AppendLine($"  Page {p,3}: {tag}");

                if (problem && (p < 5 || p >= pageCount - 1))
                {
                    if (anyH && !hStrip && headerZones[p].Count > 0)
                    {
                        if (hFromStart != null) DumpAccumDetail(sb, "Header(start)", headerZones[p], hFromStart, false);
                        if (hFromEnd != null) DumpAccumDetail(sb, "Header(end)", headerZones[p], hFromEnd, true);
                    }
                    if (anyF && !fStrip && footerZones[p].Count > 0)
                    {
                        if (fFromEnd != null) DumpAccumDetail(sb, "Footer(end)", footerZones[p], fFromEnd, true);
                    }
                }
            }
            sb.AppendLine();
            sb.AppendLine($"Summary: header stripped {hOk}/{hOk + hFail}, footer stripped {fOk}/{fOk + fFail}");

            string outPath = Path.Combine(Path.GetTempPath(), $"TextDiffDiag_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(outPath, sb.ToString());
            return outPath;
        }

        private static void DumpZones(StringBuilder sb, string label, List<ZoneWord>[] zones, int pageCount)
        {
            sb.AppendLine($"=== {label} ZONES (pages 0-4 + last) ===");
            for (int p = 0; p < pageCount; p++)
            {
                if (p >= 5 && p < pageCount - 1) continue;
                sb.AppendLine($"  Page {p} ({zones[p].Count} words):");
                foreach (var w in zones[p])
                    sb.AppendLine($"    Y={w.Y:F1} raw=\"{w.Raw}\" norm=\"{w.Norm}\" ds=\"{DS(w.Norm)}\"");
            }
            sb.AppendLine();
        }

        private static void DumpAccumDetail(StringBuilder sb, string label, List<ZoneWord> zone, string fp, bool fromEnd)
        {
            sb.AppendLine($"    {label} zone ({zone.Count} words), accumulating from {(fromEnd ? "END" : "START")}:");
            var acc = new StringBuilder();
            int limit = Math.Min(zone.Count, 15);
            if (fromEnd)
            {
                for (int i = zone.Count - 1; i >= Math.Max(0, zone.Count - limit); i--)
                {
                    string ds = DS(zone[i].Norm);
                    acc.Insert(0, ds);
                    sb.AppendLine($"      word[{i}] raw=\"{zone[i].Raw}\" ds=\"{ds}\" accLen={acc.Length} (need={fp.Length})");
                    if (acc.Length >= fp.Length) break;
                }
            }
            else
            {
                for (int i = 0; i < limit; i++)
                {
                    string ds = DS(zone[i].Norm);
                    acc.Append(ds);
                    sb.AppendLine($"      word[{i}] raw=\"{zone[i].Raw}\" ds=\"{ds}\" accLen={acc.Length} (need={fp.Length})");
                    if (acc.Length >= fp.Length) break;
                }
            }
            sb.AppendLine($"    Accumulated: \"{Trunc(acc.ToString(), 120)}\"");
            sb.AppendLine($"    Expected:    \"{Trunc(fp, 120)}\"");
            bool match = acc.Length == fp.Length && string.Equals(acc.ToString(), fp, StringComparison.OrdinalIgnoreCase);
            sb.AppendLine($"    Match: {match}");
        }

        private static string DS(string norm)
        {
            if (string.IsNullOrEmpty(norm)) return "";
            bool allDigits = true;
            foreach (char c in norm) if (!char.IsDigit(c)) { allDigits = false; break; }
            if (allDigits) return "";
            var sb = new StringBuilder(norm.Length);
            foreach (char c in norm) if (!char.IsDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = text.Normalize(NormalizationForm.FormC);
            var sb = new StringBuilder(text.Length + 4);
            foreach (char c in text)
            {
                switch (c) {
                    case '\uFB01': sb.Append("fi"); break;
                    case '\uFB02': sb.Append("fl"); break;
                    case '\uFB00': sb.Append("ff"); break;
                    case '\uFB03': sb.Append("ffi"); break;
                    case '\uFB04': sb.Append("ffl"); break;
                    default: if (char.IsLetterOrDigit(c)) sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static void AddFPStart(List<ZoneWord> zone, Dictionary<string, int> fps)
        {
            if (zone.Count == 0) return;
            int max = Math.Min(25, zone.Count);
            var sb = new StringBuilder();
            int prevLen = 0;
            for (int c = 1; c <= max; c++)
            {
                sb.Append(DS(zone[c - 1].Norm));
                if (sb.Length >= 4 && sb.Length > prevLen)
                    fps[sb.ToString()] = fps.GetValueOrDefault(sb.ToString()) + 1;
                prevLen = sb.Length;
            }
        }

        private static void AddFPEnd(List<ZoneWord> zone, Dictionary<string, int> fps)
        {
            if (zone.Count == 0) return;
            int max = Math.Min(25, zone.Count);
            var sb = new StringBuilder();
            int prevLen = 0;
            for (int c = 1; c <= max; c++)
            {
                sb.Insert(0, DS(zone[zone.Count - c].Norm));
                if (sb.Length >= 4 && sb.Length > prevLen)
                    fps[sb.ToString()] = fps.GetValueOrDefault(sb.ToString()) + 1;
                prevLen = sb.Length;
            }
        }

        private static string? FindBest(Dictionary<string, int> fps, int thr)
        {
            string? best = null; int bLen = 0, bCnt = 0;
            foreach (var (fp, cnt) in fps)
            {
                if (cnt < thr) continue;
                // Highest count first, then longest as tiebreaker
                if (cnt > bCnt || (cnt == bCnt && fp.Length > bLen))
                { bLen = fp.Length; bCnt = cnt; best = fp; }
            }
            return best;
        }

        private static bool TestAccum(List<ZoneWord> zone, string fp, bool fromEnd)
        {
            var sb = new StringBuilder();
            if (fromEnd)
            {
                for (int i = zone.Count - 1; i >= 0; i--)
                {
                    sb.Insert(0, DS(zone[i].Norm));
                    if (sb.Length == fp.Length) return string.Equals(sb.ToString(), fp, StringComparison.OrdinalIgnoreCase);
                    if (sb.Length > fp.Length) return false;
                }
            }
            else
            {
                for (int i = 0; i < zone.Count; i++)
                {
                    sb.Append(DS(zone[i].Norm));
                    if (sb.Length == fp.Length) return string.Equals(sb.ToString(), fp, StringComparison.OrdinalIgnoreCase);
                    if (sb.Length > fp.Length) return false;
                }
            }
            return false;
        }

        private static int CountAbove(Dictionary<string, int> fps, int thr)
        { int c = 0; foreach (var kv in fps) if (kv.Value >= thr) c++; return c; }

        private static List<(string FP, int Count)> TopN(Dictionary<string, int> fps, int n)
        {
            var list = new List<(string FP, int Count)>();
            foreach (var kv in fps) list.Add((kv.Key, kv.Value));
            list.Sort((a, b) => b.Count.CompareTo(a.Count) != 0 ? b.Count.CompareTo(a.Count) : b.FP.Length.CompareTo(a.FP.Length));
            return list.GetRange(0, Math.Min(n, list.Count));
        }

        private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "...";
    }
}
