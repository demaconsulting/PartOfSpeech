using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DemaConsulting.WordSense;

// WordSenseBasicTags: part-of-speech mass of every token from the WordSense Basic classifier (HMM tagger plus cue model).
//
//   dotnet run -c Release --project tools/data/hmm-tags -p:WordSenseDll=PATH -- --in FILE --out FILE --wordsense-commit SHA
//
// --in   tokenized sentences, one sentence per line: either a tokens.tsv of the shapes development tools (header
//        `text<TAB>sentences<TAB>tokens`; only rows with one sentence are tagged) or plain lines of tokens that are
//        separated by single spaces (blank lines and lines that start with `#` are skipped)
// --out  a header of `# key = value` lines (generator, WordSense commit, SHA-256 of the DLL that ran, SHA-256 of the input,
//        sentence count), then one line per sentence in input order; a line holds one tab-separated cell
//        `form|POS|pNoun|pVerb` per token. POS is the part of speech with the most probability mass (`?` when the classifier
//        has no analysis), pNoun and pVerb are the summed analysis probabilities of the parts of speech Noun and Verb with
//        three decimals (invariant culture). A reader splits a cell from the right (a form may contain `|`).
//
// The tool reads only --in and the DLL, writes only --out, and never touches a model file of the PartOfSpeech library.
// The output is deterministic for one DLL and one input.
const string Usage = "usage: WordSenseBasicTags --in FILE --out FILE --wordsense-commit SHA";
const string TokensHeader = "text\tsentences\ttokens";

string? input = null;
string? output = null;
string? commit = null;
for (var i = 0; i < args.Length; i++)
{
    var name = args[i];
    if (i + 1 >= args.Length || name is not ("--in" or "--out" or "--wordsense-commit"))
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }

    var value = args[++i];
    switch (name)
    {
        case "--in":
            input = value;
            break;
        case "--out":
            output = value;
            break;
        default:
            commit = value;
            break;
    }
}

if (input is null || output is null || commit is null)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var sentences = new List<string[]>();
var lines = File.ReadAllLines(input, Encoding.UTF8);
var tabular = lines.Length > 0 && lines[0] == TokensHeader;
foreach (var line in lines.Skip(tabular ? 1 : 0))
{
    string tokenText;
    if (tabular)
    {
        var cells = line.Split('\t');
        if (cells.Length != 3)
        {
            Console.Error.WriteLine("a row of the tokens table does not have 3 cells");
            return 1;
        }

        if (cells[1] != "1")
        {
            continue;
        }

        tokenText = cells[2];
    }
    else
    {
        if (line.Length == 0 || line.StartsWith('#'))
        {
            continue;
        }

        tokenText = line;
    }

    var tokens = tokenText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (tokens.Length > 0)
    {
        sentences.Add(tokens);
    }
}

var dllPath = typeof(BasicWordSenseModel).Assembly.Location;
using var model = new BasicWordSenseModel();
var builder = new StringBuilder();
builder.Append("# hmm-tags 1: part-of-speech mass per token of the WordSense Basic classifier (HMM tagger plus cue model)\n");
builder.Append("# wordsense_commit = ").Append(commit).Append('\n');
builder.Append("# wordsense_dll_sha256 = ").Append(Sha256(File.ReadAllBytes(dllPath))).Append('\n');
builder.Append("# input_sha256 = ").Append(Sha256(File.ReadAllBytes(input))).Append('\n');
builder.Append("# sentences = ").Append(sentences.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
builder.Append("# cell = form|POS|pNoun|pVerb (one line per sentence, in input order)\n");
foreach (var tokens in sentences)
{
    var results = await model.ClassifyAsync(tokens);
    for (var i = 0; i < tokens.Length; i++)
    {
        // The probability mass of every part of speech over the analyses of the token; the answer is the part of speech with most mass.
        var mass = new Dictionary<PartOfSpeech, double>();
        foreach (var analysis in results[i].Analyses)
        {
            var part = analysis.Details.PartOfSpeech;
            mass[part] = mass.GetValueOrDefault(part) + analysis.Probability;
        }

        var best = mass.Count == 0 ? "?" : mass.OrderByDescending(pair => pair.Value).First().Key.ToString();
        builder.Append(i == 0 ? "" : "\t").Append(tokens[i]).Append('|').Append(best)
            .Append('|').Append(mass.GetValueOrDefault(PartOfSpeech.Noun).ToString("0.000", CultureInfo.InvariantCulture))
            .Append('|').Append(mass.GetValueOrDefault(PartOfSpeech.Verb).ToString("0.000", CultureInfo.InvariantCulture));
    }

    builder.Append('\n');
}

File.WriteAllText(output, builder.ToString(), new UTF8Encoding(false));
Console.WriteLine($"{sentences.Count} sentences tagged; wordsense dll sha256 {Sha256(File.ReadAllBytes(dllPath))}");
return 0;

static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
