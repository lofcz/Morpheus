using System;
using System.Collections.Generic;

namespace Morpheus.Rules;

public static class AkuzativRules
{
    // # Koho, co?
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

            if (c0 == 'a')
            {
                if (c1 == 'd' || c1 == 'n') // Anna, Linda
                    result = jmeno[..^1] + "u";
                else if (c1 == 'ď' || (c1 == 'l' && c2 == 'a')) // Láďa, Fiala
                    result = jmeno[..^1] + "u";
                else if (c1 == 'g' || c1 == 's') // Olga, Denisa
                    result = jmeno[..^1] + "u";
                else if (c1 == 'i') // Olivia
                    result = jmeno[..^1] + "i";
                else if (c1 == 'k') // Eliška
                {
                    if (c2 == 'z' || (c2 == 'r' && c3 == 'k') || c2 == 'č' || c2 == 'j' || c2 == 'b' || c2 == 'p' || c2 == 'i') // Procházka, Jirka, Růžička, Matějka, Rybka, Veronika
                        result = jmeno[..^1] + "u";
                    else
                        result = jmeno[..^1] + "u";
                }
                else if (c1 == 'l' && c2 == 'v') // Pavla
                    result = jmeno[..^1] + "u";
                else if (c1 == 'ň') // Soňa
                    result = jmeno[..^1] + "u";
                else if (c1 == 'o') // Figueroa
                    result = jmeno;
                else if (c1 == 'c' || c1 == 'm' || c1 == 'p') // Danica, Ema, Chalupa
                    result = jmeno[..^1] + "u";
                else if (c1 == 'r') // Klára
                {
                    if (c2 == 'í') // Míra, Drahomíra
                        result = jmeno[..^1] + "u";
                    else if (c2 == 'd' || c2 == 'e' || c2 == 'o') // Jindra, Kučera
                        result = jmeno[..^1] + "u";
                    else
                        result = jmeno[..^1] + "u";
                }
                else if (c1 == 't')
                {
                    if (c2 == 'n') // Franta
                        result = jmeno[..^1] + "u";
                    else // Agáta
                        result = jmeno[..^1] + "u";
                }
                else if (c1 == 'v') // Eva
                    result = jmeno[..^1] + "u";
                else if (c1 == 'z') // Honza
                {
                    if (c2 == 'e') // Tereza
                        result = jmeno[..^1] + "u";
                    else
                        result = jmeno[..^1] + "u";
                }
                else if (c2 == 'c' || c1 == 'h') // Průcha
                    result = jmeno[..^1] + "u";
                else if (c1 == 'e' || c1 == 'š' || c1 == 'č' || c1 == 'j' || c1 == 'l') // Nataša, Andrea, Lea / Ivča, Kája, Nikola
                    result = jmeno[..^1] + "u";
                else if (c2 == 'o') // Kučera
                    result = jmeno[..^1] + "u";
                else
                    result = jmeno;
            }
            else if (c0 == 'c') // Kadlec
            {
                if (c1 == 'e' && (c2 == 'n' || c2 == 'm')) // Vavřinec, Adamec
                    result = jmeno[..^2] + "ce";
                else
                    result = jmeno + "e";
            }
            else if (c0 == 'á')
            {
                result = jmeno[..^1] + "ou";
            }
            else if (c0 == 'š') // Tomáš
            {
                result = jmeno + "e";
            }
            else if (c0 == 'e')
            {
                if (c1 == 'g') // George
                    result = jmeno[..^1] + "ovi";
                else if (c1 == 'e' || c1 == 'o') // Zoe
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
            else if (c0 == 'j')
            {
                result = jmeno + "e";
            }
            else if (c0 == 'k')
            {
                if (c1 == 'e') // Malášek
                    result = jmeno[..^2] + "ka";
                else if (c1 == 'ě')
                {
                    if (c2 == 'n') // Zbyněk
                        result = jmeno[..^3] + "ňka";
                    else if (c2 == 'd') // Luděk
                        result = jmeno[..^3] + "ďka";
                    else if (c2 == 'n') // Vaněk
                        result = jmeno[..^3] + "ňka";
                    else
                        result = jmeno;
                }
                else // Novák
                    result = jmeno + "a";
            }
            else if (c0 == 'l')
            {
                if (c1 == 'e')
                {
                    if (c2 == 'c' || c2 == 'i' || c2 == 'u' || c2 == 'a') // Marcel, Samuel, Gabriel, Michael
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
            else if (c0 == 'o')
            {
                if (c1 == 't') // Oto
                    result = jmeno[..^1] + "u";
                else // Ronaldo, Santiago
                    result = jmeno[..^1] + "a";
            }
            else if (c0 == 'd')
            {
                if (c1 == 'l' || c1 == 'r' || c1 == 'n' || c1 == 'd') // Leopold, Richard, Roland, Strnad
                    result = jmeno + "a";
                else // Ingrid
                    result = jmeno;
            }
            else if (c0 == 'r')
            {
                if (c1 == 'a' || c1 == 'e') // Dagmar, Ester
                {
                    if (c2 == 'k' || c2 == 'v' || c2 == 'm' || c2 == 't' || c2 == 'p' || c2 == 'l' || c2 == 'g') // Otakar, Oliver, Otmar, Peter, Kašpar, Müller, Langer
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
            else if (c0 == 'ř' || c0 == 'ž') // Kovář, Ambrož
            {
                result = jmeno + "e";
            }
            else if (c0 == 'ů') // Petrů
            {
                result = jmeno;
            }
            else if (c0 == 'z') // Radůz
            {
                result = jmeno + "e";
            }
            else if (c0 == 't') // Růt
            {
                result = jmeno;
            }
            else if (c0 == 's' || c0 == 'x') // Nikolas, Max
            {
                if (c1 == 'e') // Pes
                    result = jmeno[..^2] + "sa";
                else
                    result = jmeno + "e";
            }
            else
            {
                result = jmeno + "a";
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


