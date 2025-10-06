"""
Name and company strategy - combines person names with company names in various patterns.
"""
import random
from typing import List, Tuple
from .utils import tag_entity, O


def remix_name_and_company(names: List[str], companies: List[str]) -> Tuple[str, str]:
    """Creates samples like 'John Doe, Acme Inc.' or 'John Doe @ Acme Inc.'"""
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
    
    # More diverse patterns
    pattern_type = random.random()
    if pattern_type < 0.4:
        # Name separator Company
        separator = random.choice(["from", "at", "@", "-", "|", ":", "/", ","])
        all_words = name_words + [separator] + company_words
        all_tags = name_tags + [O] + company_tags
    elif pattern_type < 0.7:
        # Company (Name)
        all_words = company_words + ["("] + name_words + [")"]
        all_tags = company_tags + [O] + name_tags + [O]
    else:
        # Company - Name (reversed order, different separator)
        separator = random.choice(["-", ":", "|", "/", ","])
        all_words = company_words + [separator] + name_words
        all_tags = company_tags + [O] + name_tags

    return " ".join(all_words), " ".join(all_tags)
