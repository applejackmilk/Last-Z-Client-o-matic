using System;
using System.Collections.Generic;
using System.Text;

namespace WindowsMCP_Integrated.OCR;

using NLog;
using TesseractOCR.Enums;
using System.Text.RegularExpressions;

public sealed class IndividualPowerRank
{
    public int Rank { get; init; }
    public string Name { get; init; } = "";
    public string Score { get; init; } = "";
}

public sealed class IndividualPowerRankParser : ITableParser<IndividualPowerRank>
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    // Guessed from the VS layout. Tune once I can see the image.
    private const double RankMaxCenterX = 0.22;
    private const double NameMinLeftX = 0.22;
    private const double NameMaxLeftX = 0.50;
    private const double ScoreMinLeftX = 0.50;

    private static readonly Regex NameRegex = new(@"^[A-Za-z0-9 ?]+$");

    private static readonly OcrOptions NameOcr = new(BinarizeMode.DarkText, TextCleaning.NameChars);
    private static readonly OcrOptions RankOcr = new(BinarizeMode.Auto, TextCleaning.DigitChars, PageSegMode.SingleWord);
    private static readonly OcrOptions ScoreOcr = new(BinarizeMode.Auto, TextCleaning.DigitChars + ",");

    private enum Column { Rank, Name, Score, Other }

    public IReadOnlyList<IndividualPowerRank> Parse(TableContext ctx)
    {
        var columns = ctx.Boxes.ToDictionary(b => b, b => Classify(b, ctx.Width));
        foreach (var (box, col) in columns) ctx.Label(box, col.ToString());

        var names = ctx.Boxes.Where(b => columns[b] == Column.Name).OrderBy(b => b.CenterY).ToList();
        var ranks = ctx.Boxes.Where(b => columns[b] == Column.Rank).ToList();
        var scores = ctx.Boxes.Where(b => columns[b] == Column.Score).ToList();

        var result = new List<IndividualPowerRank>();

        for (int i = 0; i < names.Count; i++)
        {
            TextBox nameBox = names[i];
            if (ctx.IsClipped(nameBox))
            {
                _logger.Trace($"Row skipped, name clipped: {nameBox}");
                continue;
            }

            double top = nameBox.Bounds.Top;
            double bottom = i + 1 < names.Count ? names[i + 1].Bounds.Top : ctx.Height;

            TextBox? rankBox = TextCleaning.ClosestInBand(ranks, top, bottom, nameBox.CenterY);
            TextBox? scoreBox = TextCleaning.ClosestInBand(scores, top, bottom, nameBox.CenterY);
            if (rankBox == null || scoreBox == null)
            {
                _logger.Trace($"Row skipped, missing rank/score: name={nameBox}");
                continue;
            }

            string name = TextCleaning.Name(ctx.Ocr(nameBox, NameOcr));
            string rankText = TextCleaning.DigitsOnly(ctx.Ocr(rankBox, RankOcr));
            string scoreText = TextCleaning.DigitsOnly(ctx.Ocr(scoreBox, ScoreOcr));

            if (!NameRegex.IsMatch(name) || !int.TryParse(rankText, out int rank) || scoreText.Length == 0)
            {
                _logger.Trace($"Row skipped, unreadable: name=\"{name}\" rank=\"{rankText}\" score=\"{scoreText}\"");
                continue;
            }

            _logger.Trace($"Row: Rank={rank} Name=\"{name}\" Score={scoreText} | " +
                          $"rank={rankBox} name={nameBox} score={scoreBox}");

            result.Add(new IndividualPowerRank { Rank = rank, Name = name, Score = scoreText });
        }

        return result.OrderBy(m => m.Rank).ToList();
    }

    private static Column Classify(TextBox b, int width)
    {
        double left = (double)b.Bounds.Left / width;
        double centerX = b.CenterX / width;

        if (centerX < RankMaxCenterX) return Column.Rank;
        if (left >= NameMinLeftX && left <= NameMaxLeftX) return Column.Name;
        if (left > ScoreMinLeftX) return Column.Score;
        return Column.Other;
    }

    public string[] GetCsvHeaders() => throw new NotImplementedException();
}
