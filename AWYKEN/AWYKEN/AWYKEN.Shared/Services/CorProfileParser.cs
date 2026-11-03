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
        var email = Read(header, "UMak Email")?.Replace(" ", "").ToLowerInvariant();
        if (fullName is null && studentNumber is null && program is null && email is null)
            return null;

        return new StudentProfile(
            fullName,
            firstName,
            email,
            studentNumber,
            TitleCase(Read(header, "College")),
            program,
            TitleCase(Read(header, "Year Level")),
            null,
            Read(header, "Academic Year & Semester"),
            Read(header, "Address"),
            TitleCase(Read(header, "Guardian")),
            TitleCase(Read(header, "Gender")),
            NormalizeDate(Read(header, "Date of Birth")));
    }

    private static string? Read(string header, string label)
    {
        // PdfPig may join adjacent PDF text boxes without inserting spaces,
        // producing strings such as "UMak Emailjescriba@umak.edu.ph".
        var labelMatch = Regex.Match(header, Regex.Escape(label), RegexOptions.IgnoreCase);
        if (!labelMatch.Success)
            return null;

        var valueStart = labelMatch.Index + labelMatch.Length;
        while (valueStart < header.Length && (char.IsWhiteSpace(header[valueStart]) || header[valueStart] == ':'))
            valueStart++;

        var valueEnd = header.Length;
        foreach (var nextLabel in Labels)
        {
            if (nextLabel.Equals(label, StringComparison.OrdinalIgnoreCase))
                continue;
            var nextIndex = header.IndexOf(nextLabel, valueStart, StringComparison.OrdinalIgnoreCase);
            if (nextIndex >= 0 && nextIndex < valueEnd)
                valueEnd = nextIndex;
        }

        var value = header[valueStart..valueEnd].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? NormalizeDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var cleaned = Regex.Replace(value, @"\s+", " ").Trim();
        return DateTime.TryParse(cleaned, out var date)
            ? date.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : cleaned;
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
        var given = TitleCase(parts[1]);
        var first = given?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return ($"{given} {last}", first);
    }

    private static string? TitleCase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = Regex.Replace(value, "INFORMATION\\s*SCIENCES", "INFORMATION SCIENCES", RegexOptions.IgnoreCase);
        var words = value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select((word, index) =>
            index > 0 && SmallWords.Contains(word) ? word : char.ToUpperInvariant(word[0]) + word[1..]));
    }
}
