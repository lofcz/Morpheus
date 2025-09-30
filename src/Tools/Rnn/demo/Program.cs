using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Tokenizers;
using Tokenizers.DotNet;

public class Program
{
    // Token-level IOB tag mapping (must match Python TAG_MAP indices)
    private static readonly string[] IdToTag = new[]
    {
        "O",      // 0
        "B-PER",  // 1
        "I-PER",  // 2
        "B-NICK", // 3
        "I-NICK", // 4
        "B-ORG",  // 5
        "I-ORG",  // 6
        "B-LOC",  // 7
        "I-LOC",  // 8
        "B-TIT",  // 9
        "I-TIT"   // 10
    };

    private const int MaxLength = 128; // Must match the MAX_LEN from Python training
    private const int CharMaxLen = 24; // Must match CHAR_MAX_LEN in Python

    public static void Main(string[] args)
    {
        Console.WriteLine("--- C# DEMO SCRIPT STARTED ---");
        Console.WriteLine("--- ONNX NER Demo (token-level) ---");

        var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var executionPath = Path.GetDirectoryName(assemblyLocation)!;
        var modelPath = "name_classifier.onnx";
        var tokenizerPath = "custom-bpe-tokenizer.json";

        if (!File.Exists(modelPath) || !File.Exists(tokenizerPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Model or tokenizer file not found.");
            Console.ResetColor();
            return;
        }

        // --- Tokenizer ---
        var tokenizerJson = File.ReadAllText(tokenizerPath);
        var tokenizer = new Tokenizer(tokenizerPath);
        var padTokenId = GetTokenIdFromJson(tokenizerJson, "[PAD]");

        // --- ONNX session ---
        using var session = new InferenceSession(modelPath);

        var testInputs = new List<string>
        {
            "ing. jan novák phd",
            "mvdr. markéta tučková , ten tenths media gmbh",
            "Pepa, Novák",
            "Honza Stránský",
            "1.KFC Dačice",
            "xX_SuperGamer_Xx",
            "Microsoft",
            "Rev. John Doe, Ph.D.",
            "lordoffuel"
        };

        Console.WriteLine("\n--- Running Predictions ---");
        foreach (var text in testInputs)
        {
            Predict(session, tokenizer, text, padTokenId);
        }

        Console.WriteLine("\n--- Demo Finished ---");
    }

    private static void Predict(InferenceSession session, Tokenizer tokenizer, string text, long padTokenId)
    {
        var encoding = tokenizer.Encode(text);
        var ids = encoding.Ids.Select(i => (long)i).ToList();
        var wordIds = encoding.WordIds?.ToList() ?? Enumerable.Repeat<long?>(null, encoding.Ids.Count).ToList();
        var tokens = encoding.Tokens.ToList();

        // Pad/truncate
        if (ids.Count > MaxLength)
        {
            ids = ids.Take(MaxLength).ToList();
            wordIds = wordIds.Take(MaxLength).ToList();
            tokens = tokens.Take(MaxLength).ToList();
        }
        else if (ids.Count < MaxLength)
        {
            var padCount = MaxLength - ids.Count;
            ids.AddRange(Enumerable.Repeat(padTokenId, padCount));
            wordIds.AddRange(Enumerable.Repeat<long?>(null, padCount));
            tokens.AddRange(Enumerable.Repeat("[PAD]", padCount));
        }

        var inputTensor = new DenseTensor<long>(ids.ToArray(), new[] { 1, MaxLength });
        // Prepare byte_ids [1, L, CharMaxLen]
        var byteMatrix = new long[1, MaxLength, CharMaxLen];
        for (int i = 0; i < MaxLength; i++)
        {
            string piece = tokens[i];
            if (piece.StartsWith("##")) piece = piece.Substring(2);
            var bytes = Encoding.UTF8.GetBytes(piece);
            int len = Math.Min(bytes.Length, CharMaxLen);
            for (int j = 0; j < len; j++) byteMatrix[0, i, j] = bytes[j];
            for (int j = len; j < CharMaxLen; j++) byteMatrix[0, i, j] = 0;
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputTensor),
            NamedOnnxValue.CreateFromTensor("byte_ids", new DenseTensor<long>(byteMatrix, new[] { 1, MaxLength, CharMaxLen }))
        };

        using var results = session.Run(inputs);
        var output = results.First().AsTensor<float>(); // [1, L, C]

        var dims = output.Dimensions.ToArray();
        int L = dims[1];
        int C = dims[2];

        // Argmax per position
        var flat = output.ToArray();
        var predIdx = new int[L];
        for (int i = 0; i < L; i++)
        {
            int arg = 0;
            float best = float.NegativeInfinity;
            int baseIdx = i * C; // row-major within last dim
            for (int j = 0; j < C; j++)
            {
                float v = flat[baseIdx + j];
                if (v > best) { best = v; arg = j; }
            }
            predIdx[i] = arg;
        }
        var predTags = predIdx.Select(i => i >= 0 && i < IdToTag.Length ? IdToTag[i] : "O").ToArray();
        predTags = EnforceIob(predTags);

        // Word-level mapping: take first subtoken tag for each word
        var words = new List<string>();
        var wordTags = new List<string>();
        long? prevWid = null;
        string currentWord = "";
        for (int i = 0; i < L; i++)
        {
            var wid = wordIds[i];
            if (wid == null) continue;
            if (prevWid == null || wid != prevWid)
            {
                words.Add(NormalizePieces(tokens, i));
                wordTags.Add(predTags[i]);
                prevWid = wid;
            }
        }

        var entities = ExtractEntities(words, wordTags);

        Console.WriteLine($"\nInput: \"{text}\"");
        Console.WriteLine("Words:   " + string.Join(" | ", words));
        Console.WriteLine("Tags:    " + string.Join(" | ", wordTags));
        if (entities.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Entities:");
            foreach (var (et, ty) in entities)
                Console.WriteLine($" - {et} [{ty}]");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine("Entities: (none)");
        }
    }
    
    // Manually parses the tokenizer JSON to find a specific token's ID.
    private static long GetTokenIdFromJson(string json, string token)
    {
        using var doc = JsonDocument.Parse(json);
        var vocab = doc.RootElement.GetProperty("model").GetProperty("vocab");
        if (vocab.TryGetProperty(token, out var tokenElement))
        {
            return tokenElement.GetInt64();
        }
        // Fallback if the token isn't found
        if (vocab.TryGetProperty("[UNK]", out var unkTokenElement))
        {
            return unkTokenElement.GetInt64();
        }
        return 0;
    }

    private static string[] EnforceIob(string[] tags)
    {
        var fixedTags = new string[tags.Length];
        string prevType = null;
        bool inside = false;
        for (int i = 0; i < tags.Length; i++)
        {
            var t = tags[i];
            if (t == "O") { fixedTags[i] = "O"; prevType = null; inside = false; continue; }
            var parts = t.Split('-');
            if (parts.Length != 2) { fixedTags[i] = "O"; prevType = null; inside = false; continue; }
            var bio = parts[0];
            var ent = parts[1];
            if (bio == "B") { fixedTags[i] = $"B-{ent}"; prevType = ent; inside = true; }
            else {
                if (!inside || prevType != ent) { fixedTags[i] = $"B-{ent}"; prevType = ent; inside = true; }
                else { fixedTags[i] = $"I-{ent}"; }
            }
        }
        return fixedTags;
    }

    private static string NormalizePieces(IReadOnlyList<string> tokens, int index)
    {
        // Join just the first piece token; full reconstruction across multiple pieces requires word offsets.
        var t = tokens[index];
        return t.StartsWith("##") ? t.Substring(2) : t;
    }

    private static List<(string, string)> ExtractEntities(List<string> words, List<string> wordTags)
    {
        var entities = new List<(string, string)>();
        var current = new List<string>();
        string currentType = null;
        for (int i = 0; i < words.Count; i++)
        {
            var t = wordTags[i];
            if (t == "O")
            {
                if (current.Count > 0)
                {
                    entities.Add((string.Join(" ", current), currentType ?? ""));
                    current.Clear();
                    currentType = null;
                }
                continue;
            }
            var parts = t.Split('-');
            var bio = parts[0];
            var ent = parts.Length > 1 ? parts[1] : "";
            if (bio == "B" || (currentType != null && ent != currentType))
            {
                if (current.Count > 0)
                    entities.Add((string.Join(" ", current), currentType ?? ""));
                current.Clear();
                current.Add(words[i]);
                currentType = ent;
            }
            else
            {
                current.Add(words[i]);
                currentType = ent;
            }
        }
        if (current.Count > 0)
            entities.Add((string.Join(" ", current), currentType ?? ""));
        return entities;
    }
}
