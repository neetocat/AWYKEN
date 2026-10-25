using System.Text.RegularExpressions;

namespace AWYKEN.Shared.Services;

/// <summary>Reads the student header block (name, student no., program...) from COR text.</summary>
public static class CorProfileParser
{
    private static readonly string[] Labels =
    [
        "College", "Academic Year & Semester", "Program/Major", "Year Level", "UMak Email",
        "Name", "Student No.", "Address", "Gender", "Guardian", "Date of Birth"
    ];

    private static readonly string LabelAlternation = string.Join("|", Labels.Select(Regex.Escape));

    private static readonly HashSet<string> SmallWords =
        new(StringComparer.OrdinalIgnoreCase) { "of", "in", "and", "the", "for", "at" };

    public static StudentProfile? Parse(string pageText)
    {
        if (string.IsNullOrWhiteSpace(pageText))
            return null;

        var flat = Regex.Replace(pageText, @"\s+", " ");
        var end = flat.IndexOf("Encoded Course", StringComparison.OrdinalIgnoreCase);
        var header = end >= 0 ? flat[..end] : flat;

        var (fullName, firstName) = ParseName(Read(header, "Name"));
        var studentNumber = Read(header, "Student No.")?.ToUpperInvariant();
        var program = TitleCase(Read(header, "Program/Major"));
        if (fullName is null && studentNumber is null && program is null)
            return null;

        return new StudentProfile(
            fullName,
            firstName,
            Read(header, "UMak Email")?.Replace(" ", "").ToLowerInvariant(),
            studentNumber,
            TitleCase(Read(header, "College")),
            program,
            TitleCase(Read(header, "Year Level")),
            null,
            Read(header, "Academic Year & Semester"));
    }

    private static string? Read(string header, string label)
    {
        var pattern = $@"(?<![A-Za-z]){Regex.Escape(label)}\s+(?<value>.+?)(?=\s(?:{LabelAlternation})(?![A-Za-z])|$)";
        var match = Regex.Match(header, pattern);
        var value = match.Success ? match.Groups["value"].Value.Trim() : null;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static (string? Full, string? First) ParseName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null);

        var parts = raw.Split(',', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            var single = TitleCase(raw);
            return (single, single?.Split(' ')[0]);
        }

        var last = TitleCase(parts[0]);
        var given = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var first = TitleCase(given.Length > 1 ? string.Join(' ', given[..^1]) : given[0]);
        var middleInitial = given.Length > 1 ? $" {char.ToUpperInvariant(given[^1][0])}." : "";
        return ($"{first}{middleInitial} {last}", first);
    }

    private static string? TitleCase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var words = value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select((word, index) =>
            index > 0 && SmallWords.Contains(word) ? word : char.ToUpperInvariant(word[0]) + word[1..]));
    }
}