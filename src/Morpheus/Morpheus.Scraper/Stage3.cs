using Newtonsoft.Json;

namespace Morpheus.Scraper;

public static class Stage3
{
    public static async Task Do()
    {
        // Load Stage 2 results
        var stage2Path = Path.Combine(AppContext.BaseDirectory, "stage2-results.json");
        if (!File.Exists(stage2Path))
        {
            Console.WriteLine($"Stage 2 results file not found: {stage2Path}");
            Console.WriteLine("Please run Stage 2 first.");
            return;
        }

        var stage2Json = await File.ReadAllTextAsync(stage2Path);
        var nameDataList = JsonConvert.DeserializeObject<List<NameData>>(stage2Json) ?? new List<NameData>();
        Console.WriteLine($"Loaded {nameDataList.Count} names from Stage 2");

        // Clean up all declension data
        var cleanedCount = 0;
        foreach (var nameData in nameDataList)
        {
            if (CleanNameData(nameData))
            {
                cleanedCount++;
            }
        }

        Console.WriteLine($"Cleaned {cleanedCount} name entries");

        // Save cleaned results
        var stage3Path = Path.Combine(AppContext.BaseDirectory, "stage3.json");
        await File.WriteAllTextAsync(stage3Path, JsonConvert.SerializeObject(nameDataList, Formatting.Indented));
        Console.WriteLine($"Saved cleaned data to {stage3Path}");
    }

    private static bool CleanNameData(NameData nameData)
    {
        var hasChanges = false;

        // Clean all declension variants
        foreach (var variant in nameData.Declensions)
        {
            if (variant.Declension != null)
            {
                hasChanges |= CleanDeclensionTable(variant.Declension);
            }
        }

        // Clean possessive forms
        if (nameData.PossessiveForms != null)
        {
            hasChanges |= CleanPossessiveFormsTable(nameData.PossessiveForms);
        }

        // Clean family naming
        if (nameData.FamilyNaming != null)
        {
            hasChanges |= CleanFamilyNamingTable(nameData.FamilyNaming);
        }

        return hasChanges;
    }

    private static bool CleanDeclensionTable(DeclensionTable declension)
    {
        var hasChanges = false;

        // Clean all case forms
        hasChanges |= CleanCaseInfo(declension.Nominative);
        hasChanges |= CleanCaseInfo(declension.Genitive);
        hasChanges |= CleanCaseInfo(declension.Dative);
        hasChanges |= CleanCaseInfo(declension.Accusative);
        hasChanges |= CleanCaseInfo(declension.Vocative);
        hasChanges |= CleanCaseInfo(declension.Locative);
        hasChanges |= CleanCaseInfo(declension.Instrumental);

        // Clean plural
        if (declension.Plural != null)
        {
            var cleaned = CleanString(declension.Plural);
            if (cleaned != declension.Plural)
            {
                declension.Plural = cleaned;
                hasChanges = true;
            }
        }

        return hasChanges;
    }

    private static bool CleanCaseInfo(CaseInfo? caseInfo)
    {
        if (caseInfo?.Form == null) return false;

        var cleaned = CleanString(caseInfo.Form);
        if (cleaned != caseInfo.Form)
        {
            caseInfo.Form = cleaned;
            return true;
        }

        return false;
    }

    private static bool CleanPossessiveFormsTable(PossessiveFormsTable possessive)
    {
        var hasChanges = false;

        hasChanges |= CleanPossessiveForm(possessive.MasculineAnimate);
        hasChanges |= CleanPossessiveForm(possessive.MasculineInanimate);
        hasChanges |= CleanPossessiveForm(possessive.Feminine);
        hasChanges |= CleanPossessiveForm(possessive.Neuter);

        return hasChanges;
    }

    private static bool CleanPossessiveForm(PossessiveForm? form)
    {
        if (form == null) return false;

        var hasChanges = false;

        if (form.Singular != null)
        {
            var cleaned = CleanString(form.Singular);
            if (cleaned != form.Singular)
            {
                form.Singular = cleaned;
                hasChanges = true;
            }
        }

        if (form.Plural != null)
        {
            var cleaned = CleanString(form.Plural);
            if (cleaned != form.Plural)
            {
                form.Plural = cleaned;
                hasChanges = true;
            }
        }

        return hasChanges;
    }

    private static bool CleanFamilyNamingTable(FamilyNamingTable family)
    {
        var hasChanges = false;

        if (family.Couple != null)
        {
            var cleaned = CleanString(family.Couple);
            if (cleaned != family.Couple)
            {
                family.Couple = cleaned;
                hasChanges = true;
            }
        }

        if (family.Family != null)
        {
            var cleaned = CleanString(family.Family);
            if (cleaned != family.Family)
            {
                family.Family = cleaned;
                hasChanges = true;
            }
        }

        if (family.Children != null)
        {
            var cleaned = CleanString(family.Children);
            if (cleaned != family.Children)
            {
                family.Children = cleaned;
                hasChanges = true;
            }
        }

        return hasChanges;
    }

    private static string CleanString(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input?.Trim() ?? "";

        var result = input;

        // Remove parenthetical expressions like "(expr)"
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s*\([^)]*\)", "");

        // Remove alternatives after "/" - keep only the first part
        var slashIndex = result.IndexOf('/');
        if (slashIndex >= 0)
        {
            result = result.Substring(0, slashIndex);
        }

        // Trim whitespace
        result = result.Trim();

        return result;
    }
}
