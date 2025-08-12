using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Morpheus;

public enum CzechCase
{
    Nominative = 1,  // 1. pád – kdo, co
    Genitive = 2,    // 2. pád – koho, čeho
    Dative = 3,      // 3. pád – komu, čemu
    Accusative = 4,  // 4. pád – koho, co
    Vocative = 5,    // 5. pád – oslovujeme
    Locative = 6,    // 6. pád – o kom, o čem
    Instrumental = 7 // 7. pád – s kým, s čím
}

public enum DetectedGender
{
    Masculine,
    Feminine,
    Ambiguous
}

public enum DetectedEntityType
{
    Name,
    Company,
    Nickname,
    Invalid
}

public sealed class DeclensionOptions
{
    public bool OmitFirstName { get; init; }
    public bool OmitLastName { get; init; }
    public bool OmitTitles { get; init; }
    public bool Explain { get; init; }
}

public sealed class DeclensionResult
{
    public required string Input { get; init; }
    public required string Output { get; init; }
    public required CzechCase TargetCase { get; init; }
    public required DetectedGender Gender { get; init; }
    public required DetectedEntityType EntityType { get; init; }
    public string? Explanation { get; init; }
}

public static class Declension
{
    // Titles/abbreviations to preserve or optionally omit
    private static readonly string[] KnownTitles =
    {
        "Pan", "Paní",
        "Mgr.", "Bc.", "Ing.", "Ing. arch.", "MUDr.", "MVDr.", "JUDr.", "RNDr.",
        "PhDr.", "ThDr.", "Th.D.", "Ph.D.", "DiS.", "MBA", "LL.M.", "Jr.", "Sr."
    };

    // Step 1: Normalize input (trim, spaces, dash types)
    private static string NormalizeInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        
        // Trim and normalize spaces
        var normalized = input.Trim();
        normalized = Regex.Replace(normalized, @"\s{2,}", " "); // Multiple spaces to single space
        
        // Normalize various dash types to standard dash
        normalized = normalized.Replace('–', '-').Replace('—', '-').Replace('−', '-');
        
        return normalized;
    }

    // Step 2: Handle titles (detect and temporarily remove)
    private static (List<string> titles, string inputWithoutTitles) ExtractTitles(string input)
    {
        var tokens = Tokenize(input);
        var titles = tokens.Where(t => t.IsTitle).Select(t => t.Original).ToList();
        var wordsOnly = tokens.Where(t => t.IsWord).Select(t => t.Original);
        var inputWithoutTitles = string.Join(" ", wordsOnly);
        
        return (titles, inputWithoutTitles);
    }

    // Step 3: Infer gender using prebuilt data + heuristics
    private static DetectedGender InferGender(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return DetectedGender.Ambiguous;

        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return DetectedGender.Ambiguous;

        var firstName = words[0];
        var lastName = words.Length > 1 ? words[1] : null;

        // Try scraped gender data first (most accurate)
        var normalizedFirstName = firstName.ToLowerInvariant().Trim();
        if (Data.ScrapedDeclensionData.Genders.TryGetValue(normalizedFirstName, out var scrapedGender))
        {
            return scrapedGender switch
            {
                0 => DetectedGender.Masculine,
                1 => DetectedGender.Feminine,
                _ => DetectedGender.Ambiguous
            };
        }

        // Fallback: use generated dataset for first names
        var firstCandidates = new List<string> { firstName };
        if (firstName.Contains('-')) 
            firstCandidates.AddRange(firstName.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (var cand in firstCandidates)
        {
            if (Morpheus.Data.NameGenderData.Names.TryGetValue(cand, out var g))
            {
                if (g == Morpheus.Data.NameGenderData.NameGender.Female) return DetectedGender.Feminine;
                if (g == Morpheus.Data.NameGenderData.NameGender.Male) return DetectedGender.Masculine;
            }
        }

        // Surname morphology heuristics
        if (!string.IsNullOrWhiteSpace(lastName))
        {
            if (lastName.EndsWith("ová", StringComparison.OrdinalIgnoreCase)) return DetectedGender.Feminine;
            if (lastName.EndsWith("á", StringComparison.OrdinalIgnoreCase)) return DetectedGender.Feminine;
        }

        return DetectedGender.Ambiguous;
    }

    // Step 4: Infer entity type (name, company, nickname, invalid)
    private static DetectedEntityType InferEntityType(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return DetectedEntityType.Invalid;

        // Company heuristics
        var companyPatterns = new[]
        {
            "s\\.\\s*r\\.\\s*o\\.", "a\\.s\\.", "s\\.p\\.", "spol\\.", "\\bSE\\b", "\\bHolding\\b", "&\\s*Co"
        };
        foreach (var pat in companyPatterns)
        {
            if (Regex.IsMatch(input, pat, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return DetectedEntityType.Company;
            }
        }

        // Check if it looks like a proper name
        if (Regex.IsMatch(input, @"^[A-ZÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ][a-záčďéěíňóřšťúůýž]+(\s+[A-ZÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ][a-záčďéěíňóřšťúůýž]+)*$"))
        {
            return DetectedEntityType.Name;
        }

        // Everything else could be nickname or invalid
        return DetectedEntityType.Nickname;
    }

    public static DeclensionResult Decline(string input, CzechCase @case, DeclensionOptions? options = null)
    {
        options ??= new DeclensionOptions();

        // Step 1: Normalize input
        var normalizedInput = NormalizeInput(input);

        // Step 2: Handle titles (detect and temporarily remove)
        var (titleStrings, inputWithoutTitles) = ExtractTitles(normalizedInput);

        // Step 3: Infer gender
        var detectedGender = InferGender(inputWithoutTitles);

        // Step 4: Infer entity type
        var entityType = InferEntityType(inputWithoutTitles);

        // Step 5: Infer the declension result
        var declinedOutput = InferDeclensionResult(inputWithoutTitles, @case, detectedGender, entityType, options);

        // Reconstruct final output with titles if not omitted
        var finalOutput = ReconstructOutput(titleStrings, declinedOutput, options);

        string? explanation = null;
        if (options.Explain)
        {
            explanation = $"case={@case}; gender={detectedGender}; type={entityType}";
        }

        return new DeclensionResult
        {
            Input = input,
            Output = finalOutput,
            TargetCase = @case,
            Gender = detectedGender,
            EntityType = entityType,
            Explanation = explanation
        };
    }

    // Step 5: Infer the declension result
    private static string InferDeclensionResult(string input, CzechCase @case, DetectedGender gender, DetectedEntityType entityType, DeclensionOptions options)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        if (entityType == DetectedEntityType.Company) return input; // Companies don't decline

        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var declinedWords = new List<string>();

        for (int i = 0; i < words.Length; i++)
        {
            var word = words[i];
            bool isFirst = i == 0;
            bool skip = (isFirst && options.OmitFirstName) || (!isFirst && options.OmitLastName);
            if (skip) continue;

            var declined = DeclineWord(word, @case, gender, entityType, isFirst);
            declinedWords.Add(declined);
        }

        return string.Join(" ", declinedWords);
    }

    private static string ReconstructOutput(List<string> titles, string declinedContent, DeclensionOptions options)
    {
        var parts = new List<string>();

        if (titles.Count > 0 && !options.OmitTitles)
        {
            parts.Add(string.Join(" ", titles));
        }

        if (!string.IsNullOrWhiteSpace(declinedContent))
        {
            parts.Add(declinedContent);
        }

        return string.Join(" ", parts);
    }

    private static string DeclineWord(string word, CzechCase @case, DetectedGender gender, DetectedEntityType entityType, bool isFirstWord)
    {
        // Try prebuilt lookup first
        var prebuiltResult = TryPrebuiltLookup(word, @case, gender, entityType, isFirstWord);
        if (!string.IsNullOrEmpty(prebuiltResult))
        {
            return MatchCasing(word, prebuiltResult);
        }

        // Fallback to rule-based declension
        var ruleResult = @case switch
        {
            CzechCase.Genitive => Rules.GenitivRules.Transform(word),
            CzechCase.Dative => Rules.DativRules.Transform(word),
            CzechCase.Accusative => Rules.AkuzativRules.Transform(word),
            CzechCase.Vocative => Rules.VokativRules.Transform(word),
            CzechCase.Locative => Rules.LokativRules.Transform(word),
            CzechCase.Instrumental => Rules.InstrumentalRules.Transform(word),
            _ => word
        };

        return MatchCasing(word, ruleResult);
    }
    
    private static string TryPrebuiltLookup(string original, CzechCase @case, DetectedGender gender, DetectedEntityType entityType, bool isFirstWord)
    {
        var normalizedName = original.ToLowerInvariant().Trim();
        
        // Map enums to integers for variant key construction
        var genderInt = (int)gender;
        var typeInt = isFirstWord ? 0 : 1; // 0 = FirstName, 1 = LastName
        
        // Construct variant key: "name_gender_type"
        var variantKey = $"{normalizedName}_{genderInt}_{typeInt}";
        
        // Try to find exact variant match
        if (Data.ScrapedDeclensionData.Declensions.TryGetValue(variantKey, out var declensions))
        {
            var caseKey = (int)@case; // Direct cast from CzechCase enum to int
            
            if (declensions.TryGetValue(caseKey, out var form))
            {
                return form;
            }
        }
        
        return string.Empty; // Not found in prebuilt data
    }
    private static string MatchCasing(string pattern, string value)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(value)) return value;

        // All uppercase
        if (pattern.ToUpperInvariant() == pattern)
        {
            return value.ToUpperInvariant();
        }

        // Title-case (naive): first letter uppercase, rest preserved as-is
        if (char.IsLetter(pattern[0]) && char.IsUpper(pattern[0]))
        {
            if (value.Length == 1) return value.ToUpperInvariant();
            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        // Default: lower-case
        return value.ToLowerInvariant();
    }

    private sealed class Token
    {
        public string Original { get; set; }
        public bool IsWord { get; set; }
        public bool IsTitle { get; set; }

        public Token(string original, bool isWord, bool isTitle)
        {
            Original = original;
            IsWord = isWord;
            IsTitle = isTitle;
        }
    }

    private static IReadOnlyList<Token> Tokenize(string input)
    {
        var parts = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var tokens = new List<Token>(parts.Length);
        foreach (var p in parts)
        {
            var isTitle = KnownTitles.Contains(p, StringComparer.OrdinalIgnoreCase) || Regex.IsMatch(p, @"^[A-ZÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ]\.$");
            var isWord = !isTitle && Regex.IsMatch(p, @"\p{L}+");
            tokens.Add(new Token(p, isWord, isTitle));
        }
        return tokens;
    }
}


