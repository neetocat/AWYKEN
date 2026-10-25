using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Forms;
using Tesseract;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace AWYKEN.Shared.Services;

public sealed class CorScheduleImporter
{
    private const long MaxFileSize = 10 * 1024 * 1024;
    private static readonly Regex CfnPattern = new(@"\b(?<cfn>[A-Z]\d{6,})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TimeRangePattern = new(
        @"\b(?<start>\d{1,2}:?\d{2}\s*(?:AM|PM))\s*(?:-|–|to)\s*(?<end>\d{1,2}:?\d{2}\s*(?:AM|PM))\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CourseCodePattern = new(@"^[A-Z]{2,10}(?:\s*\d{1,3})?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<CorImportResult> ImportAsync(IBrowserFile file, CancellationToken cancellationToken = default)
    {
        if (file.Size == 0)
            return CorImportResult.Failure("The selected file is empty.");
        if (file.Size > MaxFileSize)
            return CorImportResult.Failure("The file is larger than the 10 MB limit.");

        var extension = Path.GetExtension(file.Name);
        if (!new[] { ".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }
                .Contains(extension, StringComparer.OrdinalIgnoreCase))
            return CorImportResult.Failure("Choose a PDF, PNG, JPG, BMP, or TIFF file.");

        await using var input = file.OpenReadStream(MaxFileSize, cancellationToken);
        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        try
        {
            StudentProfile? profile = null;
            List<ScheduleItem> classes;
            if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                buffer.Position = 0;
                using var document = PdfDocument.Open(buffer);
                var pages = document.GetPages().ToList();
                classes = [];
                foreach (var page in pages)
                {
                    profile ??= CorProfileParser.Parse(page.Text);
                    var pageClasses = ParseTableRows(GetPdfWords(page), page.Width);
                    if (pageClasses.Count == 0)
                        pageClasses = ParseClasses(page.Text);

                    if (pageClasses.Count == 0)
                    {
                        foreach (var image in page.GetImages())
                        {
                            try
                            {
                                var imageBytes = image.TryGetPng(out var png) ? png : image.RawBytes.ToArray();
                                if (imageBytes.Length == 0)
                                    continue;
                                pageClasses = ReadImageClasses(imageBytes);
                                if (pageClasses.Count > 0)
                                    break;
                            }
                            catch (Exception exception) when (exception is not OperationCanceledException)
                            {
                                // A PDF may contain non-text images such as logos; keep looking for its scan.
                            }
                        }
                    }

                    pageClasses = ApplyTextTitles(pageClasses, page.Text);
                    classes.AddRange(pageClasses);
                }
            }
            else
            {
                classes = ReadImageClasses(bytes);
            }

            classes = classes
                .GroupBy(item => (item.CourseCode, item.Section), StringTupleComparer.Instance)
                .Select(group => group.First()).ToList();
            var section = classes.Where(c => !string.IsNullOrWhiteSpace(c.Section))
                .GroupBy(c => c.Section!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            profile = profile is null ? null : profile with { Section = section };

            return classes.Count == 0
                ? CorImportResult.Failure("No class rows were recognized. Use a clear COR image or PDF and review the file format.")
                : CorImportResult.Success(classes, profile);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return CorImportResult.Failure("The file could not be scanned. Check that it is a valid, clear PDF or image.");
        }
    }

    private static List<ScheduleItem> ReadImageClasses(byte[] bytes)
    {
        var tessdata = Path.Combine(AppContext.BaseDirectory, "tessdata");
        if (!File.Exists(Path.Combine(tessdata, "eng.traineddata")))
            throw new InvalidOperationException("English OCR data is not installed.");

        using var engine = new TesseractEngine(tessdata, "eng", EngineMode.LstmOnly);
        engine.SetVariable("preserve_interword_spaces", true);
        engine.SetVariable("user_defined_dpi", 300);
        using var image = Pix.LoadFromMemory(bytes);
        return ReadImageClasses(engine, image);
    }

    private static List<ScheduleItem> ReadImageClasses(TesseractEngine engine, Pix image)
    {
        var passes = new List<List<ScheduleItem>>();
        List<ScheduleItem>? croppedTextCandidates = null;
        var portraitCor = image.Height > image.Width * 1.08;
        AddPass(image, PageSegMode.Auto);
        AddPass(image, PageSegMode.SingleBlock);
        AddPass(image, PageSegMode.SparseText);

        // Small or compressed uploads often have too few pixels for the COR
        // table. Upscale and run both grayscale and adaptive-threshold variants.
        var largestSide = Math.Max(image.Width, image.Height);
        if (largestSide < 2600)
        {
            var scale = portraitCor || largestSide < 900 ? 3f : 2f;
            using var enlarged = image.Scale(scale, scale);
            AddPass(enlarged, PageSegMode.Auto);
            AddPass(enlarged, PageSegMode.SingleBlock);
            AddPass(enlarged, PageSegMode.SparseText);

            // On portrait CORs the encoded-course table occupies a small band
            // of the page. OCRing the whole page makes its rows too tiny, so
            // scan that band separately at the enlarged resolution.
            if (portraitCor && TryGetCourseTableRegion(enlarged, out var tableRegion))
            {
                AddPass(enlarged, PageSegMode.Auto, tableRegion);
                AddPass(enlarged, PageSegMode.SingleBlock, tableRegion);
                AddPass(enlarged, PageSegMode.SparseText, tableRegion);
            }

            try
            {
                using var gray = enlarged.ConvertRGBToGray();
                if (portraitCor && TryGetCourseTableRegion(gray, out var grayTableRegion))
                    AddPass(gray, PageSegMode.SingleBlock, grayTableRegion);
                else
                    AddPass(gray, PageSegMode.Auto);
                using var thresholded = gray.BinarizeOtsuAdaptiveThreshold(32, 32, 2, 2, 0.1f);
                if (portraitCor && TryGetCourseTableRegion(thresholded, out var thresholdTableRegion))
                {
                    AddPass(thresholded, PageSegMode.Auto, thresholdTableRegion);
                    AddPass(thresholded, PageSegMode.SingleBlock, thresholdTableRegion);
                }
                else
                {
                    AddPass(thresholded, PageSegMode.Auto);
                    AddPass(thresholded, PageSegMode.SingleBlock);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or TesseractException or ArgumentException)
            {
                // Some image formats are already grayscale or binary; the
                // enlarged original passes above remain available.
            }
        }

        return croppedTextCandidates is { Count: >= 2 }
            ? MergeOcrCandidates([croppedTextCandidates])
            : MergeOcrCandidates(passes);

        void AddPass(Pix source, PageSegMode mode, Rect? region = null)
        {
            using var page = region is null
                ? engine.Process(source, mode)
                : engine.Process(source, region.Value, mode);
            var words = new List<LayoutWord>();
            using var iterator = page.GetIterator();
            iterator.Begin();
            do
            {
                var text = iterator.GetText(PageIteratorLevel.Word)?.Trim();
                if (!string.IsNullOrWhiteSpace(text)
                    && iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var bounds))
                    words.Add(new LayoutWord(text, bounds.X1, (bounds.Y1 + bounds.Y2) / 2d));
            } while (iterator.Next(PageIteratorLevel.Word));

            var parsed = ParseTableRows(words, source.Width);
            // Text-line parsing is useful on a cropped table but noisy on the
            // full-page OCR, where the profile/footer can resemble course rows.
            if (region is not null && mode == PageSegMode.SingleBlock)
            {
                var textCandidates = ParseOcrTableText(page.GetText());
                if (textCandidates.Count > (croppedTextCandidates?.Count ?? 0))
                    croppedTextCandidates = textCandidates;
            }

            parsed = parsed
                .Select(item => item with
                {
                    CourseCode = NormalizeCourseCode(item.CourseCode),
                    Title = NormalizeOcrTitle(item.Title)
                }).ToList();
            if (parsed.Count > 0)
                passes.Add(parsed);
        }
    }

    private static bool TryGetCourseTableRegion(Pix image, out Rect region)
    {
        // The standard University of Makati COR layout places Encoded Course(s)
        // between roughly 24% and 47% of a portrait page's height.
        var x1 = (int)(image.Width * 0.035);
        var x2 = (int)(image.Width * 0.975);
        var y1 = (int)(image.Height * 0.235);
        var y2 = (int)(image.Height * 0.47);
        region = new Rect(x1, y1, x2 - x1, y2 - y1);
        return region.Width > 0 && region.Height > 0;
    }

    private static List<ScheduleItem> MergeOcrCandidates(IEnumerable<List<ScheduleItem>> passes) =>
        passes.SelectMany(items => items)
            .Where(item => IsPlausibleCfn(item.Cfn)
                && Regex.IsMatch(item.CourseCode, @"^[A-Z]{2,10}(?:\s*\d{1,3})?(?:-[A-Z]{2,10})?$", RegexOptions.IgnoreCase)
                && item.Title.Count(char.IsLetter) >= 4
                && item.Title.Length <= 140
                && item.Location.Length <= 80)
            .GroupBy(OcrMergeKey,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(OcrQualityScore).First())
            .OrderBy(item => item.SortTime)
            .ToList();

    private static int OcrQualityScore(ScheduleItem item)
    {
        var score = 0;
        if (CfnPattern.IsMatch(item.Cfn ?? "")) score += 4;
        if (Regex.IsMatch(item.CourseCode, @"^[A-Z]{2,10}(?:\s*\d{1,3})?$", RegexOptions.IgnoreCase)) score += 3;
        if (item.Title.Any(char.IsLetter)) score += 2;
        if (item.Title.Contains(' ')) score += 2;
        score += Math.Min(item.Title.Length / 25, 4);
        if (TimeRangePattern.IsMatch(item.MeetingTimes ?? "")) score += 2;
        if (!string.IsNullOrWhiteSpace(item.Location)) score++;
        if (item.Units.HasValue) score++;
        if (item.Title.Length > 85) score -= 4;
        if (Regex.IsMatch(item.Title, @"(?:[IVX]+|[A-Z0-9]+)-[A-Z][A-Z0-9-]+|\b\d{1,2}:\d{2}\s*(?:AM|PM)\b", RegexOptions.IgnoreCase))
            score -= 12;
        if (Regex.IsMatch(item.Title, @"SOC\s*STUD|[=§#]", RegexOptions.IgnoreCase)) score -= 8;
        if (Regex.IsMatch(item.Location, @"\b(?:CFN|Course|Descriptive|Section|Units|Time|Days|Room|Encoded)\b", RegexOptions.IgnoreCase))
            score -= 8;
        return score;
    }

    private static string OcrMergeKey(ScheduleItem item)
    {
        var code = NormalizeCourseCode(item.CourseCode);
        var title = Regex.Replace(item.Title.ToUpperInvariant(), @"[^A-Z0-9]", "");
        title = Regex.Replace(title, @"^THE", "");
        var titleStem = title[..Math.Min(12, title.Length)];
        return titleStem.Length == 0 ? $"{code}|{item.StartTime}" : titleStem;
    }

    private static string NormalizeCourseCode(string value)
    {
        var code = Regex.Replace(value.Trim().ToUpperInvariant(), @"(?<=[A-Z])\s+(?=[A-Z])", "");
        return code.Equals("PES", StringComparison.OrdinalIgnoreCase)
            ? "PE 3"
            : Regex.Replace(code, @"^PE\s*3$", "PE 3");
    }

    private static string NormalizeOcrTitle(string value) =>
        Regex.Replace(value.Trim().Trim('.', ',', ';', '-'), @"\s+", " ");

    private static string NormalizeOcrCfn(string value)
    {
        var token = Regex.Replace(value, @"[^A-Z0-9]", "", RegexOptions.IgnoreCase).ToUpperInvariant();
        if (!token.StartsWith('A') || token.Length is < 7 or > 12)
            return token;

        var characters = token.ToCharArray();
        for (var index = 1; index < characters.Length; index++)
        {
            characters[index] = characters[index] switch
            {
                'I' or 'L' or '|' => '1', 'Z' => '2', 'O' or 'Q' or 'D' => '0',
                'S' => '5', 'G' => '6', 'B' => '8', _ => characters[index]
            };
        }
        var normalized = new string(characters);
        return normalized.Count(char.IsDigit) >= 5 ? normalized : token;
    }

    private static bool IsPlausibleCfn(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var token = Regex.Replace(value, @"[^A-Z0-9]", "", RegexOptions.IgnoreCase);
        return token.StartsWith('A') && token.Length is >= 7 and <= 12 && token.Count(char.IsDigit) >= 4;
    }

    private static bool IsPossibleUnit(string value) =>
        decimal.TryParse(value, CultureInfo.InvariantCulture, out var units)
        && (units is >= 1 and <= 6 || units is >= 10 and <= 60 && units % 10 == 0);

    private static List<ScheduleItem> ParseOcrTableText(string text)
    {
        var rows = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var rawLine in Regex.Split(text, @"\r\n|\n|\r"))
        {
            var line = Regex.Replace(rawLine.Trim(), @"\s+", " ");
            if (line.Length == 0)
                continue;
            if (Regex.IsMatch(line, @"^Total\s+Units", RegexOptions.IgnoreCase))
                break;

            var id = Regex.Match(line, @"^(?<id>[A-Z0-9]{7,12})\b", RegexOptions.IgnoreCase);
            if (id.Success && IsPlausibleCfn(NormalizeOcrCfn(id.Groups["id"].Value)))
            {
                if (current.Length > 0)
                    rows.Add(current.ToString());
                current.Clear();
                current.Append(line);
            }
            else if (current.Length > 0)
            {
                current.Append(' ').Append(line);
            }
        }
        if (current.Length > 0)
            rows.Add(current.ToString());

        var result = new List<ScheduleItem>();
        foreach (var row in rows)
        {
            var id = Regex.Match(row, @"^(?<id>[A-Z0-9]{7,12})\b", RegexOptions.IgnoreCase);
            var rest = Regex.Replace(row[id.Length..].TrimStart(), @"^[^A-Z0-9]+", "", RegexOptions.IgnoreCase);
            var code = Regex.Match(rest,
                @"^(?<code>[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)*(?:\s+\d{1,3})?)\s+", RegexOptions.IgnoreCase);
            if (!id.Success || !code.Success)
                continue;

            var courseCode = NormalizeCourseCode(code.Groups["code"].Value);
            var afterCode = rest[code.Length..];
            var normalized = NormalizeTimeText(afterCode);
            var timeMatches = TimeRangePattern.Matches(normalized);

            var unitCandidates = Regex.Matches(afterCode, @"(?<![A-Z])(?<units>\d{1,2}(?:\.\d+)?)(?![A-Z])",
                RegexOptions.IgnoreCase).Cast<Match>().ToList();
            var unitBoundary = timeMatches.Count > 0 ? timeMatches[0].Index : afterCode.Length;
            var unitMatch = unitCandidates.LastOrDefault(match => match.Index < unitBoundary
                && IsPossibleUnit(match.Groups["units"].Value));
            if (unitMatch is null)
                continue;

            var beforeUnits = afterCode[..unitMatch.Index].Trim();
            var sectionMatch = Regex.Match(beforeUnits,
                @"(?<section>\S{1,14}\s+STUD|\S{1,14}SOCSTUD)\s*$", RegexOptions.IgnoreCase);
            var title = sectionMatch.Success
                ? beforeUnits[..sectionMatch.Index].Trim(' ', '-', ':')
                : beforeUnits;
            title = Regex.Replace(title,
                @"\s+\S{1,14}\s+STUD(?:\s+\d{1,2}(?:\.\d+)?)?\s*$", "", RegexOptions.IgnoreCase);
            var section = sectionMatch.Success ? sectionMatch.Groups["section"].Value.Trim() : "";

            var tail = timeMatches.Count > 0
                ? normalized[(timeMatches[^1].Index + timeMatches[^1].Length)..].Trim()
                : "";
            var dayMatch = Regex.Match(tail, @"^,?\s*(?<days>(?:TH|M|T|W|F|S|U){1,7})\b", RegexOptions.IgnoreCase);
            var days = dayMatch.Success ? FormatDays(dayMatch.Groups["days"].Value) : "";
            var location = dayMatch.Success ? tail[dayMatch.Length..].Trim() : "Room needs review";
            var unitsValue = decimal.TryParse(unitMatch.Groups["units"].Value, CultureInfo.InvariantCulture, out var parsedUnits)
                ? parsedUnits is >= 10 and <= 60 && parsedUnits % 10 == 0 ? parsedUnits / 10 : parsedUnits
                : (decimal?)null;
            var firstTime = timeMatches.Count > 0 ? timeMatches[0] : null;
            var meetingTimes = timeMatches.Count == 0 ? "Time needs review" : string.Join(", ", timeMatches.Select(match =>
                $"{NormalizeTime(match.Groups["start"].Value)}–{NormalizeTime(match.Groups["end"].Value)}"));

            result.Add(new ScheduleItem(
                $"import-{Guid.NewGuid():N}", courseCode, NormalizeOcrTitle(title),
                firstTime is null ? "" : NormalizeTime(firstTime.Groups["start"].Value),
                firstTime is null ? "" : NormalizeTime(firstTime.Groups["end"].Value),
                string.IsNullOrWhiteSpace(location) ? "Room needs review" : location,
                "", "", "Face-to-face", GetColor(result.Count), null,
                NormalizeOcrCfn(id.Groups["id"].Value), section, unitsValue, days, meetingTimes));
        }
        return result;
    }

    private static List<LayoutWord> GetPdfWords(UglyToad.PdfPig.Content.Page page)
    {
        var letters = page.Letters
            // Keep PDF space glyphs: discarding them here makes the later
            // word-boundary handling ineffective for PDFs that encode spaces
            // as explicit glyphs with very narrow advances.
            .Where(letter => !string.IsNullOrEmpty(letter.Value))
            .Select(letter => new LayoutLetter(letter.Value, letter.Location.X,
                page.Height - letter.Location.Y, letter.Width, letter.FontSize))
            .OrderBy(letter => letter.Y).ThenBy(letter => letter.X).ToList();
        var lines = new List<List<LayoutLetter>>();

        foreach (var letter in letters)
        {
            var line = lines.LastOrDefault(candidate =>
                Math.Abs(candidate.Average(item => item.Y) - letter.Y) <= Math.Max(1.5, letter.FontSize * 0.2));
            if (line is null)
            {
                line = [];
                lines.Add(line);
            }
            line.Add(letter);
        }

        var words = new List<LayoutWord>();
        foreach (var line in lines)
        {
            var ordered = line.OrderBy(letter => letter.X).ToList();
            var current = new System.Text.StringBuilder();
            var startX = 0d;
            var endX = 0d;
            foreach (var letter in ordered)
            {
                var value = letter.Value;
                var gap = letter.X - endX;
                if (char.IsWhiteSpace(value[0]) || (current.Length > 0 && gap > Math.Max(1.5, letter.FontSize * 0.28)))
                {
                    AddPdfWord();
                    if (char.IsWhiteSpace(value[0]))
                        continue;
                }
                if (current.Length == 0)
                    startX = letter.X;
                current.Append(value);
                endX = letter.X + letter.Width;
            }
            AddPdfWord();

            void AddPdfWord()
            {
                if (current.Length == 0)
                    return;
                words.Add(new LayoutWord(current.ToString(), startX, ordered.Average(letter => letter.Y)));
                current.Clear();
            }
        }

        return words;
    }

    private static List<ScheduleItem> ParseTableRows(IReadOnlyList<LayoutWord> words, double pageWidth)
    {
        if (words.Count == 0)
            return [];

        var columnNames = new[] { "CFN", "Course", "Descriptive", "Section", "Units", "Time", "Days", "Room" };
        var defaults = new[] { .05, .116, .236, .512, .624, .661, .791, .845 };
        var cfnHeader = words.Where(word => IsCfnHeader(word.Text))
            .OrderBy(word => word.Y).FirstOrDefault();
        var headerY = cfnHeader?.Y ?? double.NaN;
        var anchors = defaults.Select((fraction, index) =>
        {
            var matchingHeaders = words.Where(word => MatchesColumnHeader(word.Text, columnNames[index]));
            var header = double.IsNaN(headerY)
                ? matchingHeaders.FirstOrDefault()
                : matchingHeaders.OrderBy(word => Math.Abs(word.Y - headerY)).FirstOrDefault();
            return header is null ? fraction * pageWidth : header.X;
        }).ToArray();

        int ColumnFor(LayoutWord word)
        {
            var column = 0;
            for (var index = 1; index < anchors.Length; index++)
            {
                // A word belongs to the column whose header starts before it.
                // Midpoint boundaries split rightmost words (such as the final
                // "PM" in a time range) into the next column when the PDF has
                // narrow inter-word spacing.
                if (word.X < anchors[index]) break;
                column = index;
            }
            return column;
        }

        if (double.IsNaN(headerY))
            headerY = -12;
        var unitLabels = words.Where(word => Regex.Replace(word.Text, @"[^A-Z]", "", RegexOptions.IgnoreCase)
            .Equals("UNITS", StringComparison.OrdinalIgnoreCase)).ToList();
        var tableEndY = words.Where(word =>
                Regex.Replace(word.Text, @"[^A-Z]", "", RegexOptions.IgnoreCase)
                    .Equals("TOTALUNITS", StringComparison.OrdinalIgnoreCase)
                || (Regex.Replace(word.Text, @"[^A-Z]", "", RegexOptions.IgnoreCase)
                        .Equals("TOTAL", StringComparison.OrdinalIgnoreCase)
                    && unitLabels.Any(units => Math.Abs(units.Y - word.Y) <= 18 && units.X > word.X)))
            .Select(word => word.Y).DefaultIfEmpty(double.MaxValue).Min();
        // Text elsewhere on the COR can look like a CFN to the loose OCR
        // candidate check (for example "Date: 07/14/2026"). Never let a
        // footer candidate extend the final course row past the table total.
        var rowStarts = words.Where(word => ColumnFor(word) == 0
                && word.Y < tableEndY
                && word.Y > headerY + 10 && IsCfnCandidate(word.Text))
            .OrderBy(word => word.Y).ToList();
        var results = new List<ScheduleItem>();
        for (var rowIndex = 0; rowIndex < rowStarts.Count; rowIndex++)
        {
            var rowStart = rowStarts[rowIndex];
            // PDF text coordinates can place course rows only ~11 points apart.
            // A wide fixed window (previously 12 points) excluded the current
            // row entirely on dense COR tables. Start just above the CFN line,
            // and include wrapped cell lines up to the next CFN row.
            var top = rowStart.Y - 1.5;
            var bottom = rowIndex + 1 < rowStarts.Count
                ? rowStarts[rowIndex + 1].Y - 1.5
                : tableEndY - 1.5;
            var rowWords = words.Where(word => word.Y >= top && word.Y < bottom).ToList();
            var cells = Enumerable.Range(0, 8).Select(column => JoinInReadingOrder(
                rowWords.Where(word => ColumnFor(word) == column), Math.Max(3, pageWidth * 0.0065))).ToArray();

            var cfnMatch = CfnPattern.Match(cells[0]);
            var cfn = cfnMatch.Success
                ? cfnMatch.Groups["cfn"].Value.ToUpperInvariant()
                : Regex.Match(cells[0], @"[A-Z0-9]{7,}", RegexOptions.IgnoreCase).Value.ToUpperInvariant();
            cfn = NormalizeOcrCfn(cfn);
            var courseCode = cells[1].Trim();
            if (courseCode.Equals("PE3", StringComparison.OrdinalIgnoreCase)
                || courseCode.Equals("PES", StringComparison.OrdinalIgnoreCase))
                courseCode = "PE 3";
            var title = cells[2].Trim();
            if (courseCode.Equals("COPROGR", StringComparison.OrdinalIgnoreCase)
                && title.Contains("OBJECT ORIENTED PROGRAMMING", StringComparison.OrdinalIgnoreCase))
                courseCode = "OOPROGR";
            var section = NormalizeSection(cells[3]);
            var units = decimal.TryParse(Regex.Match(cells[4], @"\d+(?:\.\d+)?").Value,
                CultureInfo.InvariantCulture, out var parsedUnits)
                ? parsedUnits is >= 10 and <= 60 && parsedUnits % 10 == 0 ? parsedUnits / 10 : parsedUnits
                : (decimal?)null;
            var normalizedTimeText = NormalizeTimeText(cells[5]);
            var timeMatches = TimeRangePattern.Matches(normalizedTimeText);
            if (string.IsNullOrWhiteSpace(cfn) || string.IsNullOrWhiteSpace(courseCode) || timeMatches.Count == 0)
                continue;

            var meetingTimes = string.Join(", ", timeMatches.Select(match =>
                $"{NormalizeTime(match.Groups["start"].Value)}–{NormalizeTime(match.Groups["end"].Value)}"));
            var daysText = Regex.Replace(cells[6].ToUpperInvariant(), @"[^A-Z]", "");
            var days = FormatDays(daysText);
            var room = cells[7].Trim();
            var first = timeMatches[0];
            results.Add(new ScheduleItem(
                $"import-{Guid.NewGuid():N}", courseCode, string.IsNullOrWhiteSpace(title) ? courseCode : title,
                NormalizeTime(first.Groups["start"].Value), NormalizeTime(first.Groups["end"].Value),
                string.IsNullOrWhiteSpace(room) ? "Room needs review" : room,
                "", "", "Face-to-face", GetColor(results.Count), null,
                cfn, section, units, days, meetingTimes));
        }

        return results;
    }

    private static List<ScheduleItem> ApplyTextTitles(List<ScheduleItem> classes, string text)
    {
        // Text extractors may concatenate the prior cell's room/day text with
        // the next CFN, so do not require a word boundary before the ID.
        var cfnMatches = Regex.Matches(text, @"(?<cfn>[A-Z]\d{6,})", RegexOptions.IgnoreCase)
            .Cast<Match>().ToList();
        if (cfnMatches.Count == 0)
            return classes;

        return classes.Select(item =>
        {
            var rowIndex = cfnMatches.FindIndex(match => match.Groups["cfn"].Value
                .Equals(item.Cfn, StringComparison.OrdinalIgnoreCase));
            if (rowIndex < 0)
                return item;

            var rowStart = cfnMatches[rowIndex].Index + cfnMatches[rowIndex].Length;
            var rowEnd = rowIndex + 1 < cfnMatches.Count ? cfnMatches[rowIndex + 1].Index : text.Length;
            var row = text[rowStart..rowEnd];
            var sectionIndex = string.IsNullOrWhiteSpace(item.Section)
                ? -1
                : row.IndexOf(item.Section, StringComparison.OrdinalIgnoreCase);
            if (sectionIndex < 0)
                return item;

            var codeAndTitle = row[..sectionIndex];
            var codeIndex = 0;
            foreach (var character in item.CourseCode.Where(character => !char.IsWhiteSpace(character)))
            {
                while (codeIndex < codeAndTitle.Length && char.IsWhiteSpace(codeAndTitle[codeIndex]))
                    codeIndex++;
                if (codeIndex >= codeAndTitle.Length
                    || char.ToUpperInvariant(codeAndTitle[codeIndex]) != char.ToUpperInvariant(character))
                    return item;
                codeIndex++;
            }

            var title = codeAndTitle[codeIndex..].Trim(' ', '-', ':');
            return title.Length > 0 ? item with { Title = title } : item;
        }).ToList();
    }

    private static string JoinInReadingOrder(IEnumerable<LayoutWord> source, double lineTolerance)
    {
        var lines = new List<List<LayoutWord>>();
        foreach (var word in source.OrderBy(item => item.Y).ThenBy(item => item.X))
        {
            var line = lines.LastOrDefault(candidate =>
                Math.Abs(candidate.Average(item => item.Y) - word.Y) <= lineTolerance);
            if (line is null)
            {
                line = [];
                lines.Add(line);
            }
            line.Add(word);
        }

        return string.Join(" ", lines.OrderBy(line => line.Average(item => item.Y))
            .Select(line => string.Join(" ", line.OrderBy(item => item.X).Select(item => item.Text))));
    }

    private static bool MatchesColumnHeader(string value, string expected)
    {
        var token = Regex.Replace(value, @"[^A-Z]", "", RegexOptions.IgnoreCase).ToUpperInvariant();
        return expected switch
        {
            "CFN" => token is "CFN" or "CN",
            "Course" => token.StartsWith("COURSE", StringComparison.Ordinal),
            "Descriptive" => token.StartsWith("DESCRIP", StringComparison.Ordinal),
            "Section" => token.StartsWith("SECTION", StringComparison.Ordinal),
            "Units" => token.StartsWith("UNIT", StringComparison.Ordinal),
            "Time" => token is "TIME" or "TINE" or "T1ME",
            "Days" => token.StartsWith("DAY", StringComparison.Ordinal),
            "Room" => token.StartsWith("ROOM", StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool IsCfnHeader(string value) =>
        Regex.Replace(value, @"[^A-Z]", "", RegexOptions.IgnoreCase).ToUpperInvariant() is "CFN" or "CN";

    private static bool IsCfnCandidate(string value)
    {
        var token = Regex.Replace(value, @"[^A-Z0-9]", "", RegexOptions.IgnoreCase);
        return token.Length is >= 7 and <= 12
            && token.Any(char.IsLetter)
            && token.Count(char.IsDigit) >= 4;
    }

    private static string NormalizeTimeText(string value)
    {
        var normalized = value.ToUpperInvariant();
        normalized = normalized.Replace("°", "", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"[OGQD]", "0");
        normalized = Regex.Replace(normalized, @"[IL]", "1");
        normalized = Regex.Replace(normalized, @"Z", "2");
        normalized = Regex.Replace(normalized, @"S", "5");
        normalized = Regex.Replace(normalized, @"B", "8");
        normalized = Regex.Replace(normalized, @"(?<=\d)[FT](?=\d{3}\s*(?:AM|PM))", "");
        normalized = Regex.Replace(normalized, @"(?<=\d)4M\b", "AM");
        normalized = Regex.Replace(normalized, @"(?<!\d)00(?=\d{3}\s*(?:AM|PM))", "0");
        normalized = Regex.Replace(normalized, @"(?<=\d)MM\b", "PM");
        normalized = Regex.Replace(normalized, @"(?<=\d)\.(?=\d{2}\s*(?:AM|PM))", ":");
        normalized = Regex.Replace(normalized, @"(AM|PM)00(?=\d{3}\s*(?:AM|PM))", "$1-0");
        normalized = Regex.Replace(normalized, @"(AM|PM)0(?=\d{3}\s*(?:AM|PM))", "$1-0");
        // Correct a scan that reads noon as "00 PM", while preserving normal
        // times whose minutes are :00 (for example, "06:00 PM").
        normalized = Regex.Replace(normalized, @"(?<!\d:)\b00\s*PM(?=\s*[-–])", "12:00 PM");
        normalized = Regex.Replace(normalized, @"(AM|PM)\s*[^0-9A-Z]*\s*(?=\d{1,2}:?\d{2}\s*(?:AM|PM))", "$1-");
        normalized = Regex.Replace(normalized, @"(AM|PM)(?=\d{1,2}:?\d{2}\s*(?:AM|PM))", "$1-");
        return normalized;
    }

    private static string NormalizeSection(string value)
    {
        var section = Regex.Replace(value.Trim().ToUpperInvariant(), @"\s*-\s*", "-");
        section = Regex.Replace(section, @"^IL(?=-)", "II");
        if (Regex.IsMatch(section, @"(?:BE|[®@])?ECSAD$", RegexOptions.IgnoreCase))
            return "II-ECSAD";
        return section;
    }

    private static List<ScheduleItem> ParseClasses(string text)
    {
        var results = new List<ScheduleItem>();
        var lines = BuildRows(text);

        foreach (var line in lines)
        {
            var cfn = CfnPattern.Match(line);
            if (!cfn.Success)
                continue;

            var afterCfn = line[(cfn.Index + cfn.Length)..].Trim();
            var section = Regex.Match(afterCfn,
                @"(?<section>(?:[IVX]+|[A-Z0-9]+)-[A-Z][A-Z0-9-]+)\b", RegexOptions.IgnoreCase);
            var timeMatches = TimeRangePattern.Matches(afterCfn);
            if (!section.Success || timeMatches.Count == 0)
                continue;

            var codeMatch = CourseCodePattern.Match(afterCfn);
            if (!codeMatch.Success || codeMatch.Index > section.Index)
                continue;

            var beforeSection = afterCfn[..section.Index].Trim();
            var codeInPrefix = CourseCodePattern.Match(beforeSection);
            if (!codeInPrefix.Success)
                continue;

            var courseCode = Regex.Replace(codeInPrefix.Value, @"\s+", " ").Trim().ToUpperInvariant();
            var title = beforeSection[codeInPrefix.Length..].Trim(' ', '-', ':');
            if (string.IsNullOrWhiteSpace(title))
                title = courseCode;

            var afterSection = afterCfn[(section.Index + section.Length)..];
            var unitsMatch = Regex.Match(afterSection, @"\b(?<units>\d+(?:\.\d+)?)\b");
            var units = decimal.TryParse(unitsMatch.Groups["units"].Value, CultureInfo.InvariantCulture, out var parsedUnits)
                ? parsedUnits : (decimal?)null;
            var times = string.Join(", ", timeMatches.Select(match =>
                $"{NormalizeTime(match.Groups["start"].Value)}–{NormalizeTime(match.Groups["end"].Value)}"));

            var lastTime = timeMatches[^1];
            var tail = afterCfn[(lastTime.Index + lastTime.Length)..].Trim();
            var dayMatch = Regex.Match(tail, @"^,?\s*(?<days>(?:TH|M|T|W|F|S|U){1,7})\b", RegexOptions.IgnoreCase);
            var days = dayMatch.Success ? FormatDays(dayMatch.Groups["days"].Value) : "";
            var room = dayMatch.Success ? tail[dayMatch.Length..].Trim() : "";
            var firstRange = timeMatches[0];
            var online = line.Contains("online", StringComparison.OrdinalIgnoreCase);

            results.Add(new ScheduleItem(
                $"import-{Guid.NewGuid():N}", courseCode, title,
                NormalizeTime(firstRange.Groups["start"].Value), NormalizeTime(firstRange.Groups["end"].Value),
                string.IsNullOrWhiteSpace(room) ? "Room needs review" : room,
                "", "", online ? "Online" : "Face-to-face", GetColor(results.Count), null,
                cfn.Groups["cfn"].Value.ToUpperInvariant(), section.Groups["section"].Value.ToUpperInvariant(),
                units, days, times));
        }

        return results.GroupBy(item => (item.CourseCode, item.Section), StringTupleComparer.Instance)
            .Select(group => group.First()).ToList();
    }

    private static IEnumerable<string> BuildRows(string text)
    {
        var matches = Regex.Matches(text, @"(?<cfn>[A-Z]\d{6,})", RegexOptions.IgnoreCase)
            .Cast<Match>().ToList();
        for (var index = 0; index < matches.Count; index++)
        {
            var start = matches[index].Index;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : text.Length;
            var row = text[start..end];
            var footerIndex = row.IndexOf("Total Units", StringComparison.OrdinalIgnoreCase);
            if (footerIndex >= 0)
                row = row[..footerIndex];
            row = Regex.Replace(row.Trim(), @"\s+", " ");
            if (row.Length > 0)
                yield return row;
        }
    }

    private static string FormatDays(string value)
    {
        var days = new List<string>();
        for (var index = 0; index < value.Length;)
        {
            if (value.AsSpan(index).StartsWith("TH", StringComparison.OrdinalIgnoreCase))
            {
                days.Add("Thu");
                index += 2;
                continue;
            }
            days.Add(char.ToUpperInvariant(value[index]) switch
            {
                'M' => "Mon", 'T' => "Tue", 'W' => "Wed", 'F' => "Fri", 'S' => "Sat", 'U' => "Sun",
                _ => ""
            });
            index++;
        }
        return string.Join(", ", days.Where(day => day.Length > 0).Distinct());
    }

    private static string NormalizeTime(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time.ToString("h:mm tt", CultureInfo.InvariantCulture)
            : Regex.Match(value, @"^(?<hour>\d{1,2}):?(?<minute>\d{2})\s*(?<period>AM|PM)$", RegexOptions.IgnoreCase) is { Success: true } match
                && int.TryParse(match.Groups["hour"].Value, out var hour)
                && int.TryParse(match.Groups["minute"].Value, out var minute)
                ? $"{(hour % 12 == 0 ? 12 : hour % 12)}:{minute:00} {match.Groups["period"].Value.ToUpperInvariant()}"
                : value.Trim().ToUpperInvariant();

    private static string GetColor(int index) => new[] { "#5268ca", "#e58a37", "#8056a9", "#15905d", "#c66b00" }[index % 5];

    private sealed class StringTupleComparer : IEqualityComparer<(string CourseCode, string? Section)>
    {
        public static readonly StringTupleComparer Instance = new();
        public bool Equals((string CourseCode, string? Section) x, (string CourseCode, string? Section) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.CourseCode, y.CourseCode)
            && StringComparer.OrdinalIgnoreCase.Equals(x.Section, y.Section);
        public int GetHashCode((string CourseCode, string? Section) value) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.CourseCode),
                value.Section is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(value.Section));
    }

    private sealed record LayoutWord(string Text, double X, double Y);
    private sealed record LayoutLetter(string Value, double X, double Y, double Width, double FontSize);
}


