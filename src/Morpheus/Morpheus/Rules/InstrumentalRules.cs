using System;
using System.Collections.Generic;

namespace Morpheus.Rules;

public static class InstrumentalRules
{
    // # S kým, s čím?
    // Faithful port of (7) instrumental/python/instrumental.py
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
                if (c1 == 'i') // Olivia
                    result = jmeno[..^1] + "í";
                else if (c1 == 'o') // Figueroa
                    result = jmeno;
                else
                    result = jmeno[..^1] + "ou";
            }
            else if (c0 == 'c') // Kadlec
            {
                if (c1 == 'e' && (c2 == 'n' || c2 == 'm')) // Vavřinec, Adamec
                    result = jmeno[..^2] + "cem";
                else
                    result = jmeno + "em";
            }
            else if (c0 == 'á')
            {
                result = jmeno[..^1] + "ou";
            }
            else if (c0 == 'š') // Tomáš
            {
                result = jmeno + "em";
            }
            else if (c0 == 'e')
            {
                if (c1 == 'g') // George
                    result = jmeno[..^1] + "m";
                else if (c1 == 'c' || c1 == 'i' || c1 == 'š') // Alice, Amálie, Miluše
                    result = jmeno[..^1] + "í";
                else
                    result = jmeno;
            }
            else if (c0 == 'h' || c0 == 'i')
            {
                if (c1 == 'c') // Bedřich, Vojtěch
                    result = jmeno + "em";
                else // Sarah, Niki
                    result = jmeno;
            }
            else if (c0 == 'j' || (c0 == 'm' && c1 != 'a') || c0 == 'f' || c0 == 'n' || (c0 == 't' && c1 != 'ů') || c0 == 'p' || c0 == 'b' || c0 == 'g' || c0 == 'v')
            {
                result = jmeno + "em";
            }
            else if (c0 == 'm' && c1 == 'a') // Miriam
            {
                result = jmeno;
            }
            else if (c0 == 'k')
            {
                if (c1 == 'e') // Malášek
                    result = jmeno[..^2] + "kem";
                else if (c1 == 'ě')
                {
                    if (c2 == 'n') // Zbyněk
                        result = jmeno[..^3] + "ňkem";
                    else if (c2 == 'd') // Luděk
                        result = jmeno[..^3] + "ďkem";
                    else if (c2 == 'n') // Vaněk
                        result = jmeno[..^3] + "ňkem";
                    else
                        result = jmeno;
                }
                else // Novák
                    result = jmeno + "em";
            }
            else if (c0 == 'l')
            {
                if (c1 == 'e')
                {
                    if (c2 == 'c' || c2 == 'i' || c2 == 'u' || c2 == 'a') // Marcel, Samuel, Gabriel, Michael
                        result = jmeno + "em";
                    else // Karel
                        result = jmeno[..^2] + "lem";
                }
                else if (c1 == 'o' && c2 == 'k') // Nikol
                    result = jmeno;
                else // Král, Michal, Bohumil, Anatol, Přemysl
                    result = jmeno + "em";
            }
            else if (c0 == 'o')
            {
                if (c1 == 't') // Oto
                    result = jmeno[..^1] + "ou";
                else // Ronaldo, Santiago
                    result = jmeno[..^1] + "em";
            }
            else if (c0 == 'd')
            {
                if (c1 == 'l' || c1 == 'r' || c1 == 'n' || (c1 == 'i' && c2 == 'v')) // Leopold, Richard, Roland, Strnad, David
                    result = jmeno + "em";
                else // Ingrid
                    result = jmeno;
            }
            else if (c0 == 'r')
            {
                if (c1 == 'a' || c1 == 'e' || c1 == 'd' || c1 == 'u' || c1 == 'í' || c1 == 'o' || c1 == 't') // Dagmar, Ester, Alexandr, Artur, Bohumíř, Ctibor
                {
                    if (c2 == 'k' || c2 == 'v' || (c2 == 'm' && c3 != 'g') || c2 == 'n' || (c2 == 't' && c3 != 's') || c2 == 'p' || c2 == 'l' || c2 == 'g' || c2 == 'b' || c2 == 'd' || c2 == 'e') // Otakar, Oliver, Otmar, Peter, Kašpar, Müller, Langer, Lubor, Teodor
                        result = jmeno + "em";
                    else
                        result = jmeno;
                }
                else
                    result = jmeno + "a"; // rare from ref
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
                result = jmeno[..^1] + "m";
            }
            else if (c0 == 'ř' || c0 == 'ž') // Kovář, Ambrož
            {
                result = jmeno + "em";
            }
            else if (c0 == 'ů') // Petrů
            {
                result = jmeno;
            }
            else if (c0 == 'z') // Radůz
            {
                result = jmeno + "em";
            }
            else if (c0 == 't') // Růt
            {
                result = jmeno;
            }
            else if (c0 == 's' || c0 == 'x') // Nikolas, Max
            {
                result = jmeno + "em";
            }
            else
            {
                result = jmeno[..^1] + "ou";
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


