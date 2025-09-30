"""
Name-orgtype-company strategy - combines person names with organization types and companies.
"""
import random
from typing import List, Tuple
from .utils import tag_entity, O


def remix_name_orgtype_company(names: List[str], companies: List[str], org_types: List[str]) -> Tuple[str, str]:
    """
    Creates patterns like:
    - Zbyněk Tkadlčík, fa Albatros
    - Zbyněk Tkadlčík firma Albatros  
    - Zbyněk Tkadlčík společnost Albatros
    """
    if not names or not companies or not org_types:
        return "", ""
    
    name_text = random.choice(names)
    company_text = random.choice(companies)
    org_type = random.choice(org_types)
    
    # Sometimes shuffle name order (surname first)
    if random.random() < 0.3 and len(name_text.split()) >= 2:
        parts = name_text.split()
        name_text = " ".join(reversed(parts))
    
    name_words, name_tags = tag_entity(name_text, "PER")
    company_words, company_tags = tag_entity(company_text, "ORG")
    org_type_words = str(org_type).split()
    
    # Decide pattern
    if random.random() < 0.5:
        # Name, orgtype Company  (e.g., "Novák, fa Albatros")
        all_words = name_words + [","] + org_type_words + company_words
        all_tags = name_tags + [O] + [O] * len(org_type_words) + company_tags
    else:
        # Name orgtype Company (e.g., "Novák firma Albatros")
        all_words = name_words + org_type_words + company_words
        all_tags = name_tags + [O] * len(org_type_words) + company_tags
    
    return " ".join(all_words), " ".join(all_tags)
