using System.Text;
using Newtonsoft.Json;

namespace Morpheus.Scraper;

public static class Stage4
{
    public static async Task Do()
    {
        // Load Stage 3 results
        var stage3Path = Path.Combine(AppContext.BaseDirectory, "stage3.json");
        if (!File.Exists(stage3Path))
        {
            Console.WriteLine($"Stage 3 results file not found: {stage3Path}");
            Console.WriteLine("Please run Stage 3 first.");
            return;
        }

        var stage3Json = await File.ReadAllTextAsync(stage3Path);
        var nameDataList = JsonConvert.DeserializeObject<List<NameData>>(stage3Json) ?? new List<NameData>();
        Console.WriteLine($"Loaded {nameDataList.Count} names from Stage 3");

        // Generate lookup dictionaries for each case using integer keys
        var declensionData = new Dictionary<string, Dictionary<int, string>>();
        var possessiveData = new Dictionary<string, Dictionary<int, string>>();
        var familyData = new Dictionary<string, Dictionary<int, string>>();
        var genderData = new Dictionary<string, int>();
        var typeData = new Dictionary<string, int>();

        Console.WriteLine("Processing names for dictionary generation...");
        
        foreach (var nameData in nameDataList)
        {
            if (string.IsNullOrEmpty(nameData.Name)) continue;

            var normalizedName = NormalizeName(nameData.Name);
            
            // Process each declension variant
            foreach (var variant in nameData.Declensions)
            {
                if (variant.Declension == null) continue;

                // Create unique key for this variant (name + gender + type) using integer values
                var genderInt = ConvertGenderToInt(variant.Gender);
                var typeInt = ConvertTypeToInt(variant.Type);
                var variantKey = $"{normalizedName}_{genderInt}_{typeInt}";

                // Store gender and type info as enum values
                if (!genderData.ContainsKey(normalizedName))
                {
                    genderData[normalizedName] = (int)variant.Gender;
                }
                if (!typeData.ContainsKey(normalizedName))
                {
                    typeData[normalizedName] = (int)variant.Type;
                }

                // Process declension cases using enum integer keys
                if (!declensionData.ContainsKey(variantKey))
                {
                    declensionData[variantKey] = new Dictionary<int, string>();
                }

                AddCaseToDict(declensionData[variantKey], 1, variant.Declension.Nominative?.Form);    // CzechCase.Nominative
                AddCaseToDict(declensionData[variantKey], 2, variant.Declension.Genitive?.Form);     // CzechCase.Genitive
                AddCaseToDict(declensionData[variantKey], 3, variant.Declension.Dative?.Form);       // CzechCase.Dative
                AddCaseToDict(declensionData[variantKey], 4, variant.Declension.Accusative?.Form);   // CzechCase.Accusative
                AddCaseToDict(declensionData[variantKey], 5, variant.Declension.Vocative?.Form);     // CzechCase.Vocative
                AddCaseToDict(declensionData[variantKey], 6, variant.Declension.Locative?.Form);     // CzechCase.Locative
                AddCaseToDict(declensionData[variantKey], 7, variant.Declension.Instrumental?.Form); // CzechCase.Instrumental
                AddCaseToDict(declensionData[variantKey], 8, variant.Declension.Plural);             // Special: Plural form
            }

            // Process possessive forms using enum integer keys
            if (nameData.PossessiveForms != null)
            {
                if (!possessiveData.ContainsKey(normalizedName))
                {
                    possessiveData[normalizedName] = new Dictionary<int, string>();
                }

                AddPossessiveToDict(possessiveData[normalizedName], 0, nameData.PossessiveForms.MasculineAnimate?.Singular);    // MasculineAnimate_Singular
                AddPossessiveToDict(possessiveData[normalizedName], 1, nameData.PossessiveForms.MasculineAnimate?.Plural);      // MasculineAnimate_Plural
                AddPossessiveToDict(possessiveData[normalizedName], 2, nameData.PossessiveForms.MasculineInanimate?.Singular); // MasculineInanimate_Singular
                AddPossessiveToDict(possessiveData[normalizedName], 3, nameData.PossessiveForms.MasculineInanimate?.Plural);   // MasculineInanimate_Plural
                AddPossessiveToDict(possessiveData[normalizedName], 4, nameData.PossessiveForms.Feminine?.Singular);           // Feminine_Singular
                AddPossessiveToDict(possessiveData[normalizedName], 5, nameData.PossessiveForms.Feminine?.Plural);             // Feminine_Plural
                AddPossessiveToDict(possessiveData[normalizedName], 6, nameData.PossessiveForms.Neuter?.Singular);             // Neuter_Singular
                AddPossessiveToDict(possessiveData[normalizedName], 7, nameData.PossessiveForms.Neuter?.Plural);               // Neuter_Plural
            }

            // Process family naming using enum integer keys
            if (nameData.FamilyNaming != null)
            {
                if (!familyData.ContainsKey(normalizedName))
                {
                    familyData[normalizedName] = new Dictionary<int, string>();
                }

                AddFamilyToDict(familyData[normalizedName], 0, nameData.FamilyNaming.Couple);   // Couple
                AddFamilyToDict(familyData[normalizedName], 1, nameData.FamilyNaming.Family);   // Family
                AddFamilyToDict(familyData[normalizedName], 2, nameData.FamilyNaming.Children); // Children
            }
        }

        Console.WriteLine($"Generated dictionaries:");
        Console.WriteLine($"  - Declension variants: {declensionData.Count}");
        Console.WriteLine($"  - Possessive forms: {possessiveData.Count}");
        Console.WriteLine($"  - Family naming: {familyData.Count}");
        Console.WriteLine($"  - Gender mappings: {genderData.Count}");
        Console.WriteLine($"  - Type mappings: {typeData.Count}");

        // Generate C# code file
        await GenerateDataFile(declensionData, possessiveData, familyData, genderData, typeData);
        
        Console.WriteLine("Stage 4 completed - generated ScrapedDeclensionData.g.cs");
    }

    private static string NormalizeName(string name)
    {
        return name.Trim().ToLowerInvariant();
    }

    private static void AddCaseToDict(Dictionary<int, string> dict, int caseKey, string? form)
    {
        if (!string.IsNullOrWhiteSpace(form))
        {
            dict[caseKey] = form.Trim();
        }
    }

    private static void AddPossessiveToDict(Dictionary<int, string> dict, int key, string? form)
    {
        if (!string.IsNullOrWhiteSpace(form))
        {
            dict[key] = form.Trim();
        }
    }

    private static void AddFamilyToDict(Dictionary<int, string> dict, int key, string? form)
    {
        if (!string.IsNullOrWhiteSpace(form))
        {
            dict[key] = form.Trim();
        }
    }

    private static async Task GenerateDataFile(
        Dictionary<string, Dictionary<int, string>> declensionData,
        Dictionary<string, Dictionary<int, string>> possessiveData,
        Dictionary<string, Dictionary<int, string>> familyData,
        Dictionary<string, int> genderData,
        Dictionary<string, int> typeData)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// This file was generated by Morpheus.Scraper Stage4");
        sb.AppendLine("// Do not edit manually - regenerate using the scraper");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("using System.Collections.Frozen;");
        sb.AppendLine();
        sb.AppendLine("namespace Morpheus.Data;");
        sb.AppendLine();
        sb.AppendLine("public enum PossessiveFormKey");
        sb.AppendLine("{");
        sb.AppendLine("    MasculineAnimateSingular = 0,");
        sb.AppendLine("    MasculineAnimatePlural = 1,");
        sb.AppendLine("    MasculineInanimateSingular = 2,");
        sb.AppendLine("    MasculineInanimatePlural = 3,");
        sb.AppendLine("    FeminineSingular = 4,");
        sb.AppendLine("    FemininePlural = 5,");
        sb.AppendLine("    NeuterSingular = 6,");
        sb.AppendLine("    NeuterPlural = 7");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("public enum FamilyFormKey");
        sb.AppendLine("{");
        sb.AppendLine("    Couple = 0,");
        sb.AppendLine("    Family = 1,");
        sb.AppendLine("    Children = 2");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("public enum DeclensionFormKey");
        sb.AppendLine("{");
        sb.AppendLine("    Nominative = 1,");
        sb.AppendLine("    Genitive = 2,");
        sb.AppendLine("    Dative = 3,");
        sb.AppendLine("    Accusative = 4,");
        sb.AppendLine("    Vocative = 5,");
        sb.AppendLine("    Locative = 6,");
        sb.AppendLine("    Instrumental = 7,");
        sb.AppendLine("    Plural = 8");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("public static class ScrapedDeclensionData");
        sb.AppendLine("{");

        // Generate declension dictionary
        sb.AppendLine("    public static readonly FrozenDictionary<string, FrozenDictionary<int, string>> Declensions =");
        sb.AppendLine("        new Dictionary<string, FrozenDictionary<int, string>>");
        sb.AppendLine("        {");
        foreach (var kvp in declensionData.OrderBy(x => x.Key))
        {
            sb.AppendLine($"            [\"{EscapeString(kvp.Key)}\"] = new Dictionary<int, string>");
            sb.AppendLine("            {");
            foreach (var caseKvp in kvp.Value.OrderBy(x => x.Key))
            {
                sb.AppendLine($"                [{caseKvp.Key}] = \"{EscapeString(caseKvp.Value)}\",");
            }
            sb.AppendLine("            }.ToFrozenDictionary(),");
        }
        sb.AppendLine("        }.ToFrozenDictionary();");
        sb.AppendLine();

        // Generate possessive dictionary
        sb.AppendLine("    public static readonly FrozenDictionary<string, FrozenDictionary<int, string>> Possessives =");
        sb.AppendLine("        new Dictionary<string, FrozenDictionary<int, string>>");
        sb.AppendLine("        {");
        foreach (var kvp in possessiveData.OrderBy(x => x.Key))
        {
            sb.AppendLine($"            [\"{EscapeString(kvp.Key)}\"] = new Dictionary<int, string>");
            sb.AppendLine("            {");
            foreach (var possKvp in kvp.Value.OrderBy(x => x.Key))
            {
                sb.AppendLine($"                [{possKvp.Key}] = \"{EscapeString(possKvp.Value)}\",");
            }
            sb.AppendLine("            }.ToFrozenDictionary(),");
        }
        sb.AppendLine("        }.ToFrozenDictionary();");
        sb.AppendLine();

        // Generate family naming dictionary
        sb.AppendLine("    public static readonly FrozenDictionary<string, FrozenDictionary<int, string>> FamilyNaming =");
        sb.AppendLine("        new Dictionary<string, FrozenDictionary<int, string>>");
        sb.AppendLine("        {");
        foreach (var kvp in familyData.OrderBy(x => x.Key))
        {
            sb.AppendLine($"            [\"{EscapeString(kvp.Key)}\"] = new Dictionary<int, string>");
            sb.AppendLine("            {");
            foreach (var famKvp in kvp.Value.OrderBy(x => x.Key))
            {
                sb.AppendLine($"                [{famKvp.Key}] = \"{EscapeString(famKvp.Value)}\",");
            }
            sb.AppendLine("            }.ToFrozenDictionary(),");
        }
        sb.AppendLine("        }.ToFrozenDictionary();");
        sb.AppendLine();

        // Generate gender dictionary
        sb.AppendLine("    public static readonly FrozenDictionary<string, int> Genders =");
        sb.AppendLine("        new Dictionary<string, int>");
        sb.AppendLine("        {");
        foreach (var kvp in genderData.OrderBy(x => x.Key))
        {
            sb.AppendLine($"            [\"{EscapeString(kvp.Key)}\"] = {kvp.Value},");
        }
        sb.AppendLine("        }.ToFrozenDictionary();");
        sb.AppendLine();

        // Generate type dictionary
        sb.AppendLine("    public static readonly FrozenDictionary<string, int> Types =");
        sb.AppendLine("        new Dictionary<string, int>");
        sb.AppendLine("        {");
        foreach (var kvp in typeData.OrderBy(x => x.Key))
        {
            sb.AppendLine($"            [\"{EscapeString(kvp.Key)}\"] = {kvp.Value},");
        }
        sb.AppendLine("        }.ToFrozenDictionary();");
        sb.AppendLine();

        sb.AppendLine("}");

        // Write to Data folder in main project (source folder, not bin)
        var currentDir = Directory.GetCurrentDirectory(); // Morpheus.Scraper/bin/Debug/net8.0
        var scraperProjectDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(currentDir))); // Back to Morpheus.Scraper project
        var srcMorpheusDir = Path.GetDirectoryName(scraperProjectDir); // Back to src/Morpheus
        var dataFolder = Path.Combine(srcMorpheusDir!, "Morpheus", "Data");
        
        if (!Directory.Exists(dataFolder))
        {
            Directory.CreateDirectory(dataFolder);
        }

        var outputPath = Path.Combine(dataFolder, "ScrapedDeclensionData.g.cs");
        await File.WriteAllTextAsync(outputPath, sb.ToString());
        Console.WriteLine($"Generated {outputPath}");
    }

    private static string EscapeString(string input)
    {
        return input.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
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

    private static int ConvertTypeToInt(NameType type)
    {
        return type switch
        {
            NameType.FirstName => 0,
            NameType.LastName => 1,
            _ => 0
        };
    }
}
