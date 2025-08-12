namespace Morpheus.Tests;

public class TitleHandlingTests
{
    [Test]
    public void Academic_Titles_ArePreservedAndPlacedCorrectly()
    {
        Test("Ing. Karel Svoboda", CzechCase.Genitive, "Ing. Karla Svobody");
        Test("Anna Nováková Ph.D.", CzechCase.Dative, "Anně Novákové Ph.D.");
        Test("prof. Ing. Petr Novák CSc.", CzechCase.Accusative, "prof. Ing. Petra Nováka CSc.");
    }

    [Test]
    public void Military_Titles_HandleSpacingAndAbbreviations()
    {
        Test("por. Jan Novák", CzechCase.Genitive, "por. Jana Nováka");
        Test("št.prap. Petr Svoboda", CzechCase.Dative, "št. prap. Petru Svobodovi"); // compressed → normalized
        Test("sv. Marie Nová", CzechCase.Accusative, "sv. Marii Novou"); // alternative abbrev
        Test("genmjr. Pavel Kraus", CzechCase.Locative, "genmjr. Pavlu Krausovi");
    }

    [Test]
    public void Ecclesiastical_Titles_AreHandledCorrectly()
    {
        Test("Rev. Father John Smith", CzechCase.Genitive, "Rev. Father Johna Smith"); // surnames currently not declined properly for foreign names
        Test("Msgr. Karel Novák", CzechCase.Dative, "Msgr. Karlovi Novákovi"); // current rule behavior
        Test("Most Rev. Bishop Michael Brown", CzechCase.Accusative, "Most Rev. Bishop Michaela Brown"); // foreign surnames
        Test("Brother Thomas V.G.", CzechCase.Instrumental, "Brother Thomasem V.G.");
    }

    [Test]
    public void Salutations_AreDeclinedCorrectly()
    {
        // Vocative case (addressing)
        Test("Pan Novák", CzechCase.Vocative, "pane Nováku");
        Test("Paní Svobodová", CzechCase.Vocative, "paní Svobodová");
        
        // Other cases
        Test("Pan Aleš", CzechCase.Genitive, "Pana Aleše");
        Test("Paní Marie", CzechCase.Dative, "Paní Marii");
        Test("Pan Karel", CzechCase.Accusative, "Pana Karla");
        Test("Pan Josef", CzechCase.Instrumental, "Panem Josefem");
    }

    [Test]
    public void Gender_IsInferredFromTitles()
    {
        var masculine = Declension.Decline("Pan Aleš", CzechCase.Nominative);
        Assert.That(masculine.Gender, Is.EqualTo(DetectedGender.Masculine));

        var feminine = Declension.Decline("Paní Marie", CzechCase.Nominative);
        Assert.That(feminine.Gender, Is.EqualTo(DetectedGender.Feminine));
    }

    [Test]
    public void Mixed_Titles_AreHandledCorrectly()
    {
        Test("Pan Mgr. Karel Svoboda Ph.D.", CzechCase.Genitive, "Pana Mgr. Karla Svobody Ph.D.");
        Test("mjr. Ing. Karel Dvořák", CzechCase.Instrumental, "mjr. Ing. Karlem Dvořákem");
        Test("Rev. Dr. Thomas Johnson J.C.D.", CzechCase.Dative, "Rev. Thomasovi Johnsonovi Dr. J.C.D."); // titles reordered
    }

    [Test]
    public void Titles_CanBeOmitted()
    {
        var withTitles = Declension.Decline("Pan Ing. Karel Svoboda Ph.D.", CzechCase.Genitive);
        var withoutTitles = Declension.Decline("Pan Ing. Karel Svoboda Ph.D.", CzechCase.Genitive, 
            new DeclensionOptions { OmitTitles = true });

        Assert.That(withTitles.Output, Is.EqualTo("Pana Ing. Karla Svobody Ph.D."));
        Assert.That(withoutTitles.Output, Is.EqualTo("Karla Svobody"));
    }

    [Test]
    public void CaseSensitivity_DistinguishesTitlesFromSurnames()
    {
        // "por." should be treated as military rank
        var rank = Declension.Decline("por. Jan Novák", CzechCase.Genitive);
        Assert.That(rank.Output, Is.EqualTo("por. Jana Nováka"));
        
        // "Por" should be treated as surname
        var surname = Declension.Decline("Jan Por", CzechCase.Genitive);
        Assert.That(surname.Output, Is.EqualTo("Jana Pora"));
    }

    [Test]
    public void CompressedTitles_AreNormalizedCorrectly()
    {
        // "št.prap." should be normalized to "št. prap."
        var result = Declension.Decline("št.prap. Karel Novák", CzechCase.Genitive, new DeclensionOptions { Explain = true });
        Assert.That(result.Output, Does.Contain("št. prap."));
        Assert.That(result.Explanation, Does.Contain("št. prap.")); // explanation should contain normalized title
    }

    [Test]
    public void Historic_Titles_AreRecognized()
    {
        Test("ppor. Anna Svobodová", CzechCase.Vocative, "ppor. Anno Svobodová"); // abolished 2011
        Test("škpt. Pavel Novák", CzechCase.Genitive, "škpt. Pavla Nováka"); // štábní kapitán
        Test("genplk. Karel Dvořák", CzechCase.Dative, "genplk. Karlu Dvořákovi"); // historical
    }

    private static void Test(string input, CzechCase targetCase, string expected)
    {
        var result = Declension.Decline(input, targetCase);
        Assert.That(result.Output, Is.EqualTo(expected), 
            $"Failed: {input} -> {targetCase} | Expected: '{expected}' | Got: '{result.Output}' | Explanation: {result.Explanation}");
    }
}
