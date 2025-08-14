using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using System.Reflection;

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
            var assembly = Assembly.GetExecutingAssembly();
            var assemblyDir = Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;
            var indexPath = Path.Combine(assemblyDir, "Data", "names_index.bk");
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
        var searcher = NameSearcherLazy.Value;
        if (searcher == null) return TokenRole.Unknown;

        // Exact match only to avoid false positives
        var results = searcher.Search(token, 0);
        if (results == null || results.Count == 0) return TokenRole.Unknown;

        // Take the first result (index ensures unique per key) and map its role
        var role = results[0].Role;
        bool isFirst = role.HasFlag(NameRole.First);
        bool isSurname = role.HasFlag(NameRole.Surname);
        if (isFirst && !isSurname) return TokenRole.FirstName;
        if (isSurname && !isFirst) return TokenRole.LastName;
        // If both or none, keep unknown to be refined later
        return TokenRole.Unknown;
    }
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

    // Step 3: Handle titles (detect and temporarily remove) using pre-assigned token roles
    private static ParsedTitles ExtractTitles(List<NameToken> tokens)
    {
        var result = new ParsedTitles();
        var namePartTokens = new List<string>();
        
        foreach (var token in tokens)
        {
            switch (token.Role)
            {
                case TokenRole.Title:
                    var titleInfo = KnownTitles.GetValueOrDefault(token.Original);
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
        var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nameTokens = new List<NameToken>();
        
        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var nameToken = new NameToken
            {
                Original = token,
                Normalized = token.ToLowerInvariant().Trim(),
                Position = i
            };
            
            // Assign role based on various criteria
            nameToken.Role = DetermineTokenRole(nameToken, i, tokens);
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
        var companyBasePatterns = new Dictionary<string, int>
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
            foreach (var pattern in companyBasePatterns)
            {
                var basePattern = pattern.Key;
                var expectedTokens = pattern.Value;
                
                if (i + expectedTokens <= tokens.Count)
                {
                    // Extract normalized tokens and remove dots/spaces
                    var ngram = string.Join("", tokens.Skip(i).Take(expectedTokens)
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
            var token = tokens[i];
            if (token.Role == TokenRole.Unknown)
            {
                var clean = token.Normalized.Replace(".", "").Replace(" ", "");
                if (clean == "&" || clean == "and" || clean == "co" || clean == "holding" || clean == "se")
                {
                    token.Role = TokenRole.CompanySpecifier;
                }
            }
        }
    }

    private static TokenRole DetermineTokenRole(NameToken token, int position, string[] allTokens)
    {
        var normalized = token.Normalized;
        
        // 1. Check for titles
        var foundTitle = FindTitle(token.Original, position, allTokens);
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
        var indexRole = ResolveRoleFromIndex(token.Original);
        if (indexRole != TokenRole.Unknown)
        {
            return indexRole;
        }

        bool couldBeFirstName = Data.ScrapedDeclensionData.FirstNames.Contains(normalized) ||
                                Data.ScrapedDeclensionData.FirstNames.Contains(Normalizer.RemoveDiacritics(normalized));

        bool couldBeLastName = Data.ScrapedDeclensionData.LastNames.Contains(normalized) ||
                                Data.ScrapedDeclensionData.LastNames.Contains(Normalizer.RemoveDiacritics(normalized));

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
        var nameTokens = tokens.Where(t => t.Role == TokenRole.FirstName || 
                                          t.Role == TokenRole.LastName || 
                                          t.Role == TokenRole.Unknown).ToList();
        
        if (nameTokens.Count == 0) return;
		
		// If at least one first name is already detected, treat all unknown tokens as last names
		if (nameTokens.Any(t => t.Role == TokenRole.FirstName))
		{
			foreach (var token in nameTokens)
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
		var lastNameToken = nameTokens.LastOrDefault(t => t.Role == TokenRole.Unknown || 
														 t.Role == TokenRole.FirstName || 
														 t.Role == TokenRole.LastName);
		
		foreach (var token in nameTokens)
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
        var companyPatterns = new[]
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
        var surnameEndings = new[]
        {
            "ová", "ský", "ská", "ní", "ec", "ák", "ek", "ík", "an", "el", "ka", "ny"
        };
        
        foreach (var ending in surnameEndings)
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
    // Step 4: Infer gender from tokens (enhanced with scraped data and token roles)
    private static DetectedGender InferGender(List<NameToken> tokens)
    {
        var nameTokens = tokens.Where(t => t.Role == TokenRole.FirstName || t.Role == TokenRole.LastName).ToList();
        if (nameTokens.Count == 0) return DetectedGender.Ambiguous;

        var firstNameTokens = nameTokens.Where(t => t.Role == TokenRole.FirstName).ToList();
        var lastNameTokens = nameTokens.Where(t => t.Role == TokenRole.LastName).ToList();

        // Collect gender evidence from all sources with confidence weights
        var genderEvidence = new List<GenderEvidence>();

        // 1. Scraped data evidence (highest confidence) - first names only
        foreach (var token in firstNameTokens)
        {
            var normalizedWithoutDiacritics = Normalizer.RemoveDiacritics(token.Normalized);
            
            if (Data.ScrapedDeclensionData.Names.TryGetValue(normalizedWithoutDiacritics, out var nameData))
            {
                var gender = nameData.Gender switch
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
        foreach (var token in firstNameTokens)
        {
            var candidates = new List<string> { token.Original };
            if (token.Original.Contains('-'))
                candidates.AddRange(token.Original.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            foreach (var cand in candidates)
            {
                if (Morpheus.Data.NameGenderData.Names.TryGetValue(cand, out var g))
                {
                    var gender = g switch
                    {
                        Morpheus.Data.NameGenderData.NameGender.Female => DetectedGender.Feminine,
                        Morpheus.Data.NameGenderData.NameGender.Male => DetectedGender.Masculine,
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
        foreach (var token in lastNameTokens)
        {
            if (token.Original.EndsWith("ová", StringComparison.OrdinalIgnoreCase))
            {
                genderEvidence.Add(new GenderEvidence(DetectedGender.Feminine, 5, $"Surname ending: {token.Original} (-ová)"));
            }
            else if (token.Original.EndsWith("á", StringComparison.OrdinalIgnoreCase))
            {
                genderEvidence.Add(new GenderEvidence(DetectedGender.Feminine, 3, $"Surname ending: {token.Original} (-á)"));
            }
        }

        // 4. General morphological evidence (all name tokens) - covers adjectives, nouns, titles
        foreach (var token in nameTokens)
        {
            var word = token.Original;
            
            // Feminine endings
            if (word.EndsWith("ová", StringComparison.OrdinalIgnoreCase))
            {
                genderEvidence.Add(new GenderEvidence(DetectedGender.Feminine, 6, $"Feminine ending: {word} (-ová)"));
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
        var masculineScore = genderEvidence.Where(e => e.Gender == DetectedGender.Masculine).Sum(e => e.Confidence);
        var feminineScore = genderEvidence.Where(e => e.Gender == DetectedGender.Feminine).Sum(e => e.Confidence);

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
        var nameTokens = tokens.Where(t => t.Role == TokenRole.FirstName || t.Role == TokenRole.LastName).ToList();
        if (nameTokens.Count > 0)
        {
            return DetectedEntityType.Name;
        }

        // If we only have unknown tokens, try some basic patterns
        var unknownTokens = tokens.Where(t => t.Role == TokenRole.Unknown).ToList();
        if (unknownTokens.Count > 0)
        {
            var combinedText = string.Join(" ", unknownTokens.Select(t => t.Original));
            
            // Company patterns
            if (Regex.IsMatch(combinedText, @"\b(bank|banka|úvěr|pojišť|holding|group|ltd\.|inc\.|corp\.)", RegexOptions.IgnoreCase))
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
        var normalizedInput = NormalizeInput(input);

        // Step 2: Assign token roles
        var tokens = AssignTokenRoles(normalizedInput);

        // Step 3: Handle titles (detect and temporarily remove)
        var parsedTitles = ExtractTitles(tokens);

        // Step 4: Infer gender (use title-implied gender if available)
        var detectedGender = parsedTitles.ImpliedGender ?? InferGender(tokens);

        // Step 5: Infer entity type
        var entityType = InferEntityType(tokens);

        // Step 6: Infer the declension result
        var declinedOutput = InferDeclensionResult(tokens, @case, detectedGender, entityType, options);

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

    // Step 6: Infer the declension result using pre-assigned token roles
    private static string InferDeclensionResult(List<NameToken> tokens, CzechCase @case, DetectedGender gender, DetectedEntityType entityType, DeclensionOptions options)
    {
        if (tokens.Count == 0) return string.Empty;
        if (entityType == DetectedEntityType.Company) 
        {
            // Return all company-related tokens (but not titles)
            return string.Join(" ", tokens.Where(t => t.Role != TokenRole.Title && 
                                                     t.Role != TokenRole.Bracket && 
                                                     t.Role != TokenRole.Nickname).Select(t => t.Original));
        }

        var declinedWords = new List<string>();

        foreach (var token in tokens)
        {
            // Only decline firstname and lastname tokens
            if (token.Role != TokenRole.FirstName && token.Role != TokenRole.LastName)
                continue;

            // Apply omit options
            bool skip = (token.Role == TokenRole.FirstName && options.OmitFirstName) || 
                       (token.Role == TokenRole.LastName && options.OmitLastName);
            if (skip) continue;

            var declined = DeclineWordWithRole(token, @case, gender, entityType);
            declinedWords.Add(declined);
        }

        return string.Join(" ", declinedWords);
    }

    private static string DeclineWordWithRole(NameToken token, CzechCase @case, DetectedGender gender, DetectedEntityType entityType)
    {
        bool isLastName = token.Role == TokenRole.LastName;
        
        // Special case: nepřechýlená příjmení (uninflected surnames)
        // If we have a feminine person with a masculine surname, the surname should remain unchanged
        // but we should try to restore proper diacritics
        if (isLastName && gender == DetectedGender.Feminine)
        {
            // Check if this surname has a masculine form in our data
            var masculineSurnameResult = TryPrebuiltLookup(token.Original, @case, DetectedGender.Masculine, entityType, isLastName);
            if (!string.IsNullOrEmpty(masculineSurnameResult))
            {
                // Check if there's also a specific feminine form
                var feminineSurnameResult = TryPrebuiltLookup(token.Original, @case, DetectedGender.Feminine, entityType, isLastName);
                
                // If there's no specific feminine form or the feminine form is the same as nominative,
                // use the original surname (nepřechýlené příjmení) but with diacritic restoration
                if (string.IsNullOrEmpty(feminineSurnameResult) || 
                    feminineSurnameResult == token.Original)
                {
                    // Apply diacritic restoration for nepřechýlená příjmení
                    var restoredSurname = Rules.VokativRulesFromPython.TransformFeminineLastName(token.Original);
                    return MatchCasing(token.Original, restoredSurname);
                }
                
                // Otherwise use the specific feminine form
                return MatchCasing(token.Original, feminineSurnameResult);
            }
        }
        
        // Try prebuilt lookup first with proper role
        var prebuiltResult = TryPrebuiltLookup(token.Original, @case, gender, entityType, isLastName);
        if (!string.IsNullOrEmpty(prebuiltResult))
        {
            return MatchCasing(token.Original, prebuiltResult);
        }

        // Fallback to rule-based declension
        // For surnames ending in -ova/-ová, treat them as feminine forms
        if (isLastName && (token.Original.EndsWith("ova", StringComparison.OrdinalIgnoreCase) || 
                          token.Original.EndsWith("ová", StringComparison.OrdinalIgnoreCase)))
        {
            // This is already a feminine surname form, just apply proper casing
            var result = token.Original;
            if (result.EndsWith("ova", StringComparison.OrdinalIgnoreCase))
            {
                // Convert "ova" to "ová" with proper casing
                result = result.Substring(0, result.Length - 3) + "ová";
            }
            return MatchCasing(token.Original, result);
        }
        
        var ruleResult = @case switch
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

    private static string TryPrebuiltLookup(string original, CzechCase @case, DetectedGender gender, DetectedEntityType entityType, bool isLastWord)
    {
        var normalizedName = original.ToLowerInvariant().Trim();
        var caseKey = (int)@case; // Direct cast from CzechCase enum to int
        
        // Determine type: 0 = křestní jméno (first/middle), 1 = příjmení (surname)
        var typeInt = isLastWord ? 1 : 0;
        
        // Try exact match with inferred gender
        var result = TryLookupWithGenderAndType(normalizedName, (int)gender, typeInt, caseKey);
        if (!string.IsNullOrEmpty(result)) return result;
        
        return string.Empty; // Not found in prebuilt data
    }

    private static string TryLookupWithGenderAndType(string normalizedName, int genderInt, int typeInt, int caseKey)
    {
        if (Data.ScrapedDeclensionData.Names.TryGetValue(normalizedName, out var nameData))
        {
            // Choose forms based on type: 0 = FirstName, 1 = LastName
            var forms = typeInt == 0 ? nameData.FirstNameForms : nameData.LastNameForms;
            
            foreach (var form in forms)
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
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(value)) return value;

        // All uppercase pattern
        if (pattern.ToUpperInvariant() == pattern)
        {
            return value.ToUpperInvariant();
        }

        // Smart capitalization: if pattern is all lowercase (like user typed names),
        // still capitalize the first letter for proper names
        if (pattern.ToLowerInvariant() == pattern && char.IsLetter(pattern[0]))
        {
            if (value.Length == 1) return value.ToUpperInvariant();
            return char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
        }

        // Title-case (original logic): first letter uppercase, rest preserved as-is
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
}


