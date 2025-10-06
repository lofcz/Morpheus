"""
Entity in context strategy - wraps a single entity in a simple contextual phrase.
"""
import random
from typing import List, Tuple
from .utils import tag_entity, O

def remix_entity_in_context(names: List[str], companies: List[str], cities: List[str]) -> Tuple[str, str]:
    """
    Wraps a single entity (person, org, or location) in a simple context
    to teach the model about boundaries and non-entity words.
    e.g., "celé jméno je [Jan Novák]" or "pracuje pro [Microsoft]"
    """
    entity_type = random.choice(["name", "company", "city"])
    
    text, base_tag = "", ""
    if entity_type == "name" and names:
        text = random.choice(names)
        base_tag = "PER"
    elif entity_type == "company" and companies:
        text = random.choice(companies)
        base_tag = "ORG"
    elif entity_type == "city" and cities:
        text = random.choice(cities)
        base_tag = "LOC"
        
    if not text:
        return "", ""
        
    entity_words, entity_tags = tag_entity(text, base_tag)
    
    # Define more realistic, multi-word context patterns
    contexts = {
        "PER": [
            ("jméno je", "after"), ("celé jméno", "after"), ("uživatel", "after"),
            ("autor článku je", "after"), ("přihlásit jako", "after")
        ],
        "ORG": [
            ("pracuje pro", "after"), ("název firmy", "after"), ("společnost", "after"),
            ("organizace", "after"), ("brand", "after")
        ],
        "LOC": [
            ("město", "after"), ("adresa je v", "after"), ("narodil se v", "after"),
            ("pobočka v", "after"), ("bydliště", "after")
        ],
    }
    
    context_phrase, position = random.choice(contexts[base_tag])
    context_words = context_phrase.split()
    context_tags = [O] * len(context_words)
    
    if position == "after":
        final_words = context_words + entity_words
        final_tags = context_tags + entity_tags
    else: # before
        final_words = entity_words + context_words
        final_tags = entity_tags + context_tags
        
    return " ".join(final_words), " ".join(final_tags)
