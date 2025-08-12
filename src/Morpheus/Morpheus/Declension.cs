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

public enum DetectedEntityKind
{
    Male,
    Female,
    Ambiguous,   // obojetné jméno
    Company      // právnická osoba/entita
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
    public required DetectedEntityKind EntityKind { get; init; }
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

    // Very lightweight gender detection as a baseline.
    // We will extend this with suffix dictionaries later.
    private static DetectedEntityKind DetectEntityKind(string wholeInput, string firstName, string? lastName)
    {
        // Prefer company detection on the full original input to catch suffixes like "s.r.o." even if not tokenized as a word
        var full = wholeInput;
        if (string.IsNullOrWhiteSpace(full) && string.IsNullOrWhiteSpace(firstName)) return DetectedEntityKind.Ambiguous;

        // Company heuristics: s.r.o., a.s., SE, s.p., spol., s. r. o., & Co, s.r.o
        var companyPatterns = new[]
        {
            "s\\.\\s*r\\.\\s*o\\.", "a\\.s\\.", "s\\.p\\.", "spol\\.", "\\bSE\\b", "\\bHolding\\b", "&\\s*Co"
        };
        foreach (var pat in companyPatterns)
        {
            if (Regex.IsMatch(full, pat, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return DetectedEntityKind.Company;
            }
        }

        // Primary: use generated dataset (first name lookup); treat Neutral as unknown and continue heuristics
        if (!string.IsNullOrWhiteSpace(firstName))
        {
            // Consider hyphenated compound first names as well
            var firstCandidates = new List<string> { firstName };
            if (firstName.Contains('-')) firstCandidates.AddRange(firstName.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            foreach (var cand in firstCandidates)
            {
                if (Morpheus.Data.NameGenderData.Names.TryGetValue(cand, out var g))
                {
                    if (g == Morpheus.Data.NameGenderData.NameGender.Female) return DetectedEntityKind.Female;
                    if (g == Morpheus.Data.NameGenderData.NameGender.Male) return DetectedEntityKind.Male;
                    // Neutral -> continue to heuristics
                }
            }
        }

        // Surname morphology (Czech): strong female indicators
        if (!string.IsNullOrWhiteSpace(lastName))
        {
            var ln = lastName;
            if (ln.EndsWith("ová", StringComparison.OrdinalIgnoreCase)) return DetectedEntityKind.Female;
            if (ln.EndsWith("á", StringComparison.OrdinalIgnoreCase)) return DetectedEntityKind.Female; // adjective-like feminine
        }

        // No evidence → Ambiguous (do not guess)
        return DetectedEntityKind.Ambiguous;
    }

    public static DeclensionResult Decline(string input, CzechCase @case, DeclensionOptions? options = null)
    {
        options ??= new DeclensionOptions();
        var tokens = Tokenize(input);

        // Extract titles, first name, last name (very simple heuristic for now)
        var titles = tokens.Where(t => t.IsTitle).Select(t => t.Original).ToList();
        var words = tokens.Where(t => t.IsWord).Select(t => t.Original).ToList();

        string? firstName = words.FirstOrDefault();
        string? lastName = words.Skip(1).FirstOrDefault();

        var kind = DetectEntityKind(input, firstName ?? string.Empty, lastName);

        // Apply options to omit elements
        if (options.OmitTitles) titles.Clear();
        if (options.OmitFirstName && firstName != null)
        {
            firstName = null;
        }
        if (options.OmitLastName && lastName != null)
        {
            lastName = null;
        }

        // Declension per token (apply to all words)
        var outputParts = new List<string>();

        if (titles.Count > 0 && !options.OmitTitles)
        {
            outputParts.Add(string.Join(" ", titles));
        }

        var wordList = words.ToList();
        for (int i = 0; i < wordList.Count; i++)
        {
            var w = wordList[i];
            bool isFirst = i == 0;
            bool skip = (isFirst && options.OmitFirstName) || (!isFirst && options.OmitLastName);
            if (skip) continue;

            // Use reference rules for each token; special surname handling stays in rules
            var declined = DeclineToken(w, @case, kind, isFirst);
            outputParts.Add(declined);
        }

        var output = string.Join(" ", outputParts.Where(p => !string.IsNullOrWhiteSpace(p)));

        string? explanation = null;
        if (options.Explain)
        {
            explanation = $"case={@case}; kind={kind}";
        }

        return new DeclensionResult
        {
            Input = input,
            Output = output,
            TargetCase = @case,
            EntityKind = kind,
            Explanation = explanation
        };
    }

    private static string DeclineFirstName(string name, CzechCase @case, DetectedEntityKind kind)
    {
        var declined = DeclineByReferenceRules(name, @case, kind);
        return MatchCasing(name, declined);
    }

    private static string DeclineLastName(string surname, CzechCase @case, DetectedEntityKind kind)
    {
        var lower = surname.ToLowerInvariant();

        // Female surnames ending with -ová behave like adjectives across cases
        if (lower.EndsWith("ová"))
        {
            return MatchCasing(surname, @case switch
            {
                CzechCase.Nominative => surname,
                CzechCase.Genitive => ReplaceEnding(surname, "á", "é"),
                CzechCase.Dative => ReplaceEnding(surname, "á", "é"),
                CzechCase.Accusative => ReplaceEnding(surname, "á", "ou"),
                CzechCase.Vocative => surname, // surnames usually unchanged in vocative
                CzechCase.Locative => ReplaceEnding(surname, "á", "é"),
                CzechCase.Instrumental => ReplaceEnding(surname, "á", "ou"),
                _ => surname
            });
        }

        // Female adjective-like surnames ending with -á (e.g., Skalická)
        if (kind == DetectedEntityKind.Female && lower.EndsWith("á"))
        {
            return MatchCasing(surname, @case switch
            {
                CzechCase.Nominative => surname,
                CzechCase.Genitive => ReplaceEnding(surname, "á", "é"),
                CzechCase.Dative => ReplaceEnding(surname, "á", "é"),
                CzechCase.Accusative => ReplaceEnding(surname, "á", "ou"),
                CzechCase.Vocative => surname,
                CzechCase.Locative => ReplaceEnding(surname, "á", "é"),
                CzechCase.Instrumental => ReplaceEnding(surname, "á", "ou"),
                _ => surname
            });
        }

        // Apply reference rules for general surname handling
        var declined = DeclineByReferenceRules(surname, @case, kind);
        return MatchCasing(surname, declined);
    }

    private static string DeclineToken(string token, CzechCase @case, DetectedEntityKind kind, bool isFirstWord)
    {
        // First-name specific corrections where reference rules bias towards surnames
        if (isFirstWord && kind == DetectedEntityKind.Female)
        {
            var s = token.ToLowerInvariant();
            if (@case == CzechCase.Locative)
            {
                if (s.Length >= 2 && s.EndsWith("a") && (s[^2] == 'd' || s[^2] == 'n'))
                {
                    // Hana -> Haně, Linda -> Lindě, Anna -> Anně
                    var fixedForm = s.Substring(0, s.Length - 1) + "ě";
                    return MatchCasing(token, fixedForm);
                }
            }
        }

        return DeclineByReferenceRules(token, @case, kind);
    }

    // Very small baseline of vocative logic to be replaced by full rule engine later
    private static string DeclineToVocativeBaseline(string text, bool isFemale)
    {
        var lower = text.ToLowerInvariant();
        if (isFemale)
        {
            if (lower.EndsWith("a")) return ReplaceEnding(text, "a", "o");
            return text; // women surnames generally unchanged
        }

        if (lower.EndsWith("r")) return text + "e"; // Petr -> Petře (approx.)
        if (lower.EndsWith("l")) return text + "e"; // Karel -> Karle (approx., not exact)
        if (lower.EndsWith("c")) return ReplaceEnding(text, "ec", "če");
        if (lower.EndsWith("k")) return text + "u"; // Novák -> Nováku
        if (lower.EndsWith("s")) return text + "i"; // Tomás-> Tomási (approx.)
        if (lower.EndsWith("o")) return text; // Hugo -> Hugo
        return text;
    }

    private static string GenitiveHeuristic(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.EndsWith("á")) return ReplaceEnding(name, "á", "é");
        if (lower.EndsWith("o")) return ReplaceEnding(name, "o", "a");
        if (lower.EndsWith("l")) return name + "a"; // many male names
        if (lower.EndsWith("k")) return name + "a";
        return name + "a";
    }

    private static string DativeHeuristic(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.EndsWith("á")) return ReplaceEnding(name, "á", "é");
        if (lower.EndsWith("o")) return name + "vi";
        if (lower.EndsWith("l")) return name + "ovi";
        if (lower.EndsWith("k")) return name + "ovi";
        return name + "ovi";
    }

    private static string AccusativeHeuristic(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.EndsWith("á")) return ReplaceEnding(name, "á", "ou");
        if (lower.EndsWith("e")) return ReplaceEnding(name, "e", "i");
        if (lower.EndsWith("ř") || lower.EndsWith("ž")) return name + "e";
        if (lower.EndsWith("k")) return name + "a";
        return name + "a";
    }

    private static string LocativeHeuristic(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.EndsWith("á")) return ReplaceEnding(name, "á", "é");
        if (lower.EndsWith("o")) return name + "vi";
        if (lower.EndsWith("l")) return name + "ovi";
        if (lower.EndsWith("k")) return name + "ovi";
        return name + "ovi";
    }

    private static string InstrumentalHeuristic(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.EndsWith("á")) return ReplaceEnding(name, "á", "ou");
        if (lower.EndsWith("a")) return ReplaceEnding(name, "a", "ou");
        if (lower.EndsWith("o")) return ReplaceEnding(name, "o", "em");
        if (lower.EndsWith("k")) return name + "em";
        return name + "em";
    }

    private static string ReplaceEnding(string original, string oldEnding, string newEnding)
    {
        if (original.EndsWith(oldEnding, StringComparison.OrdinalIgnoreCase))
        {
            return original.Substring(0, original.Length - oldEnding.Length) + newEnding;
        }
        return original + newEnding;
    }

    // Use faithful rule ports from Morpheus.Rules.*
    private static string DeclineByReferenceRules(string original, CzechCase @case, DetectedEntityKind kind)
    {
        if (kind == DetectedEntityKind.Company) return original;
        return @case switch
        {
            CzechCase.Genitive => Rules.GenitivRules.Transform(original),
            CzechCase.Dative => Rules.DativRules.Transform(original),
            CzechCase.Accusative => Rules.AkuzativRules.Transform(original),
            CzechCase.Vocative => Rules.VokativRules.Transform(original),
            CzechCase.Locative => Rules.LokativRules.Transform(original),
            CzechCase.Instrumental => Rules.InstrumentalRules.Transform(original),
            _ => original
        };
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


