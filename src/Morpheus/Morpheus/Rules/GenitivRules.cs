using System;
using System.Collections.Generic;

namespace Morpheus.Rules;

public static class GenitivRules
{
    // # Koho, čeho?
    // Faithful port of (2) genitiv/python/genitiv.py
    public static string Transform(string input)
    {
        var output = new List<string>();
        var names = input.Contains(" ") ? input.Split(' ', StringSplitOptions.RemoveEmptyEntries) : new[] { input.ToLowerInvariant() };

        foreach (var raw in names)
        {
            var jmeno = raw.ToLowerInvariant();
            if (jmeno.Length == 0) { output.Add(raw); continue; }

            string result;
            char c0 = jmeno[^1];
            char c1 = jmeno.Length > 1 ? jmeno[^2] : '\0';
            char c2 = jmeno.Length > 2 ? jmeno[^3] : '\0';
            char c3 = jmeno.Length > 3 ? jmeno[^4] : '\0';
            char c4 = jmeno.Length > 4 ? jmeno[^5] : '\0';

            if (c0 == 'a')
            {
                if (c1 == 'd' || c1 == 'n') // Anna, Linda
                    result = jmeno[..^1] + "y";
                else if (c1 == 'č' || c1 == 'j') // Ivča, Kája
                    result = jmeno[..^1] + "i";
                else if (c1 == 'ď' || c1 == 'c') // Láďa, Danica
                    result = jmeno[..^1] + "i";
                else if (c1 == 'g') // Olga
                    result = jmeno[..^1] + "y";
                else if (c1 == 'i') // Olivia
                    result = jmeno[..^1] + "e";
                else if (c1 == 'k') // Eliška
                    result = jmeno[..^1] + "y";
                else if (c1 == 'l' && c2 == 'v') // Pavla
                    result = jmeno[..^1] + "y";
                else if (c1 == 'l') // Fiala, Nikola
                    result = jmeno[..^1] + "y";
                else if (c1 == 'o') // Figueroa
                    result = jmeno;
                else if (c1 == 'r') // Klára, Svoboda, Kučera
                    result = jmeno[..^1] + "y";
                else if (c1 == 't') // Alžběta
                    result = jmeno[..^1] + "y";
                else if (c1 == 'v') // Eva
                    result = jmeno[..^1] + "y";
                else if (c1 == 'z') // Honza, Tereza
                    result = jmeno[..^1] + "y";
                else if (c1 == 'ň') // Soňa
                    result = jmeno[..^2] + "ni";
                else if (c2 == 'c' || c1 == 'h') // Průcha
                    result = jmeno[..^1] + "y";
                else if (c1 == 'e' || c1 == 'š') // Nataša, Andrea, Lea
                {
                    if (c2 == 'r') result = jmeno[..^1] + "ji"; else result = jmeno[..^1] + "i";
                }
                else
                    result = jmeno[..^1] + "y";
            }
            else if (c0 == 'á')
            {
                result = jmeno[..^1] + "é";
            }
            else if (c0 == 'e')
            {
                if (c1 == 'g') // George
                    result = jmeno;
                else if (c1 == 'e' || c1 == 'o') // Lee, Zoe
                    result = jmeno;
                else if (c1 == 'i' || c1 == 'c' || c1 == 'š') // Lucie, Alice, Danuše
                    result = jmeno;
                else
                    result = jmeno[..^1] + "i";
            }
            else if (c0 == 'h' || c0 == 'i')
            {
                if (c1 == 'c') // Bedřich, Vojtěch
                    result = jmeno + "a";
                else // Sarah, Niki
                    result = jmeno;
            }
            else if (c0 == 'k')
            {
                if (c1 == 'e') // Malášek
                    result = jmeno[..^2] + "ka";
                else if (c1 == 'ě')
                {
                    if (c2 == 'n') // Zbyněk, Vaněk
                        result = jmeno[..^3] + "ňka";
                    else if (c2 == 'd') // Luděk
                        result = jmeno[..^3] + "ďka";
                    else
                        result = jmeno; // fallback
                }
                else // Novák
                    result = jmeno + "a";
            }
            else if (c0 == 'l')
            {
                if (c1 == 'e')
                {
                    if (c2 == 'c' || c2 == 'i' || c2 == 'u') // Marcel, Samuel, Gabriel
                        result = jmeno + "a";
                    else // Karel
                        result = jmeno[..^2] + "la";
                }
                else if (c1 == 'o' && c2 == 'k') // Nikol
                    result = jmeno;
                else if (c1 == 'a' || c1 == 'i' || c1 == 'o' || c1 == 's') // Michal, Bohumil, Anatol, Přemysl
                    result = jmeno + "a";
                else // Král
                    result = jmeno + "e";
            }
            else if (c0 == 'm')
            {
                if (c1 == 'a') // Miriam
                    result = jmeno;
                else // Maxim
                    result = jmeno + "a";
            }
            else if (c0 == 'o')
            {
                if (c1 == 't') // Oto
                    result = jmeno[..^1] + "y";
                else // Ronaldo, Santiago
                    result = jmeno[..^1] + "a";
            }
            else if (c0 == 'r')
            {
                if (c1 == 'a' || c1 == 'e') // Dagmar, Ester
                {
                    if (c2 == 'k' || c2 == 'm' || c2 == 'p' || c2 == 'l') // Otakar, Otmar, Kašpar
                        result = jmeno + "a";
                    else
                        result = jmeno;
                }
                else
                    result = jmeno + "a";
            }
            else if (c0 == 'y' || c0 == 'í' || c0 == 'é')
            {
                if (c1 == 'l') // Emily
                    result = jmeno;
                else // Harry, Jiří, René
                    result = jmeno + "ho";
            }
            else if (c0 == 'ý')
            {
                result = jmeno[..^1] + "ého";
            }
            else if (c0 == 'd')
            {
                if (c1 == 'n') // Zikmund
                    result = jmeno + "a";
                else // Ingrid
                    result = jmeno;
            }
            else if (c0 == 'c')
            {
                if (c1 == 'n') // Vincenc
                    result = jmeno[..^1] + "e";
                else
                {
                    if (c1 == 'l') // Šolc
                        result = jmeno + "e";
                    else // Vavřinec
                        result = jmeno[..^2] + "ce";
                }
            }
            else if (c0 == 'ž')
            {
                result = jmeno + "e";
            }
            else if (c0 == 't')
            {
                if (c1 == 'í') // Vít
                    result = jmeno + "a";
                else // Růt
                    result = jmeno;
            }
            else if (c0 == 'ů') // Petrů
            {
                result = jmeno;
            }
            else if (c0 == 'c' || c0 == 'j' || c0 == 'ř' || c0 == 'š') // Tomáš, Ondřej, Kadlec
            {
                result = jmeno + "e";
            }
            else if (c0 == 'x' || c0 == 's') // Max, Nikolas
            {
                result = jmeno + "e";
            }
            else
            {
                result = jmeno + "a";
            }

            // Capitalize per reference implementation
            output.Add(Capitalize(result));
        }

        return string.Join(" ", output);
    }

    private static string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length == 1) return s.ToUpperInvariant();
        return char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
    }
}


