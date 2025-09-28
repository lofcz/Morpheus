using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text.Json;
using Tokenizers;
using Tokenizers.DotNet;

public class Program
{
    // Corresponds to the labels used during Python training: 0=Name, 1=Nickname, 2=Company
    private static readonly string[] Labels = { "Name", "Nickname", "Company" };
    private const int MaxLength = 128; // Must match the MAX_LEN from Python training

    public static void Main(string[] args)
    {
        Console.WriteLine("--- C# DEMO SCRIPT STARTED ---");
        Console.WriteLine("--- ONNX Name Classifier Demo ---");

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

        // --- Correct Tokenizer Loading ---
        // 1. Read the JSON file content
        var tokenizerJson = File.ReadAllText(tokenizerPath);
        
        // 2. Create the tokenizer instance from the JSON content
        var tokenizer = new Tokenizer("custom-bpe-tokenizer.json");

        // 3. Manually parse the JSON to get the Pad Token ID, as GetPadTokenId() doesn't exist
        var padTokenId = GetTokenIdFromJson(tokenizerJson, "[PAD]");

        // Initialize the ONNX runtime session
        using var session = new InferenceSession(modelPath);

        var testInputs = new List<string>
        {
            "John Smith",
            "Honza Stránský",
            "1.KFC Dačice",
            "xX_SuperGamer_Xx",
            "Microsoft",
            "Dr. Eleanor Vance",
            "The quick brown fox",
            "Staglin",
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
        var encodedIds = tokenizer.Encode(text);
        var inputIds = encodedIds.Select(id => (long)id).ToList();

        while (inputIds.Count < MaxLength)
        {
            inputIds.Add(padTokenId);
        }

        if (inputIds.Count > MaxLength)
        {
            inputIds = inputIds.Take(MaxLength).ToList();
        }

        var dimensions = new[] { 1, MaxLength };
        var inputTensor = new DenseTensor<long>(inputIds.ToArray(), dimensions);
        
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputTensor)
        };

        using var results = session.Run(inputs);
        var outputTensor = results.First().AsTensor<float>();

        var probabilities = Softmax(outputTensor.ToArray());
        var predictedIndex = Array.IndexOf(probabilities, probabilities.Max());
        var predictedLabel = Labels[predictedIndex];
        var confidence = probabilities[predictedIndex];

        Console.WriteLine($"\nInput: \"{text}\"");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  => Predicted: {predictedLabel} (Confidence: {confidence:P2})");
        Console.ResetColor();
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

    private static float[] Softmax(float[] logits)
    {
        var maxLogit = logits.Max();
        var exps = logits.Select(l => MathF.Exp(l - maxLogit)).ToArray();
        var sumExps = exps.Sum();
        return exps.Select(e => e / sumExps).ToArray();
    }
}
