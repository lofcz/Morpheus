using System;
using System.Collections.Generic;

namespace Morpheus.Rules;

public static class DativRules
{
    // # Komu, čemu?
    // Faithful port of (3) dativ/python/dativ.py
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
                if (c1 == 'd' || c1 == 'n' || c1 == 'b') // Anna, Linda, Ljuba
                {
                    if ((c2 == 'o' && (c3 == 'b' || c3 == 'd')) || (c2 == 'a' && c3 == 't')) // Svoboda, Smetana, Škoda
                        result = jmeno[..^1] + "ovi";
                    else
                        result = jmeno[..^1] + "ě";
                }
                else if (c1 == 'ď' || (c1 == 'l' && c2 == 'a')) // Láďa, Fiala
                {
                    if (c3 == 'n') // Naďa
                        result = jmeno[..^2] + "dě";
                    else
                        result = jmeno[..^1] + "ovi";
                }
                else if (c1 == 'm') // Ema
                    result = jmeno[..^1] + "ě";
                else if (c1 == 's') // Štursa
                    result = jmeno[..^1] + "ovi";
                else if (c1 == 'g') // Olga
                    result = jmeno[..^2] + "ze";
                else if (c1 == 'i') // Olivia
                    result = jmeno[..^1] + "i";
                else if (c1 == 'k') // Eliška
                {
                    if (c2 == 'z' || (c2 == 'r' && c3 == 'k') || c2 == 'č' || c2 == 'j' || c2 == 'b' || c2 == 'p' || c2 == 'n' || c2 == 'l') // Procházka, Jirka, Růžička, Matějka, Rybka, Červenka, Havelka
                        result = jmeno[..^1] + "ovi";
                    else
                        result = jmeno[..^2] + "ce";
                }
                else if (c1 == 'l' && c2 == 'v') // Pavla
                    result = jmeno[..^1] + "e";
                else if (c1 == 'ň') // Soňa
                    result = jmeno[..^2] + "ně";
                else if (c1 == 'o') // Figueroa
                    result = jmeno;
                else if (c1 == 'r') // Klára
                {
                    if (c2 == 'í')
                    {
                        if (c4 == 'o') // Drahomíra
                            result = jmeno[..^2] + "ře";
                        else // Míra
                            result = jmeno[..^1] + "ovi";
                    }
                    else if (c2 == 'd' || c2 == 'e' || c2 == 'v' || c2 == 'o') // Jindra, Kučera, Vávra, Sýkora
                        result = jmeno[..^1] + "ovi";
                    else
                        result = jmeno[..^2] + "ře";
                }
                else if (c1 == 't')
                {
                    if (c2 == 'n' || c2 == 'j' || c2 == 'r' || c2 == 'á') // Franta, Vojta, Bárta
                        result = jmeno[..^1] + "ovi";
                    else // Agáta
                        result = jmeno[..^1] + "ě";
                }
                else if (c1 == 'v') // Eva
                    result = jmeno[..^1] + "ě";
                else if (c1 == 'z') // Honza
                {
                    if (c2 == 'e') // Tereza
                        result = jmeno[..^1] + "e";
                    else
                        result = jmeno[..^1] + "ovi";
                }
                else if (c2 == 'c' || c1 == 'h') // Průcha
                    result = jmeno[..^1] + "ovi";
                else if (c1 == 'e' || c1 == 'š' || c1 == 'č' || c1 == 'j' || c1 == 'l') // Nataša, Andrea, Lea / Ivča, Kája, Nikola / ...
                    result = jmeno[..^1] + "e";
                else if (c2 == 'o') //  Kučera
                    result = jmeno[..^1] + "ovi";
                else
                    result = jmeno;
            }
            else if (c0 == 'á')
            {
                result = jmeno[..^1] + "é";
            }
            else if (c0 == 'e')
            {
                if (c1 == 'g') // George
                    result = jmeno[..^1] + "ovi";
                else if (c1 == 'e' || c1 == 'o') // Lee, Zoe
                    result = jmeno;
                else
                    result = jmeno[..^1] + "i";
            }
            else if (c0 == 'h' || c0 == 'i')
            {
                if (c1 == 'c') // Bedřich, Vojtěch
                    result = jmeno + "ovi";
                else // Sarah, Niki
                    result = jmeno;
            }
            else if (c0 == 'k')
            {
                if (c1 == 'e') // Malášek
                    result = jmeno[..^2] + "kovi";
                else if (c1 == 'ě')
                {
                    if (c2 == 'n') // Zbyněk
                        result = jmeno[..^3] + "ňkovi";
                    else if (c2 == 'd') // Luděk
                        result = jmeno[..^3] + "ďkovi";
                    else if (c2 == 'n') // Vaněk
                        result = jmeno[..^3] + "ňkovi";
                    else
                        result = jmeno;
                }
                else // Novák
                    result = jmeno + "ovi";
            }
            else if (c0 == 'l')
            {
                if (c1 == 'e')
                {
                    if (c2 == 'c' || c2 == 'i' || c2 == 'u') // Marcel, Samuel, Gabriel
                        result = jmeno + "ovi";
                    else // Karel
                        result = jmeno[..^2] + "lovi";
                }
                else if (c1 == 'o' && c2 == 'k') // Nikol
                    result = jmeno;
                else if (c1 == 'a' || c1 == 'i' || c1 == 'o') // Michal, Bohumil, Anatol
                    result = jmeno + "ovi";
                else // Král
                    result = jmeno + "ovi";
            }
            else if (c0 == 'o') // Ronaldo, Santiago
            {
                result = jmeno + "vi";
            }
            else if (c0 == 'r')
            {
                if (c1 == 'a' || c1 == 'e') // Dagmar, Ester
                {
                    if (c2 == 'k' || c2 == 'v' || c2 == 'm' || c2 == 't' || c2 == 'p' || c2 == 'l' || c2 == 'g') // Otakar, Oliver, Otmar, Peter, Kašpar, Müller, Langer
                        result = jmeno + "ovi";
                    else
                        result = jmeno;
                }
                else
                    result = jmeno + "ovi";
            }
            else if (c0 == 'y' || c0 == 'í' || c0 == 'é')
            {
                if (c1 == 'l') // Emily
                    result = jmeno;
                else // Harry, Jiří, René
                    result = jmeno + "mu";
            }
            else if (c0 == 'ý')
            {
                result = jmeno[..^1] + "ému";
            }
            else if (c0 == 'c') // Kadlec
            {
                if (c1 == 'e' && (c2 == 'n' || c2 == 'm')) // Vavřinec
                    result = jmeno[..^2] + "covi";
                else
                    result = jmeno + "ovi";
            }
            else if (c0 == 't') // Růt
            {
                if (c1 == 'í') // Vít
                    result = jmeno + "ovi";
                else
                    result = jmeno;
            }
            else if (c0 == 'ů' || c0 == 'd') // Petrů, Ingrid
            {
                if (c1 == 'r' || c1 == 'n' || c1 == 'a') // Richard, Roland, Strnad
                    result = jmeno + "ovi";
                else
                    result = jmeno;
            }
            else
            {
                result = jmeno + "ovi";
            }

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


