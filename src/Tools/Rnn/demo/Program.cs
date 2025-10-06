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
        // Encode returns uint[] - just the token IDs
        var tokenIds = tokenizer.Encode(text);
        var ids = tokenIds.Select(i => (long)i).ToList();
        
        // Store original length before padding
        int originalLength = ids.Count;

        // Pad/truncate token IDs
        if (ids.Count > MaxLength)
        {
            ids = ids.Take(MaxLength).ToList();
            originalLength = MaxLength;
        }
        else if (ids.Count < MaxLength)
        {
            var padCount = MaxLength - ids.Count;
            ids.AddRange(Enumerable.Repeat(padTokenId, padCount));
        }

        var inputTensor = new DenseTensor<long>(ids.ToArray(), new[] { 1, MaxLength });
        
        // Prepare byte_ids [1, L, CharMaxLen]
        // Since we don't have individual tokens, we'll reconstruct text from original input
        // Split text into words for byte encoding
        var words = text.Split(new[] { ' ', ',', '.', ';', ':', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        var byteData = new long[1 * MaxLength * CharMaxLen];
        
        // Approximate: encode each token position with text bytes
        // This is a simplified approach since we don't have exact token strings
        var textBytes = Encoding.UTF8.GetBytes(text);
        int tokenIndex = 0;
        int charPos = 0;
        
        // Distribute text bytes across tokens proportionally
        int bytesPerToken = Math.Max(1, textBytes.Length / Math.Max(1, originalLength));
        for (int i = 0; i < originalLength && i < MaxLength; i++)
        {
            int endPos = Math.Min(charPos + bytesPerToken, textBytes.Length);
            int len = Math.Min(endPos - charPos, CharMaxLen);
            
            for (int j = 0; j < len && charPos < textBytes.Length; j++)
            {
                byteData[i * CharMaxLen + j] = textBytes[charPos++];
            }
        }
        
        var byteTensor = new DenseTensor<long>(byteData, new[] { 1, MaxLength, CharMaxLen });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputTensor),
            NamedOnnxValue.CreateFromTensor("byte_ids", byteTensor)
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
            int baseIdx = i * C;
            for (int j = 0; j < C; j++)
            {
                float v = flat[baseIdx + j];
                if (v > best) { best = v; arg = j; }
            }
            predIdx[i] = arg;
        }
        
        // Get predicted tags (only for non-padded positions)
        var predTags = predIdx.Take(originalLength)
            .Select(i => i >= 0 && i < IdToTag.Length ? IdToTag[i] : "O")
            .ToArray();
        predTags = EnforceIob(predTags);

        // Extract entities from consecutive B/I tags
        var entities = new List<(string, string)>();
        var currentEntity = new List<string>();
        string currentType = null;
        
        // Since we don't have word boundaries, we'll work with the original words
        var wordList = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        int wordsPerToken = Math.Max(1, wordList.Count / Math.Max(1, originalLength));
        
        for (int i = 0; i < predTags.Length; i++)
        {
            var tag = predTags[i];
            
            if (tag == "O")
            {
                if (currentEntity.Count > 0)
                {
                    entities.Add((string.Join(" ", currentEntity), currentType ?? ""));
                    currentEntity.Clear();
                    currentType = null;
                }
            }
            else if (tag.Contains("-"))
            {
                var parts = tag.Split('-');
                var bio = parts[0];
                var ent = parts[1];
                
                if (bio == "B" || currentType != ent)
                {
                    if (currentEntity.Count > 0)
                    {
                        entities.Add((string.Join(" ", currentEntity), currentType ?? ""));
                    }
                    currentEntity.Clear();
                    currentType = ent;
                }
                
                // Add approximate words for this token
                int wordStart = i * wordsPerToken;
                int wordEnd = Math.Min(wordStart + wordsPerToken, wordList.Count);
                for (int w = wordStart; w < wordEnd; w++)
                {
                    currentEntity.Add(wordList[w]);
                }
            }
        }
        
        if (currentEntity.Count > 0)
        {
            entities.Add((string.Join(" ", currentEntity), currentType ?? ""));
        }

        Console.WriteLine($"\nInput: \"{text}\"");
        Console.WriteLine($"Tokens: {originalLength} token(s)");
        Console.WriteLine($"Tags: {string.Join(" ", predTags)}");
        
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

}
