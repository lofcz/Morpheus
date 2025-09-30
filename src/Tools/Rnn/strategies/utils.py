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
]
TITLES_AFTER = [
    "Ph.D.", "DSc.", "CSc.", "Dr.", "DrSc.", "Th.D.", "DiS.", "dr. h. c.",
    "prof. h. c.", "MBA", "LL.M.", "Jr.", "Sr.", "PP.", "J.Em.", "J.Exc.",
    "J.M.", "Vdp.", "AMPLMUS", "A.R.D.", "Vldp.", "R.D.", "Dp.", "Vp.",
    "Rev. dom.", "Ct.p.", "V.G.", "P.A.", "J.C.D.", "S.T.D.", "D.D.", "Dr. eccl."
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
            c if c not in "áčďéěíňóřšťúůýžÁČĎÉĚÍŇÓŘŠŤÚŮÝŽąćęłńóśźżĄĆĘŁŃÓŚŹŻäôĺľŕäôĺľŕё" else unicodedata.normalize("NFD", c)[0]
            for c in text
        )
    # 2. Introduce typos: swap adjacent characters 5% of the time
    if random.random() < 0.05 and len(text) > 2:
        idx = random.randint(0, len(text) - 2)
        chars = list(text)
        chars[idx], chars[idx + 1] = chars[idx + 1], chars[idx]
        text = "".join(chars)
    # 3. Randomly remove a character 3% of the time
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
    """Produce realistic corruptions of titles: missing/extra dots, spacing, casing, minor typos."""
    # Always start with lowercase to ensure consistency
    s = title.lower()
    
    # Random casing variations (10% chance to uppercase)
    if random.random() < 0.10:
        s = s.upper()
    
    # --- Dot and Space Corruption ---
    # This section manipulates dots and can introduce spaces, which increases the token count.
    if "." in s:
        roll = random.random()
        if roll < 0.30:  # Remove all dots
            s = s.replace(".", "")
        elif roll < 0.50:  # Remove some dots
            s = "".join(c if c != '.' or random.random() > 0.5 else '' for c in s)
        elif roll < 0.70:  # Add spaces around dots
            if random.random() < 0.5:
                s = s.replace(".", " .")  # e.g., "MUDr." -> "MUDr ."
            else:
                s = s.replace(".", " . ")  # e.g., "MUDr." -> "MUDr . "
        elif roll < 0.80:  # Double some dots
            s = "".join(c if c != '.' or random.random() > 0.5 else '..' for c in s)
        # else: keep dots as is (20% chance)

    # --- Whitespace Injection inside abbreviated titles ---
    # This will also increase token count. e.g., "Ing." -> "I ng."
    if "." in s and len(s) > 2 and random.random() < 0.15:
        # Find a random place to insert a space, but not at the start/end or next to an existing space
        eligible_indices = [i for i, c in enumerate(s) if i > 0 and c != ' ' and s[i-1] != ' ']
        if eligible_indices:
            idx_to_insert = random.choice(eligible_indices)
            s = s[:idx_to_insert] + " " + s[idx_to_insert:]

    # Minor typo: swap or substitute a character (letters only)
    if random.random() < 0.10 and len(s) > 2:
        letters = [i for i, c in enumerate(s) if c.isalpha()]
        if letters:
            i = random.choice(letters)
            chars = list(s)
            chars[i] = random.choice("abcdefghijklmnopqrstuvwxyz")
            s = "".join(chars)
            
    # Final cleanup: ensure multiple spaces are collapsed into one.
    # This is critical for ensuring split() works as expected by the tagger.
    return " ".join(s.split())
