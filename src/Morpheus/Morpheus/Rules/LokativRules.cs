using System;
using System.Collections.Generic;

namespace Morpheus.Rules;

public static class LokativRules
{
    // # O kom, o čem?
    // Faithful port of (6) lokativ/python/lokativ.py
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
                if (c1 == 'd' || c1 == 'n')
                {
                    if (c2 == 'o' || c2 == 'a') // Svoboda, Smetana
                        result = jmeno[..^1] + "ovi";
                    else // Anna, Linda
                        result = jmeno[..^1] + "ě";
                }
                else if (c1 == 'č' || c1 == 'j' || c1 == 'c') // Ivča, Kája, Danica
                    result = jmeno[..^1] + "e";
                else if (c1 == 'ď')
                {
                    if (c2 == 'a') // Naďa
                        result = jmeno[..^2] + "dě";
                    else // Láďa
                        result = jmeno[..^1] + "ovi";
                }
                else if (c1 == 'g') // Olga
                    result = jmeno[..^2] + "ze";
                else if (c1 == 'i') // Olivia
                    result = jmeno[..^1] + "i";
                else if (c1 == 'm') // Ema
                    result = jmeno[..^1] + "ě";
                else if (c1 == 'k')
                {
                    if (c2 == 'z' || c2 == 'č' || c2 == 'n' || c2 == 'j') // Procházka, Růžička, Červenka, Matějka
                        result = jmeno[..^2] + "kovi";
                    else // Eliška
                        result = jmeno[..^2] + "ce";
                }
                else if (c1 == 'l' && c2 == 'v') // Pavla
                    result = jmeno[..^1] + "e";
                else if (c1 == 'l') // Fiala, Nikola
                {
                    if (c2 == 'a')
                        result = jmeno[..^1] + "ovi";
                    else
                        result = jmeno[..^1] + "e";
                }
                else if (c1 == 'o') // Figueroa
                    result = jmeno;
                else if (c1 == 'r')
                {
                    if (c2 == 'e' || c2 == 'd' || c2 == 'o' || c2 == 'v') // Kučera, Sýkora, Vávra
                        result = jmeno[..^1] + "ovi";
                    else // Klára
                        result = jmeno[..^2] + "ře";
                }
                else if (c1 == 't')
                {
                    if (c2 == 'j' || c2 == 'n' || c2 == 'r') // Vojta, Valenta, Bárta
                        result = jmeno[..^1] + "ovi";
                    else // Alžběta
                        result = jmeno[..^1] + "ě";
                }
                else if (c1 == 'v') // Eva
                    result = jmeno[..^1] + "ě";
                else if (c1 == 'z')
                {
                    if (c2 == 'n') // Honza
                        result = jmeno[..^1] + "ovi";
                    else // Tereza
                        result = jmeno[..^1] + "e";
                }
                else if (c1 == 'ň') // Soňa
                    result = jmeno[..^2] + "ně";
                else if (c2 == 'c' || c1 == 'h' || c1 == 's' || c1 == 'p') // Průcha, Štursa, Chalupa, Smetana
                    result = jmeno[..^1] + "ovi";
                else if (c1 == 'e' || c1 == 'š') // Nataša, Andrea, Lea
                    result = jmeno[..^1] + "e";
                else
                    result = jmeno[..^1] + "e";
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
                else if (c1 == 'i' || c1 == 'c' || c1 == 'š') // Lucie, Alice, Danuše
                    result = jmeno[..^1] + "i";
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
                    if (c2 == 'c' || c2 == 'i' || c2 == 'u' || c2 == 'a') // Marcel, Samuel, Gabriel
                        result = jmeno + "ovi";
                    else // Karel
                        result = jmeno[..^2] + "lovi";
                }
                else if (c1 == 'o' && c2 == 'k') // Nikol
                    result = jmeno;
                else if (c1 == 'a' || c1 == 'i' || c1 == 'o' || c1 == 's') // Michal, Bohumil, Anatol, Přemysl
                    result = jmeno + "ovi";
                else // Král
                    result = jmeno + "ovi";
            }
            else if (c0 == 'm')
            {
                if (c1 == 'a')
                {
                    if (c2 == 'd') // Adam
                        result = jmeno + "ovi";
                    else // Miriam
                        result = jmeno;
                }
                else // Maxim
                    result = jmeno + "ovi";
            }
            else if (c0 == 'o')
            {
                if (c1 == 't') // Oto
                    result = jmeno + "vi";
                else // Ronaldo, Santiago
                    result = jmeno + "vi";
            }
            else if (c0 == 'r')
            {
                if (c1 == 'a' || c1 == 'e') // Dagmar, Ester
                {
                    if (c2 == 'k' || (c2 == 'm' && (c3 == 't' || c3 == 'e')) || c2 == 'p' || c2 == 'l' || c2 == 'v' || c2 == 't' || c2 == 'g') // Otakar, Otmar, Kašpar, Oliver, Peter, Langer
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
                    result = jmeno + "m";
            }
            else if (c0 == 'ý')
            {
                result = jmeno[..^1] + "ém";
            }
            else if (c0 == 'd')
            {
                if (c1 == 'i' && c2 == 'r') // Ingrid
                    result = jmeno;
                else
                    result = jmeno + "ovi";
            }
            else if (c0 == 'c')
            {
                if (c1 == 'n') // Vincenc
                    result = jmeno + "ovi";
                else
                {
                    if (c1 == 'l' || c1 == 'á') // Šolc, Bonifác
                        result = jmeno + "ovi";
                    else // Vavřinec
                        result = jmeno[..^2] + "covi";
                }
            }
            else if (c0 == 'ž') // Ambrož
            {
                result = jmeno + "ovi";
            }
            else if (c0 == 't')
            {
                if (c1 == 'ů') // Růt
                    result = jmeno;
                else
                    result = jmeno + "ovi";
            }
            else if (c0 == 'ů') // Petrů
            {
                result = jmeno;
            }
            else if (c0 == 'c' || c0 == 'j' || c0 == 'ř' || c0 == 'š') // Tomáš, Ondřej, Kadlec
            {
                result = jmeno + "ovi";
            }
            else if (c0 == 'x' || c0 == 's') // Max, Nikolas
            {
                result = jmeno + "ovi";
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


