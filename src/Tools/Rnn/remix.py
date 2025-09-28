import os
import random
import argparse
import pandas as pd
from typing import List, Tuple
import unicodedata

# --- Configuration ---
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))

# Define IOB tags
B_PER, I_PER = "B-PER", "I-PER"
B_ORG, I_ORG = "B-ORG", "I-ORG"
B_NICK, I_NICK = "B-NICK", "I-NICK"
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
    "Mother", "Pastor", "Padre"
]
TITLES_AFTER = [
    "Ph.D.", "DSc.", "CSc.", "Dr.", "DrSc.", "Th.D.", "DiS.", "dr. h. c.",
    "prof. h. c.", "MBA", "LL.M.", "Jr.", "Sr.", "PP.", "J.Em.", "J.Exc.",
    "J.M.", "Vdp.", "AMPLMUS", "A.R.D.", "Vldp.", "R.D.", "Dp.", "Vp.",
    "Rev. dom.", "Ct.p.", "V.G.", "P.A.", "J.C.D.", "S.T.D.", "D.D.", "Dr. eccl."
]


def cripple_entity(text: str) -> str:
    """
    Introduces noise into a name or company name, like typos or missing diacritics.
    Lowercasing is handled by the tokenizer's normalizer later.
    """
    # 1. Probabilistically remove diacritics (very common)
    if random.random() < 0.5:
        # Decompose into base character + combining mark, then remove combining marks
        nfkd_form = unicodedata.normalize('NFD', text)
        text = "".join([c for c in nfkd_form if not unicodedata.combining(c)])

    # 2. Probabilistically introduce one typo
    if random.random() < 0.2 and len(text) > 2:
        typo_type = random.choice(["delete", "substitute", "swap"])
        pos = random.randint(0, len(text) - 1)

        if typo_type == "delete":
            text = text[:pos] + text[pos+1:]
        elif typo_type == "substitute":
            random_char = random.choice("abcdefghijklmnopqrstuvwxyz")
            text = text[:pos] + random_char + text[pos+1:]
        elif typo_type == "swap" and pos < len(text) - 1:
            text = text[:pos] + text[pos+1] + text[pos] + text[pos+2:]
            
    return text


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

def tag_entity(text: str, base_tag: str) -> Tuple[List[str], List[str]]:
    """Splits text into words and assigns IOB tags."""
    words = text.split()
    if not words:
        return [], []
    
    tags = [f"B-{base_tag}"]
    tags.extend([f"I-{base_tag}"] * (len(words) - 1))
    return words, tags

def load_entities(path: str) -> List[str]:
    """Loads a list of entities from a file, one per line."""
    if not os.path.exists(path):
        print(f"Warning: Data file not found at {path}. This entity type will be skipped.")
        return []
    with open(path, "r", encoding="utf-8") as f:
        return [line.strip() for line in f if line.strip()]

def remix_single_entity(names: List[str], companies: List[str], nicknames: List[str]) -> Tuple[str, str]:
    """Creates a sample with a single entity."""
    entity_type = random.choice(["name", "company", "nickname"])
    
    text, tag = "", ""
    if entity_type == "name" and names:
        text = random.choice(names)
        _, tags = tag_entity(text, "PER")
        tag = " ".join(tags)
    elif entity_type == "company" and companies:
        text = random.choice(companies)
        _, tags = tag_entity(text, "ORG")
        tag = " ".join(tags)
    elif entity_type == "nickname" and nicknames:
        text = random.choice(nicknames)
        _, tags = tag_entity(text, "NICK")
        tag = " ".join(tags)
        
    return text, tag

def remix_name_with_title(names: List[str]) -> Tuple[str, str]:
    """Adds a common Czech title to a name, including complex/incorrect variations."""
    if not names:
        return "", ""
    name_text = random.choice(names)
    name_words, name_tags = tag_entity(name_text, "PER")
    
    # --- Decide on the complexity of the title pattern ---
    pattern_roll = random.random()

    final_words = []
    final_tags = []

    # Case 1: Simple, single title (most common) - 60% chance
    if pattern_roll < 0.6:
        place_it_wrong = random.random() < 0.15
        place_before = random.random() < 0.7 

        if not place_it_wrong:
            if place_before and TITLES_BEFORE:
                title = random.choice(TITLES_BEFORE)
                title_words = title.split()
                final_words = title_words + name_words
                final_tags = [O] * len(title_words) + name_tags
            elif TITLES_AFTER:
                title = random.choice(TITLES_AFTER)
                title_words = title.split()
                final_words = name_words + [","] + title_words
                final_tags = name_tags + [O] + ([O] * len(title_words))
        else: # Intentionally place it wrong
            if place_before and TITLES_BEFORE:
                title = random.choice(TITLES_BEFORE)
                title_words = title.split()
                final_words = name_words + [","] + title_words
                final_tags = name_tags + [O] + ([O] * len(title_words))
            elif TITLES_AFTER:
                title = random.choice(TITLES_AFTER)
                title_words = title.split()
                final_words = title_words + name_words
                final_tags = [O] * len(title_words) + name_tags

    # Case 2: Two titles (less common) - 30% chance
    elif pattern_roll < 0.9 and len(TITLES_BEFORE) > 1 and len(TITLES_AFTER) > 1:
        sub_pattern = random.choice(["before_before", "after_after", "before_after"])

        if sub_pattern == "before_before":
            title1, title2 = random.sample(TITLES_BEFORE, 2)
            t1_words, t2_words = title1.split(), title2.split()
            final_words = t1_words + t2_words + name_words
            final_tags = [O] * (len(t1_words) + len(t2_words)) + name_tags
        elif sub_pattern == "after_after":
            title1, title2 = random.sample(TITLES_AFTER, 2)
            t1_words, t2_words = title1.split(), title2.split()
            final_words = name_words + [","] + t1_words + [","] + t2_words
            final_tags = name_tags + [O] + ([O] * len(t1_words)) + [O] + ([O] * len(t2_words))
        else: # "before_after"
            title1 = random.choice(TITLES_BEFORE)
            title2 = random.choice(TITLES_AFTER)
            t1_words, t2_words = title1.split(), title2.split()
            final_words = t1_words + name_words + [","] + t2_words
            final_tags = [O] * len(t1_words) + name_tags + [O] + ([O] * len(t2_words))
            
    # Case 3: Duplicated title (e.g., Mgr. et Mgr.) - 10% chance
    else:
        if TITLES_BEFORE:
            title = random.choice(["Mgr.", "Ing."])
            separator = random.choice(["et", "a"])
            title_words = title.split()
            final_words = title_words + [separator] + title_words + name_words
            final_tags = [O] * len(title_words) + [O] + [O] * len(title_words) + name_tags
    
    # Final fallback if something went wrong
    if not final_words:
        return name_text, " ".join(name_tags)
        
    return " ".join(final_words), " ".join(final_tags)

def remix_name_with_nickname(names: List[str], nicknames: List[str]) -> Tuple[str, str]:
    """
    Creates samples with a nickname placed before, inside, or after a real name,
    using a wide variety of delimiters.
    e.g., 'Matěj (lofcz) Štágl', '"lofcz" Matěj Štágl', 'Matěj Štágl - lofcz'
    """
    if not names or not nicknames:
        return "", ""
    
    name_text = random.choice(names)
    nickname_text = random.choice(nicknames)
    
    # Use simpler, single-word nicknames for cleaner patterns
    if len(nickname_text.split()) > 1:
        # Fallback to just the nickname if it's complex
        _, nick_tags = tag_entity(nickname_text, "NICK")
        return nickname_text, " ".join(nick_tags)

    name_words, name_tags = name_text.split(), tag_entity(name_text, "PER")[1]
    nick_words, nick_tags = nickname_text.split(), tag_entity(nickname_text, "NICK")[1]

    DELIMITER_PAIRS = [
        ('(', ')'), ('[', ']'), ('{', '}'),
        ("'", "'"), ('"', '"'), ('„', '“'), 
        ('`', '`'), ('-', '-'), ('_', '_'),
        ('', '') # No delimiters
    ]
    
    q_start, q_end = random.choice(DELIMITER_PAIRS)
    
    # Choose placement: before, inside, or after
    placement = random.choice(["before", "inside", "after"])

    # The "inside" pattern only makes sense for multi-word names
    if len(name_words) < 2:
        placement = random.choice(["before", "after"])

    final_words, final_tags = [], []

    # Helper to add delimiters if they exist
    def wrap_with_delimiters(words, tags):
        if q_start and q_end:
            return [q_start] + words + [q_end], [O] + tags + [O]
        elif q_start: # Handles single dash/underscore case
             return [q_start] + words + [q_start], [O] + tags + [O]
        return words, tags

    wrapped_nick_words, wrapped_nick_tags = wrap_with_delimiters(nick_words, nick_tags)

    if placement == "before":
        final_words = wrapped_nick_words + name_words
        final_tags = wrapped_nick_tags + name_tags
    elif placement == "after":
        final_words = name_words + wrapped_nick_words
        final_tags = name_tags + wrapped_nick_tags
    elif placement == "inside":
        insert_pos = random.randint(1, len(name_words) - 1)
        # Correctly tag the second part of the name with I-PER
        name_tags[insert_pos:] = [I_PER] * len(name_tags[insert_pos:])
        
        final_words = name_words[:insert_pos] + wrapped_nick_words + name_words[insert_pos:]
        final_tags = name_tags[:insert_pos] + wrapped_nick_tags + name_tags[insert_pos:]

    return " ".join(final_words), " ".join(final_tags)


def remix_name_company_patterns(names: List[str], companies: List[str]) -> Tuple[str, str]:
    """
    Generates patterns like "Name - Company", "Company, Name", etc.
    Optionally adds a (crippled) business suffix to the company.
    """
    if not names or not companies:
        return "", ""

    name_text = random.choice(names)
    company_text = random.choice(companies)

    name_words, name_tags = tag_entity(name_text, "PER")
    company_words, company_tags = tag_entity(company_text, "ORG")

    # Optionally add and cripple a suffix
    if random.random() < 0.5:
        suffix = random.choice(COMPANY_SUFFIXES)
        crippled_suffix = cripple_text(suffix)
        company_words.extend(crippled_suffix.split())
        # The whole suffix is part of the organization
        company_tags.extend([I_ORG] * len(crippled_suffix.split()))

    pattern = random.choice([
        "name_sep_company", "company_sep_name"
    ])
    separator = random.choice(["-", ",", " | "])

    if pattern == "name_sep_company":
        all_words = name_words + separator.split() + company_words
        all_tags = name_tags + [O] * len(separator.split()) + company_tags
    else: # company_sep_name
        all_words = company_words + separator.split() + name_words
        all_tags = company_tags + [O] * len(separator.split()) + name_tags

    return " ".join(all_words), " ".join(all_tags)

def remix_company_with_multiple_names(names: List[str], companies: List[str]) -> Tuple[str, str]:
    """Generates patterns like 'Company - Name1 and Name2'"""
    if len(names) < 2 or not companies:
        return "", ""
    
    company_text = random.choice(companies)
    name1_text, name2_text = random.sample(names, 2)

    company_words, company_tags = tag_entity(company_text, "ORG")
    name1_words, name1_tags = tag_entity(name1_text, "PER")
    name2_words, name2_tags = tag_entity(name2_text, "PER")

    separator1 = random.choice(["-", ":"])
    separator2 = random.choice(["a", "&", "and", "+"])

    all_words = company_words + [separator1] + name1_words + [separator2] + name2_words
    all_tags = company_tags + [O] + name1_tags + [O] + name2_tags

    return " ".join(all_words), " ".join(all_tags)


def remix_company_with_suffix(names: List[str], companies: List[str], nicknames: List[str]) -> Tuple[str, str]:
    """Adds a business suffix like 's.r.o.' to a company or a person's name."""
    if not companies and not names:
        return "", ""
    
    # Decide whether to use a company name or a person's name as the base
    use_company = random.random() > 0.3
    
    if use_company and companies:
        base_text = random.choice(companies)
        base_words, base_tags = tag_entity(base_text, "ORG")
    elif names:
        base_text = random.choice(names)
        base_words, base_tags = tag_entity(base_text, "ORG") # Tag as ORG in this context
    else: # Fallback
        return "", ""

    suffix = random.choice(COMPANY_SUFFIXES)
    crippled_suffix = cripple_text(suffix)
    
    final_words = base_words + crippled_suffix.split()
    final_tags = base_tags + [I_ORG] * len(crippled_suffix.split())

    return " ".join(final_words), " ".join(final_tags)

def remix_email(names: List[str]) -> Tuple[str, str]:
    """Generates a fake email from a name and tags it as a nickname."""
    if not names:
        return "", ""
    
    all_email_domains = [
        # --- Czech Providers (Major) ---
        "seznam.cz",       # Most popular in Czech Republic
        "email.cz",        # Alias for Seznam.cz
        "post.cz",         # Alias for Centrum.cz
        "centrum.cz",      # Second most popular
        "atlas.cz",        # Merged with Centrum.cz
        
        # --- Czech Providers (ISP / Legacy) ---
        "volny.cz",        # A major legacy provider, originally from an ISP
        "tiscali.cz",      # Another popular legacy ISP provider
        "iol.cz",          # Legacy O2 (Internet OnLine)
        "o2.cz",           # O2 Czech Republic
        "upcmail.cz",      # Former UPC customers (now Vodafone)
        "t-email.cz",      # T-Mobile email service
        
        # --- Slovak Providers (Major) ---
        "zoznam.sk",       # Slovak equivalent of Seznam
        "azet.sk",         # Very popular Slovak portal and email service
        "centrum.sk",      # Slovak version of Centrum
        "pobox.sk",        # Common Slovak email provider
        "atlas.sk",        # Slovak version of Atlas
        "post.sk",         # A Slovak mail service
        "szm.sk",          # Another domain from Zoznam.sk
        
        # --- Slovak Providers (ISP / Legacy) ---
        "orangemail.sk",   # Orange Slovensko
        "t-com.sk",        # Slovak Telekom
        "stonline.sk",     # Legacy Slovak Telekom
        "nextra.sk",       # Legacy ISP
        
        # --- International Providers (Major Global) ---
        "gmail.com",       # Google
        "googlemail.com",  # Google's alternative domain, common in UK/Germany
        "outlook.com",     # Microsoft
        "hotmail.com",     # Microsoft (Legacy)
        "live.com",        # Microsoft (Legacy)
        "msn.com",         # Microsoft (Legacy)
        "yahoo.com",       # Yahoo
        "yahoo.co.uk",     # Yahoo UK
        "ymail.com",       # Yahoo alternative domain
        "rocketmail.com",  # Yahoo alternative domain (Legacy)
        "icloud.com",      # Apple
        "me.com",          # Apple (Legacy)
        "mac.com",         # Apple (Legacy)
        "aol.com",         # AOL (still very common in the US)
        "zoho.com",        # Popular business & personal email
        "mail.com",        # A popular generic email service
        "gmx.com",         # Popular in Europe, especially Germany
        "gmx.net",         # Alternative GMX domain
        "yandex.com",      # Yandex (popular in Eastern Europe)
        "yandex.ru",       # Yandex (Russian domain)
        
        # --- International Providers (Privacy-Focused) ---
        "proton.me",       # Proton Mail's current primary domain
        "protonmail.com",  # Proton Mail (Legacy)
        "pm.me",           # Proton Mail's short domain
        "tutanota.com",    # Tutanota (encrypted email)
        "tuta.com",        # Tutanota's new primary domain
        "keemail.me",      # Tutanota's alternative domain
        "fastmail.com",    # Very popular, reliable paid email service
        "mailbox.org",     # German privacy-focused provider
        "posteo.de",       # German privacy-focused provider (paid)
        "skiff.com",       # End-to-end encrypted email service
        
        # --- International Providers (Regional / Country-Specific) ---
        "web.de",          # Germany (very popular)
        "gmx.de",          # Germany (very popular)
        "t-online.de",     # Germany (Deutsche Telekom)
        "freenet.de",      # Germany
        "laposte.net",     # France (national post service)
        "orange.fr",       # France (Orange ISP)
        "sfr.fr",          # France (SFR ISP)
        "free.fr",         # France (Free ISP)
        "wp.pl",           # Poland (Wirtualna Polska)
        "o2.pl",           # Poland
        "onet.pl",         # Poland
        "interia.pl",      # Poland
        "mail.ru",         # Russia (VK Group)
        "rambler.ru",      # Russia
        "libero.it",       # Italy
        "virgilio.it",     # Italy
        "terra.com.br",    # Brazil
        "uol.com.br",      # Brazil
        "bol.com.br",      # Brazil
        "rediffmail.com",  # India
        "qq.com",          # China (Tencent)
        "163.com",         # China (NetEase)
        "126.com",         # China (NetEase)
        "sina.com",        # China
        "naver.com",       # South Korea
        "hanmail.net",     # South Korea (now Daum)
        "daum.net",        # South Korea
        "btinternet.com",  # UK (British Telecom)
        "talktalk.net",    # UK
        "sky.com",         # UK
        "comcast.net",     # USA (Comcast/Xfinity ISP)
        "verizon.net",     # USA (Verizon ISP)
        "att.net",         # USA (AT&T ISP)
        "sbcglobal.net",   # USA (Legacy AT&T)
    ]

    name_text = random.choice(names).lower().replace(" ", ".")
    domain = random.choice(all_email_domains)
    email = f"{name_text}@{domain}"
    
    # The whole email is a single token for our purposes, tagged as a nickname
    return email, B_NICK

def remix_name_and_location(names: List[str], cities: List[str]) -> Tuple[str, str]:
    """Creates a sample like 'Jan Novák, Praha' in various permutations."""
    if not names or not cities:
        return "", ""
    
    name_text = random.choice(names)
    location_text = random.choice(cities)

    name_words, name_tags = tag_entity(name_text, "PER")
    location_words, location_tags = tag_entity(location_text, "LOC")
    
    pattern = random.choice([
        "name_sep_city", "city_sep_name", "name_city", "name_prep_city"
    ])
    
    final_words, final_tags = [], []

    if pattern == "name_sep_city":
        separator = random.choice([",", " - "])
        final_words = name_words + separator.split() + location_words
        final_tags = name_tags + [O] * len(separator.split()) + location_tags
    elif pattern == "city_sep_name":
        separator = random.choice([",", " - "])
        final_words = location_words + separator.split() + name_words
        final_tags = location_tags + [O] * len(separator.split()) + name_words
    elif pattern == "name_city":
        final_words = name_words + location_words
        final_tags = name_tags + location_tags
    elif pattern == "name_prep_city":
        # Handle Czech prepositions "v" and "ve"
        preposition = "v"
        # Use "ve" for words starting with v, f, or specific consonant clusters
        if location_words[0].lower().startswith(('v', 'f', 's', 'z', 'p', 'b', 'm')):
             if len(location_words[0]) > 1 and location_words[0][1].lower() in "sztk":
                 preposition = "ve"
        
        final_words = name_words + [preposition] + location_words
        final_tags = name_tags + [O] + location_tags

    return " ".join(final_words), " ".join(final_tags)

def remix_single_city(cities: List[str]) -> Tuple[str, str]:
    """Creates a sample with a single city name."""
    if not cities:
        return "", ""
    city_text = random.choice(cities)
    _, city_tags = tag_entity(city_text, "LOC")
    return city_text, " ".join(city_tags)


def remix_gibberish() -> Tuple[str, str]:
    """Generates a random string of characters."""
    length = random.randint(5, 15)
    chars = "abcdefghijklmnopqrstuvwxyz0123456789_-"
    text = "".join(random.choice(chars) for _ in range(length))
    return text, B_NICK


def remix_two_names(names: List[str]) -> Tuple[str, str]:
    """Creates a sample with two names, e.g., 'John Doe and Jane Smith'."""
    if len(names) < 2:
        return "", ""
    
    name1_text, name2_text = random.sample(names, 2)
    name1_words, name1_tags = tag_entity(name1_text, "PER")
    name2_words, name2_tags = tag_entity(name2_text, "PER")
    
    separator = random.choice(["a", "and", "&", ","])
    
    all_words = name1_words + [separator] + name2_words
    all_tags = name1_tags + [O] + name2_tags
    
    return " ".join(all_words), " ".join(all_tags)

def remix_name_and_company(names: List[str], companies: List[str]) -> Tuple[str, str]:
    """Creates samples like 'John Doe, Acme Inc.' or 'John Doe @ Acme Inc.'"""
    if not names or not companies:
        return "", ""
        
    name_text = random.choice(names)
    company_text = random.choice(companies)
    
    name_words, name_tags = tag_entity(name_text, "PER")
    company_words, company_tags = tag_entity(company_text, "ORG")
    
    # Pattern 1: Name, Company
    if random.random() > 0.5:
        separator = random.choice([",", "from", "at", "@"])
        all_words = name_words + [separator] + company_words
        all_tags = name_tags + [O] + company_tags
    # Pattern 2: Company (Name)
    else:
        all_words = company_words + ["("] + name_words + [")"]
        all_tags = company_tags + [O] + name_tags + [O]

    return " ".join(all_words), " ".join(all_tags)


def main():
    parser = argparse.ArgumentParser(description="Synthesize an NER dataset from clean entity files.")
    parser.add_argument("--names-file", default=os.path.join(SCRIPT_DIR, "data", "classes", "names.txt"))
    parser.add_argument("--companies-file", default=os.path.join(SCRIPT_DIR, "data", "classes", "companies.txt"))
    parser.add_argument("--nicknames-file", default=os.path.join(SCRIPT_DIR, "data", "classes", "nicknames.txt"))
    parser.add_argument("--cities-file", default=os.path.join(SCRIPT_DIR, "data", "classes", "cities.txt"))
    parser.add_argument("--output-file", default=os.path.join(SCRIPT_DIR, "data", "dataset.csv"))
    parser.add_argument("--num-samples", type=int, default=100_000_000, help="Total number of samples to generate.")
    parser.add_argument("--chunk-size", type=int, default=500_000, help="Number of samples to generate and shuffle in memory at a time before writing to disk.")
    parser.add_argument("--seed", type=int, default=42, help="Random seed for reproducibility.")
    args = parser.parse_args()

    random.seed(args.seed)

    print("Loading source entity files...")
    names = load_entities(args.names_file)
    companies = load_entities(args.companies_file)
    nicknames = load_entities(args.nicknames_file)
    cities = load_entities(args.cities_file)

    # --- Noise Injection: Probabilistically strip suffixes from companies ---
    if companies:
        print("Injecting noise: Probabilistically stripping suffixes from company names...")
        processed_companies = []
        # Sort by length, longest first, to match "spol. s r.o." before "s.r.o."
        sorted_suffixes = sorted(COMPANY_SUFFIXES, key=len, reverse=True)
        
        stripped_count = 0
        for company in companies:
            # With a 40% probability, try to strip a suffix
            if random.random() < 0.4:
                original_company = company
                company_lower = company.lower()
                
                for suffix in sorted_suffixes:
                    suffix_lower = suffix.lower()
                    # Check for suffix as a whole word at the end, possibly preceded by a comma
                    if company_lower.endswith(f" {suffix_lower}") or company_lower.endswith(f",{suffix_lower}"):
                        # Find the start of the suffix in the original string to slice correctly
                        suffix_start_index = company_lower.rfind(suffix_lower)
                        company = company[:suffix_start_index].strip(" ,")
                        break # Stop after the first (longest) match
                
                # If after stripping the company name is empty, revert to original
                if not company.strip():
                    company = original_company
                elif company != original_company:
                    stripped_count += 1

            processed_companies.append(company)
        
        companies = processed_companies # Replace the original list
        print(f"Stripped suffixes from {stripped_count:,} company names.")

    # --- Noise Injection: Introduce typos and remove diacritics ---
    if names:
        print("Injecting noise: Introducing typos/diacritic removal into names...")
        crippled_count = 0
        crippled_names = []
        for name in names:
            if random.random() < 0.3: # 30% chance to cripple a name
                crippled_name = cripple_entity(name)
                if crippled_name != name:
                    crippled_count += 1
                crippled_names.append(crippled_name)
            else:
                crippled_names.append(name)
        names = crippled_names
        print(f"Applied noise to {crippled_count:,} names.")

    if companies:
        print("Injecting noise: Introducing typos/diacritic removal into companies...")
        crippled_count = 0
        crippled_companies = []
        for company in companies:
            if random.random() < 0.3: # 30% chance to cripple a company
                crippled_company = cripple_entity(company)
                if crippled_company != company:
                    crippled_count += 1
                crippled_companies.append(crippled_company)
            else:
                crippled_companies.append(company)
        companies = crippled_companies
        print(f"Applied noise to {crippled_count:,} company names.")

    # --- Prefix Stripping: Remove common prefixes like 't.j.' ---
    if names:
        processed_names = []
        stripped_count = 0
        for name in names:
            if name.lower().startswith("t.j. "):
                new_name = name[4:].strip()
                if new_name:
                    processed_names.append(new_name)
                    stripped_count += 1
            else:
                processed_names.append(name)
        if stripped_count > 0:
            names = processed_names
            print(f"Stripped 't.j.' prefix from {stripped_count:,} names.")
            
    if companies:
        processed_companies = []
        stripped_count = 0
        for company in companies:
            if company.lower().startswith("t.j. "):
                new_company = company[4:].strip()
                if new_company:
                    processed_companies.append(new_company)
                    stripped_count += 1
            else:
                processed_companies.append(company)
        if stripped_count > 0:
            companies = processed_companies
            print(f"Stripped 't.j.' prefix from {stripped_count:,} company names.")


    remix_strategies = [
        # Foundational strategies
        {"func": remix_single_entity, "weight": 0.30},
        {"func": remix_two_names, "weight": 0.08},
        {"func": remix_name_and_company, "weight": 0.10}, # Kept for its unique patterns
        
        # Czech-specific and common variations
        {"func": remix_name_with_title, "weight": 0.08},
        {"func": remix_name_with_nickname, "weight": 0.05},
        {"func": remix_company_with_suffix, "weight": 0.08}, # Kept for person -> org pattern
        {"func": remix_email, "weight": 0.05},
        {"func": remix_name_and_location, "weight": 0.05},
        {"func": remix_gibberish, "weight": 0.03},

        # NEW complex patterns requested by user
        {"func": remix_name_company_patterns, "weight": 0.15},
        {"func": remix_company_with_multiple_names, "weight": 0.05},
        {"func": remix_single_city, "weight": 0.03},
    ]
    
    # --- Dynamic Strategy Filtering and Argument Handling ---

    # Define required data for each strategy
    strategy_reqs = {
        remix_single_entity: ["names", "companies", "nicknames"],
        remix_two_names: ["names"],
        remix_name_and_company: ["names", "companies"],
        remix_name_with_title: ["names"],
        remix_name_with_nickname: ["names", "nicknames"],
        remix_company_with_suffix: ["names", "companies", "nicknames"],
        remix_email: ["names"],
        remix_name_and_location: ["names", "cities"],
        remix_gibberish: [],
        remix_name_company_patterns: ["names", "companies"],
        remix_company_with_multiple_names: ["names", "companies"],
        remix_single_city: ["cities"],
    }
    
    available_data = { "names": names, "companies": companies, "nicknames": nicknames, "cities": cities }

    # Filter out strategies that cannot be run with the available data
    initial_strategies = remix_strategies
    remix_strategies = []
    for s in initial_strategies:
        func = s["func"]
        
        # Handle special conditions first
        if func == remix_two_names and len(names) < 2: continue
        if func == remix_company_with_multiple_names and (len(names) < 2 or not companies): continue
        if func == remix_name_with_nickname and (not names or not nicknames): continue
        if func == remix_name_and_location and (not names or not cities): continue
        if func == remix_single_city and not cities: continue

        # General check for required data files
        can_run = all(available_data.get(req) for req in strategy_reqs.get(func, []))
        if can_run:
            remix_strategies.append(s)

    # Re-normalize weights so they sum to 1 after filtering
    total_weight = sum(s["weight"] for s in remix_strategies)
    if total_weight > 0:
        for s in remix_strategies:
            s["weight"] /= total_weight
    
    population = [s["func"] for s in remix_strategies]
    weights = [s["weight"] for s in remix_strategies]
    
    if not population:
        print("Error: No remix strategies can be run with the available data. Check source files.")
        return

    # --- Scalable Generation Loop ---
    # Write header first, then append in chunks
    header_df = pd.DataFrame(columns=["text", "tags"])
    header_df.to_csv(args.output_file, index=False)
    
    buffer = []
    total_generated = 0
    
    print(f"Generating {args.num_samples:,} samples in chunks of {args.chunk_size:,}...")

    for i in range(1, args.num_samples + 1):
        strategy = random.choices(population, weights=weights, k=1)[0]
        
        # Build arguments for the chosen strategy dynamically
        required_args = strategy_reqs.get(strategy, [])
        func_args = {arg: available_data[arg] for arg in required_args}
        
        text, tags = strategy(**func_args)
        
        if text and tags:
            # --- Final Normalization: Convert to lowercase ---
            text = text.lower()

            buffer.append({"text": text, "tags": tags})
            
        # Write chunk to disk when buffer is full or at the very end
        if len(buffer) >= args.chunk_size or (i == args.num_samples and buffer):
            total_generated += len(buffer)
            print(f"Shuffling and writing chunk of {len(buffer):,} samples... (Total: {total_generated:,}/{args.num_samples:,})")
            
            random.shuffle(buffer)
            chunk_df = pd.DataFrame(buffer)
            chunk_df.to_csv(args.output_file, mode='a', header=False, index=False)
            
            buffer = [] # Reset buffer

    print(f"Successfully saved {total_generated:,} NER dataset samples to {args.output_file}")


if __name__ == "__main__":
    main()
