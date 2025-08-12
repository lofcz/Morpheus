namespace Morpheus.Tests;

public class DeclensionTests
{
    [Test]
    public void Decline_Hana_Skalicka_AllowsTitlesOmissionAndDetectsFemale()
    {
        var tt = "Ondřej Šteffl";
        var xx = Declension.Decline(tt, CzechCase.Vocative, new DeclensionOptions { OmitTitles = false });
        
        var input = "Hana Skalická";
        DeclensionResult resultVoc = Morpheus.Declension.Decline(input, CzechCase.Vocative, new DeclensionOptions { OmitTitles = false });
        Assert.That(resultVoc.Gender, Is.EqualTo(DetectedGender.Feminine));
        Assert.That(resultVoc.Output.Contains("Hano"));

        var resultGen = Morpheus.Declension.Decline(input, CzechCase.Genitive);
        Assert.That(resultGen.Output, Does.Contain("Hany"));

        var resultDat = Morpheus.Declension.Decline(input, CzechCase.Dative);
        Assert.That(resultDat.Output, Does.Contain("Haně"));

        var resultAcc = Morpheus.Declension.Decline(input, CzechCase.Accusative);
        Assert.That(resultAcc.Output, Does.Contain("Hanu"));

        var resultLoc = Morpheus.Declension.Decline(input, CzechCase.Locative);
        Assert.That(resultLoc.Output, Does.Contain("Haně"));

        var resultIns = Morpheus.Declension.Decline(input, CzechCase.Instrumental);
        Assert.That(resultIns.Output, Does.Contain("Hanou"));
    }

    [Test]
    public void Options_OmitFirstOrLastName()
    {
        var input = "Hana Skalická";
        var onlySurname = Morpheus.Declension.Decline(input, CzechCase.Genitive, new DeclensionOptions { OmitFirstName = true });
        Assert.That(onlySurname.Output, Does.Not.Contain("Hany"));
        Assert.That(onlySurname.Output, Is.Not.Empty);

        var onlyFirstName = Morpheus.Declension.Decline(input, CzechCase.Genitive, new DeclensionOptions { OmitLastName = true });
        Assert.That(onlyFirstName.Output, Does.Contain("Hany"));
        Assert.That(onlyFirstName.Output.Split(' ').Length, Is.EqualTo(1));
    }

    [Test]
    public void Company_IsDetectedAndNotDeclined()
    {
        var input = "ACME s.r.o.";
        var res = Morpheus.Declension.Decline(input, CzechCase.Dative);
        Assert.That(res.EntityType, Is.EqualTo(DetectedEntityType.Company));
        Assert.That(res.Output, Is.EqualTo(input));
    }
}


