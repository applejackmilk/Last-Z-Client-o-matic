using System;
using System.Collections.Generic;
using System.Text;

namespace WindowsMCP_Integrated.OCR;

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using NLog;
using OpenCvSharp;
using TesseractOCR.Enums;

public sealed class MemberVSRankParser : ITableParser<MemberVSRank>
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    // Layout as fractions of image width (tune using the debug image)
    private const double RankMaxCenterX = 0.22;
    private const double NameMinLeftX = 0.28;
    private const double NameMaxLeftX = 0.48;
    private const double ScoreMinLeftX = 0.50;
    private const double MaxNameToAllianceDy = 0.10;

    private static readonly Regex NameRegex = new(@"^[A-Za-z0-9 ?]+$");
    private static readonly Regex AllianceRegex = new(@"^\[[^\]]+\]\S.*$");

    private static readonly OcrOptions NameOcr = new(BinarizeMode.WhiteFill, TextCleaning.NameChars);
    private static readonly OcrOptions AllianceOcr = new(BinarizeMode.Auto, TextCleaning.NameChars + "[]");
    private static readonly OcrOptions RankOcr = new(BinarizeMode.Auto, TextCleaning.DigitChars, PageSegMode.SingleWord);
    private static readonly OcrOptions ScoreOcr = new(BinarizeMode.WhiteFill, TextCleaning.DigitChars + ",");

    private enum Column { Rank, Name, Score, Other }
    public string[] ConvertToStringArray(MemberVSRank item)
       => new string[] { item.Rank.ToString(), item.Name, item.AllianceName, item.Score.ToString("N0") };
    public List<string[]> ToStringTable(IReadOnlyList<MemberVSRank> list)
        => list.Select(ConvertToStringArray).ToList();
    public IEnumerable<MemberVSRank> Cleanup(IReadOnlyList<MemberVSRank> list)
    {
        int currentRank = 1;
        var byScore = list.OrderByDescending(l => l.Score).Distinct().ToList();
        for (int i = 0; i < byScore.Count; i++)
        {
            if (byScore[i].Rank != currentRank)
            {
                byScore[i].Rank = currentRank;
            }
            currentRank++;
            yield return byScore[i];
        }
    }
    public IReadOnlyList<MemberVSRank> Parse(TableContext ctx)
    {
        var columns = ctx.Boxes.ToDictionary(b => b, b => Classify(b, ctx.Width));
        foreach (var (box, col) in columns) ctx.Label(box, col.ToString());

        var nameColumn = ctx.Boxes.Where(b => columns[b] == Column.Name).OrderBy(b => b.CenterY).ToList();
        var ranks = ctx.Boxes.Where(b => columns[b] == Column.Rank).ToList();
        var scores = ctx.Boxes.Where(b => columns[b] == Column.Score).ToList();
        double maxDy = MaxNameToAllianceDy * ctx.Width;

        var members = new List<MemberVSRank>();

        // The name column alternates name, alliance, name, alliance...
        for (int i = 0; i < nameColumn.Count - 1; i++)
        {
            TextBox nameBox = nameColumn[i];
            TextBox allianceBox = nameColumn[i + 1];

            if (allianceBox.CenterY - nameBox.CenterY > maxDy)
            {
                _logger.Trace($"No alliance below, skipping partial row: {nameBox}");
                continue;
            }
            i++; // name + alliance consumed

            var member = TryBuild(ctx, nameBox, allianceBox, ranks, scores);
            if (member != null) members.Add(member);
        }

        return members.OrderBy(m => m.Rank).ToList();
    }

    private MemberVSRank? TryBuild(TableContext ctx, TextBox nameBox, TextBox allianceBox,
        List<TextBox> ranks, List<TextBox> scores)
    {
        // Rule 2: the full name and alliance must be visible
        if (ctx.IsClipped(nameBox) || ctx.IsClipped(allianceBox))
        {
            _logger.Trace($"Row skipped, clipped: name={nameBox} alliance={allianceBox}");
            return null;
        }

        string name = TextCleaning.Name(ctx.Ocr(nameBox, NameOcr));
        string alliance = Regex.Replace(TextCleaning.Compact(ctx.Ocr(allianceBox, AllianceOcr)),
                                        @"^(\[[^\]]+\])\s+", "$1");

        if (!NameRegex.IsMatch(name) || !AllianceRegex.IsMatch(alliance))
        {
            _logger.Trace($"Row skipped, unreadable: name=\"{name}\" alliance=\"{alliance}\"");
            return null;
        }

        // Rule 1: rank/score must sit at or below the top of the name (and above the alliance bottom)
        double top = nameBox.Bounds.Top, bottom = allianceBox.Bounds.Bottom, mid = (top + bottom) / 2;
        TextBox? rankBox = TextCleaning.ClosestInBand(ranks, top, bottom, mid);
        TextBox? scoreBox = TextCleaning.ClosestInBand(scores, top, bottom, mid);
        if (scoreBox == null)
        {
            _logger.Trace($"Row skipped, missing score: name=\"{name}\"");
            return null;
        }

        string rankText = rankBox == null ? "0" : TextCleaning.DigitsOnly(ctx.Ocr(rankBox, RankOcr));
        string scoreText = TextCleaning.DigitsOnly(ctx.Ocr(scoreBox, ScoreOcr));
        if (!int.TryParse(rankText, out int rank) || scoreText.Length == 0)
        {
            _logger.Trace($"Row skipped, unreadable rank/score: name=\"{name}\" rank=\"{rankText}\" score=\"{scoreText}\"");
            return null;
        }

        _logger.Trace($"Row: Rank={rank} Name=\"{name}\" Alliance=\"{alliance}\" Score={scoreText} | " +
                      $"rank={(rankBox == null ? "n/a" : rankBox)} name={nameBox} alliance={allianceBox} score={scoreBox}");

        return new MemberVSRank(rank, name, alliance, scoreText);

    }

    public string[] GetCsvHeaders()
        => new[] { "Rank", "Name", "AllianceName", "Score" };

    private static Column Classify(TextBox b, int width)
    {
        double left = (double)b.Bounds.Left / width;
        double centerX = b.CenterX / width;

        if (centerX < RankMaxCenterX) return Column.Rank;
        if (left >= NameMinLeftX && left <= NameMaxLeftX) return Column.Name;
        if (left > ScoreMinLeftX) return Column.Score;
        return Column.Other;
    }
}
