using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Morpheus.Tests;

public class SpecialDumpTests
{
    private sealed class NamesPayload
    {
        public string[] names { get; set; } = Array.Empty<string>();
        public string[] surnames { get; set; } = Array.Empty<string>();
        public string[] fullnames { get; set; } = Array.Empty<string>();
    }

    private static NamesPayload Load()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "names.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<NamesPayload>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
    }

    [Test, Explicit("Manual dump of names declension across all cases")] 
    public void Dump_Names_AllCases()
    {
        var data = Load();
        DumpList("Jména", data.names);
    }

    [Test, Explicit("Manual dump of surnames declension across all cases")] 
    public void Dump_Surnames_AllCases()
    {
        var data = Load();
        DumpList("Příjmení", data.surnames);
    }

    [Test, Explicit("Manual dump of full names declension across all cases")] 
    public void Dump_FullNames_AllCases()
    {
        var data = Load();
        DumpList("Celá jména", data.fullnames);
    }

    private static void DumpList(string heading, string[] inputs)
    {
        TestContext.Out.WriteLine($"## {heading}");
        foreach (var c in Enum.GetValues(typeof(CzechCase)).Cast<CzechCase>()
                                 .Where(x => x != CzechCase.Nominative))
        {
            TestContext.Out.WriteLine($"\n### {(int)c}. pád - {CaseLabel(c)}");
            TestContext.Out.WriteLine("jméno | vyskloňováno");
            TestContext.Out.WriteLine("--- | ---");
            foreach (var s in inputs)
            {
                var res = Declension.Decline(s, c);
                TestContext.Out.WriteLine($"{s} | {res.Output}");
            }
        }
    }

    private static string CaseLabel(CzechCase c) => c switch
    {
        CzechCase.Nominative => "kdo, co",
        CzechCase.Genitive => "koho, čeho",
        CzechCase.Dative => "komu, čemu",
        CzechCase.Accusative => "koho, co",
        CzechCase.Vocative => "oslovujeme",
        CzechCase.Locative => "o kom, o čem",
        CzechCase.Instrumental => "s kým, s čím",
        _ => c.ToString()
    };
}


