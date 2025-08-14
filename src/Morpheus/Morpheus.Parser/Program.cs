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
        
        BuildOrRunCombinedIndex();
    }


    static void BuildOrRunCombinedIndex()
    {
        string firstNamesExcelPath = "jmena.xls";
        string surnamesExcelPath = "prijmeni.xls";
        string indexOutputPath = "names_index.bk";
        
        Console.WriteLine("Parsing Czech government names and surnames (combined index)...");
        
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
            
            Console.WriteLine("No existing index found. Building new combined index from Excel files...\n");
            
            // Parse first names with counts
            var parsedFirstNames = ParseFirstNamesWithCounts(firstNamesExcelPath);
            var firstNameEntries = parsedFirstNames.Entries;
            var firstNameCounts = parsedFirstNames.IndexKeyToCount;
            Console.WriteLine($"Parsed {firstNameEntries.Count} unique first names from Excel.");

            // Parse surnames
            var surnameCounts = ParseSurnameCounts(surnamesExcelPath);
            Console.WriteLine($"Parsed {surnameCounts.Count} unique surnames from Excel.");

            // Parse additional data from extra/ directory
            var extraFirstNames = ParseExtraDataFile("extra/first/data.txt");
            Console.WriteLine($"Parsed {extraFirstNames.Count} additional names from extra/first/data.txt");
            
            var extraLastNames = ParseExtraDataFile("extra/last/data.txt");
            Console.WriteLine($"Parsed {extraLastNames.Count} additional surnames from extra/last/data.txt");

            // Combine into dictionary by index key, union roles, prefer gender from first-name data
            var combined = new Dictionary<string, CombinedItem>(StringComparer.Ordinal);

            // Add first names (role = First)
            foreach (var entry in firstNameEntries)
            {
                var idx = entry.IndexKey;
                if (!combined.TryGetValue(idx, out var item))
                {
                    combined[idx] = new CombinedItem
                    {
                        DisplayName = entry.Name, // keep diacritics/casing for display
                        IndexKey = entry.IndexKey,
                        Gender = entry.Gender,
                        Role = NameRole.First,
                        FirstCount = firstNameCounts.GetValueOrDefault(idx, 0)
                    };
                }
                else
                {
                    item.Role |= NameRole.First;
                    // Keep any non-unknown gender if available
                    if (item.Gender is SimpleGender s1 && s1.Type == SimpleGender.GenderType.Unknown)
                    {
                        item.Gender = entry.Gender;
                    }
                    item.FirstCount = firstNameCounts.TryGetValue(idx, out var c2) ? c2 : item.FirstCount;
                }
            }

            // Add surnames (role = Surname, gender = Unknown if only surname)
            foreach (var surnameOriginal in ParseSurnamesOriginals(surnamesExcelPath))
            {
                var surnameIndexKey = CreateIndexKey(surnameOriginal);
                if (string.IsNullOrEmpty(surnameIndexKey)) continue;

                if (!combined.TryGetValue(surnameIndexKey, out var item))
                {
                    combined[surnameIndexKey] = new CombinedItem
                    {
                        DisplayName = NormalizeName(surnameOriginal), // normalized capitalization for display
                        IndexKey = surnameIndexKey,
                        Gender = new SimpleGender(SimpleGender.GenderType.Unknown),
                        Role = NameRole.Surname,
                        SurnameCount = surnameCounts.GetValueOrDefault(surnameIndexKey, 0)
                    };
                }
                else
                {
                    item.Role |= NameRole.Surname;
                    if (item.SurnameCount == 0 && surnameCounts.TryGetValue(surnameIndexKey, out var sc2))
                    {
                        item.SurnameCount = sc2;
                    }
                    // If gender is unknown, keep; otherwise preserve the existing gender from first-name data
                }
            }

            // Add extra first names (role from data, gender only from first names)
            foreach (var extraEntry in extraFirstNames)
            {
                if (string.IsNullOrEmpty(extraEntry.IndexKey)) continue;

                if (!combined.TryGetValue(extraEntry.IndexKey, out var item))
                {
                    combined[extraEntry.IndexKey] = new CombinedItem
                    {
                        DisplayName = extraEntry.Name,
                        IndexKey = extraEntry.IndexKey,
                        Gender = extraEntry.Role.HasFlag(NameRole.First) ? extraEntry.Gender : new SimpleGender(SimpleGender.GenderType.Unknown),
                        Role = extraEntry.Role,
                        FirstCount = extraEntry.Role.HasFlag(NameRole.First) ? 1 : 0,
                        SurnameCount = extraEntry.Role.HasFlag(NameRole.Surname) ? 1 : 0
                    };
                }
                else
                {
                    item.Role |= extraEntry.Role;
                    // Only update gender from first names (keep existing if it's not Unknown)
                    if (extraEntry.Role.HasFlag(NameRole.First) && 
                        item.Gender is SimpleGender sg && sg.Type == SimpleGender.GenderType.Unknown)
                    {
                        item.Gender = extraEntry.Gender;
                    }
                    if (extraEntry.Role.HasFlag(NameRole.First)) item.FirstCount = Math.Max(1, item.FirstCount);
                    if (extraEntry.Role.HasFlag(NameRole.Surname)) item.SurnameCount = Math.Max(1, item.SurnameCount);
                }
            }

            // Add extra last names (role = Surname, ignore gender as per requirements)
            foreach (var extraEntry in extraLastNames)
            {
                if (string.IsNullOrEmpty(extraEntry.IndexKey)) continue;

                if (!combined.TryGetValue(extraEntry.IndexKey, out var item))
                {
                    combined[extraEntry.IndexKey] = new CombinedItem
                    {
                        DisplayName = extraEntry.Name,
                        IndexKey = extraEntry.IndexKey,
                        Gender = new SimpleGender(SimpleGender.GenderType.Unknown), // Ignore gender for surnames
                        Role = NameRole.Surname,
                        SurnameCount = 1
                    };
                }
                else
                {
                    item.Role |= NameRole.Surname;
                    item.SurnameCount = Math.Max(1, item.SurnameCount);
                    // Don't update gender - keep existing gender info from first names
                }
            }

            // Apply dominance rule for role pollution: 95% threshold and minimum 10 total
            foreach (var kvp in combined)
            {
                var item = kvp.Value;
                if (item.Role.HasFlag(NameRole.First) && item.Role.HasFlag(NameRole.Surname))
                {
                    int total = item.FirstCount + item.SurnameCount;
                    if (total >= 10)
                    {
                        int dominant = Math.Max(item.FirstCount, item.SurnameCount);
                        float ratio = total == 0 ? 0 : (float)dominant / total;
                        if (ratio >= 0.95f)
                        {
                            item.Role = item.FirstCount >= item.SurnameCount ? NameRole.First : NameRole.Surname;
                        }
                    }
                }
            }

            // Stats
            int firstOnly = combined.Values.Count(v => v.Role == NameRole.First);
            int surnameOnly = combined.Values.Count(v => v.Role == NameRole.Surname);
            int both = combined.Values.Count(v => v.Role.HasFlag(NameRole.First) && v.Role.HasFlag(NameRole.Surname));
            Console.WriteLine("Final combined index composition:");
            Console.WriteLine($"  - First names only: {firstOnly}");
            Console.WriteLine($"  - Surnames only:   {surnameOnly}");
            Console.WriteLine($"  - Both:            {both}");
            
            Console.WriteLine("Building optimized BK tree index...");
            BuildOptimizedIndex(combined, indexOutputPath);
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
    
    static List<ExtraDataEntry> ParseExtraDataFile(string filePath)
    {
        var entries = new List<ExtraDataEntry>();
        
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"Warning: Extra data file not found: {filePath}");
            return entries;
        }
        
        var lines = File.ReadAllLines(filePath);
        Console.WriteLine($"Processing extra data file: {filePath} with {lines.Length} lines...");
        
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            
            var parts = line.Split(',');
            if (parts.Length < 3) continue;
            
            var nameType = parts[0].Trim(); // F, L, or B
            var name = parts[1].Trim();
            var gender = parts[2].Trim(); // M, F, A, U
            
            if (string.IsNullOrEmpty(name)) continue;
            
            // Parse role from first character
            NameRole role = nameType switch
            {
                "F" => NameRole.First,
                "L" => NameRole.Surname,
                "B" => NameRole.First | NameRole.Surname,
                _ => NameRole.First // default
            };
            
            // Parse gender info
            GenderInfo genderInfo;
            if (gender == "A" && parts.Length >= 5) // Androgenous with ratios
            {
                if (float.TryParse(parts[3], out var maleRatio) && 
                    float.TryParse(parts[4], out var femaleRatio))
                {
                    genderInfo = new AndrogyneGender(maleRatio, femaleRatio);
                }
                else
                {
                    genderInfo = new SimpleGender(SimpleGender.GenderType.Unknown);
                }
            }
            else
            {
                genderInfo = gender switch
                {
                    "M" => new SimpleGender(SimpleGender.GenderType.Male),
                    "F" => new SimpleGender(SimpleGender.GenderType.Female),
                    "U" => new SimpleGender(SimpleGender.GenderType.Unknown),
                    _ => new SimpleGender(SimpleGender.GenderType.Unknown)
                };
            }
            
            entries.Add(new ExtraDataEntry
            {
                Name = name,
                IndexKey = CreateIndexKey(name),
                Gender = genderInfo,
                Role = role
            });
        }
        
        Console.WriteLine($"Successfully parsed {entries.Count} entries from {filePath}");
        return entries;
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

    private sealed class FirstNamesParseResult
    {
        public List<ProcessedNameEntry> Entries { get; set; } = new();
        public Dictionary<string, int> IndexKeyToCount { get; set; } = new();
    }

    static FirstNamesParseResult ParseFirstNamesWithCounts(string excelPath)
    {
        var maleNames = new Dictionary<string, int>();
        var femaleNames = new Dictionary<string, int>();

        using var stream = File.Open(excelPath, FileMode.Open, FileAccess.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var dataSet = reader.AsDataSet();

        if (dataSet.Tables.Count > 0)
        {
            ParseWorksheet(dataSet.Tables[0], maleNames, "Male names");
        }
        if (dataSet.Tables.Count > 1)
        {
            ParseWorksheet(dataSet.Tables[1], femaleNames, "Female names");
        }

        var entries = ProcessNames(maleNames, femaleNames);

        // Build counts by index key (sum of male+female occurrences) using normalization used for index
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in maleNames)
        {
            var idx = CreateIndexKey(kv.Key);
            if (string.IsNullOrEmpty(idx)) continue;
            counts[idx] = counts.TryGetValue(idx, out var c) ? c + kv.Value : kv.Value;
        }
        foreach (var kv in femaleNames)
        {
            var idx = CreateIndexKey(kv.Key);
            if (string.IsNullOrEmpty(idx)) continue;
            counts[idx] = counts.TryGetValue(idx, out var c) ? c + kv.Value : kv.Value;
        }

        return new FirstNamesParseResult
        {
            Entries = entries,
            IndexKeyToCount = counts
        };
    }

    static HashSet<string> ParseSurnamesExcelFile(string excelPath)
    {
        var surnameIndexKeys = new HashSet<string>(StringComparer.Ordinal);

        using var stream = File.Open(excelPath, FileMode.Open, FileAccess.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var dataSet = reader.AsDataSet();

        // Iterate all sheets (specified as 5 total)
        for (int t = 0; t < dataSet.Tables.Count; t++)
        {
            var table = dataSet.Tables[t];
            Console.WriteLine($"Processing Surnames worksheet {t + 1}/{dataSet.Tables.Count} with {table.Rows.Count} rows...");

            // Data starts on row 2 (1-based), so index 1 in 0-based
            for (int i = 1; i < table.Rows.Count; i++)
            {
                var row = table.Rows[i];
                if (row == null || row.ItemArray.Length == 0) continue;

                // Column A = index 0
                var value = row[0]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(value)) continue;

                // Create normalized index key: RemoveDiacritics + lowercase + trim
                var indexKey = CreateIndexKey(value);
                if (string.IsNullOrEmpty(indexKey)) continue;

                surnameIndexKeys.Add(indexKey);
            }
        }

        return surnameIndexKeys;
    }

    static Dictionary<string, int> ParseSurnameCounts(string excelPath)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        using var stream = File.Open(excelPath, FileMode.Open, FileAccess.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var dataSet = reader.AsDataSet();

        for (int t = 0; t < dataSet.Tables.Count; t++)
        {
            var table = dataSet.Tables[t];
            for (int i = 1; i < table.Rows.Count; i++)
            {
                var row = table.Rows[i];
                if (row == null || row.ItemArray.Length == 0) continue;
                var value = row[0]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(value)) continue;
                var idx = CreateIndexKey(value);
                if (string.IsNullOrEmpty(idx)) continue;
                counts[idx] = counts.TryGetValue(idx, out var c) ? c + 1 : 1;
            }
        }
        return counts;
    }

    static IEnumerable<string> ParseSurnamesOriginals(string excelPath)
    {
        using var stream = File.Open(excelPath, FileMode.Open, FileAccess.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var dataSet = reader.AsDataSet();

        for (int t = 0; t < dataSet.Tables.Count; t++)
        {
            var table = dataSet.Tables[t];

            for (int i = 1; i < table.Rows.Count; i++)
            {
                var row = table.Rows[i];
                if (row == null || row.ItemArray.Length == 0) continue;
                var value = row[0]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(value)) continue;
                yield return value;
            }
        }
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
            gender = ConvertGenderToJson(entry.Gender),
            role = entry.Role
        }).ToList();
        
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        string json = JsonSerializer.Serialize(jsonEntries, options);
        File.WriteAllText(outputPath, json);
    }

    static void GenerateJsonFileFromCombined(Dictionary<string, CombinedItem> combined, string outputPath)
    {
        var jsonEntries = combined.Values.Select(v => new {
            name = v.DisplayName,
            gender = ConvertGenderToJson(v.Gender),
            role = v.Role
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
            "Jiří", "Jiri", "jiri", "jyyri", "kahleova"  // Test the fixed problematic name
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
            Console.WriteLine($"  ✅ {exactResults.Count} exact match(es) found ({stopwatch.Elapsed} ms):");
            foreach (var result in exactResults.Take(5))
            {
                Console.WriteLine($"     → {result.Name} ({result.Gender}, {FormatRole(result.Role)})");
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
                Console.WriteLine($"  🔍 {fuzzyResults.Count} fuzzy match(es) found ({stopwatch.Elapsed} ms):");
                foreach (var result in fuzzyResults.Take(3))
                {
                    Console.WriteLine($"     → {result.Name} ({result.Gender}, {FormatRole(result.Role)})");
                }
                if (fuzzyResults.Count > 3)
                {
                    Console.WriteLine($"     ... and {fuzzyResults.Count - 3} more");
                }
            }
            else
            {
                Console.WriteLine($"  ❌ No matches found ({stopwatch.Elapsed} ms)");
            }
        }
        Console.WriteLine();
    }
    
    static string FormatRole(NameRole role)
    {
        bool isFirst = role.HasFlag(NameRole.First);
        bool isSurname = role.HasFlag(NameRole.Surname);
        if (isFirst && isSurname) return "Both";
        if (isFirst) return "First";
        if (isSurname) return "Surname";
        return "Unknown";
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
    
    static void BuildOptimizedIndex(Dictionary<string, CombinedItem> combined, string indexOutputPath)
    {
        // Convert to NameEntry enumerable
        var nameEntries = combined.Values.Select(kvp => 
            new Morpheus.NameEntry(kvp.DisplayName, kvp.IndexKey, kvp.Gender, kvp.Role));
        
        // Use the optimized builder
        Morpheus.BKTreeBuilder.BuildIndex(nameEntries, indexOutputPath);
    }
}

public class ProcessedNameEntry
{
    public string Name { get; }
    public string IndexKey { get; }
    public GenderInfo Gender { get; }
    public NameRole Role { get; }
    
    public ProcessedNameEntry(string name, string indexKey, GenderInfo gender, NameRole role = NameRole.First)
    {
        Name = name;
        IndexKey = indexKey;
        Gender = gender;
        Role = role;
    }
}

class ExtraDataEntry
{
    public string Name { get; set; } = string.Empty;
    public string IndexKey { get; set; } = string.Empty;
    public GenderInfo Gender { get; set; } = new SimpleGender(SimpleGender.GenderType.Unknown);
    public NameRole Role { get; set; } = NameRole.First;
}

class CombinedItem
{
    public string DisplayName { get; set; } = string.Empty; // diacritics-preserved display name
    public string IndexKey { get; set; } = string.Empty;    // normalized key
    public GenderInfo Gender { get; set; } = new SimpleGender(SimpleGender.GenderType.Unknown);
    public NameRole Role { get; set; } = NameRole.First;
    public int FirstCount { get; set; }
    public int SurnameCount { get; set; }
}