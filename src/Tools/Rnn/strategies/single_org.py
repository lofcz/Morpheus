"""
Single organization strategy - generates samples with a single company name.
"""
import random
from typing import List, Tuple
from .utils import tag_entity

def remix_single_org(companies: List[str]) -> Tuple[str, str]:
    """Creates a sample with a single company name, often without suffixes."""
    if not companies:
        return "", ""
    
    company_text = random.choice(companies)
    
    _, company_tags = tag_entity(company_text, "ORG")
    
    return company_text, " ".join(company_tags)
