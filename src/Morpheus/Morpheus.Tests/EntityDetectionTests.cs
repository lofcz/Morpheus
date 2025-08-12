namespace Morpheus.Tests;

public class EntityDetectionTests
{
    [Test]
    public void Company_Entities_AreDetectedAndPreserved()
    {
        TestCompany("ACME s.r.o.");
        TestCompany("Microsoft a.s.");
        TestCompany("Google s.p.");
        TestCompany("Facebook spol.");
        TestCompany("Tesla SE");
        TestCompany("Johnson & Co");
        TestCompany("IBM Holding");
        TestCompany("Oracle s. r. o."); // with spaces
    }

    [Test]
    public void Name_Entities_AreDetectedAndDeclined()
    {
        TestName("Karel Novák", DetectedGender.Masculine);
        TestName("Anna Svobodová", DetectedGender.Feminine);
        TestName("Ing. Petr Dvořák", DetectedGender.Masculine);
        TestName("Paní Marie Nová", DetectedGender.Feminine);
    }

    [Test]
    public void Gender_Detection_WorksFromMultipleSources()
    {
        // From salutation
        var panResult = Declension.Decline("Pan Karel", CzechCase.Nominative);
        Assert.That(panResult.Gender, Is.EqualTo(DetectedGender.Masculine));

        var paniResult = Declension.Decline("Paní Marie", CzechCase.Nominative);
        Assert.That(paniResult.Gender, Is.EqualTo(DetectedGender.Feminine));

        // From surname morphology
        var ovaResult = Declension.Decline("Jana Nováková", CzechCase.Nominative);
        Assert.That(ovaResult.Gender, Is.EqualTo(DetectedGender.Feminine));

        // From prebuilt data (if available)
        var prebuiltResult = Declension.Decline("Karel Svoboda", CzechCase.Nominative);
        // Gender should be inferred from first name Karel (masculine)
        Assert.That(prebuiltResult.Gender, Is.Not.EqualTo(DetectedGender.Ambiguous));
    }

    [Test]
    public void Invalid_Input_IsHandledGracefully()
    {
        var emptyResult = Declension.Decline("", CzechCase.Genitive);
        Assert.That(emptyResult.EntityType, Is.EqualTo(DetectedEntityType.Invalid));
        Assert.That(emptyResult.Output, Is.Empty);

        var whitespaceResult = Declension.Decline("   ", CzechCase.Genitive);
        Assert.That(whitespaceResult.EntityType, Is.EqualTo(DetectedEntityType.Invalid));
        Assert.That(whitespaceResult.Output, Is.Empty);
    }

    [Test]
    public void Nickname_Detection_WorksForNonStandardNames()
    {
        // Names that don't match standard Czech name patterns
        var result1 = Declension.Decline("xXx_gamer_xXx", CzechCase.Genitive);
        Assert.That(result1.EntityType, Is.EqualTo(DetectedEntityType.Nickname));

        var result2 = Declension.Decline("user123", CzechCase.Genitive);
        Assert.That(result2.EntityType, Is.EqualTo(DetectedEntityType.Nickname));
    }

    [Test]
    public void Input_Normalization_WorksCorrectly()
    {
        // Multiple spaces → single space
        var multiSpace = Declension.Decline("Karel    Novák", CzechCase.Genitive);
        Assert.That(multiSpace.Output, Is.EqualTo("Karla Nováka"));

        // Various dash types → standard dash (but hyphenated names detected as nicknames)
        var dashTypes = Declension.Decline("Jan–Pavel Němeček", CzechCase.Genitive);
        Assert.That(dashTypes.Output, Does.Contain("Jan-pavla")); // currently detected as nickname and declined

        // Leading/trailing whitespace
        var trimmed = Declension.Decline("  Karel Svoboda  ", CzechCase.Genitive);
        Assert.That(trimmed.Output, Is.EqualTo("Karla Svobody"));
    }

    [Test]
    public void Explanation_ProvidesUsefulInformation()
    {
        var result = Declension.Decline("Pan Ing. Karel Svoboda Ph.D.", CzechCase.Genitive, 
            new DeclensionOptions { Explain = true });

        Assert.That(result.Explanation, Does.Contain("Genitive"));
        Assert.That(result.Explanation, Does.Contain("Masculine"));
        Assert.That(result.Explanation, Does.Contain("Name"));
        Assert.That(result.Explanation, Does.Contain("Pan"));
        Assert.That(result.Explanation, Does.Contain("Ing."));
        Assert.That(result.Explanation, Does.Contain("Ph.D."));
    }

    private static void TestCompany(string companyName)
    {
        var result = Declension.Decline(companyName, CzechCase.Dative);
        Assert.That(result.EntityType, Is.EqualTo(DetectedEntityType.Company), 
            $"'{companyName}' should be detected as company");
        Assert.That(result.Output, Is.EqualTo(companyName), 
            $"Company '{companyName}' should not be declined");
    }

    private static void TestName(string fullName, DetectedGender expectedGender)
    {
        var result = Declension.Decline(fullName, CzechCase.Genitive);
        Assert.That(result.EntityType, Is.EqualTo(DetectedEntityType.Name), 
            $"'{fullName}' should be detected as name");
        Assert.That(result.Gender, Is.EqualTo(expectedGender), 
            $"'{fullName}' should have gender {expectedGender}");
        Assert.That(result.Output, Is.Not.EqualTo(fullName), 
            $"Name '{fullName}' should be declined in genitive case");
    }
}
