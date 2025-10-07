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
        
        # Add explicit surname pattern support (40% chance to use only surname)
        if random.random() < 0.4:
            parts = text.split()
            if len(parts) >= 2:
                text = parts[-1]  # Last word = surname
        # 30% chance to use surname-first format
        elif random.random() < 0.3:
            parts = text.split()
            if len(parts) >= 2:
                text = " ".join(reversed(parts))
                
    elif entity_type == "company" and companies:
        text = random.choice(companies)
        base_tag = "ORG"
    elif entity_type == "nickname" and nicknames:
        text = random.choice(nicknames)
        base_tag = "NICK"
    
    if not text:
        return "", ""

    # 25% chance to apply ad-hoc crippling
    # BUT: Don't cripple nicknames with numbers/underscores (gaming handles)
    should_cripple = random.random() < 0.25
    if should_cripple and entity_type == "nickname":
        # Skip crippling for gaming handles (contain digits or underscores)
        if any(c.isdigit() or c in '_-' for c in text):
            should_cripple = False
    
    if should_cripple:
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
