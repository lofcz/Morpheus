using System;
using System.Collections.Generic;

namespace Morpheus.Rules;

public static class VokativRules
{
    /// <summary>
    /// Transform a name to vocative case with full context information
    /// This provides the best accuracy by using known gender and role information
    /// </summary>
    public static string TransformWithContext(string input, DetectedGender gender, bool isLastName)
    {
        if (string.IsNullOrEmpty(input)) return input;
        
        // Use the Python vokativ approach with full context for maximum accuracy
        if (gender == DetectedGender.Feminine)
        {
            if (isLastName)
            {
                // Feminine last names remain unchanged (nepřechýlená příjmení)
                return VokativRulesFromPython.TransformFeminineLastName(input);
            }

            // Feminine first names: a → o, otherwise unchanged
            return VokativRulesFromPython.TransformFeminineFirstName(input);
        }

        // Masculine names - use the comprehensive masculine rules, pass isLastName for heuristics
        return VokativRulesFromPython.TransformMasculineVocative(input, isLastName);
    }
}


