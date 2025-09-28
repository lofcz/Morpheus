using System;
using System.Collections.Concurrent;
using System.Linq;
using Morpheus.Data;

namespace Morpheus.Rules;

/// <summary>
/// Classifier that decides whether a feminine bearer of a surname should borrow the
/// masculine vocative form (ne-přechýlená příjmení).
/// The decision is based solely on statistics in <see cref="ScrapedDeclensionData"/> –
/// no hard-coded suffix heuristics.
/// </summary>
public static class BorrowedVocativeRules
{
    private const double FeminineRatioThreshold = 0.25; // ≥ 25 % feminine attestations ⇒ treat as genuine feminine lemma

    // cache by lemma (lower-case, without diacritics matters already in key)
    private static readonly ConcurrentDictionary<string, string?> _cache = new();

    /// <summary>
    /// Attempt to obtain the borrowed masculine vocative for a surname worn by a woman.
    /// Returns true only when evidence is strong that the surname is an undecorated masculine
    /// lemma (nepřechýlené) and the masculine vocative form exists in the dataset.
    /// </summary>
    public static bool TryGetBorrowedVocative(string original, out string borrowedVocative)
    {
        borrowedVocative = string.Empty;
        if (string.IsNullOrWhiteSpace(original)) return false;

        string lemma = original.ToLowerInvariant();

        // Cached decision: null = do not borrow, non-null = borrowed form
        if (_cache.TryGetValue(lemma, out var cached))
        {
            if (cached is null) return false;
            borrowedVocative = cached;
            return true;
        }

        if (!ScrapedDeclensionData.Names.TryGetValue(lemma, out var data))
        {
            _cache[lemma] = null;
            return false;
        }

        // We look only at surname forms (typeInt = 1)
        var lastNameForms = data.LastNameForms;
        if (lastNameForms.Count == 0)
        {
            _cache[lemma] = null;
            return false;
        }

        int cntM = lastNameForms.Count(f => f.Gender == (int)DetectedGender.Masculine);
        int cntF = lastNameForms.Count(f => f.Gender == (int)DetectedGender.Feminine);

        int total = cntM + cntF;
        if (total == 0)
        {
            _cache[lemma] = null;
            return false;
        }

        double relF = cntF / (double)total;

        // Too many feminine attestations ⇒ assume genuine feminine lemma → no borrowing
        if (relF >= FeminineRatioThreshold)
        {
            _cache[lemma] = null;
            return false;
        }

        // Must have a distinct masculine vocative form
        string mascVoc = GetCaseForm(lastNameForms, (int)DetectedGender.Masculine, CzechCase.Vocative);
        string mascNom = GetCaseForm(lastNameForms, (int)DetectedGender.Masculine, CzechCase.Nominative);

        if (string.IsNullOrEmpty(mascVoc) || string.Equals(mascVoc, mascNom, StringComparison.Ordinal))
        {
            _cache[lemma] = null;
            return false;
        }

        // Cross-validate: dataset vocative must equal what our rule engine produces for masculine surname
        string predicted = VokativRules.TransformWithContext(lemma,
                                        DetectedGender.Masculine,
                                        isLastName: true).ToLowerInvariant();

        if (!string.Equals(mascVoc, predicted, StringComparison.Ordinal))
        {
            _cache[lemma] = null;
            return false;
        }

        // Extra morphological safety: require nominative ends in 'a' and vocative is exactly that 'a'->'o'
        if (!(lemma.EndsWith("a", StringComparison.Ordinal) &&
              mascVoc == lemma.Substring(0, lemma.Length - 1) + "o"))
        {
            _cache[lemma] = null;
            return false;
        }

        // Borrow!
        borrowedVocative = mascVoc;
        _cache[lemma] = borrowedVocative;
        return true;
    }

    private static string GetCaseForm(System.Collections.Generic.IEnumerable<ScrapedDeclensionData.DeclensionForm> forms,
                                      int genderInt,
                                      CzechCase @case)
    {
        int idx = (int)@case - 1; // list stored 0-based
        foreach (var form in forms)
        {
            if (form.Gender == genderInt && idx < form.Cases.Count && !string.IsNullOrEmpty(form.Cases[idx]))
            {
                return form.Cases[idx].ToLowerInvariant();
            }
        }
        return string.Empty;
    }
}


