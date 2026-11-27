using System.Text.Json;
using Tesseract;

if (args.Length != 1)
{
    Console.Error.WriteLine("Expected one image file path.");
    return 2;
}

var tessdata = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tessdata"));
if (!File.Exists(Path.Combine(tessdata, "eng.traineddata")))
{
    Console.Error.WriteLine("OCR data is missing on the server. Contact the administrator.");
    return 3;
}

try
{
    var imageBytes = await File.ReadAllBytesAsync(args[0]);
    using var engine = new TesseractEngine(tessdata, "eng", EngineMode.LstmOnly);
    engine.SetVariable("preserve_interword_spaces", true);
    engine.SetVariable("user_defined_dpi", 300);
    using var image = Pix.LoadFromMemory(imageBytes);

    var recognizedText = new List<string>();
    foreach (var mode in new[] { PageSegMode.Auto, PageSegMode.SingleBlock, PageSegMode.SparseText })
    {
        using var page = engine.Process(image, mode);
        var text = page.GetText();
        if (!string.IsNullOrWhiteSpace(text))
            recognizedText.Add(text);
    }

    Console.Out.Write(JsonSerializer.Serialize(new { text = string.Join(Environment.NewLine, recognizedText) }));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
