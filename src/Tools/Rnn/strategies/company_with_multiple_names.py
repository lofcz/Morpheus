"""
Company with multiple names strategy - combines one company with multiple person names.
"""
import random
from typing import List, Tuple
from .utils import tag_entity, O


def remix_company_with_multiple_names(names: List[str], companies: List[str]) -> Tuple[str, str]:
    """Generates patterns like 'Company - Name1 and Name2'"""
    if len(names) < 2 or not companies:
        return "", ""
    
    company_text = random.choice(companies)
    name1_text, name2_text = random.sample(names, 2)
    
    # Sometimes shuffle name order (surname first)
    if random.random() < 0.3 and len(name1_text.split()) >= 2:
        parts = name1_text.split()
        name1_text = " ".join(reversed(parts))
    if random.random() < 0.3 and len(name2_text.split()) >= 2:
        parts = name2_text.split()
        name2_text = " ".join(reversed(parts))

    company_words, company_tags = tag_entity(company_text, "ORG")
    name1_words, name1_tags = tag_entity(name1_text, "PER")
    name2_words, name2_tags = tag_entity(name2_text, "PER")

    separator1 = random.choice(["-", ":", "|", "/"])
    separator2 = random.choice(["a", "&", "and", "+", "|", "/"])

    all_words = company_words + [separator1] + name1_words + [separator2] + name2_words
    all_tags = company_tags + [O] + name1_tags + [O] + name2_tags

    return " ".join(all_words), " ".join(all_tags)
