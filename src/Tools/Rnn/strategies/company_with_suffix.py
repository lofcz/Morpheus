"""
Company with suffix strategy - adds business suffixes like s.r.o. to companies or names.
"""
import random
from typing import List, Tuple
from .utils import tag_entity, cripple_text, COMPANY_SUFFIXES, I_ORG


def remix_company_with_suffix(names: List[str], companies: List[str]) -> Tuple[str, str]:
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
        # FIX: Keep name as PER, not ORG
        base_words, base_tags = tag_entity(base_text, "PER")
    else: # Fallback
        return "", ""

    suffix = random.choice(COMPANY_SUFFIXES)
    # Cripple the suffix to generate variations (e.g., "s.r.o." -> "sro")
    crippled_suffix = cripple_text(suffix)
    
    # Build final words and tags
    final_words = base_words + crippled_suffix.split()
    
    if use_company:
        # Company base: entire thing is ORG
        final_tags = base_tags + [I_ORG] * len(crippled_suffix.split())
    else:
        # Name base: name stays PER, suffix becomes separate ORG entity
        suffix_parts = crippled_suffix.split()
        if suffix_parts:
            final_tags = base_tags + ["B-ORG"] + ["I-ORG"] * (len(suffix_parts) - 1)
        else:
            final_tags = base_tags

    return " ".join(final_words), " ".join(final_tags)
