"""
Shared utilities and constants for NER dataset generation strategies.
"""
import random
import unicodedata
import re
from typing import List, Tuple

# Define IOB tags
B_PER, I_PER = "B-PER", "I-PER"
B_ORG, I_ORG = "B-ORG", "I-ORG"
B_NICK, I_NICK = "B-NICK", "I-NICK"
B_TIT, I_TIT = "B-TIT", "I-TIT"
O = "O"

# List of Czech and common international company suffixes
COMPANY_SUFFIXES = [
    "s.r.o.", "a.s.", "v.o.s.", "k.s.", "z.s.", "o.p.s.", "spol. s r.o.", "v likvidaci", "družstvo",
    "LLC", "Ltd.", "Inc.", "GmbH", "S.A.", "Corp.", "Limited", "Incorporated"
]

# Comprehensive lists of titles based on user-provided C# code
TITLES_BEFORE = [
    "Pan", "Paní", "Bc.", "BcA.", "Ing.", "Ing. arch.", "MUDr.", "MDDr.", "MVDr.",
    "MgA.", "Mgr.", "JUDr.", "PhDr.", "RNDr.", "PharmDr.", "ThLic.", "ThDr.",
    "akad. arch.", "ak. mal.", "ak. soch.", "MSDr.", "PaedDr.", "PhMr.", "RCDr.",
    "RSDr.", "RTDr.", "ThMgr.", "as.", "odb. as.", "doc.", "prof.", "voj.",
    "svob.", "sv.", "des.", "čet.", "rtn.", "rtm.", "nrtm.", "prap.", "nprap.",
    "št. prap.", "šprap.", "por.", "npor.", "kpt.", "mjr.", "pplk.", "plk.",
    "brig.gen.", "genmjr.", "genpor.", "arm.gen.", "ppor.", "škpt.", "šrtm.",
    "gen.", "genplk.", "Rev.", "Very Rev.", "Most Rev.", "Rt. Rev.", "Right Rev.",
    "Fr.", "Father", "Sister", "Br.", "Brother", "Dcn.", "Deacon", "Bp.", "Bishop",
    "Abp.", "Archbishop", "Msgr.", "Monsignor", "Card.", "Cardinal", "Dom", "Abbot",
    "Mother", "Pastor", "Padre",
    # Promovaný titles
    "prom.", "promovaný", "promovaná",
    "prom. lékař", "promovaný lékař", "promovaná lékařka",
    "prom. právník", "promovaný právník", "promovaná právnička",
    "prom. biolog", "promovaný biolog", "promovaná bioložka",
    "prom. historik", "promovaný historik", "promovaná historička",
    "inženýr architekt", "prom. architekt", "promovaný architekt", "promovaná architektka",
    "prom. matematik", "promovaný matematik", "promovaná matematička",
    "prom. fyzik", "promovaný fyzik", "promovaná fyzička",
    "prom. chemik", "promovaný chemik", "promovaná chemička",
    "prom. geolog", "promovaný geolog", "promovaná geoložka",
    "prom. filosof", "promovaný filosof", "promovaná filosofka",
    "prom. psycholog", "promovaný psycholog", "promovaná psycholožka",
    "prom. ekonom", "promovaný ekonom", "promovaná ekonomka",
    "prom. farmaceut", "promovaný farmaceut", "promovaná farmaceutka",
    "prom. umělec", "promovaný umělec", "promovaná umělkyně",
    "prom. pedagog", "promovaný pedagog", "promovaná pedagožka",
    "prom. sociolog", "promovaný sociolog", "promovaná socioložka",
    # Full word titles
    "magistr", "inženýr", "doktor", "bakalář", "profesor", "docent"
]
TITLES_AFTER = [
    "Ph.D.", "DSc.", "CSc.", "Dr.", "DrSc.", "Th.D.", "DiS.", "dr. h. c.",
    "prof. h. c.", "MBA", "LL.M.", "Jr.", "Sr.", "PP.", "J.Em.", "J.Exc.",
    "J.M.", "Vdp.", "AMPLMUS", "A.R.D.", "Vldp.", "R.D.", "Dp.", "Vp.",
    "Rev. dom.", "Ct.p.", "V.G.", "P.A.", "J.C.D.", "S.T.D.", "D.D.", "Dr. eccl.",
    "Ph.D", "PhD" # Variations
]


# --- Allowed character filtering (to protect tokenizer vocab) ---
# We keep only: a-z, digits, limited punctuation, and specific diacritics for cs/sk/pl plus Russian Cyrillic.
_LATIN_BASE = "abcdefghijklmnopqrstuvwxyz"
_CS = "áčďéěíňóřšťúůýž"
_SK = "áäčďéíĺľňóôŕšťúýž"
_PL = "ąćęłńóśźż"
_RUS_CYR_RANGE = ("\u0430", "\u044f")  # 'а'..'я'
_RUS_EXTRA = "ё"  # include 'ё'
_DIGITS = "0123456789"
_PUNCT = " .,'\";-_/()[]@:+&|"  # minimal set used by our patterns (incl. semicolon and quotes)

_ALLOWED_LATIN = set(_LATIN_BASE + _CS + _SK + _PL)
_ALLOWED_CYR = set(chr(c) for c in range(ord(_RUS_CYR_RANGE[0]), ord(_RUS_CYR_RANGE[1]) + 1)) | set(_RUS_EXTRA)
_ALLOWED = _ALLOWED_LATIN | _ALLOWED_CYR | set(_DIGITS) | set(_PUNCT)

_MAP_CHARS = {
    # quotes
    """: '"', """: '"', "„": '"', "‟": '"', "‹": "'", "›": "'", "'": "'", "‚": "'",
    # dashes
    "–": "-", "—": "-", "−": "-",
    # spaces
    "\u00A0": " ",  # nbsp
}

_FULL_CHAR_SET_FOR_RE = _ALLOWED | set(_MAP_CHARS.keys())
_RE_CHAR_CLASS_CONTENT = "".join(
    '\\' + c if c in r'\]-^\'' else c
    for c in sorted(list(_FULL_CHAR_SET_FOR_RE))
)
_ALLOWED_CHARS_PATTERN = re.compile(f"^[{_RE_CHAR_CLASS_CONTENT}]+$", re.IGNORECASE)


def _map_char(c: str) -> str:
    return _MAP_CHARS.get(c, c)

def _is_allowed(c: str) -> bool:
    return c in _ALLOWED


def has_only_allowed_chars(text: str) -> bool:
    """Checks if all characters in a string are in the allowed set using a compiled regex."""
    if not text:
        return True
    return bool(_ALLOWED_CHARS_PATTERN.fullmatch(text))


def cripple_text(text: str) -> str:
    """Randomly introduces common variations/typos into a string."""
    text = text
    # Action 1: Remove dots
    if random.random() < 0.4:
        text = text.replace(".", "")
    
    # Action 2: Add/remove spaces around dots (if they still exist)
    if "." in text and random.random() < 0.3:
        text = text.replace(". ", ".").replace(".", ". ")

    # Action 3: Change case
    if random.random() < 0.2:
        text = text.upper()
    
    # Action 4: Collapse spaces
    text = " ".join(text.split())

    return text


def cripple_entity(text: str) -> str:
    """
    Introduces noise into a name or company name, like typos or missing diacritics.
    Lowercasing is handled by the tokenizer's normalizer later.
    """
    # 1. Probabilistically remove diacritics (very common)
    if random.random() < 0.5:
        text = "".join(
            c for c in unicodedata.normalize("NFD", text)
            if unicodedata.category(c) != "Mn"
        )

    # 2. Introduce keyboard-based typos (e.g., 'a' -> 's')
    if random.random() < 0.1 and len(text) > 1:
        chars = list(text)
        idx = random.randint(0, len(text) - 1)
        key_map = {
            'a': 's', 's': 'ad', 'd': 'sf', 'f': 'dg', 'g': 'fh', # etc.
        }
        if chars[idx].lower() in key_map:
            swap_chars = key_map[chars[idx].lower()]
            swap_char = random.choice(swap_chars)
            chars[idx] = swap_char.upper() if chars[idx].isupper() else swap_char
            text = "".join(chars)

    # 3. Introduce other diacritic mistakes (e.g., 'á' -> 'í')
    if random.random() < 0.1 and len(text) > 1:
        chars = list(text)
        idx = random.randint(0, len(text) - 1)
        diacritic_map = {
            'á': 'íéú', 'í': 'áéú', 'é': 'áíú', 'ú': 'áíé',
        }
        if chars[idx].lower() in diacritic_map:
            swap_chars = diacritic_map[chars[idx].lower()]
            swap_char = random.choice(swap_chars)
            chars[idx] = swap_char.upper() if chars[idx].isupper() else swap_char
            text = "".join(chars)
            
    # 4. Swap adjacent characters
    if random.random() < 0.05 and len(text) > 2:
        idx = random.randint(0, len(text) - 2)
        chars = list(text)
        chars[idx], chars[idx + 1] = chars[idx + 1], chars[idx]
        text = "".join(chars)
        
    # 5. Randomly remove a character
    if random.random() < 0.03 and len(text) > 2:
        idx = random.randint(0, len(text) - 1)
        text = text[:idx] + text[idx + 1 :]
        
    return text


def cripple_iy(text: str) -> str:
    """Swaps one or more i/y characters in a string to simulate common typos."""
    chars = list(text)
    iy_indices = [i for i, char in enumerate(chars) if char.lower() in 'iy']
    
    if not iy_indices:
        return text

    # First swap is guaranteed
    idx_to_swap = random.choice(iy_indices)
    char_to_swap = chars[idx_to_swap]
    
    if char_to_swap == 'i': chars[idx_to_swap] = 'y'
    elif char_to_swap == 'y': chars[idx_to_swap] = 'i'
    elif char_to_swap == 'I': chars[idx_to_swap] = 'Y'
    elif char_to_swap == 'Y': chars[idx_to_swap] = 'I'
    
    iy_indices.remove(idx_to_swap)

    # Subsequent swaps with 50% chance
    while iy_indices and random.random() < 0.5:
        idx_to_swap = random.choice(iy_indices)
        char_to_swap = chars[idx_to_swap]
        
        if char_to_swap == 'i': chars[idx_to_swap] = 'y'
        elif char_to_swap == 'y': chars[idx_to_swap] = 'i'
        elif char_to_swap == 'I': chars[idx_to_swap] = 'Y'
        elif char_to_swap == 'Y': chars[idx_to_swap] = 'I'

        iy_indices.remove(idx_to_swap)
    
    return "".join(chars)


def tag_entity(text: str, entity_type: str) -> Tuple[List[str], List[str]]:
    """
    Tags an entity text with IOB labels.
    entity_type: 'PER', 'ORG', 'NICK', 'TIT', 'LOC'
    Returns (words, tags).
    """
    words = text.split()
    if not words:
        return [], []
    
    if entity_type == "PER":
        tags = [B_PER] + [I_PER] * (len(words) - 1)
    elif entity_type == "ORG":
        tags = [B_ORG] + [I_ORG] * (len(words) - 1)
    elif entity_type == "NICK":
        tags = [B_NICK] + [I_NICK] * (len(words) - 1)
    elif entity_type == "TIT":
        tags = [B_TIT] + [I_TIT] * (len(words) - 1)
    elif entity_type == "LOC":
        tags = ["B-LOC"] + ["I-LOC"] * (len(words) - 1)
    else:
        tags = [O] * len(words)
    
    return words, tags


def tag_title_text(title_text: str) -> Tuple[List[str], List[str]]:
    """Tags a title string as TIT."""
    words = title_text.split()
    if not words:
        return [], []
    tags = [B_TIT] + [I_TIT] * (len(words) - 1)
    return words, tags


def corrupt_title_string(title: str) -> str:
    """
    REDUCED CORRUPTION: Only apply mild, realistic variations.
    Examples:
      - "Ing." -> "Ing" (remove dot), "ing." (lowercase), "ING." (uppercase)
      - "Ph.D." -> "PhD" (remove dots), "Ph. D." (add space)
    
    NO MORE: excessive dot fragmentation, random letter insertion, aggressive spacing
    """
    if not title:
        return title
    
    s = title.lower()
    
    # 1. Casing variations (30% total: 15% upper, 15% keep lower, 70% original)
    roll = random.random()
    if roll < 0.15:
        s = s.upper()
    elif roll < 0.30:
        pass  # Keep lowercase
    else:
        s = title  # Keep original casing
    
    # 2. Dot variations (only if title has dots, and only simple changes)
    if "." in s:
        roll = random.random()
        if roll < 0.40:  # 40% chance to remove all dots
            s = s.replace(".", "")
        elif roll < 0.60:  # 20% chance to add space after dot
            s = s.replace(".", ". ")
    
    # 3. Minor typo (5% chance) - only adjacent character swap
    if random.random() < 0.05 and len(s) > 2:
        letters = [i for i, c in enumerate(s) if c.isalpha()]
        if letters:
            i = random.choice(letters)
            chars = list(s)
            # Only swap with adjacent character, not random replacement
            if i + 1 < len(chars) and chars[i+1].isalpha():
                chars[i], chars[i+1] = chars[i+1], chars[i]
            s = "".join(chars)
    
    # Final cleanup
    return " ".join(s.split())
