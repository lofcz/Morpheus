using System;
using System.Linq;

namespace Morpheus;

public static class NameCasing
{
    /// <summary>
    /// Normalize output casing for a token based on its role using original input as a pattern.
    /// - For first/last names: preserve inner capitals (e.g., MacDonald, NicDonald), 
    ///   lowercase particles (d'Arc, vanBerg), capitalized articles (LaFontaine), 
    ///   and hyphenated parts (Jean-Paul).
    /// - For titles: lookup canonical form or fallback to title case.
    /// Other roles are returned unchanged.
    /// </summary>
    public static string NormalizeForRole(string value, TokenRole role, string? originalPattern)
    {
        if (string.IsNullOrEmpty(value)) return value;

        if (role is TokenRole.FirstName or TokenRole.LastName)
        {
            return NormalizeFromPattern(value, originalPattern);
        }

        if (role is TokenRole.Title)
        {
            return NormalizeTitle(value);
        }

        return value;
    }

    private static string NormalizeFromPattern(string value, string? pattern)
    {
        if (string.IsNullOrEmpty(value)) return value;

        // 1) Check if this follows a cultural prefix pattern (Mac/Mc/O'/Nic)
        if (IsCulturalPrefixPattern(value))
        {
            return CanonicalizeCulturalPrefix(value);
        }

        // 2) Check for lowercase particle patterns (d', de, van, von, etc.)
        if (IsLowercaseParticlePattern(value))
        {
            return CanonicalizeLowercaseParticle(value);
        }

        // 3) Check for capitalized article patterns (La, Le, El, etc.)
        if (IsCapitalizedArticlePattern(value))
        {
            return CanonicalizeCapitalizedArticle(value);
        }

        // 4) Hyphenated double names: TitleCase each segment
        int hyphenIndex = value.IndexOf('-');
        if (hyphenIndex >= 0)
        {
            string[] parts = value.Split('-', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].ToFirstUpperRestLower();
            }
            return string.Join("-", parts);
        }

        // 5) Default: TitleCase (first upper, rest lower)
        return value.ToFirstUpperRestLower();
    }

    private static readonly string[] CulturalPrefixes = { "Mac", "Mc", "Nic", "O'" };

    private static bool IsCulturalPrefixPattern(string word)
    {
        foreach (string prefix in CulturalPrefixes)
        {
            if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && 
                word.Length >= prefix.Length + 2) // Need at least 2 chars after prefix
            {
                string remaining = word.Substring(prefix.Length);
                return remaining.Length > 1 && 
                       char.IsLetter(remaining[0]) &&
                       char.IsUpper(remaining[0]) && 
                       remaining.Substring(1).All(char.IsLower);
            }
        }
        return false;
    }

    private static string CanonicalizeCulturalPrefix(string word)
    {
        foreach (string prefix in CulturalPrefixes)
        {
            if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && 
                word.Length >= prefix.Length + 1)
            {
                string remaining = word.Substring(prefix.Length);
                string normalizedRemaining = remaining.ToFirstUpperRestLower();
                return prefix + normalizedRemaining;
            }
        }
        return word.ToFirstUpperRestLower();
    }

    private static readonly string[] LowercaseParticles = { "d'", "de", "da", "di", "du", "del", "della", "van", "von", "vom", "zur", "ben", "ibn", "bin", "te", "ter", "ten" };

    private static bool IsLowercaseParticlePattern(string word)
    {
        return IsPatternMatch(word, LowercaseParticles);
    }

    private static bool IsPatternMatch(string word, string[] prefixes)
    {
        foreach (string prefix in prefixes)
        {
            if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && 
                word.Length > prefix.Length)
            {
                string remaining = word.Substring(prefix.Length);
                // Check if remaining part is substantial (length > 1) and follows proper casing pattern
                return remaining.Length > 1 && 
                       char.IsLetter(remaining[0]) && 
                       char.IsUpper(remaining[0]) && 
                       remaining.Substring(1).All(char.IsLower);
            }
        }
        return false;
    }

    private static string CanonicalizeLowercaseParticle(string word)
    {
        return CanonicalizePattern(word, LowercaseParticles, prefix => prefix.ToLowerInvariant());
    }

    private static string CanonicalizePattern(string word, string[] prefixes, Func<string, string> prefixTransform)
    {
        foreach (string prefix in prefixes)
        {
            if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && 
                word.Length > prefix.Length)
            {
                string remaining = word.Substring(prefix.Length);
                string normalizedRemaining = remaining.ToFirstUpperRestLower();
                return prefixTransform(prefix) + normalizedRemaining;
            }
        }
        return word.ToFirstUpperRestLower();
    }

    private static readonly string[] CapitalizedArticles = { "La", "Le", "Les", "El", "Al", "Las", "Los", "Il", "Lo", "Gli" };

    private static bool IsCapitalizedArticlePattern(string word)
    {
        return IsPatternMatch(word, CapitalizedArticles);
    }

    private static string CanonicalizeCapitalizedArticle(string word)
    {
        return CanonicalizePattern(word, CapitalizedArticles, prefix => prefix.ToFirstUpperRestLower());
    }

    private static string NormalizeTitle(string title)
    {
        // Look up canonical form in the known titles table
        string? canonical = Declension.GetCanonicalTitle(title);
        if (canonical != null)
        {
            return canonical;
        }

        // If not found, fall back to title case (first upper, rest lower)
        return title.ToFirstUpperRestLower();
    }
}


