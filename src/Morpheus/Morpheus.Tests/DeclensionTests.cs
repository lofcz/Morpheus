using System.Data;
using System.Linq;
using System.Text;
using ExcelDataReader;

namespace Morpheus.Tests;

public class DeclensionTests
{
    [Test]
    public void Decline_Hana_Skalicka_AllowsTitlesOmissionAndDetectsFemale()
    {
        var tt = "Liška Michal";
        var xx = Declension.Decline(tt, CzechCase.Vocative, new DeclensionOptions { OmitTitles = false });
        
        var input = "Hana Skalická";

        var resultGen = Morpheus.Declension.Decline(input, CzechCase.Genitive);
        Assert.That(resultGen.Output, Is.EqualTo("Hany Skalické"));

        var resultDat = Morpheus.Declension.Decline(input, CzechCase.Dative);
        Assert.That(resultDat.Output, Is.EqualTo("Haně Skalické"));

        var resultAcc = Morpheus.Declension.Decline(input, CzechCase.Accusative);
        Assert.That(resultAcc.Output, Is.EqualTo("Hanu Skalickou"));

        var resultVoc = Morpheus.Declension.Decline(input, CzechCase.Vocative, new DeclensionOptions { OmitTitles = false });
        Assert.That(resultVoc.Output, Is.EqualTo("Hano Skalická"));
        
        var resultLoc = Morpheus.Declension.Decline(input, CzechCase.Locative);
        Assert.That(resultLoc.Output, Is.EqualTo("Haně Skalické"));

        var resultIns = Morpheus.Declension.Decline(input, CzechCase.Instrumental);
        Assert.That(resultIns.Output, Is.EqualTo("Hanou Skalickou"));
    }

    [Test]
    public void Options_OmitFirstOrLastName()
    {
        var input = "Hana Skalická";
        var onlySurname = Morpheus.Declension.Decline(input, CzechCase.Genitive, new DeclensionOptions { OmitFirstName = true });
        Assert.That(onlySurname.Output, Does.Not.Contain("Hany"));
        Assert.That(onlySurname.Output, Is.Not.Empty);

        var onlyFirstName = Morpheus.Declension.Decline(input, CzechCase.Genitive, new DeclensionOptions { OmitLastName = true });
        Assert.That(onlyFirstName.Output, Does.Contain("Hany"));
        Assert.That(onlyFirstName.Output.Split(' ').Length, Is.EqualTo(1));
    }

    [Test]
    public void Company_IsDetectedAndNotDeclined()
    {
        var input = "ACME s.r.o.";
        var res = Morpheus.Declension.Decline(input, CzechCase.Dative);
        Assert.That(res.EntityType, Is.EqualTo(DetectedEntityType.Company));
        Assert.That(res.Output, Is.EqualTo(input));
    }

    [Test]
    public void VocativeTestCases_ShouldDeclineCorrectly()
    {
        var testCases = LoadTestCasesFromFile("vocative.txt", CzechCase.Vocative).ToList();
        var failures = new List<string>();
        var passed = 0;
        
        foreach (var (input, czechCase, expected) in testCases)
        {
            var result = Morpheus.Declension.Decline(input, czechCase);
            var acceptableOutputs = ParseAcceptableOutputs(expected);
            
            if (acceptableOutputs.Contains(result.Output))
            {
                passed++;
            }
            else
            {
                var expectedDisplay = acceptableOutputs.Count > 1 
                    ? $"one of [{string.Join(", ", acceptableOutputs.Select(o => $"'{o}'"))}]"
                    : $"'{expected}'";
				failures.Add($"{input} → Expected: {expectedDisplay}, Got: '{result.Output}' ({DisplayGender(result.Gender)})");
            }
        }
        
        Console.WriteLine($"Test Results: {passed} passed, {failures.Count} failed out of {testCases.Count} total");
        Console.WriteLine($"Success rate: {(double)passed / testCases.Count:P1}");
        
        if (failures.Count > 0)
        {
            Console.WriteLine("\nAll failures:");
            for (int i = 0; i < failures.Count; i++)
            {
                Console.WriteLine($"  {i + 1}) {failures[i]}");
            }
        }
        
        // Fail if there are any failures
        if (failures.Count > 0)
        {
            var successRate = (double)passed / testCases.Count;
            // Just throw a simple exception without Assert framework overhead
            throw new Exception($"{failures.Count} test failures found. Success rate: {successRate:P1}. See console output above for details.");
        }
    }
    
    static string DisplayGender(DetectedGender gender)
    {
        return gender switch
        {
            DetectedGender.Masculine => "MALE",
            DetectedGender.Feminine => "FEMALE",
            _ => "ANDROGENOUS"
        };
    }

    private static IEnumerable<(string input, CzechCase czechCase, string expected)> LoadTestCasesFromFile(string filename, CzechCase czechCase)
    {
        var testFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, filename);
        if (!File.Exists(testFilePath))
        {
            // Try relative to the test project directory
            testFilePath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!, filename);
        }
        
        if (!File.Exists(testFilePath))
        {
            Assert.Fail($"Test data file not found: {filename}");
        }
        
        var lines = File.ReadAllLines(testFilePath);
        
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith("#"))
                continue; // Skip empty lines and comments
                
            var parts = line.Split('|', 2);
            if (parts.Length != 2)
                continue; // Skip malformed lines
                
            var input = parts[0].Trim();
            var expected = parts[1].Trim();
            
            yield return (input, czechCase, expected);
        }
    }

    /// <summary>
    /// Parse acceptable outputs from test data, handling multiple forms
    /// Supports:
    /// 1. Bracketed alternatives: "Šnajdrov(a/á)" -> ["Šnajdrova", "Šnajdrová"]
    /// 2. Slash-separated: "Šnajdrova / Šnajdrová" -> ["Šnajdrova", "Šnajdrová"]
    /// 3. Simple form: "Šnajdrova" -> ["Šnajdrova"]
    /// </summary>
    private static List<string> ParseAcceptableOutputs(string expected)
    {
        var outputs = new List<string>();
        
        // Handle bracketed alternatives first: "prefix(option1/option2)suffix"
        var bracketStart = expected.IndexOf('(');
        var bracketEnd = expected.IndexOf(')', bracketStart + 1);
        
        if (bracketStart >= 0 && bracketEnd > bracketStart)
        {
            var prefix = expected.Substring(0, bracketStart);
            var suffix = expected.Substring(bracketEnd + 1);
            var options = expected.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
            
            var optionParts = options.Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (var option in optionParts)
            {
                outputs.Add(prefix + option.Trim() + suffix);
            }
            return outputs;
        }
        
        // Handle slash-separated alternatives: "form1 / form2" (only if no brackets)
        if (expected.Contains('/'))
        {
            var parts = expected.Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                outputs.Add(part.Trim());
            }
            return outputs;
        }
        
        // Simple form - no alternatives
        outputs.Add(expected);
        return outputs;
    }

    private static void AssertDeclension(string input, CzechCase czechCase, string expected)
    {
        var result = Morpheus.Declension.Decline(input, czechCase);
        var acceptableOutputs = ParseAcceptableOutputs(expected);
        
        if (!acceptableOutputs.Contains(result.Output))
        {
            var expectedDisplay = acceptableOutputs.Count > 1 
                ? $"one of [{string.Join(", ", acceptableOutputs.Select(o => $"'{o}'"))}]"
                : $"'{expected}'";
            Assert.Fail($"Failed for input '{input}' in case {czechCase}. Expected {expectedDisplay}, got '{result.Output}'");
        }
    }

    [Test]
    [Explicit] // Only run when explicitly requested
    public void ExtractExcelDataToVocativeFile()
    {
        // Register encoding provider for Excel files
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        
        var excelPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "ref", "source1.xlsx");
        if (!File.Exists(excelPath))
        {
            excelPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!, "ref", "source1.xlsx");
        }
        
        Assert.That(File.Exists(excelPath), $"Excel file not found: {excelPath}");
        
        var extractedCases = ExtractTestCasesFromExcel(excelPath);
        
        var vocativePath = Path.Combine(Path.GetDirectoryName(excelPath)!, "..", "vocative.txt");
        AppendTestCasesToVocativeFile(vocativePath, extractedCases);
        
        Console.WriteLine($"Extracted {extractedCases.Count} test cases from Excel file");
        Console.WriteLine($"Appended to: {vocativePath}");
    }

    private static List<(string input, string expected)> ExtractTestCasesFromExcel(string excelPath)
    {
        var testCases = new List<(string input, string expected)>();
        
        using var stream = File.Open(excelPath, FileMode.Open, FileAccess.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var dataSet = reader.AsDataSet();
        
        if (dataSet.Tables.Count == 0)
        {
            throw new InvalidOperationException("No worksheets found in Excel file");
        }
        
        var table = dataSet.Tables[0]; // First worksheet
        
        // Start from row 3 (index 2), skip header rows
        for (int i = 2; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            
            // Column A (index 0) = input, Column C (index 2) = expected output
            var columnA = row[0]?.ToString()?.Trim();
            var columnC = row.ItemArray.Length > 2 ? row[2]?.ToString()?.Trim() : null;
            
            if (!string.IsNullOrWhiteSpace(columnA) && !string.IsNullOrWhiteSpace(columnC))
            {
                testCases.Add((columnA, columnC));
            }
        }
        
        return testCases;
    }

    private static void AppendTestCasesToVocativeFile(string vocativePath, List<(string input, string expected)> testCases)
    {
        var lines = new List<string>();
        
        // Read existing content if file exists
        if (File.Exists(vocativePath))
        {
            lines.AddRange(File.ReadAllLines(vocativePath));
        }
        
        // Add extracted test cases
        foreach (var (input, expected) in testCases)
        {
            var testCaseLine = $"{input} | {expected}";
            if (!lines.Contains(testCaseLine))
            {
                lines.Add(testCaseLine);
            }
        }
        
        // Write back to file
        File.WriteAllLines(vocativePath, lines);
    }
}


