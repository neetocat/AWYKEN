using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Forms;
using UglyToad.PdfPig;

namespace AWYKEN.Shared.Services;

public sealed class CorScheduleImporter
{
    private const long MaxFileSize = 10 * 1024 * 1024;
    private static readonly Regex CourseCodePattern = new(
        @"\b[A-Z]{2,6}[ -]?\d{3,4}[A-Z]?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TimeRangePattern = new(
        @"\b(?<start>\d{1,2}(?::\d{2})?\s*(?:AM|PM)?)\s*(?:-|–|to)\s*(?<end>\d{1,2}(?::\d{2})?\s*(?:AM|PM)?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<CorImportResult> ImportAsync(IBrowserFile file, CancellationToken cancellationToken = default)
    {
        if (file.Size == 0)
            return CorImportResult.Failure("The selected file is empty.");

        if (file.Size > MaxFileSize)
            return CorImportResult.Failure("The file is larger than the 10 MB limit.");

        if (!string.Equals(Path.GetExtension(file.Name), ".pdf", StringComparison.OrdinalIgnoreCase))
            return CorImportResult.Failure("Please upload a PDF COR file.");

        await using var input = file.OpenReadStream(MaxFileSize, cancellationToken);
        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        try
        {
            using var document = PdfDocument.Open(buffer);
            var text = string.Join(Environment.NewLine, document.GetPages().Select(page => page.Text));
            var classes = ParseClasses(text);

            return classes.Count == 0
                ? CorImportResult.Failure("No class text was found. Use a text-based COR PDF or enter your classes manually.")
                : CorImportResult.Success(classes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return CorImportResult.Failure("The COR PDF could not be read. Make sure it is a valid PDF.");
        }
    }

    private static List<ScheduleItem> ParseClasses(string text)
    {
        var results = new List<ScheduleItem>();
        var id = 1;

        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = Regex.Replace(rawLine.Trim(), @"\s+", " ");
            var courseMatch = CourseCodePattern.Match(line);
            if (!courseMatch.Success)
                continue;

            var courseCode = courseMatch.Value.ToUpperInvariant().Replace(" ", "");
            var timeMatch = TimeRangePattern.Match(line);
            var hasTime = timeMatch.Success;
            var startTime = hasTime ? NormalizeTime(timeMatch.Groups["start"].Value) : "TIME NEEDS REVIEW";
            var endTime = hasTime ? NormalizeTime(timeMatch.Groups["end"].Value) : "";
            var remainder = line.Remove(courseMatch.Index, courseMatch.Length);
            if (hasTime)
                remainder = remainder.Remove(timeMatch.Index > courseMatch.Index ? timeMatch.Index - courseMatch.Length : 0, timeMatch.Length);

            var parts = remainder.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var title = CleanTitle(parts.FirstOrDefault() ?? remainder);
            if (string.IsNullOrWhiteSpace(title))
                title = courseCode;

            var location = parts.Skip(1).FirstOrDefault() ?? FindLocation(line) ?? "Location needs review";
            var online = line.Contains("online", StringComparison.OrdinalIgnoreCase)
                || line.Contains("google meet", StringComparison.OrdinalIgnoreCase)
                || line.Contains("zoom", StringComparison.OrdinalIgnoreCase);
            var mode = online ? "Online" : "Face-to-face";
            if (online && location == "Location needs review")
                location = line.Contains("zoom", StringComparison.OrdinalIgnoreCase) ? "Zoom" : "Google Meet";

            results.Add(new ScheduleItem(
                id++.ToString(), courseCode, title, startTime, endTime, location,
                "Instructor needs review", "IR", mode, GetColor(results.Count),
                online ? null : null));
        }

        return results
            .GroupBy(item => item.CourseCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static string CleanTitle(string value)
    {
        var title = Regex.Replace(value, @"^[\s:;,\-]+|[\s:;,\-]+$", "");
        title = Regex.Replace(title, @"\b(?:MON|TUE|WED|THU|FRI|SAT|SUN)(?:DAY)?\b", "", RegexOptions.IgnoreCase);
        return Regex.Replace(title, @"\s{2,}", " ").Trim();
    }

    private static string? FindLocation(string line)
    {
        var match = Regex.Match(line, @"(?:Room|Rm\.?|Building|Bldg\.?)\s*[A-Za-z0-9 -]+", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.Trim() : null;
    }

    private static string NormalizeTime(string value)
    {
        var normalized = Regex.Replace(value.Trim().ToUpperInvariant(), @"\s+", " ");
        return DateTime.TryParse(normalized, out var time)
            ? time.ToString("h:mm tt")
            : normalized;
    }

    private static string GetColor(int index) => new[] { "#5268ca", "#e58a37", "#8056a9", "#15905d", "#c66b00" }[index % 5];
}

public sealed record CorImportResult(IReadOnlyList<ScheduleItem> Classes, string? Error)
{
    public bool IsSuccess => Error is null && Classes.Count > 0;

    public static CorImportResult Success(IReadOnlyList<ScheduleItem> classes) => new(classes, null);

    public static CorImportResult Failure(string error) => new([], error);
}
