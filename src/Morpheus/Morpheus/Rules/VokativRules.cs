using System;
using System.Collections.Generic;

namespace Morpheus.Rules;

public static class VokativRules
{
    // # Oslovujeme, voláme.
    public static string Transform(string input)
    {
        var output = new List<string>();
        var names = input.Contains(" ") ? input.Split(' ', StringSplitOptions.RemoveEmptyEntries) : new[] { input };

        foreach (var jmeno in names)
        {
            var ljmeno = " " + jmeno.ToLowerInvariant();
            var c = ljmeno[^1];
            (string oldEnd, string newEnd) replacepair;

            switch (c)
            {
                case 'a':
                    replacepair = ljmeno[^2] == 'i' ? ("a", "e") : ("a", "o");
                    break;
                case 'n':
                    switch (ljmeno[^2])
                    {
                        case 'o':
                            replacepair = ljmeno[^3] == 'i' ? (ljmeno[^5] == 'y' ? ("", "e") : ("", "")) : ("", "e");
                            break;
                        case 'i':
                            switch (ljmeno[^3])
                            {
                                case 'r': replacepair = ljmeno[^4] == 'a' ? ("", "e") : ("", ""); break;
                                case 'l': replacepair = ljmeno[^4] == 'r' ? ("", "e") : ("", ""); break;
                                default: replacepair = ("", "e"); break;
                            }
                            break;
                        case 'í': replacepair = ljmeno[^3] == 'r' ? ("", "") : ("", "e"); break;
                        case 'e':
                            switch (ljmeno[^3])
                            {
                                case 'm': replacepair = ljmeno[^4] == 'm' ? ("", "e") : ("", ""); break;
                                case 'r': replacepair = ljmeno[^4] == 'o' ? ("", "e") : ("", ""); break;
                                default: replacepair = ("", "e"); break;
                            }
                            break;
                        case 'y': replacepair = ljmeno[^3] == 'r' ? ("", "e") : ("", ""); break;
                        case 'á': replacepair = ljmeno[^3] == 'p' ? ("án", "ane") : ("", "e"); break;
                        default: replacepair = ljmeno[^2] == 'u' ? ("", "o") : ("", "e"); break;
                    }
                    break;
                case 'l':
                    switch (ljmeno[^2])
                    {
                        case 'e':
                            switch (ljmeno[^3])
                            {
                                case 'i': replacepair = ljmeno[^4] == 'r' ? ("", "") : ("", "i"); break;
                                case 'r': replacepair = ljmeno[^4] == 'a' ? ("el", "le") : ("", "i"); break;
                                case 'v': replacepair = ljmeno[^5] == 'p' ? ("el", "le") : ("el", "li"); break;
                                case 'k': replacepair = ljmeno[^4] == 'a' ? ("", "") : ("", "i"); break;
                                default: replacepair = ljmeno[^3] == 'h' ? ("", "") : ("", "i"); break;
                            }
                            break;
                        case 'i': replacepair = ljmeno[^3] == 'a' ? ("", "o") : ("", "e"); break;
                        case 'ě':
                        case 'á':
                        case 's': replacepair = ("", "i"); break;
                        case 'ů': replacepair = ("ůl", "ole"); break;
                        default: replacepair = ("", "e"); break;
                    }
                    break;
                case 'm':
                    switch (ljmeno[^2])
                    {
                        case 'a': replacepair = ljmeno[^3] == 'i' ? (ljmeno[^4] == 'r' ? ("", "") : ("", "e")) : ("", "e"); break;
                        default: replacepair = ljmeno[^2] == 'ů' ? ("ům", "ome") : ("", "e"); break;
                    }
                    break;
                case 'c':
                    switch (ljmeno[^2])
                    {
                        case 'e': replacepair = ljmeno[^3] == 'v' ? (ljmeno[^4] == 'š' ? ("vec", "evče") : ("ec", "če")) : ("ec", "če"); break;
                        case 'i': replacepair = ljmeno[^4] == 'o' ? ("", "i") : ("", "u"); break;
                        default: replacepair = ljmeno[^2] == 'a' ? ("", "u") : ("", "i"); break;
                    }
                    break;
                case 'e':
                    switch (ljmeno[^2])
                    {
                        case 'n': replacepair = ljmeno[^3] == 'n' ? (ljmeno[^7] == 'b' ? ("", "") : ("e", "o")) : (ljmeno[^3] == 'g' ? ("e", "i") : ("", "")); break;
                        case 'c': replacepair = ljmeno[^3] == 'i' ? (ljmeno[^4] == 'r' ? ("e", "i") : ("", "")) : (ljmeno[^3] == 'v' ? ("", "") : ("e", "i")); break;
                        case 'd': replacepair = ljmeno[^3] == 'l' ? ("e", "o") : ("", ""); break;
                        case 'g': replacepair = ljmeno[^3] == 'r' ? (ljmeno[^4] == 'a' ? ("", "") : ("e", "i")) : ("e", "i"); break;
                        case 'l':
                            if (ljmeno[^3] == 'l')
                            {
                                var c4 = ljmeno[^4];
                                replacepair = c4 == 'e' ? ("e", "o") : (c4 == 'o' ? ("", "") : ("e", "i"));
                            }
                            else replacepair = ("", "");
                            break;
                        case 's': replacepair = ljmeno[^3] == 's' ? ("e", "i") : ("e", "o"); break;
                        case 'h': replacepair = ljmeno[^3] == 't' ? ("", "") : ("e", "i"); break;
                        default: replacepair = ljmeno[^2] == 'k' ? ("", "u") : ("", ""); break;
                    }
                    break;
                case 's':
                    switch (ljmeno[^2])
                    {
                        case 'e':
                            switch (ljmeno[^3])
                            {
                                case 'n':
                                    switch (ljmeno[^4])
                                    {
                                        case 'e': replacepair = ("s", ""); break;
                                        case 'á': replacepair = ("", "i"); break;
                                        default: replacepair = ("", ""); break;
                                    }
                                    break;
                                case 'l':
                                    var c4 = ljmeno[^4];
                                    replacepair = c4 == 'u' ? (ljmeno[^5] == 'j' ? ("", "i") : ("s", "")) : ((c4 == 'o' || c4 == 'r') ? ("", "i") : ("s", ""));
                                    break;
                                case 'r': replacepair = ljmeno[^4] == 'e' ? ("s", "ro") : ("", "i"); break;
                                case 'd':
                                case 't':
                                case 'm': replacepair = ("s", ""); break;
                                case 'u': replacepair = ("s", "u"); break;
                                case 'p': replacepair = ("es", "se"); break;
                                case 'x': replacepair = ("es", "i"); break;
                                default: replacepair = ("", "i"); break;
                            }
                            break;
                        case 'i':
                            var c3i = ljmeno[^3];
                            if (c3i == 'r')
                                replacepair = ljmeno[^4] == 'a' ? (ljmeno[^5] == 'p' ? ("s", "de") : ("s", "to")) : ("", "i");
                            else if (c3i == 'n') replacepair = ljmeno[^4] == 'f' ? ("s", "de") : ("", "i");
                            else replacepair = c3i == 'm' ? ("s", "do") : ("", "i");
                            break;
                        case 'o':
                            var c3o = ljmeno[^3];
                            if (c3o == 'm') replacepair = ljmeno[^4] == 'i' ? ("os", "e") : ("", "i");
                            else if (c3o == 'k') replacepair = ("", "e");
                            else if (c3o == 'x') replacepair = ("os", "i");
                            else replacepair = ("os", "e");
                            break;
                        case 'a':
                            var c3a = ljmeno[^3];
                            if (c3a == 'r') replacepair = ljmeno[^4] == 'a' ? ("", "i") : ("as", "e");
                            else if (c3a == 'l') replacepair = ljmeno[^4] == 'l' ? ("s", "do") : ("", "i");
                            else replacepair = c3a == 'y' ? ("as", "e") : ("", "i");
                            break;
                        case 'r': replacepair = ljmeno[^3] == 'a' ? ("s", "te") : ("", "i"); break;
                        case 'u':
                            var c3u = ljmeno[^3];
                            if (c3u == 'n')
                            {
                                var c4u = ljmeno[^4];
                                replacepair = c4u == 'e' ? (ljmeno[^5] == 'v' ? ("us", "ero") : ("", "i")) : (c4u == 'g' ? ("", "i") : ("us", "e"));
                            }
                            else if (c3u == 'e') replacepair = ljmeno[^4] == 'z' ? ("zeus", "die") : ("us", "e");
                            else if (c3u == 'm') replacepair = ljmeno[^4] == 't' ? ("us", "e") : ("", "i");
                            else if (c3u == 'g' || c3u == 'a') replacepair = ("", "i");
                            else if (c3u == 'h') replacepair = ("", "e");
                            else if (c3u == 'c' || c3u == 'k') replacepair = ("s", "");
                            else replacepair = ("us", "e");
                            break;
                        case 'y': replacepair = ljmeno[^4] == 'a' ? ("", "i") : ("", ""); break;
                        default: replacepair = ljmeno[^2] == 'é' ? ("s", "e") : ("", "i"); break;
                    }
                    break;
                case 'o': replacepair = ljmeno[^2] == 'l' ? ("", "i") : ("", ""); break;
                case 'x': replacepair = ljmeno[^2] == 'n' ? ("x", "go") : ("", "i"); break;
                case 'i':
                    var c2i = ljmeno[^2];
                    if (c2i == 'n') replacepair = ljmeno[^4] == 'e' ? ("", "") : ("", "o");
                    else if (c2i == 'm') replacepair = ljmeno[^3] == 'a' ? ("", "") : ("", "o");
                    else if (c2i == 'r') replacepair = ljmeno[^3] == 'i' ? ("", "o") : ("", "");
                    else replacepair = (c2i == 's' || c2i == 'a' || c2i == 'o' || c2i == 'c' || c2i == 't') ? ("", "i") : ("", "");
                    break;
                case 't':
                    var c2t = ljmeno[^2];
                    if (c2t == 'i') replacepair = ljmeno[^3] == 'l' ? ("", "e") : ("", "");
                    else if (c2t == 'u') replacepair = ljmeno[^3] == 'r' ? ("", "") : ("", "e");
                    else replacepair = ("", "e");
                    break;
                case 'r':
                    var c2r = ljmeno[^2];
                    if (c2r == 'e')
                    {
                        var c3r = ljmeno[^3];
                        if (c3r == 'd') replacepair = ljmeno[^4] == 'i' ? (ljmeno[^5] == 'e' ? ("", "e") : ("", "i")) : ("er", "re");
                        else if (c3r == 't')
                        {
                            var c4r = ljmeno[^4];
                            replacepair = c4r == 'e' ? (ljmeno[^5] == 'p' ? ("", "e") : ("", "o")) : (c4r == 's' ? (ljmeno[^5] == 'o' ? ("", "e") : ("", "")) : (c4r == 'n' ? ("", "i") : ("", "e")));
                        }
                        else replacepair = (c3r == 'g' || c3r == 'k') ? ("er", "ře") : ("", "e");
                    }
                    else if (c2r == 'a')
                    {
                        var c3r = ljmeno[^3];
                        replacepair = c3r == 'm' ? (ljmeno[^4] == 'g' ? ("", "") : ("", "e")) : (c3r == 'l' ? (ljmeno[^5] == 'p' ? ("", "") : ("", "e")) : ("", "e"));
                    }
                    else if (c2r == 'o') replacepair = ljmeno[^3] == 'n' ? ("", "o") : ("", "e");
                    else replacepair = (c2r == 'd' || c2r == 't' || c2r == 'b') ? ("r", "ře") : ("", "e");
                    break;
                case 'j':
                    var c2j = ljmeno[^2];
                    if (c2j == 'o') replacepair = ljmeno[^3] == 't' ? ("oj", "ý") : ("", "i");
                    else if (c2j == 'i') replacepair = ljmeno[^3] == 'd' ? ("", "i") : ("ij", "ý");
                    else replacepair = c2j == 'y' ? ("yj", "ý") : ("", "i");
                    break;
                case 'd':
                    var c2d = ljmeno[^2];
                    if (c2d == 'i') replacepair = ljmeno[^3] == 'r' ? ("", "") : ("", "e");
                    else if (c2d == 'u') replacepair = ljmeno[^3] == 'a' ? ("", "") : ("", "e");
                    else replacepair = ("", "e");
                    break;
                case 'y':
                    var c2y = ljmeno[^2];
                    replacepair = (c2y == 'a' || c2y == 'g' || c2y == 'o') ? ("", "i") : ("", "");
                    break;
                case 'h':
                    var c2h = ljmeno[^2];
                    if (c2h == 'c')
                    {
                        var c3h = ljmeno[^3];
                        replacepair = c3h == 'r' ? ("", "i") : (c3h == 'ý' ? ("", "") : ("", "u"));
                    }
                    else if (c2h == 't') replacepair = ljmeno[^3] == 'e' ? ("", "e") : ("", "i");
                    else if (c2h == 'a') replacepair = ljmeno[^3] == 'o' ? ("", "u") : ("", "");
                    else replacepair = (c2h == 'ů') ? ("ůh", "ože") : ("", "i");
                    break;
                case 'v': replacepair = ljmeno[^2] == 'ů' ? ("", "") : ("", "e"); break;
                case 'u': replacepair = ljmeno[^2] == 't' ? ("", "") : ("", "i"); break;
                case 'k':
                    var c2k = ljmeno[^2];
                    replacepair = c2k == 'ě' ? (ljmeno[^3] == 'n' ? ("něk", "ňku") : ("k", "če")) : (c2k == 'e' ? ("ek", "ku") : ("", "u"));
                    break;
                case 'g': replacepair = ljmeno[^2] == 'i' ? (ljmeno[^3] == 'e' ? ("", "") : ("", "u")) : ("", "u"); break;
                case 'ň': replacepair = ljmeno[^2] == 'o' ? ("ň", "ni") : ("ůň", "oni"); break;
                case 'f':
                case 'p':
                case 'b': replacepair = ("", "e"); break;
                case 'w':
                case 'í':
                case 'á':
                case 'ý':
                case 'ů':
                case 'é': replacepair = ("", ""); break;
                default: replacepair = ("", "i"); break;
            }

            // Apply replacement with casing preservation similar to reference
            string outName;
            if (replacepair.oldEnd == "" && replacepair.newEnd == "")
            {
                outName = jmeno;
            }
            else if (replacepair.newEnd == "")
            {
                outName = jmeno.Substring(0, jmeno.Length - replacepair.oldEnd.Length);
            }
            else if (replacepair.oldEnd == "")
            {
                outName = jmeno + (char.IsLower(jmeno[^1]) ? replacepair.newEnd : replacepair.newEnd.ToUpperInvariant());
            }
            else
            {
                var replaceending = jmeno.Substring(jmeno.Length - replacepair.oldEnd.Length);
                if (replaceending.ToUpperInvariant() == replaceending)
                    outName = jmeno.Substring(0, jmeno.Length - replacepair.oldEnd.Length) + replacepair.newEnd.ToUpperInvariant();
                else if (System.Text.RegularExpressions.Regex.IsMatch(replaceending, @"^[A-ZÁČĎÉÍŇÓŘŠŤÚŮÝŽ][a-záčďéěíňóřšťúůýž]*$"))
                    outName = jmeno.Substring(0, jmeno.Length - replacepair.oldEnd.Length) + char.ToUpperInvariant(replacepair.newEnd[0]) + replacepair.newEnd.Substring(1).ToLowerInvariant();
                else if (char.IsUpper(jmeno[^1]))
                    outName = jmeno.Substring(0, jmeno.Length - replacepair.oldEnd.Length) + replacepair.newEnd.ToUpperInvariant();
                else
                    outName = jmeno.Substring(0, jmeno.Length - replacepair.oldEnd.Length) + replacepair.newEnd;
            }

            // Capitalize token like reference does at the end of each function
            output.Add(Capitalize(outName));
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


