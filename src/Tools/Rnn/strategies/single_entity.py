"""
Single entity strategy - generates samples with a single name, company, or nickname.
"""
import random
from typing import List, Tuple
from .utils import tag_entity, cripple_entity, cripple_text


def remix_single_entity(names: List[str], companies: List[str], nicknames: List[str]) -> Tuple[str, str]:
    """Creates a sample with a single entity, with a chance of ad-hoc corruption."""
    entity_type = random.choice(["name", "company", "nickname"])
    
    text = ""
    base_tag = ""

    if entity_type == "name" and names:
        text = random.choice(names)
        base_tag = "PER"
    elif entity_type == "company" and companies:
        text = random.choice(companies)
        base_tag = "ORG"
    elif entity_type == "nickname" and nicknames:
        text = random.choice(nicknames)
        base_tag = "NICK"
    
    if not text:
        return "", ""

    # 25% chance to apply ad-hoc crippling
    if random.random() < 0.25:
        if entity_type == "company":
            # Apply general typos and also specific dot/space corruption for companies
            text = cripple_entity(text)
            text = cripple_text(text)
        else:
            # Apply general typos for names and nicknames
            text = cripple_entity(text)
            
    if not text.strip():
        return "", ""

    _, tags = tag_entity(text, base_tag)
    tag_str = " ".join(tags)
        
    return text, tag_str
