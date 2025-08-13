using System.Text;

namespace Morpheus;

public class Normalizer
{
    static char[] mc_Convert;

    /// <summary>
    /// This function is ultra fast because it uses a lookup table.
    /// This function does not depend on Windows functionality. It also works on Linux.
    /// This function removes all diacritics, accents, etc.
    /// For example "Crème Brûlée mit Soße" is converted to "Creme Brulee mit Sosse".
    /// </summary>
    public static string RemoveDiacritics(string s_Text)
    {
        StringBuilder i_Out = new StringBuilder(s_Text.Length);
        foreach (char c_Char in s_Text)
        {
            i_Out.Append(c_Char < mc_Convert.Length ? mc_Convert[c_Char] : c_Char);
        }
        return i_Out.ToString();
    }

    // static constructor
    // See https://www.compart.com/en/unicode/U+0180
    static Normalizer()
    {
        mc_Convert = new char[0x270];

        // Fill char array with translation of each character to itself
        for (int i = 0; i < 0x270; i++)
        {
            mc_Convert[i] = (char)i;
        }

        // Store the replacements for 310 special characters
        #region Fill mc_Convert

        mc_Convert[0x0C0] = 'A'; // À
        mc_Convert[0x0C1] = 'A'; // Á
        mc_Convert[0x0C2] = 'A'; // Â
        mc_Convert[0x0C3] = 'A'; // Ã
        mc_Convert[0x0C4] = 'A'; // Ä
        mc_Convert[0x0C5] = 'A'; // Å
        mc_Convert[0x0C6] = 'A'; // Æ
        mc_Convert[0x0C7] = 'C'; // Ç
        mc_Convert[0x0C8] = 'E'; // È
        mc_Convert[0x0C9] = 'E'; // É
        mc_Convert[0x0CA] = 'E'; // Ê
        mc_Convert[0x0CB] = 'E'; // Ë
        mc_Convert[0x0CC] = 'I'; // Ì
        mc_Convert[0x0CD] = 'I'; // Í
        mc_Convert[0x0CE] = 'I'; // Î
        mc_Convert[0x0CF] = 'I'; // Ï
        mc_Convert[0x0D0] = 'D'; // Ð
        mc_Convert[0x0D1] = 'N'; // Ñ
        mc_Convert[0x0D2] = 'O'; // Ò
        mc_Convert[0x0D3] = 'O'; // Ó
        mc_Convert[0x0D4] = 'O'; // Ô
        mc_Convert[0x0D5] = 'O'; // Õ
        mc_Convert[0x0D6] = 'O'; // Ö
        mc_Convert[0x0D8] = 'O'; // Ø
        mc_Convert[0x0D9] = 'U'; // Ù
        mc_Convert[0x0DA] = 'U'; // Ú
        mc_Convert[0x0DB] = 'U'; // Û
        mc_Convert[0x0DC] = 'U'; // Ü
        mc_Convert[0x0DD] = 'Y'; // Ý
        mc_Convert[0x0DF] = 's'; // ß
        mc_Convert[0x0E0] = 'a'; // à
        mc_Convert[0x0E1] = 'a'; // á
        mc_Convert[0x0E2] = 'a'; // â
        mc_Convert[0x0E3] = 'a'; // ã
        mc_Convert[0x0E4] = 'a'; // ä
        mc_Convert[0x0E5] = 'a'; // å
        mc_Convert[0x0E6] = 'a'; // æ
        mc_Convert[0x0E7] = 'c'; // ç
        mc_Convert[0x0E8] = 'e'; // è
        mc_Convert[0x0E9] = 'e'; // é
        mc_Convert[0x0EA] = 'e'; // ê
        mc_Convert[0x0EB] = 'e'; // ë
        mc_Convert[0x0EC] = 'i'; // ì
        mc_Convert[0x0ED] = 'i'; // í
        mc_Convert[0x0EE] = 'i'; // î
        mc_Convert[0x0EF] = 'i'; // ï
        mc_Convert[0x0F1] = 'n'; // ñ
        mc_Convert[0x0F2] = 'o'; // ò
        mc_Convert[0x0F3] = 'o'; // ó
        mc_Convert[0x0F4] = 'o'; // ô
        mc_Convert[0x0F5] = 'o'; // õ
        mc_Convert[0x0F6] = 'o'; // ö
        mc_Convert[0x0F8] = 'o'; // ø
        mc_Convert[0x0F9] = 'u'; // ù
        mc_Convert[0x0FA] = 'u'; // ú
        mc_Convert[0x0FB] = 'u'; // û
        mc_Convert[0x0FC] = 'u'; // ü
        mc_Convert[0x0FD] = 'y'; // ý
        mc_Convert[0x0FF] = 'y'; // ÿ
        mc_Convert[0x100] = 'A'; // Ā
        mc_Convert[0x101] = 'a'; // ā
        mc_Convert[0x102] = 'A'; // Ă
        mc_Convert[0x103] = 'a'; // ă
        mc_Convert[0x104] = 'A'; // Ą
        mc_Convert[0x105] = 'a'; // ą
        mc_Convert[0x106] = 'C'; // Ć
        mc_Convert[0x107] = 'c'; // ć
        mc_Convert[0x108] = 'C'; // Ĉ
        mc_Convert[0x109] = 'c'; // ĉ
        mc_Convert[0x10A] = 'C'; // Ċ
        mc_Convert[0x10B] = 'c'; // ċ
        mc_Convert[0x10C] = 'C'; // Č
        mc_Convert[0x10D] = 'c'; // č
        mc_Convert[0x10E] = 'D'; // Ď
        mc_Convert[0x10F] = 'd'; // ď
        mc_Convert[0x110] = 'D'; // Đ
        mc_Convert[0x111] = 'd'; // đ
        mc_Convert[0x112] = 'E'; // Ē
        mc_Convert[0x113] = 'e'; // ē
        mc_Convert[0x114] = 'E'; // Ĕ
        mc_Convert[0x115] = 'e'; // ĕ
        mc_Convert[0x116] = 'E'; // Ė
        mc_Convert[0x117] = 'e'; // ė
        mc_Convert[0x118] = 'E'; // Ę
        mc_Convert[0x119] = 'e'; // ę
        mc_Convert[0x11A] = 'E'; // Ě
        mc_Convert[0x11B] = 'e'; // ě
        mc_Convert[0x11C] = 'G'; // Ĝ
        mc_Convert[0x11D] = 'g'; // ĝ
        mc_Convert[0x11E] = 'G'; // Ğ
        mc_Convert[0x11F] = 'g'; // ğ
        mc_Convert[0x120] = 'G'; // Ġ
        mc_Convert[0x121] = 'g'; // ġ
        mc_Convert[0x122] = 'G'; // Ģ
        mc_Convert[0x123] = 'g'; // ģ
        mc_Convert[0x124] = 'H'; // Ĥ
        mc_Convert[0x125] = 'h'; // ĥ
        mc_Convert[0x126] = 'H'; // Ħ
        mc_Convert[0x127] = 'h'; // ħ
        mc_Convert[0x128] = 'I'; // Ĩ
        mc_Convert[0x129] = 'i'; // ĩ
        mc_Convert[0x12A] = 'I'; // Ī
        mc_Convert[0x12B] = 'i'; // ī
        mc_Convert[0x12C] = 'I'; // Ĭ
        mc_Convert[0x12D] = 'i'; // ĭ
        mc_Convert[0x12E] = 'I'; // Į
        mc_Convert[0x12F] = 'i'; // į
        mc_Convert[0x130] = 'I'; // İ
        mc_Convert[0x131] = 'i'; // ı
        mc_Convert[0x134] = 'J'; // Ĵ
        mc_Convert[0x135] = 'j'; // ĵ
        mc_Convert[0x136] = 'K'; // Ķ
        mc_Convert[0x137] = 'k'; // ķ
        mc_Convert[0x138] = 'K'; // ĸ
        mc_Convert[0x139] = 'L'; // Ĺ
        mc_Convert[0x13A] = 'l'; // ĺ
        mc_Convert[0x13B] = 'L'; // Ļ
        mc_Convert[0x13C] = 'l'; // ļ
        mc_Convert[0x13D] = 'L'; // Ľ
        mc_Convert[0x13E] = 'l'; // ľ
        mc_Convert[0x13F] = 'L'; // Ŀ
        mc_Convert[0x140] = 'l'; // ŀ
        mc_Convert[0x141] = 'L'; // Ł
        mc_Convert[0x142] = 'l'; // ł
        mc_Convert[0x143] = 'N'; // Ń
        mc_Convert[0x144] = 'n'; // ń
        mc_Convert[0x145] = 'N'; // Ņ
        mc_Convert[0x146] = 'n'; // ņ
        mc_Convert[0x147] = 'N'; // Ň
        mc_Convert[0x148] = 'n'; // ň
        mc_Convert[0x149] = 'n'; // ŉ
        mc_Convert[0x14C] = 'O'; // Ō
        mc_Convert[0x14D] = 'o'; // ō
        mc_Convert[0x14E] = 'O'; // Ŏ
        mc_Convert[0x14F] = 'o'; // ŏ
        mc_Convert[0x150] = 'O'; // Ő
        mc_Convert[0x151] = 'o'; // ő
        mc_Convert[0x152] = 'O'; // Œ
        mc_Convert[0x153] = 'o'; // œ
        mc_Convert[0x154] = 'R'; // Ŕ
        mc_Convert[0x155] = 'r'; // ŕ
        mc_Convert[0x156] = 'R'; // Ŗ
        mc_Convert[0x157] = 'r'; // ŗ
        mc_Convert[0x158] = 'R'; // Ř
        mc_Convert[0x159] = 'r'; // ř
        mc_Convert[0x15A] = 'S'; // Ś
        mc_Convert[0x15B] = 's'; // ś
        mc_Convert[0x15C] = 'S'; // Ŝ
        mc_Convert[0x15D] = 's'; // ŝ
        mc_Convert[0x15E] = 'S'; // Ş
        mc_Convert[0x15F] = 's'; // ş
        mc_Convert[0x160] = 'S'; // Š
        mc_Convert[0x161] = 's'; // š
        mc_Convert[0x162] = 'T'; // Ţ
        mc_Convert[0x163] = 't'; // ţ
        mc_Convert[0x164] = 'T'; // Ť
        mc_Convert[0x165] = 't'; // ť
        mc_Convert[0x166] = 'T'; // Ŧ
        mc_Convert[0x167] = 't'; // ŧ
        mc_Convert[0x168] = 'U'; // Ũ
        mc_Convert[0x169] = 'u'; // ũ
        mc_Convert[0x16A] = 'U'; // Ū
        mc_Convert[0x16B] = 'u'; // ū
        mc_Convert[0x16C] = 'U'; // Ŭ
        mc_Convert[0x16D] = 'u'; // ŭ
        mc_Convert[0x16E] = 'U'; // Ů
        mc_Convert[0x16F] = 'u'; // ů
        mc_Convert[0x170] = 'U'; // Ű
        mc_Convert[0x171] = 'u'; // ű
        mc_Convert[0x172] = 'U'; // Ų
        mc_Convert[0x173] = 'u'; // ų
        mc_Convert[0x174] = 'W'; // Ŵ
        mc_Convert[0x175] = 'w'; // ŵ
        mc_Convert[0x176] = 'Y'; // Ŷ
        mc_Convert[0x177] = 'y'; // ŷ
        mc_Convert[0x178] = 'Y'; // Ÿ
        mc_Convert[0x179] = 'Z'; // Ź
        mc_Convert[0x17A] = 'z'; // ź
        mc_Convert[0x17B] = 'Z'; // Ż
        mc_Convert[0x17C] = 'z'; // ż
        mc_Convert[0x17D] = 'Z'; // Ž
        mc_Convert[0x17E] = 'z'; // ž
        mc_Convert[0x180] = 'b'; // ƀ
        mc_Convert[0x189] = 'D'; // Ɖ
        mc_Convert[0x191] = 'F'; // Ƒ
        mc_Convert[0x192] = 'f'; // ƒ
        mc_Convert[0x193] = 'G'; // Ɠ
        mc_Convert[0x197] = 'I'; // Ɨ
        mc_Convert[0x198] = 'K'; // Ƙ
        mc_Convert[0x199] = 'k'; // ƙ
        mc_Convert[0x19A] = 'l'; // ƚ
        mc_Convert[0x19F] = 'O'; // Ɵ
        mc_Convert[0x1A0] = 'O'; // Ơ
        mc_Convert[0x1A1] = 'o'; // ơ
        mc_Convert[0x1AB] = 't'; // ƫ
        mc_Convert[0x1AC] = 'T'; // Ƭ
        mc_Convert[0x1AD] = 't'; // ƭ
        mc_Convert[0x1AE] = 'T'; // Ʈ
        mc_Convert[0x1AF] = 'U'; // Ư
        mc_Convert[0x1B0] = 'u'; // ư
        mc_Convert[0x1B6] = 'z'; // ƶ
        mc_Convert[0x1CD] = 'A'; // Ǎ
        mc_Convert[0x1CE] = 'a'; // ǎ
        mc_Convert[0x1CF] = 'I'; // Ǐ
        mc_Convert[0x1D0] = 'i'; // ǐ
        mc_Convert[0x1D1] = 'O'; // Ǒ
        mc_Convert[0x1D2] = 'o'; // ǒ
        mc_Convert[0x1D3] = 'U'; // Ǔ
        mc_Convert[0x1D4] = 'u'; // ǔ
        mc_Convert[0x1D5] = 'U'; // Ǖ
        mc_Convert[0x1D6] = 'u'; // ǖ
        mc_Convert[0x1D7] = 'U'; // Ǘ
        mc_Convert[0x1D8] = 'u'; // ǘ
        mc_Convert[0x1D9] = 'U'; // Ǚ
        mc_Convert[0x1DA] = 'u'; // ǚ
        mc_Convert[0x1DB] = 'U'; // Ǜ
        mc_Convert[0x1DC] = 'u'; // ǜ
        mc_Convert[0x1DE] = 'A'; // Ǟ
        mc_Convert[0x1DF] = 'a'; // ǟ
        mc_Convert[0x1E0] = 'A'; // Ǡ
        mc_Convert[0x1E1] = 'a'; // ǡ
        mc_Convert[0x1E2] = 'A'; // Ǣ
        mc_Convert[0x1E3] = 'a'; // ǣ
        mc_Convert[0x1E4] = 'G'; // Ǥ
        mc_Convert[0x1E5] = 'g'; // ǥ
        mc_Convert[0x1E6] = 'G'; // Ǧ
        mc_Convert[0x1E7] = 'g'; // ǧ
        mc_Convert[0x1E8] = 'K'; // Ǩ
        mc_Convert[0x1E9] = 'k'; // ǩ
        mc_Convert[0x1EA] = 'O'; // Ǫ
        mc_Convert[0x1EB] = 'o'; // ǫ
        mc_Convert[0x1EC] = 'O'; // Ǭ
        mc_Convert[0x1ED] = 'o'; // ǭ
        mc_Convert[0x1F0] = 'j'; // ǰ
        mc_Convert[0x1F4] = 'G'; // Ǵ
        mc_Convert[0x1F5] = 'g'; // ǵ
        mc_Convert[0x1F8] = 'N'; // Ǹ
        mc_Convert[0x1F9] = 'n'; // ǹ
        mc_Convert[0x1FA] = 'A'; // Ǻ
        mc_Convert[0x1FB] = 'a'; // ǻ
        mc_Convert[0x1FC] = 'A'; // Ǽ
        mc_Convert[0x1FD] = 'a'; // ǽ
        mc_Convert[0x1FE] = 'O'; // Ǿ
        mc_Convert[0x1FF] = 'o'; // ǿ
        mc_Convert[0x200] = 'A'; // Ȁ
        mc_Convert[0x201] = 'a'; // ȁ
        mc_Convert[0x202] = 'A'; // Ȃ
        mc_Convert[0x203] = 'A'; // ȃ
        mc_Convert[0x204] = 'E'; // Ȅ
        mc_Convert[0x205] = 'e'; // ȅ
        mc_Convert[0x206] = 'E'; // Ȇ
        mc_Convert[0x207] = 'e'; // ȇ
        mc_Convert[0x208] = 'I'; // Ȉ
        mc_Convert[0x209] = 'i'; // ȉ
        mc_Convert[0x20A] = 'I'; // Ȋ
        mc_Convert[0x20B] = 'i'; // ȋ
        mc_Convert[0x20C] = 'O'; // Ȍ
        mc_Convert[0x20D] = 'o'; // ȍ
        mc_Convert[0x20E] = 'O'; // Ȏ
        mc_Convert[0x20F] = 'o'; // ȏ
        mc_Convert[0x210] = 'R'; // Ȑ
        mc_Convert[0x211] = 'r'; // ȑ
        mc_Convert[0x212] = 'R'; // Ȓ
        mc_Convert[0x213] = 'r'; // ȓ
        mc_Convert[0x214] = 'U'; // Ȕ
        mc_Convert[0x215] = 'u'; // ȕ
        mc_Convert[0x216] = 'U'; // Ȗ
        mc_Convert[0x217] = 'u'; // ȗ
        mc_Convert[0x218] = 'S'; // Ș
        mc_Convert[0x219] = 's'; // ș
        mc_Convert[0x21A] = 'T'; // Ț
        mc_Convert[0x21B] = 't'; // ț
        mc_Convert[0x21E] = 'H'; // Ȟ
        mc_Convert[0x21F] = 'h'; // ȟ
        mc_Convert[0x224] = 'Z'; // Ȥ
        mc_Convert[0x225] = 'z'; // ȥ
        mc_Convert[0x226] = 'A'; // Ȧ
        mc_Convert[0x227] = 'a'; // ȧ
        mc_Convert[0x228] = 'E'; // Ȩ
        mc_Convert[0x229] = 'e'; // ȩ
        mc_Convert[0x22A] = 'O'; // Ȫ
        mc_Convert[0x22B] = 'o'; // ȫ
        mc_Convert[0x22C] = 'O'; // Ȭ
        mc_Convert[0x22D] = 'o'; // ȭ
        mc_Convert[0x22E] = 'O'; // Ȯ
        mc_Convert[0x22F] = 'o'; // ȯ
        mc_Convert[0x230] = 'O'; // Ȱ
        mc_Convert[0x231] = 'o'; // ȱ
        mc_Convert[0x232] = 'Y'; // Ȳ
        mc_Convert[0x233] = 'y'; // ȳ
        mc_Convert[0x234] = 'l'; // ȴ
        mc_Convert[0x235] = 'n'; // ȵ
        mc_Convert[0x23A] = 'A'; // Ⱥ
        mc_Convert[0x23B] = 'C'; // Ȼ
        mc_Convert[0x23C] = 'c'; // ȼ
        mc_Convert[0x23D] = 'L'; // Ƚ
        mc_Convert[0x23E] = 'T'; // Ⱦ
        mc_Convert[0x23F] = 's'; // ȿ
        mc_Convert[0x240] = 'z'; // ɀ
        mc_Convert[0x243] = 'B'; // Ƀ
        mc_Convert[0x244] = 'U'; // Ʉ
        mc_Convert[0x246] = 'E'; // Ɇ
        mc_Convert[0x247] = 'e'; // ɇ
        mc_Convert[0x248] = 'J'; // Ɉ
        mc_Convert[0x249] = 'j'; // ɉ
        mc_Convert[0x24C] = 'R'; // Ɍ
        mc_Convert[0x24D] = 'r'; // ɍ
        mc_Convert[0x24E] = 'Y'; // Ɏ
        mc_Convert[0x24F] = 'y'; // ɏ
        mc_Convert[0x261] = 'g'; // ɡ

        #endregion
    }
}