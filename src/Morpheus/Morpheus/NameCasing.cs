using System;
using System.Linq;

namespace Morpheus;

public static class NameCasing
{
    /// <summary>
    /// Normalize output casing for a token based on its role using original input as a pattern.
    /// - For first/last names: preserve inner capitals (e.g., MacDonald), hyphenated parts, and common particles.
    /// - If the original was all lowercase, title-case the result (first upper, rest lower).
    /// - If the original was all uppercase, uppercase the whole result.
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

        // 1) Check if this follows a Gaelic pattern and canonicalize Mac/Mc/O'
        if (IsGaelicPattern(value))
        {
            return CanonicalizeMacOrMc(value);
        }

        // 2) Hyphenated double names: TitleCase each segment
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

        // 3) Default: TitleCase (first upper, rest lower)
        return value.ToFirstUpperRestLower();
    }

    private static bool IsGaelicPattern(string word)
    {
        // Check Mac pattern: "Mac" + (length > 1 && first upper && rest lower)
        if (word.StartsWith("Mac", StringComparison.OrdinalIgnoreCase) && word.Length >= 4)
        {
            string remaining = word.Substring(3);
            return remaining.Length > 1 && 
                   char.IsUpper(remaining[0]) && 
                   remaining.Substring(1).All(char.IsLower);
        }

        // Check Mc pattern: "Mc" + (length > 1 && first upper && rest lower)
        if (word.StartsWith("Mc", StringComparison.OrdinalIgnoreCase) && word.Length >= 3)
        {
            string remaining = word.Substring(2);
            return remaining.Length > 1 && 
                   char.IsUpper(remaining[0]) && 
                   remaining.Substring(1).All(char.IsLower);
        }

        // Check O' pattern: "O'" + (length > 1 && first upper && rest lower)
        if (word.StartsWith("O'", StringComparison.OrdinalIgnoreCase) && word.Length >= 3)
        {
            string remaining = word.Substring(2);
            return remaining.Length > 1 && 
                   char.IsUpper(remaining[0]) && 
                   remaining.Substring(1).All(char.IsLower);
        }

        return false;
    }

    private static string CanonicalizeMacOrMc(string word)
    {
        if (word.StartsWith("Mac", StringComparison.OrdinalIgnoreCase) && word.Length >= 4)
        {
            char firstAfter = char.ToUpperInvariant(word[3]);
            string rest = word.Length > 4 ? word.Substring(4).ToLowerInvariant() : string.Empty;
            return "Mac" + firstAfter + rest;
        }
        if (word.StartsWith("Mc", StringComparison.OrdinalIgnoreCase) && word.Length >= 3)
        {
            char firstAfter = char.ToUpperInvariant(word[2]);
            string rest = word.Length > 3 ? word.Substring(3).ToLowerInvariant() : string.Empty;
            return "Mc" + firstAfter + rest;
        }
        if (word.StartsWith("O'", StringComparison.OrdinalIgnoreCase) && word.Length >= 3)
        {
            char firstAfter = char.ToUpperInvariant(word[2]);
            string rest = word.Length > 3 ? word.Substring(3).ToLowerInvariant() : string.Empty;
            return "O'" + firstAfter + rest;
        }
        return word.ToFirstUpperRestLower();
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


