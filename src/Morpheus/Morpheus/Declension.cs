using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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

public sealed class DeclensionOptions
{
    public bool OmitFirstName { get; init; }
    public bool OmitLastName { get; init; }
    public bool OmitTitles { get; init; }
    public bool Explain { get; init; }
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
    // Comprehensive Czech titles and their properties
    private static readonly Dictionary<string, TitleInfo> KnownTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Salutations
        ["Pan"] = new TitleInfo { Type = TitleType.Salutation, Gender = TitleGender.Masculine, PlacesBefore = true },
        ["Paní"] = new TitleInfo { Type = TitleType.Salutation, Gender = TitleGender.Feminine, PlacesBefore = true },
        
        // Bachelor degrees
        ["Bc."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Bachelor, PlacesBefore = true },
        ["BcA."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Bachelor, PlacesBefore = true },
        
        // Master/Engineer degrees
        ["Ing."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["Ing. arch."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["MUDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["MDDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["MVDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["MgA."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["Mgr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["JUDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["PhDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["RNDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["PharmDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["ThLic."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["ThDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        
        // Historical master degrees
        ["akad. arch."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["ak. mal."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["ak. soch."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["MSDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["PaedDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["PhMr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["RCDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["RSDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["RTDr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        ["ThMgr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = true },
        
        // Doctoral degrees (after name)
        ["Ph.D."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Doctorate, PlacesBefore = false },
        ["DSc."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Doctorate, PlacesBefore = false },
        ["CSc."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Doctorate, PlacesBefore = false },
        ["Dr."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Doctorate, PlacesBefore = false },
        ["DrSc."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Doctorate, PlacesBefore = false },
        ["Th.D."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Doctorate, PlacesBefore = false },
        
        // Academic positions (before name, lowercase)
        ["as."] = new TitleInfo { Type = TitleType.Position, PlacesBefore = true },
        ["odb. as."] = new TitleInfo { Type = TitleType.Position, PlacesBefore = true },
        ["doc."] = new TitleInfo { Type = TitleType.Position, PlacesBefore = true },
        ["prof."] = new TitleInfo { Type = TitleType.Position, PlacesBefore = true },
        
        // Non-academic titles
        ["DiS."] = new TitleInfo { Type = TitleType.Professional, PlacesBefore = false },
        
        // Honorary and ceremonial titles
        ["dr. h. c."] = new TitleInfo { Type = TitleType.Honorary, PlacesBefore = false },
        ["prof. h. c."] = new TitleInfo { Type = TitleType.Honorary, PlacesBefore = false },
        
        // International titles (common in Czech context)
        ["MBA"] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = false },
        ["LL.M."] = new TitleInfo { Type = TitleType.Academic, Level = TitleLevel.Master, PlacesBefore = false },
        ["Jr."] = new TitleInfo { Type = TitleType.Suffix, PlacesBefore = false },
        ["Sr."] = new TitleInfo { Type = TitleType.Suffix, PlacesBefore = false },
        
        // Military ranks - Mužstvo (Enlisted)
        ["voj."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        ["svob."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        ["sv."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true }, // unofficial but common abbreviation
        
        // Military ranks - Poddůstojníci (Non-commissioned officers)
        ["des."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        ["čet."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        ["rtn."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        
        // Military ranks - Sbor praporčíků (Warrant officers)
        ["rtm."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        ["nrtm."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        ["prap."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        ["nprap."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        ["št. prap."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        ["šprap."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        
        // Military ranks - Sbor nižších důstojníků (Junior officers)
        ["por."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        ["npor."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        ["kpt."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        
        // Military ranks - Sbor vyšších důstojníků (Senior officers)
        ["mjr."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        ["pplk."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        ["plk."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        
        // Military ranks - Sbor generálů (Generals)
        ["brig.gen."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        ["genmjr."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        ["genpor."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        ["arm.gen."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true },
        
        // Historical military ranks (still may appear in documents)
        ["ppor."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true }, // podporučík (abolished 2011)
        ["škpt."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true }, // štábní kapitán
        ["šrtm."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true }, // štábní rotmistr (abolished 2011)
        ["gen."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true }, // general (historical)
        ["genplk."] = new TitleInfo { Type = TitleType.Military, PlacesBefore = true }, // generálplukovník (historical)
        
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
        ["Rev."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true }, // reverend
        ["Very Rev."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true }, // very reverend
        ["Most Rev."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true }, // most reverend
        ["Rt. Rev."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true }, // right reverend
        ["Right Rev."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true }, // right reverend
        ["Fr."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true }, // father
        ["Father"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true },
        ["Sister"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true }, // when clearly religious context
        ["Br."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true }, // brother
        ["Brother"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true },
        ["Dcn."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true }, // deacon
        ["Deacon"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true },
        ["Bp."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true }, // bishop
        ["Bishop"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true },
        ["Abp."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true }, // archbishop
        ["Archbishop"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true },
        ["Msgr."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true }, // monsignor
        ["Monsignor"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true },
        ["Card."] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true }, // cardinal
        ["Cardinal"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true },
        ["Dom"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true }, // dom (monastic)
        ["Abbot"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true },
        ["Mother"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true }, // mother superior
        ["Pastor"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true },
        ["Padre"] = new TitleInfo { Type = TitleType.Ecclesiastical, PlacesBefore = true },
        
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
        Master,
        Doctorate
    }

    private enum TitleGender
    {
        Neutral,
        Masculine,
        Feminine
    }

    private class TitleInfo
    {
        public TitleType Type { get; init; }
        public TitleLevel? Level { get; init; }
        public TitleGender Gender { get; init; } = TitleGender.Neutral;
        public bool PlacesBefore { get; init; }
        public string? VocativeMale { get; init; }
        public string? VocativeFemale { get; init; }
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
        var normalized = input.Trim();
        normalized = Regex.Replace(normalized, @"\s{2,}", " "); // Multiple spaces to single space
        
        // Normalize various dash types to standard dash
        normalized = normalized.Replace('–', '-').Replace('—', '-').Replace('−', '-');
        
        return normalized;
    }

    // Step 2: Handle titles (detect and temporarily remove)
    private static ParsedTitles ExtractTitles(string input)
    {
        var result = new ParsedTitles();
        if (string.IsNullOrWhiteSpace(input)) return result;

        var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nameTokens = new List<string>();
        
        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var foundTitle = FindTitle(token, i, tokens);
            
            if (foundTitle != null)
            {
                var titleInfo = KnownTitles[foundTitle];
                
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
                    result.BeforeTitles.Add(foundTitle);
                }
                else
                {
                    result.AfterTitles.Add(foundTitle);
                }
                
                // Skip additional tokens if this is a multi-word title
                if (foundTitle.Contains(' '))
                {
                    var titleWords = foundTitle.Split(' ').Length;
                    i += titleWords - 1; // Skip the additional words
                }
            }
            else
            {
                // This is a name token
                nameTokens.Add(token);
            }
        }
        
        result.NamePart = string.Join(" ", nameTokens);
        return result;
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
            var candidate = string.Join(" ", allTokens.Skip(position).Take(length));
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
            var withSpaces = AddSpacesAfterPeriods(token);
            if (KnownTitles.ContainsKey(withSpaces))
            {
                return withSpaces;
            }
            
            // Try common variations for compressed military titles
            var normalized = NormalizeMilitaryTitle(token);
            if (!string.IsNullOrEmpty(normalized) && KnownTitles.ContainsKey(normalized))
            {
                return normalized;
            }
        }
        
        // Special case for military titles that may have alternative abbreviations
        var militaryVariant = FindMilitaryTitleVariant(token);
        if (!string.IsNullOrEmpty(militaryVariant))
        {
            return militaryVariant;
        }
        
        return null;
    }

    private static string AddSpacesAfterPeriods(string input)
    {
        // Add space after each period except the last one
        var result = input;
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
        var patterns = new Dictionary<string, string>
        {
            ["št.prap."] = "št. prap.",
            ["brig.gen."] = "brig.gen.", // This one is already correctly spaced
            ["arm.gen."] = "arm.gen.",   // This one is already correctly spaced
            // Add more patterns as needed
        };

        return patterns.TryGetValue(input, out var normalized) ? normalized : string.Empty;
    }

    private static string? FindMilitaryTitleVariant(string token)
    {
        // Handle alternative military abbreviations
        var variants = new Dictionary<string, string>
        {
            ["sv."] = "svob.", // sv. is unofficial but common abbreviation for svobodník
        };

        if (variants.TryGetValue(token, out var canonical) && KnownTitles.ContainsKey(canonical))
        {
            return canonical;
        }

        return null;
    }

    // Step 3: Infer gender using prebuilt data + heuristics
    private static DetectedGender InferGender(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return DetectedGender.Ambiguous;

        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return DetectedGender.Ambiguous;

        var firstName = words[0];
        var lastName = words.Length > 1 ? words[1] : null;

        // Try scraped gender data first (most accurate)
        var normalizedFirstName = firstName.ToLowerInvariant().Trim();
        if (Data.ScrapedDeclensionData.Genders.TryGetValue(normalizedFirstName, out var scrapedGender))
        {
            return scrapedGender switch
            {
                0 => DetectedGender.Masculine,
                1 => DetectedGender.Feminine,
                _ => DetectedGender.Ambiguous
            };
        }

        // Fallback: use generated dataset for first names
        var firstCandidates = new List<string> { firstName };
        if (firstName.Contains('-')) 
            firstCandidates.AddRange(firstName.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (var cand in firstCandidates)
        {
            if (Morpheus.Data.NameGenderData.Names.TryGetValue(cand, out var g))
            {
                if (g == Morpheus.Data.NameGenderData.NameGender.Female) return DetectedGender.Feminine;
                if (g == Morpheus.Data.NameGenderData.NameGender.Male) return DetectedGender.Masculine;
            }
        }

        // Surname morphology heuristics
        if (!string.IsNullOrWhiteSpace(lastName))
        {
            if (lastName.EndsWith("ová", StringComparison.OrdinalIgnoreCase)) return DetectedGender.Feminine;
            if (lastName.EndsWith("á", StringComparison.OrdinalIgnoreCase)) return DetectedGender.Feminine;
        }

        return DetectedGender.Ambiguous;
    }

    // Step 4: Infer entity type (name, company, nickname, invalid)
    private static DetectedEntityType InferEntityType(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return DetectedEntityType.Invalid;

        // Company heuristics
        var companyPatterns = new[]
        {
            "s\\.\\s*r\\.\\s*o\\.", "a\\.s\\.", "s\\.p\\.", "spol\\.", "\\bSE\\b", "\\bHolding\\b", "&\\s*Co"
        };
        foreach (var pat in companyPatterns)
        {
            if (Regex.IsMatch(input, pat, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return DetectedEntityType.Company;
            }
        }

        // Check if it looks like a proper name
        if (Regex.IsMatch(input, @"^[A-ZÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ][a-záčďéěíňóřšťúůýž]+(\s+[A-ZÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ][a-záčďéěíňóřšťúůýž]+)*$"))
        {
            return DetectedEntityType.Name;
        }

        // Everything else could be nickname or invalid
        return DetectedEntityType.Nickname;
    }

    public static DeclensionResult Decline(string input, CzechCase @case, DeclensionOptions? options = null)
    {
        options ??= new DeclensionOptions();

        // Step 1: Normalize input
        var normalizedInput = NormalizeInput(input);

        // Step 2: Handle titles (detect and temporarily remove)
        var parsedTitles = ExtractTitles(normalizedInput);

        // Step 3: Infer gender (use title-implied gender if available)
        var detectedGender = parsedTitles.ImpliedGender ?? InferGender(parsedTitles.NamePart);

        // Step 4: Infer entity type
        var entityType = InferEntityType(parsedTitles.NamePart);

        // Step 5: Infer the declension result
        var declinedOutput = InferDeclensionResult(parsedTitles.NamePart, @case, detectedGender, entityType, options);

        // Reconstruct final output with titles if not omitted
        var finalOutput = ReconstructOutput(parsedTitles, declinedOutput, @case, detectedGender, options);

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

    // Step 5: Infer the declension result
    private static string InferDeclensionResult(string input, CzechCase @case, DetectedGender gender, DetectedEntityType entityType, DeclensionOptions options)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        if (entityType == DetectedEntityType.Company) return input; // Companies don't decline

        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var declinedWords = new List<string>();

        for (int i = 0; i < words.Length; i++)
        {
            var word = words[i];
            bool isFirst = i == 0;
            bool skip = (isFirst && options.OmitFirstName) || (!isFirst && options.OmitLastName);
            if (skip) continue;

            var declined = DeclineWord(word, @case, gender, entityType, isFirst);
            declinedWords.Add(declined);
        }

        return string.Join(" ", declinedWords);
    }

    private static string ReconstructOutput(ParsedTitles parsedTitles, string declinedContent, CzechCase @case, DetectedGender gender, DeclensionOptions options)
    {
        var parts = new List<string>();

        // Add "before" titles (salutations, academic titles, positions)
        if (parsedTitles.BeforeTitles.Count > 0 && !options.OmitTitles)
        {
            var beforeTitles = new List<string>();
            foreach (var title in parsedTitles.BeforeTitles)
            {
                // Apply declension to salutations for all cases
                if (KnownTitles.TryGetValue(title, out var titleInfo) && titleInfo.Type == TitleType.Salutation)
                {
                    var declinedTitle = DeclineSalutation(title, @case, gender);
                    beforeTitles.Add(declinedTitle);
                }
                else
                {
                    beforeTitles.Add(title);
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
            parts.Add(string.Join(" ", parsedTitles.AfterTitles));
        }

        return string.Join(" ", parts);
    }

    private static string DeclineSalutation(string salutation, CzechCase @case, DetectedGender gender)
    {
        var lower = salutation.ToLowerInvariant();
        
        if (lower == "pan" && gender == DetectedGender.Masculine)
        {
            return @case switch
            {
                CzechCase.Nominative => "Pan",
                CzechCase.Genitive => "Pana",
                CzechCase.Dative => "Panu",
                CzechCase.Accusative => "Pana", 
                CzechCase.Vocative => "pane",
                CzechCase.Locative => "Panu",
                CzechCase.Instrumental => "Panem",
                _ => salutation
            };
        }
        else if (lower == "paní" && gender == DetectedGender.Feminine)
        {
            return @case switch
            {
                CzechCase.Nominative => "Paní",
                CzechCase.Genitive => "Paní",
                CzechCase.Dative => "Paní",
                CzechCase.Accusative => "Paní",
                CzechCase.Vocative => "paní",
                CzechCase.Locative => "Paní",
                CzechCase.Instrumental => "Paní",
                _ => salutation
            };
        }
        
        return salutation; // fallback to original
    }

    private static string DeclineWord(string word, CzechCase @case, DetectedGender gender, DetectedEntityType entityType, bool isFirstWord)
    {
        // Try prebuilt lookup first
        var prebuiltResult = TryPrebuiltLookup(word, @case, gender, entityType, isFirstWord);
        if (!string.IsNullOrEmpty(prebuiltResult))
        {
            return MatchCasing(word, prebuiltResult);
        }

        // Fallback to rule-based declension
        var ruleResult = @case switch
        {
            CzechCase.Genitive => Rules.GenitivRules.Transform(word),
            CzechCase.Dative => Rules.DativRules.Transform(word),
            CzechCase.Accusative => Rules.AkuzativRules.Transform(word),
            CzechCase.Vocative => Rules.VokativRules.Transform(word),
            CzechCase.Locative => Rules.LokativRules.Transform(word),
            CzechCase.Instrumental => Rules.InstrumentalRules.Transform(word),
            _ => word
        };

        return MatchCasing(word, ruleResult);
    }
    
    private static string TryPrebuiltLookup(string original, CzechCase @case, DetectedGender gender, DetectedEntityType entityType, bool isFirstWord)
    {
        var normalizedName = original.ToLowerInvariant().Trim();
        
        // Map enums to integers for variant key construction
        var genderInt = (int)gender;
        var typeInt = isFirstWord ? 0 : 1; // 0 = FirstName, 1 = LastName
        
        // Construct variant key: "name_gender_type"
        var variantKey = $"{normalizedName}_{genderInt}_{typeInt}";
        
        // Try to find exact variant match
        if (Data.ScrapedDeclensionData.Declensions.TryGetValue(variantKey, out var declensions))
        {
            var caseKey = (int)@case; // Direct cast from CzechCase enum to int
            
            if (declensions.TryGetValue(caseKey, out var form))
            {
                return form;
            }
        }
        
        return string.Empty; // Not found in prebuilt data
    }
    
    private static string MatchCasing(string pattern, string value)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(value)) return value;

        // All uppercase
        if (pattern.ToUpperInvariant() == pattern)
        {
            return value.ToUpperInvariant();
        }

        // Title-case (naive): first letter uppercase, rest preserved as-is
        if (char.IsLetter(pattern[0]) && char.IsUpper(pattern[0]))
        {
            if (value.Length == 1) return value.ToUpperInvariant();
            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        // Default: lower-case
        return value.ToLowerInvariant();
    }

    private sealed class Token
    {
        public string Original { get; set; }
        public bool IsWord { get; set; }
        public bool IsTitle { get; set; }

        public Token(string original, bool isWord, bool isTitle)
        {
            Original = original;
            IsWord = isWord;
            IsTitle = isTitle;
        }
    }

    private static IReadOnlyList<Token> Tokenize(string input)
    {
        var parts = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var tokens = new List<Token>(parts.Length);
        foreach (var p in parts)
        {
            var isTitle = KnownTitles.ContainsKey(p) || Regex.IsMatch(p, @"^[A-ZÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ]\.$");
            var isWord = !isTitle && Regex.IsMatch(p, @"\p{L}+");
            tokens.Add(new Token(p, isWord, isTitle));
        }
        return tokens;
    }
}


