"""
Name-company patterns strategy - generates varied name-company combinations with separators.
"""
import random
from typing import List, Tuple
from .utils import tag_entity, cripple_text, COMPANY_SUFFIXES, O, I_ORG


def remix_name_company_patterns(names: List[str], companies: List[str]) -> Tuple[str, str]:
    """
    Generates patterns like "Name - Company", "Company, Name", etc.
    Optionally adds a (crippled) business suffix to the company.
    """
    if not names or not companies:
        return "", ""

    name_text = random.choice(names)
    company_text = random.choice(companies)
    
    # Sometimes shuffle name order (surname first)
    if random.random() < 0.3 and len(name_text.split()) >= 2:
        parts = name_text.split()
        name_text = " ".join(reversed(parts))

    name_words, name_tags = tag_entity(name_text, "PER")
    company_words, company_tags = tag_entity(company_text, "ORG")

    # Optionally add and cripple a suffix
    if random.random() < 0.5:
        suffix = random.choice(COMPANY_SUFFIXES)
        crippled_suffix = cripple_text(suffix)
        company_words.extend(crippled_suffix.split())
        company_tags.extend([I_ORG] * len(crippled_suffix.split()))

    pattern = random.choice([
        "name_sep_company", "company_sep_name"
    ])
    separator = random.choice(["-", "|", ":", "/", "&", ","])

    if pattern == "name_sep_company":
        all_words = name_words + [separator] + company_words
        all_tags = name_tags + [O] + company_tags
    else:  # company_sep_name
        all_words = company_words + [separator] + name_words
        all_tags = company_tags + [O] + name_tags

    return " ".join(all_words), " ".join(all_tags)
