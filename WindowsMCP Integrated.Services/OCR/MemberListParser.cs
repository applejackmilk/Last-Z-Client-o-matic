using System;
using System.Collections.Generic;
using System.Text;

namespace WindowsMCP_Integrated.OCR;

using System.Text.RegularExpressions;
using NLog;
using TesseractOCR.Enums;

public sealed class MemberInfo
{
    public string? Group { get; init; }     // "R4", "R3" (null if the header is scrolled off-screen)
    public string Name { get; init; } = "";
    public int Hq { get; init; }
    public string Power { get; init; } = ""; // e.g. "203.1M"
    public string LastSeen { get; init; } = ""; // "Online" or "16min ago"
}

public sealed class MemberListParser : ITableParser<MemberInfo>
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private static readonly Regex HeaderRegex = new(@"^R\d+$");
    private static readonly Regex StatusRegex = new(@"^(Online|\d+\s*\w+ ago)$");
    private static readonly Regex PowerRegex = new(@"^\d+(\.\d+)?M$");
    private static readonly Regex HqRegex = new(@"^\d{1,2}$");

    private const double HeaderMaxCenterX = 0.30; // group label sits at the far left of the header bar
    private const double ColumnSplitX = 0.50; // two cells per row
    private const double SameRowDy = 0.025; // fraction of width: boxes within this are on one text line
    private const double MaxHqWidth = 0.09;  // HQ number is narrow, which keeps names like "DRAC29" from matching

    private static readonly OcrOptions HeaderOcr = new(BinarizeMode.WhiteFill, "R" + TextCleaning.DigitChars);
    private static readonly OcrOptions StatusOcr = new(BinarizeMode.Auto, TextCleaning.Letters + TextCleaning.DigitChars + " ");
    private static readonly OcrOptions PowerOcr = new(BinarizeMode.DarkText, TextCleaning.DigitChars + ".M");
    private static readonly OcrOptions NameOcr = new(BinarizeMode.DarkText, TextCleaning.NameChars + "'’");

    private enum Kind { Header, Status, Power, Hq, Other }
    private sealed record Item(TextBox Box, Kind Kind, string Text);

    public IReadOnlyList<MemberInfo> Parse(TableContext ctx)
    {
        var items = ctx.Boxes.Select(b => Classify(ctx, b)).ToList();
        foreach (var it in items) ctx.Label(it.Box, it.Kind.ToString());

        var headers = items.Where(i => i.Kind == Kind.Header).OrderBy(i => i.Box.CenterY).ToList();
        var cells = new List<(double Y, int Col, MemberInfo Member)>();

        for (int col = 0; col < 2; col++)
        {
            var colItems = items.Where(i => ColumnOf(i.Box, ctx.Width) == col).ToList();
            double prevBottom = double.MinValue;

            foreach (var status in colItems.Where(i => i.Kind == Kind.Status).OrderBy(i => i.Box.CenterY))
            {
                // The cell is everything in this column between the previous cell/header and this status line
                double headerBottom = headers
                    .Where(h => h.Box.CenterY < status.Box.CenterY)
                    .Select(h => (double)h.Box.Bounds.Bottom)
                    .DefaultIfEmpty(double.MinValue).Max();
                double top = Math.Max(prevBottom, headerBottom);
                prevBottom = status.Box.Bounds.Bottom;

                var cell = colItems.Where(i => i.Box.CenterY > top && i.Box.CenterY < status.Box.CenterY).ToList();
                var member = TryBuild(ctx, cell, status, headers);
                if (member != null) cells.Add((status.Box.CenterY, col, member));
            }
        }

        // Reading order: top to bottom, left cell before right cell
        double tol = SameRowDy * ctx.Width;
        var sorted = cells.OrderBy(c => c.Y).ToList();
        var result = new List<MemberInfo>();
        for (int i = 0; i < sorted.Count;)
        {
            int j = i + 1;
            while (j < sorted.Count && sorted[j].Y - sorted[i].Y < tol) j++;
            result.AddRange(sorted.Skip(i).Take(j - i).OrderBy(c => c.Col).Select(c => c.Member));
            i = j;
        }
        return result;
    }

    private MemberInfo? TryBuild(TableContext ctx, List<Item> cell, Item status, List<Item> headers)
    {
        double tol = SameRowDy * ctx.Width;

        // Power: the lowest power box above the status line
        var power = cell.Where(i => i.Kind == Kind.Power).OrderByDescending(i => i.Box.CenterY).FirstOrDefault();
        if (power == null)
        {
            _logger.Trace($"Cell skipped, no power found above status \"{status.Text}\" {status.Box}");
            return null;
        }

        // HQ: narrow number left of the power, same line
        var hq = cell.Where(i => i.Kind == Kind.Hq
                              && i.Box.CenterX < power.Box.CenterX
                              && Math.Abs(i.Box.CenterY - power.Box.CenterY) < tol)
                     .OrderBy(i => power.Box.CenterX - i.Box.CenterX)
                     .FirstOrDefault();
        if (hq == null || !int.TryParse(hq.Text, out int hqLevel))
        {
            _logger.Trace($"Cell skipped, no HQ number next to power {power.Box}");
            return null;
        }

        // Name: the nearest text line above the power line. If several boxes share that line
        // (gender icon, stray avatar boxes), take the widest.
        var above = cell.Where(i => i.Kind == Kind.Other
                                 && i.Box.CenterY < power.Box.CenterY - power.Box.Bounds.Height * 0.5).ToList();
        if (above.Count == 0)
        {
            _logger.Trace($"Cell skipped, no name above power {power.Box}");
            return null;
        }
        double nameY = above.Max(i => i.Box.CenterY);
        var nameItem = above.Where(i => Math.Abs(i.Box.CenterY - nameY) < tol)
                            .OrderByDescending(i => i.Box.Bounds.Width).First();

        // Don't build cells that are cut off by the image edge
        if (new[] { nameItem, hq, power, status }.Any(i => ctx.IsClipped(i.Box)))
        {
            _logger.Trace($"Cell skipped, clipped: name={nameItem.Box} status={status.Box}");
            return null;
        }

        string name = TextCleaning.Name(ctx.Ocr(nameItem.Box, NameOcr), @"A-Za-z0-9 ?'’");
        if (name.Length == 0)
        {
            _logger.Trace($"Cell skipped, unreadable name {nameItem.Box}");
            return null;
        }

        string? group = headers.Where(h => h.Box.CenterY < nameItem.Box.Bounds.Top)
                               .OrderBy(h => h.Box.CenterY).LastOrDefault()?.Text;

        _logger.Trace($"Member: Group={group} Name=\"{name}\" HQ={hqLevel} Power={power.Text} LastSeen=\"{status.Text}\" | " +
                      $"name={nameItem.Box} hq={hq.Box} power={power.Box} status={status.Box}");

        return new MemberInfo { Group = group, Name = name, Hq = hqLevel, Power = power.Text, LastSeen = status.Text };
    }

    private static int ColumnOf(TextBox b, int width) => b.CenterX < ColumnSplitX * width ? 0 : 1;

    private static Item Classify(TableContext ctx, TextBox box)
    {
        if (box.CenterX < HeaderMaxCenterX * ctx.Width)
        {
            string h = TextCleaning.NoSpaces(ctx.Ocr(box, HeaderOcr));
            if (HeaderRegex.IsMatch(h)) return new Item(box, Kind.Header, h);
        }

        string s = TextCleaning.Compact(ctx.Ocr(box, StatusOcr));
        if (StatusRegex.IsMatch(s)) return new Item(box, Kind.Status, s);

        string p = TextCleaning.NoSpaces(ctx.Ocr(box, PowerOcr));
        if (PowerRegex.IsMatch(p)) return new Item(box, Kind.Power, p);
        if (box.Bounds.Width < MaxHqWidth * ctx.Width && HqRegex.IsMatch(p)) return new Item(box, Kind.Hq, p);

        return new Item(box, Kind.Other, "");
    }

    public string[] GetCsvHeaders() => throw new NotImplementedException();
}