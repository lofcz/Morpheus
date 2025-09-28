using System;
using Morpheus;

/// <summary>
/// Test script to demonstrate improved handling of feminine surname endings (a/á)
/// This script shows how the new logic respects user intent while fixing obvious malformation
/// </summary>
class TestFeminineVocativeImprovements
{
    static void Main()
    {
        Console.WriteLine("Testing improved feminine surname handling for a/á distinction:");
        Console.WriteLine("==================================================================");
        Console.WriteLine();

        // Test cases showing different scenarios
        var testCases = new[]
        {
            // Malformed input (all lowercase) - should be corrected
            ("jana novakova", "Malformed (all lowercase)"),
            
            // Proper input with user's intended diacritics - should be preserved  
            ("Jana Nováková", "User intended diacritics"),
            
            // Proper input without diacritics - should be respected (could be international)
            ("Jana Novakova", "User choice (no diacritics)"),
            
            // Mixed: has diacritics elsewhere but not in surname ending - should be corrected
            ("Jána Novakova", "Inconsistent diacritics"),
            
            // Full context with Czech indicators - should be corrected
            ("ing. jana novakova csc.", "Czech context (all lowercase)"),
            
            // International context - should be respected
            ("Jana Novakova Smith", "International context"),
            
            // Other endings
            ("jana svobodna", "Other ending (-na)"),
            ("Jana Svobodná", "User intended (-ná)"),
            ("maria kowalska", "Polish-style surname"),
        };

        foreach (var (input, description) in testCases)
        {
            var result = Declension.Decline(input, CzechCase.Vocative);
            Console.WriteLine($"Input:       {input}");
            Console.WriteLine($"Description: {description}");
            Console.WriteLine($"Result:      {result.Output}");
            Console.WriteLine($"Gender:      {result.Gender}");
            Console.WriteLine();
        }

        Console.WriteLine("Key improvements:");
        Console.WriteLine("- Respects user's original diacritic choices");
        Console.WriteLine("- Fixes obvious malformation (all lowercase, inconsistent diacritics)");
        Console.WriteLine("- Uses context clues to distinguish intentional vs malformed input");
        Console.WriteLine("- Conservative approach: when in doubt, respect user's choice");
    }
}
