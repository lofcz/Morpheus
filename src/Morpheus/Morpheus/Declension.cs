using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using System.Reflection;
using Morpheus.Data;
using System.Text;

namespace Morpheus;

public enum CzechCase
{
    /// <summary>
    /// 1. pád – kdo, co
    /// </summary>
    Nominative = 1,
    
    /// <summary>
    /// 2. pád – koho, čeho
    /// </summary>
    Genitive = 2,
    
    /// <summary>
    ///  3. pád – komu, čemu
    /// </summary>
    Dative = 3,
    
    /// <summary>
    /// 4. pád – koho, co
    /// </summary>
    Accusative = 4,
    
    /// <summary>
    /// 5. pád – oslovujeme, voláme
    /// </summary>
    Vocative = 5,
    
    /// <summary>
    /// 6. pád – o kom, o čem
    /// </summary>
    Locative = 6,
    
    /// <summary>
    /// 7. pád – s kým, s čím
    /// </summary>
    Instrumental = 7
}

public enum DetectedGender
{
    Masculine,
    Feminine,
    Ambiguous
}

public enum DetectedEntityType
{
    Name,
    Company,
    Nickname,
    Invalid
}

public enum TokenRole
{
    Title,
    FirstName,     // křestní jméno (včetně prostředních jmen)
    LastName,      // příjmení
    Specialization, // ml., st., mladší, starší
    CompanySpecifier, // s.r.o., a.s., etc.
    Bracket,       // (něco), [něco], {něco}
    Nickname,      // "něco", 'něco', „něco"
    Unknown
}

public class NameToken
{
    public string Original { get; set; } = string.Empty;
    public string Normalized { get; set; } = string.Empty;
    public TokenRole Role { get; set; } = TokenRole.Unknown;
    public int Position { get; set; }

    public override string ToString()
    {
        return Normalized;
    }
}

public sealed class DeclensionOptions
{
    public bool OmitFirstName { get; init; }
    public bool OmitLastName { get; init; }
    public bool OmitTitles { get; init; }
    public bool Explain { get; init; }
    public bool DisableTitleSalutation { get; init; }
    public string? CustomMaleSalutationPrefix { get; init; }
    public string? CustomFemaleSalutationPrefix { get; init; }
    public string? CustomCompanySalutationPrefix { get; init; }
}

public sealed class DeclensionResult
{
    public required string Input { get; init; }
    public required string Output { get; init; }
    public required CzechCase TargetCase { get; init; }
    public required DetectedGender Gender { get; init; }
    public required DetectedEntityType EntityType { get; init; }
    public string? Explanation { get; init; }
}

public static class Declension
{
    // Lazy-loaded BK index for name roles (First/Surname/Both)
    private static readonly Lazy<NameSearcher?> NameSearcherLazy = new Lazy<NameSearcher?>(InitializeNameSearcher, isThreadSafe: true);

    private static NameSearcher? InitializeNameSearcher()
    {
        try
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string assemblyDir = Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;
            string indexPath = Path.Combine(assemblyDir, "Data", "names_index.bk");
            if (!File.Exists(indexPath))
            {
                return null;
            }
            return new NameSearcher(indexPath);
        }
        catch
        {
            return null;
        }
    }

    private static TokenRole ResolveRoleFromIndex(string token)
    {
        NameSearcher? searcher = NameSearcherLazy.Value;
        if (searcher == null) return TokenRole.Unknown;

        // Exact match only to avoid false positives
        List<NameEntry>? results = searcher.Search(token, 0);
        if (results == null || results.Count == 0) return TokenRole.Unknown;

        // Take the first result (index ensures unique per key) and map its role
        NameRole role = results[0].Role;
        bool isFirst = role.HasFlag(NameRole.First);
        bool isSurname = role.HasFlag(NameRole.Surname);
        if (isFirst && !isSurname) return TokenRole.FirstName;
        if (isSurname && !isFirst) return TokenRole.LastName;
        // If both or none, keep unknown to be refined later
        return TokenRole.Unknown;
    }

    /// <summary>
    /// Get the canonical form of a title if it exists in the known titles lookup table.
    /// Returns the canonical form if found, null otherwise.
    /// </summary>
    public static string? GetCanonicalTitle(string title)
    {
        return KnownTitles.ContainsKey(title) ? 
            KnownTitles.Keys.FirstOrDefault(k => string.Equals(k, title, StringComparison.OrdinalIgnoreCase)) : 
            null;
    }
    
    // Comprehensive Czech titles and their properties
    private static readonly Dictionary<string, TitleInfo> KnownTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Salutations
        ["Pan"] = new TitleInfo { Type = TitleType.Salutation, Gender = TitleGender.Masculine, PlacesBefore = true },
        ["Paní"] = new TitleInfo { Type = TitleType.Salutation, Gender = TitleGender.Feminine, PlacesBefore = true },
        
        // Bachelor degrees
        ["Bc."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Bachelor, PlacesBefore = true, Rank = SalutationRank.None },
        ["BcA."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Bachelor, PlacesBefore = true, Rank = SalutationRank.None },
        ["Bc"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Bachelor, PlacesBefore = true, Rank = SalutationRank.None },
        ["BcA"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Bachelor, PlacesBefore = true, Rank = SalutationRank.None },
        
        // Master/Engineer degrees
        ["Ing."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "inženýre", FemaleVocRoot = "inženýrko" },
        ["Ing"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "inženýre", FemaleVocRoot = "inženýrko" },
        ["Ing. arch."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "architekte", FemaleVocRoot = "architektko" },
        ["MUDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["MUDr"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["MDDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["MDDr"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["MVDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["MVDr"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["MgA."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "magistře", FemaleVocRoot = "magistro" },
        ["MgA"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "magistře", FemaleVocRoot = "magistro" },
        ["Mgr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "magistře", FemaleVocRoot = "magistro" },
        ["Mgr"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "magistře", FemaleVocRoot = "magistro" },
        ["JUDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["PhDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["RNDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["PharmDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["ThLic."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "licenciáte", FemaleVocRoot = "licenciátko" },
        ["ThLic"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "licenciáte", FemaleVocRoot = "licenciátko" },
        ["ThDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        
        // Historical master degrees
        ["akad. arch."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.HistoricMaster, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "architekte", FemaleVocRoot = "architektko" },
        ["ak. arch."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.HistoricMaster, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "architekte", FemaleVocRoot = "architektko" },
        ["ak. architekt"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.HistoricMaster, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "architekte", FemaleVocRoot = "architektko" },
        ["akad. architekt"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.HistoricMaster, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "architekte", FemaleVocRoot = "architektko" },
        ["ak. mal."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.HistoricMaster, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "malíři", FemaleVocRoot = "malířko" },
        ["akad. mal."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.HistoricMaster, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "malíři", FemaleVocRoot = "malířko" },
        ["ak. malíř"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.HistoricMaster, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "malíři", FemaleVocRoot = "malířko" },
        ["akad. malíř"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.HistoricMaster, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "malíři", FemaleVocRoot = "malířko" },
        ["ak. soch."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.HistoricMaster, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "sochaři", FemaleVocRoot = "sochařko" },
        ["akad. soch."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.HistoricMaster, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "sochaři", FemaleVocRoot = "sochařko" },
        ["ak. sochař"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.HistoricMaster, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "sochaři", FemaleVocRoot = "sochařko" },
        ["akad. sochař"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.HistoricMaster, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "sochaři", FemaleVocRoot = "sochařko" },
        ["MSDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["PaedDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["PhMr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "magistře", FemaleVocRoot = "magistro" },
        ["RCDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["RSDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["RTDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["ThMgr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true, Rank = SalutationRank.Academic, MaleVocRoot = "magistře", FemaleVocRoot = "magistro" },
        
        // Doctoral degrees (after name)
        ["Ph.D."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Doctorate, PlacesBefore = false, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["Ph. D."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Doctorate, PlacesBefore = false, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["PhD"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Doctorate, PlacesBefore = false, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["DSc."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Doctorate, PlacesBefore = false, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["CSc."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Doctorate, PlacesBefore = false, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["Dr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Doctorate, PlacesBefore = false, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["DrSc."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Doctorate, PlacesBefore = false, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["Th.D."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Doctorate, PlacesBefore = false, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["Th. D."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Doctorate, PlacesBefore = false, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["ThD"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Doctorate, PlacesBefore = false, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        
        // Academic positions (before name, lowercase)
        ["as."] = new TitleInfo { Type = TitleType.Position, PlacesBefore = true, Rank = SalutationRank.None },
        ["odb. as."] = new TitleInfo { Type = TitleType.Position, PlacesBefore = true, Rank = SalutationRank.None },
        ["doc."] = new TitleInfo { Type = TitleType.Position, PlacesBefore = true, Rank = SalutationRank.AcademicPosition, MaleVocRoot = "docente", FemaleVocRoot = "docentko" },
        ["prof."] = new TitleInfo { Type = TitleType.Position, PlacesBefore = true, Rank = SalutationRank.AcademicPosition, MaleVocRoot = "profesore", FemaleVocRoot = "profesorko" },
        
        // Non-academic titles
        ["DiS."] = new TitleInfo { Type = TitleType.Professional, PlacesBefore = false },
        
        // Honorary and ceremonial titles
        ["dr. h. c."] = new TitleInfo { Type = TitleType.Honorary, PlacesBefore = false, Rank = SalutationRank.Academic, MaleVocRoot = "doktore", FemaleVocRoot = "doktorko" },
        ["prof. h. c."] = new TitleInfo { Type = TitleType.Honorary, PlacesBefore = false, Rank = SalutationRank.AcademicPosition, MaleVocRoot = "profesore", FemaleVocRoot = "profesorko" },
        
        // International titles (common in Czech context)
        ["MBA"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = false, Rank = SalutationRank.None },
        ["LL.M."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = false, Rank = SalutationRank.None },
        ["LL. M."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = false, Rank = SalutationRank.None },
        ["Jr."] = new TitleInfo { Type = TitleType.Suffix, PlacesBefore = false, Rank = SalutationRank.None },
        ["Sr."] = new TitleInfo { Type = TitleType.Suffix, PlacesBefore = false, Rank = SalutationRank.None },
        
        // Military ranks - Mužstvo (Enlisted)
        ["voj."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "vojíne", FemaleVocRoot = "vojínko" },
        ["svob."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "svobodníku", FemaleVocRoot = "svobodnice" },
        ["sv."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "svobodníku", FemaleVocRoot = "svobodnice" }, // unofficial but common abbreviation
        
        // Military ranks - Poddůstojníci (Non-commissioned officers)
        ["des."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "desátníku", FemaleVocRoot = "desátnice" },
        ["čet."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "četaři", FemaleVocRoot = "četařko" },
        ["rtn."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "rotný", FemaleVocRoot = "rotný" },
        
        // Military ranks - Sbor praporčíků (Warrant officers)
        ["rtm."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "rotmistře", FemaleVocRoot = "rotmistryně" },
        ["nrtm."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "nadrotmistře", FemaleVocRoot = "nadrotmistryně" },
        ["prap."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "praporčíku", FemaleVocRoot = "praporčice" },
        ["nprap."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "nadpraporčíku", FemaleVocRoot = "nadpraporčice" },
        ["št. prap."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "štábní praporčíku", FemaleVocRoot = "štábní praporčice" },
        ["šprap."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "štábní praporčíku", FemaleVocRoot = "štábní praporčice" },
        
        // Military ranks - Sbor nižších důstojníků (Junior officers)
        ["por."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "poručíku", FemaleVocRoot = "poručice" },
        ["npor."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "nadporučíku", FemaleVocRoot = "nadporučice" },
        ["kpt."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "kapitáne", FemaleVocRoot = "kapitánko" },
        
        // Military ranks - Sbor vyšších důstojníků (Senior officers)
        ["mjr."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "majore", FemaleVocRoot = "majorko" },
        ["pplk."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "podplukovníku", FemaleVocRoot = "podplukovnice" },
        ["plk."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "plukovníku", FemaleVocRoot = "plukovnice" },
        
        // Military ranks - Sbor generálů (Generals)
        ["brig.gen."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "brigádní generále", FemaleVocRoot = "brigádní generálko" },
        ["genmjr."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "generálmajore", FemaleVocRoot = "generálmajorko" },
        ["genpor."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "generálporučíku", FemaleVocRoot = "generálporučice" },
        ["arm.gen."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "armádní generále", FemaleVocRoot = "armádní generálko" },
        
        // Historical military ranks (still may appear in documents)
        ["ppor."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "podporučíku", FemaleVocRoot = "podporučice" }, // podporučík (abolished 2011)
        ["škpt."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "štábní kapitáne", FemaleVocRoot = "štábní kapitánko" }, // štábní kapitán
        ["šrtm."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "štábní rotmistře", FemaleVocRoot = "štábní rotmistryně" }, // štábní rotmistr (abolished 2011)
        ["gen."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "generále", FemaleVocRoot = "generálko" }, // general (historical)
        ["genplk."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true, Rank = SalutationRank.Military, MaleVocRoot = "generálplukovníku", FemaleVocRoot = "generálplukovnice" }, // generálplukovník (historical)
        
        // Ecclesiastical titles - Czech Catholic Church
        ["PP."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // papež
        ["J.Em."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // kardinál
        ["J.Exc."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // (arci)biskup
        ["J.M."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // opat, prelát
        ["Vdp."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // vysoce důstojný pán
        ["AMPLMUS"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // amplissimus
        ["A.R.D."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // veledůstojný
        ["Vldp."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // veledůstojný pán
        ["R.D."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // reverendus dominus
        ["Dp."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // důstojný pán
        ["Vp."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // velebný pán
        ["Rev. dom."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // reverendus dominus
        ["Ct.p."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // ctihodný pán
        
        // International ecclesiastical titles (English/Latin)
        ["Rev."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "reverende", FemaleVocRoot = "reverendko" },
        ["Very Rev."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "reverende", FemaleVocRoot = "reverendko" },
        ["Most Rev."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "reverende", FemaleVocRoot = "reverendko" },
        ["Rt. Rev."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "reverende", FemaleVocRoot = "reverendko" },
        ["Right Rev."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "reverende", FemaleVocRoot = "reverendko" },
        ["Fr."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "otče", FemaleVocRoot = null },
        ["Father"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "otče", FemaleVocRoot = null },
        ["Sister"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = null, FemaleVocRoot = "sestro" },
        ["Br."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "bratře", FemaleVocRoot = null },
        ["Brother"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "bratře", FemaleVocRoot = null },
        ["Dcn."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "jáhne", FemaleVocRoot = "jáhenko" },
        ["Deacon"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "jáhne", FemaleVocRoot = "jáhenko" },
        ["Bp."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "biskupe", FemaleVocRoot = "biskupko" },
        ["Bishop"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "biskupe", FemaleVocRoot = "biskupko" },
        ["Abp."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "arcibiskupe", FemaleVocRoot = "arcibiskupko" },
        ["Archbishop"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "arcibiskupe", FemaleVocRoot = "arcibiskupko" },
        ["Msgr."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "monsignore", FemaleVocRoot = "monsignorko" },
        ["Monsignor"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "monsignore", FemaleVocRoot = "monsignorko" },
        ["Card."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "kardinále", FemaleVocRoot = "kardinálko" },
        ["Cardinal"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "kardinále", FemaleVocRoot = "kardinálko" },
        ["Dom"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "dome", FemaleVocRoot = null },
        ["Abbot"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "opate", FemaleVocRoot = "abatyše" },
        ["Mother"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = null, FemaleVocRoot = "matko" },
        ["Pastor"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "pastore", FemaleVocRoot = "pastore" },
        ["Padre"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true, Rank = SalutationRank.Ecclesiastical, MaleVocRoot = "otče", FemaleVocRoot = null },
        
        // Common ecclesiastical postnominals  
        ["V.G."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // vicar general
        ["P.A."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // protonotary apostolic
        ["J.C.D."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // doctor of canon law
        ["S.T.D."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // doctor of sacred theology
        ["D.D."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false }, // doctor of divinity
        ["Dr. eccl."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = false } // ecclesiastical doctor
    };

    private enum TitleType
    {
        Salutation,
        Academic,
        Position,
        Professional,
        Honorary,
        Military,
        Ecclesiastical,
        Suffix
    }

    private enum TitleLevel
    {
        Bachelor,
        HistoricMaster, // e.g., akad. arch., ak. mal., ak. soch. — between Bachelor and Master
        Master,
        Doctorate
    }

    private enum TitleGender
    {
        Neutral,
        Masculine,
        Feminine
    }

    enum SalutationRank
    {
        None, 
        Academic,
        AcademicPosition, 
        Ecclesiastical,
        Military
    }

    private sealed class TitleInfo
    {
        public required TitleType Type { get; init; }
        public TitleLevel? Level { get; init; }
        public TitleGender Gender { get; init; } = TitleGender.Neutral;
        public bool PlacesBefore { get; init; }
        // NEW: vocative roots and salutation behaviour
        public string? MaleVocRoot { get; init; }
        public string? FemaleVocRoot { get; init; }
        public SalutationRank Rank { get; init; } = SalutationRank.None;
        public string? VocativeMale { get; init; }
        public string? VocativeFemale { get; init; }
        public string? VocativeRoot { get; init; }
        public bool OverridesName { get; init; }
    }

    private class ParsedTitles
    {
        public List<string> BeforeTitles { get; } = new();
        public List<string> AfterTitles { get; } = new();
        public string NamePart { get; set; } = string.Empty;
        public DetectedGender? ImpliedGender { get; set; }
    }

    // Step 1: Normalize input (trim, spaces, dash types)
    private static string NormalizeInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        
        // Trim and normalize spaces
        string normalized = input.Trim();
        normalized = Regex.Replace(normalized, @"\s{2,}", " "); // Multiple spaces to single space
        
        // Normalize various dash types to standard dash
        normalized = normalized.Replace('–', '-').Replace('—', '-').Replace('−', '-');
        
        return normalized;
    }

    /// <summary>
    /// Tokenize input string while inserting an implicit space after abbreviations such as "Ing." when directly followed by an uppercase letter or digit.
    /// Hyphens are preserved inside tokens (e.g., "Marie-Antonie").  Dots inside lower-case sequences like "s.r.o." are also preserved.
    /// </summary>
    private static List<string> Tokenize(string input)
    {
        List<string> tokens = new();
        if (string.IsNullOrEmpty(input)) return tokens;

        StringBuilder sb = new();

        void Flush()
        {
            if (sb.Length == 0) return;
            tokens.Add(sb.ToString());
            sb.Clear();
        }

        for (int i = 0; i < input.Length; i++)
        {
            char ch = input[i];

            if (char.IsWhiteSpace(ch)) { Flush(); continue; }

            switch (ch)
            {
                // Handle punctuation as delimiter or abbreviation
                case ',':
                case ';':
                case ':':
                    // comma / semicolon / colon act purely as separators
                    Flush();
                    continue;
                case '.':
                {
                    bool nextIsUpperOrDigit = i + 1 < input.Length && !char.IsWhiteSpace(input[i + 1]) &&
                                              (char.IsUpper(input[i + 1]) || char.IsDigit(input[i + 1]));
                    bool nextIsUpperAfterSpace = i + 1 < input.Length && char.IsWhiteSpace(input[i + 1]) &&
                                                  i + 2 < input.Length && char.IsUpper(input[i + 2]);
                    bool isEndOfString = i + 1 >= input.Length;

                    // Check for known dotted abbreviation like "Ph.D." by scanning forward
                    if (nextIsUpperOrDigit)
                    {
                        int j = i + 1;
                        string abbrev = sb.ToString() + ".";
                        while (j < input.Length && (char.IsLetter(input[j]) || input[j] == '.'))
                        {
                            abbrev += input[j];
                            j++;
                        }
                        if (!string.IsNullOrEmpty(sb.ToString()) && KnownTitles.ContainsKey(abbrev))
                        {
                            sb.Clear();
                            sb.Append(abbrev);
                            i = j - 1;
                            Flush();
                            continue;
                        }

                        // Fallback: keep dot with the current token and flush
                        sb.Append('.');
                        Flush();
                        continue;
                    }

                    // Check for known title or name abbreviation when dot is followed by space + uppercase
                    if (nextIsUpperAfterSpace)
                    {
                        string candidate = sb.ToString() + ".";
                        if (!string.IsNullOrEmpty(sb.ToString()) && KnownTitles.ContainsKey(candidate))
                        {
                            // Known title: keep dot
                            sb.Append('.');
                            Flush();
                            continue;
                        }

                        // Not a known title but looks like name abbreviation: strip dot
                        Flush();
                        continue;
                    }

                    // Dot at end of string or followed by whitespace (but not uppercase):
                    // Keep dot as part of token (e.g., "s.r.o.", "a.s.")
                    sb.Append('.');
                    if (isEndOfString)
                    {
                        Flush();
                    }
                    continue;
                }
                default:
                    // regular character (letter, digit, hyphen, etc.)
                    sb.Append(ch);
                    break;
            }
        }

        Flush();
        return tokens;
    }

    // Step 3: Handle titles (detect and temporarily remove) using pre-assigned token roles
    private static ParsedTitles ExtractTitles(List<NameToken> tokens)
    {
        ParsedTitles result = new ParsedTitles();
        List<string> namePartTokens = new List<string>();
        
        foreach (NameToken token in tokens)
        {
            switch (token.Role)
            {
                case TokenRole.Title:
                    TitleInfo? titleInfo = KnownTitles.GetValueOrDefault(token.Original);
                    if (titleInfo != null)
                    {
                        // Check if this title implies gender
                        if (titleInfo.Gender != TitleGender.Neutral && result.ImpliedGender == null)
                        {
                            result.ImpliedGender = titleInfo.Gender == TitleGender.Masculine 
                                ? DetectedGender.Masculine 
                                : DetectedGender.Feminine;
                        }
                        
                        // Place title in correct position based on its properties
                        if (titleInfo.PlacesBefore)
                        {
                            result.BeforeTitles.Add(token.Original);
                        }
                        else
                        {
                            result.AfterTitles.Add(token.Original);
                        }
                    }
                    break;
                    
                case TokenRole.FirstName:
                case TokenRole.LastName:
                    namePartTokens.Add(token.Original);
                    break;
                    
                case TokenRole.Bracket:
                case TokenRole.Nickname:
                    // Skip these for now - could be handled specially later
                    break;
                    
                case TokenRole.Specialization:
                    // Skip specializations (ml., st.)
                    break;
                    
                case TokenRole.CompanySpecifier:
                    // Don't add company specifiers to titles - they'll be handled in the declension result
                    break;
            }
        }
        
        result.NamePart = string.Join(" ", namePartTokens);
        return result;
    }

    private static List<NameToken> AssignTokenRoles(string input)
    {
        List<string> rawTokens = Tokenize(input);
        List<NameToken> nameTokens = new();
        
        for (int i = 0; i < rawTokens.Count; i++)
        {
            string token = rawTokens[i];
            NameToken nameToken = new NameToken
            {
                Original = token,
                Normalized = token.ToLowerInvariant().Trim(),
                Position = i
            };
            
            // Assign role based on various criteria
            nameToken.Role = DetermineTokenRole(nameToken, i, rawTokens.ToArray());
            nameTokens.Add(nameToken);
        }

        // Post-process to handle multi-token patterns (like "s. r. o.")
        ProcessMultiTokenPatterns(nameTokens);
        
        // Post-process to refine firstname/lastname assignments
        RefineNameTokenRoles(nameTokens);
        
        return nameTokens;
    }

    private static void ProcessMultiTokenPatterns(List<NameToken> tokens)
    {
        // Check for company specifiers using n-grams (sliding window approach)
        DetectCompanySpecifiers(tokens);
    }

    private static void DetectCompanySpecifiers(List<NameToken> tokens)
    {
        // Define base company patterns (without spaces/dots)
        Dictionary<string, int> companyBasePatterns = new Dictionary<string, int>
        {
            {"sro", 3},      // s.r.o., s. r. o., s r o
            {"as", 2},       // a.s., a. s., a s
            {"sp", 2},       // s.p., s. p., s p
            {"spol", 1},     // spol., spol
            {"corp", 1},     // corp., corp
            {"inc", 1},      // inc., inc
            {"ltd", 1},      // ltd., ltd
            {"llc", 1}       // llc., llc
        };

        // Check each possible n-gram position
        for (int i = 0; i < tokens.Count; i++)
        {
            foreach (KeyValuePair<string, int> pattern in companyBasePatterns)
            {
                string basePattern = pattern.Key;
                int expectedTokens = pattern.Value;
                
                if (i + expectedTokens <= tokens.Count)
                {
                    // Extract normalized tokens and remove dots/spaces
                    string ngram = string.Join("", tokens.Skip(i).Take(expectedTokens)
                        .Select(t => t.Normalized.Replace(".", "").Replace(" ", "")));
                    
                    if (ngram == basePattern)
                    {
                        // Mark all tokens in this n-gram as company specifiers
                        for (int j = i; j < i + expectedTokens; j++)
                        {
                            tokens[j].Role = TokenRole.CompanySpecifier;
                        }
                        i += expectedTokens - 1; // Skip ahead to avoid overlapping matches
                        break;
                    }
                }
            }
        }

        // Handle single-character specifiers that are clearly company-related
        for (int i = 0; i < tokens.Count; i++)
        {
            NameToken token = tokens[i];
            if (token.Role == TokenRole.Unknown)
            {
                string clean = token.Normalized.Replace(".", "").Replace(" ", "");
                if (clean == "&" || clean == "and" || clean == "co" || clean == "holding" || clean == "se")
                {
                    token.Role = TokenRole.CompanySpecifier;
                }
            }
        }
    }

    private static TokenRole DetermineTokenRole(NameToken token, int position, string[] allTokens)
    {
        string normalized = token.Normalized;
        
        // 1. Check for titles
        string? foundTitle = FindTitle(token.Original, position, allTokens);
        if (foundTitle != null)
        {
            return TokenRole.Title;
        }
        
        // 2. Check for brackets
        if (normalized.StartsWith('(') || normalized.StartsWith('[') || normalized.StartsWith('{'))
        {
            return TokenRole.Bracket;
        }
        
        // 3. Check for nicknames (quotes and non-standard patterns)
        if (normalized.StartsWith('"') || normalized.StartsWith('\'') || 
            normalized.StartsWith('"') || normalized.StartsWith('„') ||
            normalized.Contains('"') || normalized.Contains('\'') ||
            normalized.Contains('"') || normalized.Contains('„') ||
            Regex.IsMatch(normalized, @"[_@#\d]|xXx|^\w+\d+$", RegexOptions.IgnoreCase))
        {
            return TokenRole.Nickname;
        }
        
        // 4. Check for specializations
        if (IsSpecialization(normalized))
        {
            return TokenRole.Specialization;
        }
        
        // 5. Check for single-token company specifiers
        if (IsCompanySpecifier(normalized))
        {
            return TokenRole.CompanySpecifier;
        }

        // 6. Consult BK index for an exact role decision
        TokenRole indexRole = ResolveRoleFromIndex(token.Original);
        if (indexRole != TokenRole.Unknown)
        {
            return indexRole;
        }

        bool couldBeFirstName = ScrapedDeclensionData.FirstNames.Contains(normalized) ||
                                ScrapedDeclensionData.FirstNames.Contains(Normalizer.RemoveDiacritics(normalized));

        bool couldBeLastName = ScrapedDeclensionData.LastNames.Contains(normalized) ||
                                ScrapedDeclensionData.LastNames.Contains(Normalizer.RemoveDiacritics(normalized));

        /*if (couldBeFirstName && !couldBeLastName)
        {
            return TokenRole.FirstName;
        }

        if (couldBeLastName && !couldBeFirstName)
        {
            return TokenRole.LastName;
        }*/
        
        // 7. Default - will be refined later
        return TokenRole.Unknown;
    }

    private static void RefineNameTokenRoles(List<NameToken> tokens)
    {
        List<NameToken> nameTokens = tokens.Where(t => t.Role == TokenRole.FirstName || 
                                                       t.Role == TokenRole.LastName || 
                                                       t.Role == TokenRole.Unknown).ToList();
        
        if (nameTokens.Count == 0) return;
		
		// If at least one first name is already detected, treat all unknown tokens as last names
		if (nameTokens.Any(t => t.Role == TokenRole.FirstName))
		{
			foreach (NameToken token in nameTokens)
			{
				if (token.Role == TokenRole.Unknown)
				{
					token.Role = TokenRole.LastName;
				}
			}
			return;
		}
		
		// Simple heuristic fallback: 
		// - Last unknown/name token is likely surname
		// - Everything else is likely firstname
		NameToken? lastNameToken = nameTokens.LastOrDefault(t => t.Role == TokenRole.Unknown || 
                                                                 t.Role == TokenRole.FirstName || 
                                                                 t.Role == TokenRole.LastName);
		
		foreach (NameToken token in nameTokens)
		{
			if (token.Role == TokenRole.Unknown)
            {
                // continue;
                
				if (token == lastNameToken && nameTokens.Count > 1)
				{
					// Use morphological rules to verify if this could be a surname
					if (CouldBeSurname(token.Normalized))
					{
						token.Role = TokenRole.LastName;
					}
					else
					{
						token.Role = TokenRole.FirstName;
					}
				}
				else
				{
					token.Role = TokenRole.FirstName;
				}
			}
		}
    }

    private static bool IsSpecialization(string normalized)
    {
        return normalized is "ml." or "ml" or "st." or "st" or "mladší" or "starší" or "jun." or "jun" or "sr." or "sr";
    }

    private static bool IsCompanySpecifier(string normalized)
    {
        string[] companyPatterns = new[]
        {
            "s.r.o.", "s.r.o", "s. r. o.", "s. r. o", 
            "a.s.", "a.s", "a. s.", "a. s",
            "s.p.", "s.p", "s. p.", "s. p", 
            "spol.", "spol", "se", "holding",
            "&", "and", "co", "co.", "corp", "corp.", "inc", "inc.", "ltd", "ltd.", "llc", "llc."
        };
        return companyPatterns.Contains(normalized);
    }

    private static bool CouldBeSurname(string normalized)
    {
        // Basic morphological rules for Czech surnames
        // This is a simplified version - could be expanded
        
        // Common surname endings
        string[] surnameEndings = new[]
        {
            "ová", "ský", "ská", "ní", "ec", "ák", "ek", "ík", "an", "el", "ka", "ny"
        };
        
        foreach (string ending in surnameEndings)
        {
            if (normalized.EndsWith(ending))
            {
                return true;
            }
        }
        
        // Check if it follows typical Czech surname patterns
        // (This could be much more sophisticated)
        return normalized.Length > 3; // Basic length check
    }

    private static string? FindTitle(string token, int position, string[] allTokens)
    {
        // First, try exact match (case-insensitive for title recognition)
        if (KnownTitles.ContainsKey(token))
        {
            return token;
        }
        
        // Try multi-word titles starting at this position
        for (int length = 2; length <= Math.Min(3, allTokens.Length - position); length++)
        {
            string candidate = string.Join(" ", allTokens.Skip(position).Take(length));
            if (KnownTitles.ContainsKey(candidate))
            {
                return candidate;
            }
        }
        
        // Special handling for titles with periods that might be written without spaces
        // e.g., "Ing.arch." instead of "Ing. arch.", "št.prap." instead of "št. prap."
        if (token.Contains('.') && token.Length > 2)
        {
            // Try adding spaces after periods (except the last one)
            string withSpaces = AddSpacesAfterPeriods(token);
            if (KnownTitles.ContainsKey(withSpaces))
            {
                return withSpaces;
            }
            
            // Try common variations for compressed military titles
            string normalized = NormalizeMilitaryTitle(token);
            if (!string.IsNullOrEmpty(normalized) && KnownTitles.ContainsKey(normalized))
            {
                return normalized;
            }
        }
        
        // Special case for military titles that may have alternative abbreviations
        string? militaryVariant = FindMilitaryTitleVariant(token);
        if (!string.IsNullOrEmpty(militaryVariant))
        {
            return militaryVariant;
        }
        
        return null;
    }

    private static string AddSpacesAfterPeriods(string input)
    {
        // Add space after each period except the last one
        string result = input;
        for (int i = 0; i < result.Length - 1; i++)
        {
            if (result[i] == '.' && result[i + 1] != ' ' && result[i + 1] != '.')
            {
                result = result.Insert(i + 1, " ");
                i++; // Skip the inserted space
            }
        }
        return result;
    }

    private static string NormalizeMilitaryTitle(string input)
    {
        // Handle common compressed military title patterns
        Dictionary<string, string> patterns = new Dictionary<string, string>
        {
            ["št.prap."] = "št. prap.",
            ["brig.gen."] = "brig.gen.", // This one is already correctly spaced
            ["arm.gen."] = "arm.gen.",   // This one is already correctly spaced
            // Add more patterns as needed
        };

        return patterns.TryGetValue(input, out string? normalized) ? normalized : string.Empty;
    }

    private static string? FindMilitaryTitleVariant(string token)
    {
        // Handle alternative military abbreviations
        Dictionary<string, string> variants = new Dictionary<string, string>
        {
            ["sv."] = "svob.", // sv. is unofficial but common abbreviation for svobodník
        };

        if (variants.TryGetValue(token, out string? canonical) && KnownTitles.ContainsKey(canonical))
        {
            return canonical;
        }

        return null;
    }

    /// <summary>
    /// Filter duplicate surnames from the token list, keeping only the first occurrence of each unique surname.
    /// Only applies to tokens with LastName role. FirstName tokens are not filtered as people can have 
    /// multiple identical first names (e.g., "Jan Jan Novák").
    /// </summary>
    private static List<NameToken> FilterDuplicateSurnames(List<NameToken> tokens)
    {
        var result = new List<NameToken>();
        var seenSurnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (NameToken token in tokens)
        {
            if (token.Role == TokenRole.LastName)
            {
                // For surnames, check if we've already seen this exact name (case-insensitive)
                if (seenSurnames.Contains(token.Normalized))
                {
                    // Skip this duplicate surname
                    continue;
                }
                
                // First occurrence of this surname - add it to seen set and include in result
                seenSurnames.Add(token.Normalized);
                result.Add(token);
            }
            else
            {
                // For non-surname tokens (first names, titles, etc.), always include
                result.Add(token);
            }
        }
        
        return result;
    }

    // Step 3: Infer gender using prebuilt data + heuristics
    // Step 4: Infer gender from tokens (enhanced with scraped data and token roles)
    private static DetectedGender InferGender(List<NameToken> tokens)
    {
        List<NameToken> nameTokens = tokens.Where(t => t.Role == TokenRole.FirstName || t.Role == TokenRole.LastName).ToList();
        if (nameTokens.Count == 0) return DetectedGender.Ambiguous;

        List<NameToken> firstNameTokens = nameTokens.Where(t => t.Role == TokenRole.FirstName).ToList();
        List<NameToken> lastNameTokens = nameTokens.Where(t => t.Role == TokenRole.LastName).ToList();

        // Collect gender evidence from all sources with confidence weights
        List<GenderEvidence> genderEvidence = new List<GenderEvidence>();

        // 1. Scraped data evidence (highest confidence) - first names only
        foreach (NameToken token in firstNameTokens)
        {
            string normalizedWithoutDiacritics = Normalizer.RemoveDiacritics(token.Normalized);
            
            if (ScrapedDeclensionData.Names.TryGetValue(normalizedWithoutDiacritics, out ScrapedDeclensionData.NameDeclensionData? nameData))
            {
                DetectedGender gender = nameData.Gender switch
                {
                    0 => DetectedGender.Masculine,
                    1 => DetectedGender.Feminine,
                    _ => DetectedGender.Ambiguous
                };
                
                if (gender != DetectedGender.Ambiguous)
                {
                    genderEvidence.Add(new GenderEvidence(gender, 10, $"Scraped data: {token.Original}"));
                }
            }
        }

        // 2. Built-in gender data evidence (medium confidence) - first names only
        foreach (NameToken token in firstNameTokens)
        {
            List<string> candidates = new List<string> { token.Original };
            if (token.Original.Contains('-'))
                candidates.AddRange(token.Original.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            foreach (string cand in candidates)
            {
                if (NameGenderData.Names.TryGetValue(cand, out NameGenderData.NameGender g))
                {
                    DetectedGender gender = g switch
                    {
                        NameGenderData.NameGender.Female => DetectedGender.Feminine,
                        NameGenderData.NameGender.Male => DetectedGender.Masculine,
                        _ => DetectedGender.Ambiguous
                    };
                    
                    if (gender != DetectedGender.Ambiguous)
                    {
                        genderEvidence.Add(new GenderEvidence(gender, 7, $"Built-in data: {cand}"));
                    }
                }
            }
        }

        // 3. Surname morphology evidence (lower confidence) - last names only
        foreach (NameToken token in lastNameTokens)
        {
            string normalized = token.Normalized;
            
            if (normalized.EndsWith("ová", StringComparison.OrdinalIgnoreCase))
            {
                genderEvidence.Add(new GenderEvidence(DetectedGender.Feminine, 5, $"Surname ending: {token.Original} (-ová)"));
            }
            else if (normalized.EndsWith("ova", StringComparison.OrdinalIgnoreCase))
            {
                genderEvidence.Add(new GenderEvidence(DetectedGender.Feminine, 5, $"Surname ending: {token.Original} (-ova)"));
            }
            else if (normalized.EndsWith("á", StringComparison.OrdinalIgnoreCase))
            {
                genderEvidence.Add(new GenderEvidence(DetectedGender.Feminine, 3, $"Surname ending: {token.Original} (-á)"));
            }
        }

        // 4. General morphological evidence (all name tokens) - covers adjectives, nouns, titles
        foreach (NameToken token in nameTokens)
        {
            string word = token.Original;
            
            // Feminine endings
            if (word.EndsWith("ová", StringComparison.OrdinalIgnoreCase))
            {
                genderEvidence.Add(new GenderEvidence(DetectedGender.Feminine, 6, $"Feminine ending: {word} (-ová)"));
            }
            else if (word.EndsWith("ova", StringComparison.OrdinalIgnoreCase))
            {
                genderEvidence.Add(new GenderEvidence(DetectedGender.Feminine, 6, $"Feminine ending: {word} (-ova)"));
            }
            else if (word.EndsWith("á", StringComparison.OrdinalIgnoreCase))
            {
                genderEvidence.Add(new GenderEvidence(DetectedGender.Feminine, 4, $"Feminine ending: {word} (-á)"));
            }
            else if (word.EndsWith("ka", StringComparison.OrdinalIgnoreCase))
            {
                genderEvidence.Add(new GenderEvidence(DetectedGender.Feminine, 3, $"Feminine ending: {word} (-ka)"));
            }
            else if (word.EndsWith("ice", StringComparison.OrdinalIgnoreCase))
            {
                genderEvidence.Add(new GenderEvidence(DetectedGender.Feminine, 3, $"Feminine ending: {word} (-ice)"));
            }
            else if (word.EndsWith("ese", StringComparison.OrdinalIgnoreCase) || word.EndsWith("esa", StringComparison.OrdinalIgnoreCase))
            {
                // e.g., "komtesa" (countess), "princeza" (princess)
                genderEvidence.Add(new GenderEvidence(DetectedGender.Feminine, 5, $"Feminine title/noun: {word}"));
            }
            // Masculine endings
            else if (word.EndsWith("ský", StringComparison.OrdinalIgnoreCase) || word.EndsWith("cký", StringComparison.OrdinalIgnoreCase))
            {
                genderEvidence.Add(new GenderEvidence(DetectedGender.Masculine, 4, $"Masculine ending: {word} (-ský/-cký)"));
            }
            else if (word.EndsWith("ý", StringComparison.OrdinalIgnoreCase) && word.Length > 3)
            {
                genderEvidence.Add(new GenderEvidence(DetectedGender.Masculine, 2, $"Masculine ending: {word} (-ý)"));
            }
        }

        // 5. Analyze evidence and make decision
        if (genderEvidence.Count == 0) return DetectedGender.Ambiguous;

        // Group by gender and calculate total confidence scores
        int masculineScore = genderEvidence.Where(e => e.Gender == DetectedGender.Masculine).Sum(e => e.Confidence);
        int feminineScore = genderEvidence.Where(e => e.Gender == DetectedGender.Feminine).Sum(e => e.Confidence);

        // Require a minimum confidence difference to avoid ambiguous cases
        const int minConfidenceDifference = 2;
        
        if (Math.Abs(masculineScore - feminineScore) < minConfidenceDifference)
        {
            return DetectedGender.Ambiguous;
        }

        return masculineScore > feminineScore ? DetectedGender.Masculine : DetectedGender.Feminine;
    }

    private record GenderEvidence(DetectedGender Gender, int Confidence, string Source);

    // Step 5: Infer entity type using token roles
    private static DetectedEntityType InferEntityType(List<NameToken> tokens)
    {
        if (tokens.Count == 0) return DetectedEntityType.Invalid;

        // Check if we have company specifiers
        if (tokens.Any(t => t.Role == TokenRole.CompanySpecifier))
        {
            return DetectedEntityType.Company;
        }

        // Check if we have nicknames
        if (tokens.Any(t => t.Role == TokenRole.Nickname))
        {
            return DetectedEntityType.Nickname;
        }

        // Check if we have valid name tokens (firstname or lastname)
        List<NameToken> nameTokens = tokens.Where(t => t.Role == TokenRole.FirstName || t.Role == TokenRole.LastName).ToList();
        if (nameTokens.Count > 0)
        {
            return DetectedEntityType.Name;
        }

        // If we only have unknown tokens, try some basic patterns
        List<NameToken> unknownTokens = tokens.Where(t => t.Role == TokenRole.Unknown).ToList();
        if (unknownTokens.Count > 0)
        {
            string combinedText = string.Join(" ", unknownTokens.Select(t => t.Original));
            
            // Company patterns
            if (Regex.IsMatch(combinedText, @"\b(firm|firma|bank|banka|úvěr|pojišť|holding|group|ltd\.|inc\.|corp\.)", RegexOptions.IgnoreCase))
            {
                return DetectedEntityType.Company;
            }

            // Nickname patterns (non-standard characters, numbers, special symbols)
            if (Regex.IsMatch(combinedText, @"[_@#\d]|xXx|^\w+\d+$", RegexOptions.IgnoreCase))
            {
                return DetectedEntityType.Nickname;
            }

            // Proper name patterns (standard Czech name structure)
            if (Regex.IsMatch(combinedText, @"^[A-ZÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ][a-záčďéěíňóřšťúůýž]+(\s+[A-ZÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ][a-záčďéěíňóřšťúůýž]+)*$"))
            {
                return DetectedEntityType.Name;
            }
        }

        return DetectedEntityType.Invalid;
    }

    public static DeclensionResult Decline(string input, CzechCase @case, DeclensionOptions? options = null)
    {
        options ??= new DeclensionOptions();

        // Step 1: Normalize input
        string normalizedInput = NormalizeInput(input);

        // Step 2: Assign token roles
        List<NameToken> tokens = AssignTokenRoles(normalizedInput);

        // Step 2.5: Filter duplicate surnames (keep only first occurrence)
        tokens = FilterDuplicateSurnames(tokens);

        // Step 3: Handle titles (detect and temporarily remove)
        ParsedTitles parsedTitles = ExtractTitles(tokens);

        // Step 4: Infer gender (use title-implied gender if available)
        DetectedGender detectedGender = parsedTitles.ImpliedGender ?? InferGender(tokens);

        // Step 5: Infer entity type
        DetectedEntityType entityType = InferEntityType(tokens);

        // Step 6: Infer the declension result
        string declinedOutput = InferDeclensionResult(tokens, @case, detectedGender, entityType, options, input);

        // Reconstruct final output with titles if not omitted
        string finalOutput = ReconstructOutput(parsedTitles, declinedOutput, @case, detectedGender, options, entityType);

        string? explanation = null;
        if (options.Explain)
        {
            explanation = $"case={@case}; gender={detectedGender}; type={entityType}; titles=[{string.Join(", ", parsedTitles.BeforeTitles.Concat(parsedTitles.AfterTitles))}]";
        }

        return new DeclensionResult
        {
            Input = input,
            Output = finalOutput,
            TargetCase = @case,
            Gender = detectedGender,
            EntityType = entityType,
            Explanation = explanation
        };
    }

    // Step 6: Infer the declension result using pre-assigned token roles
    private static string InferDeclensionResult(List<NameToken> tokens, CzechCase @case, DetectedGender gender, DetectedEntityType entityType, DeclensionOptions options, string originalInput)
    {
        if (tokens.Count == 0) return string.Empty;
        if (entityType == DetectedEntityType.Company) 
        {
            // Return all company-related tokens (but not titles)
            return string.Join(" ", tokens.Where(t => t.Role != TokenRole.Title && 
                                                     t.Role != TokenRole.Bracket && 
                                                     t.Role != TokenRole.Nickname).Select(t => t.Original));
        }

        List<string> declinedWords = new List<string>();

        foreach (NameToken token in tokens)
        {
            // Only decline firstname and lastname tokens
            if (token.Role != TokenRole.FirstName && token.Role != TokenRole.LastName)
                continue;

            // Apply omit options
            bool skip = (token.Role == TokenRole.FirstName && options.OmitFirstName) || 
                       (token.Role == TokenRole.LastName && options.OmitLastName);
            if (skip) continue;

            string declined = DeclineWordWithRole(token, @case, gender, entityType, originalInput);

            declined = NameCasing.NormalizeForRole(declined, token.Role, token.Original);
            
            declinedWords.Add(declined);
        }

        return string.Join(" ", declinedWords);
    }

    private static string DeclineWordWithRole(NameToken token, CzechCase @case, DetectedGender gender, DetectedEntityType entityType, string originalInput)
    {
        bool isLastName = token.Role == TokenRole.LastName;
        
        // Special case: nepřechýlená příjmení (uninflected surnames)
        // If we have a feminine person with a masculine surname, the surname should remain unchanged
        // but we should try to restore proper diacritics
        if (isLastName && gender == DetectedGender.Feminine)
        {
            // Check if this surname has a masculine form in our data
            string masculineSurnameResult = TryPrebuiltLookup(token.Original, @case, DetectedGender.Masculine, entityType, isLastName);
            if (!string.IsNullOrEmpty(masculineSurnameResult))
            {
                // Check if there's also a specific feminine form
                string feminineSurnameResult = TryPrebuiltLookup(token.Original, @case, DetectedGender.Feminine, entityType, isLastName);
                
                // If a specific feminine form exists and differs from nominative, prefer it
                if (!string.IsNullOrEmpty(feminineSurnameResult) &&
                    !feminineSurnameResult.Equals(token.Original, StringComparison.OrdinalIgnoreCase))
                {
                    return MatchCasing(token.Original, feminineSurnameResult);
                }

                // Otherwise, for vocative borrow masculine vocative only when classifier approves
                if (@case == CzechCase.Vocative &&
                    Rules.BorrowedVocativeRules.TryGetBorrowedVocative(token.Original, out string borrowed))
                {
                    return MatchCasing(token.Original, borrowed);
                }

                // For other cases or non '-o' masculine vocatives, keep nepřechýlené příjmení but restore diacritics
                string restoredSurname = Rules.VokativRulesFromPython.TransformFeminineLastName(token.Original);
                return MatchCasing(token.Original, restoredSurname);
            }
        }
        
        // Try prebuilt lookup first with proper role
        string prebuiltResult = TryPrebuiltLookup(token.Original, @case, gender, entityType, isLastName);
        if (!string.IsNullOrEmpty(prebuiltResult))
        {
            return MatchCasing(token.Original, prebuiltResult);
        }

        // Fallback to rule-based declension
        // For surnames ending in -ova/-ová, treat them as feminine forms
        if (isLastName && token.Original.EndsWith("ová", StringComparison.OrdinalIgnoreCase))
        {
            // This is already a feminine surname form, just apply proper casing
            string result = token.Original;
            if (result.EndsWith("ova", StringComparison.OrdinalIgnoreCase))
            {
                // we might need to replace -ova with -ová
                result = Rules.VokativRulesFromPython.RestoreFeminineSurnameDiacritics(token.Original);
            }
            
            return MatchCasing(token.Original, result);
        }
        
        string ruleResult = @case switch
        {
            CzechCase.Genitive => Rules.GenitivRules.Transform(token.Original),
            CzechCase.Dative => Rules.DativRules.Transform(token.Original),
            CzechCase.Accusative => Rules.AkuzativRules.Transform(token.Original),
            CzechCase.Vocative => Rules.VokativRules.TransformWithContext(token.Original, gender, isLastName),
            CzechCase.Locative => Rules.LokativRules.Transform(token.Original),
            CzechCase.Instrumental => Rules.InstrumentalRules.Transform(token.Original),
            _ => token.Original
        };

        return MatchCasing(token.Original, ruleResult);
    }

    private static string ReconstructOutput(ParsedTitles parsedTitles, string declinedContent, CzechCase @case, DetectedGender gender, DeclensionOptions options, DetectedEntityType entityType)
    {
        List<string> parts = new List<string>();

        // Salutation overrides (companies and title-based), only in vocative and when not disabled
        if (@case == CzechCase.Vocative && !options.DisableTitleSalutation)
        {
            // Company salutation override
            if (entityType == DetectedEntityType.Company)
            {
                string company = options.CustomCompanySalutationPrefix ?? "vážení";
                return company;
            }

            // Title-based override: select highest-rank overriding title (consider both before/after)
            if (parsedTitles.BeforeTitles.Count > 0 || parsedTitles.AfterTitles.Count > 0)
            {
                TitleInfo? best = null;
                int bestLevelScore = -1; // Doctorate > Master > HistoricMaster > Bachelor > None

                static int LevelScore(TitleLevel? level) => level switch
                {
                    TitleLevel.Doctorate => 4,
                    TitleLevel.Master => 3,
                    TitleLevel.HistoricMaster => 2,
                    TitleLevel.Bachelor => 1,
                    _ => 0
                };

                IEnumerable<string> allTitles = parsedTitles.BeforeTitles.Concat(parsedTitles.AfterTitles);

                // Prefer any doctorate-level overriding titles if present
                List<TitleInfo> candidates = new List<TitleInfo>();
                foreach (string t in allTitles)
                {
                    if (KnownTitles.TryGetValue(t, out TitleInfo? info) && info.Rank != SalutationRank.None)
                        candidates.Add(info);
                }

                if (candidates.Count > 0)
                {
                    // Filter to doctorate first if any
                    var doctorate = candidates.Where(ci => ci.Level == TitleLevel.Doctorate).ToList();
                    var pool = doctorate.Count > 0 ? doctorate : candidates;

                    foreach (TitleInfo info in pool)
                    {
                        int levelScore = LevelScore(info.Level);
                        if (best == null || info.Rank > best.Rank || (info.Rank == best.Rank && levelScore > bestLevelScore))
                        {
                            best = info;
                            bestLevelScore = levelScore;
                        }
                    }

                    if (best != null)
                    {
                        string prefix = gender == DetectedGender.Feminine
                            ? (options.CustomFemaleSalutationPrefix ?? "paní ")
                            : (options.CustomMaleSalutationPrefix ?? "pane ");

                        string? root = gender == DetectedGender.Feminine ? best.FemaleVocRoot : best.MaleVocRoot;
                        if (!string.IsNullOrEmpty(root))
                        {
                            return prefix + root;
                        }
                    }
                }
            }
        }

        // Add "before" titles (salutations, academic titles, positions)
        if (parsedTitles.BeforeTitles.Count > 0 && !options.OmitTitles)
        {
            List<string> beforeTitles = new List<string>();
            foreach (string title in parsedTitles.BeforeTitles)
            {
                // Apply declension to salutations for all cases
                if (KnownTitles.TryGetValue(title, out TitleInfo? titleInfo) && titleInfo.Type == TitleType.Salutation)
                {
                    string declinedTitle = DeclineSalutation(title, @case, gender);
                    beforeTitles.Add(declinedTitle);
                }
                else
                {
                    // Normalize non-salutation titles to their canonical form
                    string normalizedTitle = NameCasing.NormalizeForRole(title, TokenRole.Title, title);
                    beforeTitles.Add(normalizedTitle);
                }
            }
            parts.Add(string.Join(" ", beforeTitles));
        }

        // Add the declined name content
        if (!string.IsNullOrWhiteSpace(declinedContent))
        {
            parts.Add(declinedContent);
        }

        // Add "after" titles (doctoral degrees, professional titles, suffixes)
        if (parsedTitles.AfterTitles.Count > 0 && !options.OmitTitles)
        {
            List<string> normalizedAfterTitles = parsedTitles.AfterTitles
                .Select(title => NameCasing.NormalizeForRole(title, TokenRole.Title, title))
                .ToList();
            parts.Add(string.Join(" ", normalizedAfterTitles));
        }

        return string.Join(" ", parts);
    }

    private static string DeclineSalutation(string salutation, CzechCase @case, DetectedGender gender)
    {
        string lower = salutation.ToLowerInvariant();

        return lower switch
        {
            "pan" when gender == DetectedGender.Masculine => @case switch
            {
                CzechCase.Nominative => "Pan",
                CzechCase.Genitive => "Pana",
                CzechCase.Dative => "Panu",
                CzechCase.Accusative => "Pana",
                CzechCase.Vocative => "pane",
                CzechCase.Locative => "Panu",
                CzechCase.Instrumental => "Panem",
                _ => salutation
            },
            "paní" when gender == DetectedGender.Feminine => @case switch
            {
                CzechCase.Nominative => "Paní",
                CzechCase.Genitive => "Paní",
                CzechCase.Dative => "Paní",
                CzechCase.Accusative => "Paní",
                CzechCase.Vocative => "paní",
                CzechCase.Locative => "Paní",
                CzechCase.Instrumental => "Paní",
                _ => salutation
            },
            _ => salutation
        };
    }

    private static string TryPrebuiltLookup(string original, CzechCase @case, DetectedGender gender, DetectedEntityType entityType, bool isLastWord)
    {
        string normalizedName = original.ToLowerInvariant().Trim();
        int caseKey = (int)@case; // Direct cast from CzechCase enum to int
        
        // Determine type: 0 = křestní jméno (first/middle), 1 = příjmení (surname)
        int typeInt = isLastWord ? 1 : 0;
        
        // Try exact match with inferred gender
        string result = TryLookupWithGenderAndType(normalizedName, (int)gender, typeInt, caseKey);
        if (!string.IsNullOrEmpty(result)) return result;
        
        return string.Empty; // Not found in prebuilt data
    }

    private static string TryLookupWithGenderAndType(string normalizedName, int genderInt, int typeInt, int caseKey)
    {
        if (ScrapedDeclensionData.Names.TryGetValue(normalizedName, out ScrapedDeclensionData.NameDeclensionData? nameData))
        {
            // Choose forms based on type: 0 = FirstName, 1 = LastName
            List<ScrapedDeclensionData.DeclensionForm>? forms = typeInt == 0 ? nameData.FirstNameForms : nameData.LastNameForms;
            
            foreach (ScrapedDeclensionData.DeclensionForm form in forms)
            {
                if (form.Gender == genderInt && caseKey <= form.Cases.Count && !string.IsNullOrEmpty(form.Cases[caseKey - 1])) // subtract 1 as we start from case = 1, but the list is indexed from 0
                {
                    return form.Cases[caseKey - 1];
                }
            }
        }
        return string.Empty;
    }
    
    private static string MatchCasing(string pattern, string value)
    {
        // Defer casing normalization to NameCasing.NormalizeForRole.
        // Here we only return the raw declined value.
        return value;
    }
}


