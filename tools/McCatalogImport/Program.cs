using System.Text.Json;

// Regenerates src/Gweb.Adapters.Mcc/Resources/mcc-codes.json from the checked-in
// source-of-truth file. See docs/adr/0004-mcc-catalog-storage.md for the refresh
// process this implements and why the source is a plain pipe-delimited file rather
// than a live scrape of the (paid/licensed) Visa Merchant Data Standards Manual.
//
// Usage: dotnet run --project tools/McCatalogImport
//   [-- <path-to-source> <path-to-output>]   (both optional, defaults below)

var sourcePath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "source", "mcc-codes-source.psv");
var outputPath = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Gweb.Adapters.Mcc", "Resources", "mcc-codes.json");

sourcePath = Path.GetFullPath(sourcePath);
outputPath = Path.GetFullPath(outputPath);

Console.WriteLine($"Reading source: {sourcePath}");

var lines = File.ReadAllLines(sourcePath);
if (lines.Length == 0)
{
    Fail("Source file is empty.");
}

var header = lines[0].Split('|');
if (header is not ["code", "description", "category"])
{
    Fail($"Expected header 'code|description|category', got '{lines[0]}'.");
}

var entries = new List<McEntry>();
var seenCodes = new HashSet<string>();

for (var lineNumber = 2; lineNumber <= lines.Length; lineNumber++)
{
    var line = lines[lineNumber - 1];
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    var fields = line.Split('|');
    if (fields.Length != 3)
    {
        Fail($"Line {lineNumber}: expected 3 pipe-delimited fields, got {fields.Length}: '{line}'");
    }

    var code = fields[0].Trim();
    var description = fields[1].Trim();
    var category = fields[2].Trim();

    if (code.Length != 4 || !code.All(char.IsAsciiDigit))
    {
        Fail($"Line {lineNumber}: '{code}' is not a 4-digit MCC code.");
    }
    if (string.IsNullOrWhiteSpace(description))
    {
        Fail($"Line {lineNumber}: empty description for code {code}.");
    }
    if (string.IsNullOrWhiteSpace(category))
    {
        Fail($"Line {lineNumber}: empty category for code {code}.");
    }
    if (!seenCodes.Add(code))
    {
        Fail($"Line {lineNumber}: duplicate code {code}.");
    }

    entries.Add(new McEntry(code, description, category));
}

entries.Sort((a, b) => string.CompareOrdinal(a.Code, b.Code));

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(outputPath, json + Environment.NewLine);

Console.WriteLine($"Wrote {entries.Count} MCC codes to: {outputPath}");
return;

static void Fail(string message)
{
    Console.Error.WriteLine($"ERROR: {message}");
    Environment.Exit(1);
}

internal sealed record McEntry(string Code, string Description, string Category);
