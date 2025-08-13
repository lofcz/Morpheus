using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Newtonsoft.Json;

namespace Morpheus.Scraper;

// Simplified data structures for generated code
public class GeneratedNameData
{
    public int Gender { get; set; } // 0=Masculine, 1=Feminine, 2=Other
    public List<GeneratedDeclensionForm> FirstNameForms { get; set; } = new();
    public List<GeneratedDeclensionForm> LastNameForms { get; set; } = new();
    public List<GeneratedPossessiveForm> PossessiveForms { get; set; } = new();
    public List<GeneratedFamilyForm> FamilyForms { get; set; } = new();
}

public class GeneratedDeclensionForm
{
    public List<string> Cases { get; set; } = new(); // 8 cases: Nom, Gen, Dat, Acc, Voc, Loc, Ins, Plural
    public int Gender { get; set; } // 0=Masculine, 1=Feminine, 2=Other - gender of this specific form
}

public class GeneratedPossessiveForm
{
    public string Masculine { get; set; } = string.Empty;
    public string Feminine { get; set; } = string.Empty;
    public string Neuter { get; set; } = string.Empty;
}

public class GeneratedFamilyForm
{
    public string Couple { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Children { get; set; } = string.Empty;
}

// JSON-serializable data structures
public class JsonDeclensionData
{
    public Dictionary<string, JsonNameData> Names { get; set; } = new();
    public Dictionary<string, string> NormalizedNameMap { get; set; } = new(); // Maps normalized name -> original name with diacritics
    public HashSet<string> FirstNames { get; set; } = new();
    public HashSet<string> LastNames { get; set; } = new();
}

public class JsonNameData
{
    public int Gender { get; set; }
    public List<JsonDeclensionForm> FirstNameForms { get; set; } = new();
    public List<JsonDeclensionForm> LastNameForms { get; set; } = new();
    public List<JsonPossessiveForm> PossessiveForms { get; set; } = new();
    public List<JsonFamilyForm> FamilyForms { get; set; } = new();
}

public class JsonDeclensionForm
{
    public List<string> Cases { get; set; } = new();
    public int Gender { get; set; } // 0=Masculine, 1=Feminine, 2=Other - gender of this specific form
}

public class JsonPossessiveForm
{
    public string Masculine { get; set; } = string.Empty;
    public string Feminine { get; set; } = string.Empty;
    public string Neuter { get; set; } = string.Empty;
}

public class JsonFamilyForm
{
    public string Couple { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Children { get; set; } = string.Empty;
}

public static class Stage4
{
    // Debug flag to speed up compilation during development
    private const bool USE_FROZEN_COLLECTIONS = false;
    
    public static async Task Do()
    {
        // Load Stage 3 results
        var stage3Path = "stage3.json";
        if (!File.Exists(stage3Path))
        {
            Console.WriteLine($"Stage 3 results not found at {stage3Path}");
            return;
        }

        var stage3Json = await File.ReadAllTextAsync(stage3Path);
        var nameDataList = JsonConvert.DeserializeObject<List<NameData>>(stage3Json) ?? new List<NameData>();
        Console.WriteLine($"Loaded {nameDataList.Count} names from Stage 3");

        // Data structure: preserve original names with diacritics for exact matching
        var nameDatabase = new Dictionary<string, GeneratedNameData>();

        Console.WriteLine("Processing names for simplified dictionary generation...");
        
        foreach (var nameEntry in nameDataList)
        {
            if (string.IsNullOrEmpty(nameEntry.Name)) continue;

            // Use the normalized name WITH diacritics as the primary key
            var normalizedName = NormalizeName(nameEntry.Name);
            
            // Get or create name data entry using the name with diacritics
            if (!nameDatabase.ContainsKey(normalizedName))
            {
                nameDatabase[normalizedName] = new GeneratedNameData();
            }

            var nameData = nameDatabase[normalizedName];

            // Collect all genders from this nameEntry's variants
            var gendersInThisEntry = nameEntry.Declensions.Select(v => v.Gender).Distinct().ToList();
            
            // Merge with existing genders if this name was already processed
            var existingGenders = new List<NameGender>();
            if (nameData.FirstNameForms.Count > 0 || nameData.LastNameForms.Count > 0)
            {
                // We already have data for this name, need to consider existing gender
                var existingGender = nameData.Gender switch
                {
                    0 => NameGender.Masculine,
                    1 => NameGender.Feminine,
                    _ => NameGender.Other
                };
                existingGenders.Add(existingGender);
            }
            
            var allGenders = gendersInThisEntry.Concat(existingGenders).Distinct().ToList();
            
            // Determine final gender: if multiple genders exist, it's androgynous
            if (allGenders.Count > 1)
            {
                nameData.Gender = 2; // Androgynous/Other
            }
            else if (allGenders.Count == 1)
            {
                nameData.Gender = ConvertGenderToInt(allGenders[0]);
            }
            else
            {
                nameData.Gender = 2; // Default to Other if no variants
            }

            // Process each variant
            foreach (var variant in nameEntry.Declensions)
            {
                if (variant.Declension == null) continue;

                    // Create declension form
                    var declensionForm = new GeneratedDeclensionForm();
                    declensionForm.Gender = ConvertGenderToInt(variant.Gender); // Store the gender of this specific form
                    declensionForm.Cases.Add(variant.Declension.Nominative?.Form ?? "");
                    declensionForm.Cases.Add(variant.Declension.Genitive?.Form ?? "");
                    declensionForm.Cases.Add(variant.Declension.Dative?.Form ?? "");
                    declensionForm.Cases.Add(variant.Declension.Accusative?.Form ?? "");
                    declensionForm.Cases.Add(variant.Declension.Vocative?.Form ?? "");
                    declensionForm.Cases.Add(variant.Declension.Locative?.Form ?? "");
                    declensionForm.Cases.Add(variant.Declension.Instrumental?.Form ?? "");
                    declensionForm.Cases.Add(variant.Declension.Plural ?? "");

                    // Add to appropriate list based on type
                    if (variant.Type == NameType.FirstName)
                    {
                        nameData.FirstNameForms.Add(declensionForm);
                    }
                    else if (variant.Type == NameType.LastName)
                    {
                        nameData.LastNameForms.Add(declensionForm);
                    }

                    // Possessive forms are handled outside the variant loop
            }

            // Add possessive forms (only once per name)
            if (nameEntry.PossessiveForms != null && nameData.PossessiveForms.Count == 0)
            {
                // Add all possessive forms from the table
                if (nameEntry.PossessiveForms.MasculineAnimate != null)
                {
                    var possessiveForm = new GeneratedPossessiveForm
                    {
                        Masculine = nameEntry.PossessiveForms.MasculineAnimate.Singular ?? "",
                        Feminine = nameEntry.PossessiveForms.MasculineAnimate.Plural ?? "", // Using placeholder
                        Neuter = nameEntry.PossessiveForms.MasculineAnimate.Gender ?? ""
                    };
                    nameData.PossessiveForms.Add(possessiveForm);
                }
            }

            // Add family forms (only once per name)
            if (nameEntry.FamilyNaming != null && nameData.FamilyForms.Count == 0)
            {
                var familyForm = new GeneratedFamilyForm
                {
                    Couple = nameEntry.FamilyNaming.Couple ?? "",
                    Family = nameEntry.FamilyNaming.Family ?? "",
                    Children = nameEntry.FamilyNaming.Children ?? ""
                };
                nameData.FamilyForms.Add(familyForm);
            }
        }

        Console.WriteLine("Generated simplified data structure:");
        Console.WriteLine($"  - Total names: {nameDatabase.Count}");
        Console.WriteLine($"  - Names with first name forms: {nameDatabase.Values.Count(n => n.FirstNameForms.Count > 0)}");
        Console.WriteLine($"  - Names with last name forms: {nameDatabase.Values.Count(n => n.LastNameForms.Count > 0)}");

        // Generate C# code file
        await GenerateSimplifiedDataFile(nameDatabase);
        
        Console.WriteLine("Stage 4 completed - generated simplified ScrapedDeclensionData.g.cs");
    }

    private static string NormalizeName(string name)
    {
        // Only normalize case and trim, preserve diacritics
        return name.ToLowerInvariant().Trim();
    }

    private static int ConvertGenderToInt(NameGender gender)
    {
        return gender switch
        {
            NameGender.Masculine => 0,
            NameGender.Feminine => 1,
            NameGender.Other => 2,
            _ => 2
        };
    }

    private static async Task GenerateSimplifiedDataFile(Dictionary<string, GeneratedNameData> nameDatabase)
    {
        // First, generate the JSON data files
        await GenerateJsonDataFiles(nameDatabase);
        
        // Then generate the C# stub class that loads from JSON
        await GenerateCSharpStubClass(nameDatabase);
    }

    private static async Task GenerateJsonDataFiles(Dictionary<string, GeneratedNameData> nameDatabase)
    {
        // Build normalized name mapping for fallback lookups
        var normalizedNameMap = new Dictionary<string, string>();
        foreach (var kvp in nameDatabase)
        {
            var originalNameWithDiacritics = kvp.Key;
            var normalizedWithoutDiacritics = Morpheus.Normalizer.RemoveDiacritics(originalNameWithDiacritics);
            
            // Only add mapping if the normalized form is different and doesn't already exist
            if (normalizedWithoutDiacritics != originalNameWithDiacritics && 
                !normalizedNameMap.ContainsKey(normalizedWithoutDiacritics))
            {
                normalizedNameMap[normalizedWithoutDiacritics] = originalNameWithDiacritics;
            }
        }
        
        // Convert to JSON-serializable format
        var jsonData = new JsonDeclensionData
        {
            Names = nameDatabase.ToDictionary(kvp => kvp.Key, kvp => new JsonNameData
            {
                Gender = kvp.Value.Gender,
                FirstNameForms = kvp.Value.FirstNameForms.Select(f => new JsonDeclensionForm { Cases = f.Cases, Gender = f.Gender }).ToList(),
                LastNameForms = kvp.Value.LastNameForms.Select(f => new JsonDeclensionForm { Cases = f.Cases, Gender = f.Gender }).ToList(),
                PossessiveForms = kvp.Value.PossessiveForms.Select(f => new JsonPossessiveForm 
                { 
                    Masculine = f.Masculine, 
                    Feminine = f.Feminine, 
                    Neuter = f.Neuter 
                }).ToList(),
                FamilyForms = kvp.Value.FamilyForms.Select(f => new JsonFamilyForm 
                { 
                    Couple = f.Couple, 
                    Family = f.Family, 
                    Children = f.Children 
                }).ToList()
            }),
            NormalizedNameMap = normalizedNameMap,
            FirstNames = nameDatabase.Where(kvp => kvp.Value.FirstNameForms.Count > 0).Select(kvp => kvp.Key).ToHashSet(),
            LastNames = nameDatabase.Where(kvp => kvp.Value.LastNameForms.Count > 0).Select(kvp => kvp.Key).ToHashSet()
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = false, // Compact for performance
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var jsonString = System.Text.Json.JsonSerializer.Serialize(jsonData, options);
        var outputPath = "../Morpheus/Data/ScrapedDeclensionData.json";
        await File.WriteAllTextAsync(outputPath, jsonString);
        
        Console.WriteLine($"Full path: {Path.GetFullPath(outputPath)}");
        
        Console.WriteLine($"Generated JSON data: {outputPath} ({jsonString.Length:N0} bytes)");
    }

    private static async Task GenerateCSharpStubClass(Dictionary<string, GeneratedNameData> nameDatabase)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// This file was generated by Morpheus.Scraper Stage4");
        sb.AppendLine("// Contains simplified declension data loaded from JSON for performance");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("using System.Text.Json;");
        if (USE_FROZEN_COLLECTIONS)
        {
            sb.AppendLine("using System.Collections.Frozen;");
        }
        sb.AppendLine("using System.Reflection;");
        sb.AppendLine();
        sb.AppendLine("namespace Morpheus.Data;");
        sb.AppendLine();
        sb.AppendLine("public static class ScrapedDeclensionData");
        sb.AppendLine("{");
        
        // Lazy-loaded properties
        if (USE_FROZEN_COLLECTIONS)
        {
            sb.AppendLine("    private static readonly Lazy<FrozenDictionary<string, NameDeclensionData>> _namesLazy =");
            sb.AppendLine("        new Lazy<FrozenDictionary<string, NameDeclensionData>>(() => LoadData().Names.ToFrozenDictionary());");
            sb.AppendLine();
            sb.AppendLine("    private static readonly Lazy<FrozenDictionary<string, string>> _normalizedNameMapLazy =");
            sb.AppendLine("        new Lazy<FrozenDictionary<string, string>>(() => LoadData().NormalizedNameMap.ToFrozenDictionary());");
            sb.AppendLine();
            sb.AppendLine("    private static readonly Lazy<FrozenSet<string>> _firstNamesLazy =");
            sb.AppendLine("        new Lazy<FrozenSet<string>>(() => LoadData().FirstNames.ToFrozenSet());");
            sb.AppendLine();
            sb.AppendLine("    private static readonly Lazy<FrozenSet<string>> _lastNamesLazy =");
            sb.AppendLine("        new Lazy<FrozenSet<string>>(() => LoadData().LastNames.ToFrozenSet());");
            sb.AppendLine();
            sb.AppendLine("    public static FrozenDictionary<string, NameDeclensionData> Names => _namesLazy.Value;");
            sb.AppendLine("    public static FrozenDictionary<string, string> NormalizedNameMap => _normalizedNameMapLazy.Value;");
            sb.AppendLine("    public static FrozenSet<string> FirstNames => _firstNamesLazy.Value;");
            sb.AppendLine("    public static FrozenSet<string> LastNames => _lastNamesLazy.Value;");
        }
        else
        {
            sb.AppendLine("    private static readonly Lazy<Dictionary<string, NameDeclensionData>> _namesLazy =");
            sb.AppendLine("        new Lazy<Dictionary<string, NameDeclensionData>>(() => LoadData().Names);");
            sb.AppendLine();
            sb.AppendLine("    private static readonly Lazy<Dictionary<string, string>> _normalizedNameMapLazy =");
            sb.AppendLine("        new Lazy<Dictionary<string, string>>(() => LoadData().NormalizedNameMap);");
            sb.AppendLine();
            sb.AppendLine("    private static readonly Lazy<HashSet<string>> _firstNamesLazy =");
            sb.AppendLine("        new Lazy<HashSet<string>>(() => LoadData().FirstNames);");
            sb.AppendLine();
            sb.AppendLine("    private static readonly Lazy<HashSet<string>> _lastNamesLazy =");
            sb.AppendLine("        new Lazy<HashSet<string>>(() => LoadData().LastNames);");
            sb.AppendLine();
            sb.AppendLine("    public static Dictionary<string, NameDeclensionData> Names => _namesLazy.Value;");
            sb.AppendLine("    public static Dictionary<string, string> NormalizedNameMap => _normalizedNameMapLazy.Value;");
            sb.AppendLine("    public static HashSet<string> FirstNames => _firstNamesLazy.Value;");
            sb.AppendLine("    public static HashSet<string> LastNames => _lastNamesLazy.Value;");
        }
        sb.AppendLine();
        
        // LoadData method
        sb.AppendLine("    private static LoadedData LoadData()");
        sb.AppendLine("    {");
        sb.AppendLine("        var assembly = Assembly.GetExecutingAssembly();");
        sb.AppendLine("        var assemblyLocation = assembly.Location;");
        sb.AppendLine("        var assemblyDir = Path.GetDirectoryName(assemblyLocation);");
        sb.AppendLine("        var jsonPath = Path.Combine(assemblyDir!, \"Data\", \"ScrapedDeclensionData.json\");");
        sb.AppendLine();
        sb.AppendLine("        if (!File.Exists(jsonPath))");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new FileNotFoundException($\"Declension data file not found: {jsonPath}\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        using var fileStream = File.OpenRead(jsonPath);");
        sb.AppendLine("        return DeserializeFromStream(fileStream);");
        sb.AppendLine("    }");
        sb.AppendLine();
        
        sb.AppendLine("    private static LoadedData DeserializeFromStream(Stream stream)");
        sb.AppendLine("    {");
        sb.AppendLine("        var options = new JsonSerializerOptions");
        sb.AppendLine("        {");
        sb.AppendLine("            PropertyNamingPolicy = JsonNamingPolicy.CamelCase");
        sb.AppendLine("        };");
        sb.AppendLine();
        sb.AppendLine("        var jsonData = JsonSerializer.Deserialize<JsonDeclensionData>(stream, options);");
        sb.AppendLine();
        sb.AppendLine("        // Convert JSON format to runtime format");
        sb.AppendLine("        var names = jsonData.Names.ToDictionary(");
        sb.AppendLine("            kvp => kvp.Key,");
        sb.AppendLine("            kvp => new NameDeclensionData");
        sb.AppendLine("            {");
        sb.AppendLine("                Gender = kvp.Value.Gender,");
        sb.AppendLine("                FirstNameForms = kvp.Value.FirstNameForms.Select(f => new DeclensionForm { Cases = f.Cases, Gender = f.Gender }).ToList(),");
        sb.AppendLine("                LastNameForms = kvp.Value.LastNameForms.Select(f => new DeclensionForm { Cases = f.Cases, Gender = f.Gender }).ToList(),");
        sb.AppendLine("                PossessiveForms = kvp.Value.PossessiveForms.Select(f => new PossessiveForm");
        sb.AppendLine("                {");
        sb.AppendLine("                    Masculine = f.Masculine,");
        sb.AppendLine("                    Feminine = f.Feminine,");
        sb.AppendLine("                    Neuter = f.Neuter");
        sb.AppendLine("                }).ToList(),");
        sb.AppendLine("                FamilyForms = kvp.Value.FamilyForms.Select(f => new FamilyForm");
        sb.AppendLine("                {");
        sb.AppendLine("                    Couple = f.Couple,");
        sb.AppendLine("                    Family = f.Family,");
        sb.AppendLine("                    Children = f.Children");
        sb.AppendLine("                }).ToList()");
        sb.AppendLine("            });");
        sb.AppendLine();
        sb.AppendLine("        return new LoadedData");
        sb.AppendLine("        {");
        sb.AppendLine("            Names = names,");
        sb.AppendLine("            NormalizedNameMap = jsonData.NormalizedNameMap,");
        sb.AppendLine("            FirstNames = jsonData.FirstNames,");
        sb.AppendLine("            LastNames = jsonData.LastNames");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Add supporting data structures
        sb.AppendLine("    private class LoadedData");
        sb.AppendLine("    {");
        sb.AppendLine("        public Dictionary<string, NameDeclensionData> Names { get; set; } = new();");
        sb.AppendLine("        public Dictionary<string, string> NormalizedNameMap { get; set; } = new();");
        sb.AppendLine("        public HashSet<string> FirstNames { get; set; } = new();");
        sb.AppendLine("        public HashSet<string> LastNames { get; set; } = new();");
        sb.AppendLine("    }");
        sb.AppendLine();
        
        // JSON deserialization classes
        sb.AppendLine("    private class JsonDeclensionData");
        sb.AppendLine("    {");
        sb.AppendLine("        public Dictionary<string, JsonNameData> Names { get; set; } = new();");
        sb.AppendLine("        public Dictionary<string, string> NormalizedNameMap { get; set; } = new();");
        sb.AppendLine("        public HashSet<string> FirstNames { get; set; } = new();");
        sb.AppendLine("        public HashSet<string> LastNames { get; set; } = new();");
        sb.AppendLine("    }");
        sb.AppendLine();
        
        sb.AppendLine("    private class JsonNameData");
        sb.AppendLine("    {");
        sb.AppendLine("        public int Gender { get; set; }");
        sb.AppendLine("        public List<JsonDeclensionForm> FirstNameForms { get; set; } = new();");
        sb.AppendLine("        public List<JsonDeclensionForm> LastNameForms { get; set; } = new();");
        sb.AppendLine("        public List<JsonPossessiveForm> PossessiveForms { get; set; } = new();");
        sb.AppendLine("        public List<JsonFamilyForm> FamilyForms { get; set; } = new();");
        sb.AppendLine("    }");
        sb.AppendLine();
        
        sb.AppendLine("    private class JsonDeclensionForm");
        sb.AppendLine("    {");
        sb.AppendLine("        public List<string> Cases { get; set; } = new();");
        sb.AppendLine("        public int Gender { get; set; }");
        sb.AppendLine("    }");
        sb.AppendLine();
        
        sb.AppendLine("    private class JsonPossessiveForm");
        sb.AppendLine("    {");
        sb.AppendLine("        public string Masculine { get; set; } = string.Empty;");
        sb.AppendLine("        public string Feminine { get; set; } = string.Empty;");
        sb.AppendLine("        public string Neuter { get; set; } = string.Empty;");
        sb.AppendLine("    }");
        sb.AppendLine();
        
        sb.AppendLine("    private class JsonFamilyForm");
        sb.AppendLine("    {");
        sb.AppendLine("        public string Couple { get; set; } = string.Empty;");
        sb.AppendLine("        public string Family { get; set; } = string.Empty;");
        sb.AppendLine("        public string Children { get; set; } = string.Empty;");
        sb.AppendLine("    }");
        sb.AppendLine();
        
        sb.AppendLine("    public class NameDeclensionData");
        sb.AppendLine("    {");
        sb.AppendLine("        public int Gender { get; set; }");
        sb.AppendLine("        public List<DeclensionForm> FirstNameForms { get; set; } = new();");
        sb.AppendLine("        public List<DeclensionForm> LastNameForms { get; set; } = new();");
        sb.AppendLine("        public List<PossessiveForm> PossessiveForms { get; set; } = new();");
        sb.AppendLine("        public List<FamilyForm> FamilyForms { get; set; } = new();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public class DeclensionForm");
        sb.AppendLine("    {");
        sb.AppendLine("        public List<string> Cases { get; set; } = new();");
        sb.AppendLine("        public int Gender { get; set; }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public class PossessiveForm");
        sb.AppendLine("    {");
        sb.AppendLine("        public string Masculine { get; set; } = string.Empty;");
        sb.AppendLine("        public string Feminine { get; set; } = string.Empty;");
        sb.AppendLine("        public string Neuter { get; set; } = string.Empty;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public class FamilyForm");
        sb.AppendLine("    {");
        sb.AppendLine("        public string Couple { get; set; } = string.Empty;");
        sb.AppendLine("        public string Family { get; set; } = string.Empty;");
        sb.AppendLine("        public string Children { get; set; } = string.Empty;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        var outputPath = "../Morpheus/Data/ScrapedDeclensionData.g.cs";
        await File.WriteAllTextAsync(outputPath, sb.ToString());
        
        Console.WriteLine($"Generated C# stub: {outputPath}");
    }

    private static string EscapeString(string input)
    {
        return input.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
