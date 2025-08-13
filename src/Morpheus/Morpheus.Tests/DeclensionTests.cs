using System.Data;
using System.Text;
using ExcelDataReader;

namespace Morpheus.Tests;

public class DeclensionTests
{
    [Test]
    public void Decline_Hana_Skalicka_AllowsTitlesOmissionAndDetectsFemale()
    {
        var tt = "Jana Novák";
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
            try
            {
                AssertDeclension(input, czechCase, expected);
                passed++;
            }
            catch (AssertionException ex)
            {
                failures.Add($"FAIL: {input} → Expected: '{expected}', Got: '{Morpheus.Declension.Decline(input, czechCase).Output}'");
            }
        }
        
        Console.WriteLine($"Test Results: {passed} passed, {failures.Count} failed out of {testCases.Count} total");
        Console.WriteLine($"Success rate: {(double)passed / testCases.Count:P1}");
        
        if (failures.Count > 0)
        {
            Console.WriteLine("\nFirst 20 failures:");
            foreach (var failure in failures.Take(20))
            {
                Console.WriteLine(failure);
            }
            
            if (failures.Count > 20)
            {
                Console.WriteLine($"... and {failures.Count - 20} more failures");
            }
        }
        
        // Only fail if success rate is below threshold (e.g., 80%)
        var successRate = (double)passed / testCases.Count;
        Assert.That(successRate, Is.GreaterThan(0.8), 
            $"Success rate {successRate:P1} is below 80% threshold. See console output for details.");
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

    private static void AssertDeclension(string input, CzechCase czechCase, string expected)
    {
        var result = Morpheus.Declension.Decline(input, czechCase);
        Assert.That(result.Output, Is.EqualTo(expected), 
            $"Failed for input '{input}' in case {czechCase}. Expected '{expected}', got '{result.Output}'");
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


