using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExcelDataReader;
using Morpheus;

namespace Morpheus.Parser;

class Program
{
    // Configuration for androgynous name classification
    private const float DominanceThreshold = 0.99f; // 99% or more to be considered dominant
    private const int MinimumSampleSize = 10; // Minimum total occurrences to apply dominance rule
    
    static void Main(string[] args)
    {
        // Register encoding provider for older Excel files
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        
        string excelPath = "jmena.xls";
        string jsonOutputPath = "names_output.json";
        string indexOutputPath = "names_index.bk";
        
        Console.WriteLine("Parsing Czech government name data...");
        
        try
        {
            // Check if index already exists
            if (File.Exists(indexOutputPath))
            {
                Console.WriteLine($"Found existing index: {indexOutputPath}");
                Console.WriteLine("Loading existing index and performing search demonstration...\n");
                
                // Demonstrate loading and searching existing index
                DemonstrateExistingIndex(indexOutputPath);
                return;
            }
            
            Console.WriteLine("No existing index found. Building new index from Excel file...\n");
            
            // Parse the Excel file
            var nameEntries = ParseExcelFile(excelPath);
            Console.WriteLine($"Parsed {nameEntries.Count} unique names.");
            
            // Generate JSON file
            GenerateJsonFile(nameEntries, jsonOutputPath);
            Console.WriteLine($"Generated JSON file: {jsonOutputPath}");
            
            // Build BK tree index
            BKTreeBuilder.BuildIndex(jsonOutputPath, indexOutputPath);
            Console.WriteLine($"Built BK tree index: {indexOutputPath}");
            
            // Demonstrate usage
            DemonstrateUsage(indexOutputPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
    
    static List<ProcessedNameEntry> ParseExcelFile(string excelPath)
    {
        var maleNames = new Dictionary<string, int>();
        var femaleNames = new Dictionary<string, int>();
        
        using var stream = File.Open(excelPath, FileMode.Open, FileAccess.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var dataSet = reader.AsDataSet();
        
        // Parse male names (first worksheet: "Mužská jména")
        if (dataSet.Tables.Count > 0)
        {
            ParseWorksheet(dataSet.Tables[0], maleNames, "Male names");
        }
        
        // Parse female names (second worksheet: "Ženská jména")
        if (dataSet.Tables.Count > 1)
        {
            ParseWorksheet(dataSet.Tables[1], femaleNames, "Female names");
        }
        
        // Save debug information
        SaveDebugInfo(maleNames, femaleNames);
        
        return ProcessNames(maleNames, femaleNames);
    }
    
    static void ParseWorksheet(DataTable table, Dictionary<string, int> nameDict, string sheetType)
    {
        Console.WriteLine($"Processing {sheetType} worksheet with {table.Rows.Count} rows...");
        
        for (int i = 2; i < table.Rows.Count; i++) // Start from row 3 (index 2)
        {
            var row = table.Rows[i];
            
            // Skip empty rows
            if (row.IsNull(1) || row.IsNull(2)) continue;
            
            try
            {
                string nameText = row[1]?.ToString()?.Trim() ?? "";
                string countText = row[2]?.ToString()?.Trim() ?? "";
                
                if (string.IsNullOrEmpty(nameText) || string.IsNullOrEmpty(countText))
                    continue;
                
                if (!int.TryParse(countText, out int count))
                    continue;
                
                // Normalize the name
                string normalizedName = NormalizeName(nameText);
                
                if (!string.IsNullOrEmpty(normalizedName))
                {
                    // Debug output for specific names we're interested in
                    if (normalizedName.ToLower().Contains("jiří") || normalizedName.ToLower().Contains("jiri"))
                    {
                        Console.WriteLine($"DEBUG: Found '{nameText}' -> normalized to '{normalizedName}' with count {count} in {sheetType}");
                    }
                    
                    // Add to existing count if the name already exists, rather than overwriting
                    if (nameDict.ContainsKey(normalizedName))
                    {
                        nameDict[normalizedName] += count;
                    }
                    else
                    {
                        nameDict[normalizedName] = count;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing row {i + 1} in {sheetType}: {ex.Message}");
            }
        }
        
        Console.WriteLine($"Processed {nameDict.Count} names from {sheetType}");
    }
    
    static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        
        string originalName = name; // Store original for debugging
        
        // Convert to title case and trim
        name = name.Trim().ToUpperInvariant();
        
        // DON'T break up names - treat each full name as unique
        // Only normalize case, not structure
        // Convert to proper case (first letter uppercase, rest lowercase for each word)
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
            {
                words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
            }
        }
        name = string.Join(" ", words);
        
        // Handle hyphenated names similarly
        if (name.Contains('-'))
        {
            var hyphenParts = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < hyphenParts.Length; i++)
            {
                if (hyphenParts[i].Length > 0)
                {
                    hyphenParts[i] = char.ToUpper(hyphenParts[i][0]) + hyphenParts[i].Substring(1).ToLower();
                }
            }
            name = string.Join("-", hyphenParts);
        }
        
        // Debug output for normalization
        if (originalName.ToLower().Contains("jiří") || originalName.ToLower().Contains("jiri"))
        {
            Console.WriteLine($"DEBUG: Normalization: input='{originalName}' -> output='{name}'");
        }
        
        return name;
    }
    
    static string CreateIndexKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        
        // Create index-friendly version: lowercase + no diacritics + normalized
        return Normalizer.RemoveDiacritics(name).ToLowerInvariant().Trim();
    }
    
    static List<ProcessedNameEntry> ProcessNames(Dictionary<string, int> maleNames, Dictionary<string, int> femaleNames)
    {
        var results = new List<ProcessedNameEntry>();
        var processedNames = new HashSet<string>();
        
        // Find androgynous names (names that appear in both lists)
        var androgyneNames = maleNames.Keys.Intersect(femaleNames.Keys).ToHashSet();
        
        Console.WriteLine($"Found {androgyneNames.Count} names appearing in both male and female lists.");
        Console.WriteLine($"Applying dominance rule: {DominanceThreshold:P0}+ dominance with {MinimumSampleSize}+ total occurrences → classify as dominant gender");
        
        // Process androgynous names with dominance logic
        foreach (var name in androgyneNames)
        {
            int maleCount = maleNames[name];
            int femaleCount = femaleNames[name];
            int totalCount = maleCount + femaleCount;
            
            float maleRatio = (float)maleCount / totalCount;
            float femaleRatio = (float)femaleCount / totalCount;
            
            // Debug output for problematic cases
            Console.WriteLine($"DEBUG: Potential androgynous name '{name}' - Male: {maleCount} ({maleRatio:P1}), Female: {femaleCount} ({femaleRatio:P1}), Total: {totalCount}");
            
            GenderInfo gender;
            
            // Check if one gender is dominant (using configurable thresholds)
            if (totalCount >= MinimumSampleSize)
            {
                if (maleRatio >= DominanceThreshold)
                {
                    Console.WriteLine($"  -> Classified as Male (dominant with {maleRatio:P1})");
                    gender = new SimpleGender(SimpleGender.GenderType.Male);
                }
                else if (femaleRatio >= DominanceThreshold)
                {
                    Console.WriteLine($"  -> Classified as Female (dominant with {femaleRatio:P1})");
                    gender = new SimpleGender(SimpleGender.GenderType.Female);
                }
                else
                {
                    Console.WriteLine($"  -> Kept as Androgynous (no clear dominance: M={maleRatio:P1}, F={femaleRatio:P1})");
                    gender = new AndrogyneGender(maleRatio, femaleRatio);
                }
            }
            else
            {
                // For small samples, keep as androgynous regardless of ratio
                Console.WriteLine($"  -> Kept as Androgynous (insufficient sample size: {totalCount})");
                gender = new AndrogyneGender(maleRatio, femaleRatio);
            }
            
            results.Add(new ProcessedNameEntry(name, CreateIndexKey(name), gender));
            processedNames.Add(name);
        }
        
        // Process remaining male names
        foreach (var kvp in maleNames)
        {
            if (!processedNames.Contains(kvp.Key))
            {
                var gender = new SimpleGender(SimpleGender.GenderType.Male);
                results.Add(new ProcessedNameEntry(kvp.Key, CreateIndexKey(kvp.Key), gender));
                processedNames.Add(kvp.Key);
            }
        }
        
        // Process remaining female names
        foreach (var kvp in femaleNames)
        {
            if (!processedNames.Contains(kvp.Key))
            {
                var gender = new SimpleGender(SimpleGender.GenderType.Female);
                results.Add(new ProcessedNameEntry(kvp.Key, CreateIndexKey(kvp.Key), gender));
                processedNames.Add(kvp.Key);
            }
        }
        
        // Count final gender classifications
        var finalMaleCount = results.Count(r => r.Gender is SimpleGender sg && sg.Type == SimpleGender.GenderType.Male);
        var finalFemaleCount = results.Count(r => r.Gender is SimpleGender sg && sg.Type == SimpleGender.GenderType.Female);
        var finalAndrogyneCount = results.Count(r => r.Gender is AndrogyneGender);
        
        Console.WriteLine($"Total processed: {results.Count} names");
        Console.WriteLine($"  - Male: {finalMaleCount} (including {finalMaleCount - (maleNames.Count - androgyneNames.Count)} reclassified from dominant androgynous)");
        Console.WriteLine($"  - Female: {finalFemaleCount} (including {finalFemaleCount - (femaleNames.Count - androgyneNames.Count)} reclassified from dominant androgynous)");
        Console.WriteLine($"  - Truly Androgynous: {finalAndrogyneCount}");
        
        return results;
    }
    
    static void GenerateJsonFile(List<ProcessedNameEntry> nameEntries, string outputPath)
    {
        var jsonEntries = nameEntries.Select(entry => new {
            name = entry.Name,
            gender = ConvertGenderToJson(entry.Gender)
        }).ToList();
        
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        string json = JsonSerializer.Serialize(jsonEntries, options);
        File.WriteAllText(outputPath, json);
    }
    
    static object ConvertGenderToJson(GenderInfo gender)
    {
        return gender switch
        {
            SimpleGender sg => (int)sg.Type,
            AndrogyneGender ag => new { male = ag.MaleRatio, female = ag.FemaleRatio },
            _ => 0 // Unknown
        };
    }
    
    static void SaveDebugInfo(Dictionary<string, int> maleNames, Dictionary<string, int> femaleNames)
    {
        // Save male names to debug file
        File.WriteAllLines("debug_male_names.txt", 
            maleNames.OrderByDescending(kvp => kvp.Value)
                    .Select(kvp => $"{kvp.Key},{kvp.Value}"));
        
        // Save female names to debug file
        File.WriteAllLines("debug_female_names.txt", 
            femaleNames.OrderByDescending(kvp => kvp.Value)
                      .Select(kvp => $"{kvp.Key},{kvp.Value}"));
        
        // Find overlap
        var overlap = maleNames.Keys.Intersect(femaleNames.Keys).ToList();
        File.WriteAllLines("debug_overlap_names.txt", 
            overlap.Select(name => $"{name},{maleNames[name]},{femaleNames[name]}"));
        
        Console.WriteLine($"Debug info saved:");
        Console.WriteLine($"  - debug_male_names.txt: {maleNames.Count} names");
        Console.WriteLine($"  - debug_female_names.txt: {femaleNames.Count} names");
        Console.WriteLine($"  - debug_overlap_names.txt: {overlap.Count} overlapping names");
    }
    
    static void DemonstrateExistingIndex(string indexPath)
    {
        Console.WriteLine("=== LOADING EXISTING INDEX ===");
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var searcher = new NameSearcher(indexPath);
        stopwatch.Stop();
        
        Console.WriteLine($"✅ Index loaded successfully in {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine($"📁 Index file: {indexPath}");
        Console.WriteLine($"📊 File size: {new FileInfo(indexPath).Length:N0} bytes\n");
        
        // Focused search demonstration with "Alex" and variations
        var testQueries = new[] { 
            "Alex", "alex", "ALEX",  // The requested search
            "Alexa", "Alexandra", "Alexander",  // Related names
            "Jiří", "Jiri", "jiri", "jyri"  // Test the fixed problematic name
        };
        
        Console.WriteLine("=== SEARCH DEMONSTRATION ===\n");
        
        foreach (var query in testQueries)
        {
            PerformTieredSearch(searcher, query);
        }
    }
    
    static void PerformTieredSearch(NameSearcher searcher, string query)
    {
        Console.WriteLine($"🔍 Searching for: '{query}'");
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // First try exact match (distance 0)
        var exactResults = searcher.Search(query, 0);
        stopwatch.Stop();
        
        if (exactResults.Count > 0)
        {
            Console.WriteLine($"  ✅ {exactResults.Count} exact match(es) found ({stopwatch.ElapsedMilliseconds} ms):");
            foreach (var result in exactResults.Take(5))
            {
                Console.WriteLine($"     → {result.Name} ({result.Gender})");
            }
            if (exactResults.Count > 5)
            {
                Console.WriteLine($"     ... and {exactResults.Count - 5} more");
            }
        }
        else
        {
            // Try fuzzy search (distance 1)
            stopwatch.Restart();
            var fuzzyResults = searcher.Search(query, 1);
            stopwatch.Stop();
            
            if (fuzzyResults.Count > 0)
            {
                Console.WriteLine($"  🔍 {fuzzyResults.Count} fuzzy match(es) found ({stopwatch.ElapsedMilliseconds} ms):");
                foreach (var result in fuzzyResults.Take(3))
                {
                    Console.WriteLine($"     → {result.Name} ({result.Gender})");
                }
                if (fuzzyResults.Count > 3)
                {
                    Console.WriteLine($"     ... and {fuzzyResults.Count - 3} more");
                }
            }
            else
            {
                Console.WriteLine($"  ❌ No matches found ({stopwatch.ElapsedMilliseconds} ms)");
            }
        }
        Console.WriteLine();
    }
    
    static void DemonstrateUsage(string indexPath)
    {
        Console.WriteLine("\n--- Demonstrating name search ---");
        
        var searcher = new NameSearcher(indexPath);
        
        // Test some common Czech names (both with and without diacritics)
        var testNames = new[] { 
            "Tereza", "tereza", "Petr", "petr", 
            "Jana", "jana", "Tomáš", "Tomas", "tomas",
            "Marie", "marie", "Pavel", "pavel",
            "Jiří", "Jiri", "jiri"  // Test the problematic name
        };
        
        foreach (var testName in testNames)
        {
            PerformTieredSearch(searcher, testName);
        }
    }
}

public class ProcessedNameEntry
{
    public string Name { get; }
    public string IndexKey { get; }
    public GenderInfo Gender { get; }
    
    public ProcessedNameEntry(string name, string indexKey, GenderInfo gender)
    {
        Name = name;
        IndexKey = indexKey;
        Gender = gender;
    }
}