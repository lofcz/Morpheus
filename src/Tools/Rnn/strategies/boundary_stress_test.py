"""
Generates examples to stress-test entity boundary detection.
"""
import random
from typing import List, Tuple
from .utils import tag_entity, O

def get_single_word_entity(entities: List[str]) -> str:
    """Utility to get a random single-word entity from a list."""
    if not entities:
        return ""
    # Pick a random entity and return its first word.
    return random.choice(entities).split()[0]

def remix_boundary_stress_test(names: List[str], companies: List[str], cities: List[str]) -> Tuple[str, str]:
    """
    Creates challenging examples for boundary detection by concatenating
    single-word entities and other tokens.
    """
    if not names and not companies and not cities:
        return "", ""

    entity_map = {"PER": names, "ORG": companies, "LOC": cities}
    available_entity_types = [t for t, elist in entity_map.items() if elist]
    
    if not available_entity_types:
        return "", ""

    pattern = random.choice([
        "entity_entity", "entity_word", "word_entity", "entity_punct", "punct_entity", "internal_caps"
    ])

    entity_type1 = random.choice(available_entity_types)
    entity1 = get_single_word_entity(entity_map[entity_type1])

    if not entity1:
        return "", ""

    text, tag = "", ""

    if pattern == "entity_entity":
        entity_type2 = random.choice(available_entity_types)
        entity2 = get_single_word_entity(entity_map[entity_type2])
        if not entity2: return "", ""
        
        text = entity1 + entity2
        tag = f"B-{entity_type1}"

    elif pattern in ["entity_word", "word_entity"]:
        word = random.choice(["cz", "com", "net", "org", "praha", "brno"])
        if pattern == "entity_word":
            text = entity1 + word
        else:
            text = word + entity1
        tag = f"B-{entity_type1}"

    elif pattern in ["entity_punct", "punct_entity"]:
        punct = random.choice([".", ",", "-", "_", ":"])
        if pattern == "entity_punct":
            text = entity1 + punct
        else:
            text = punct + entity1
        tag = f"B-{entity_type1}"

    elif pattern == "internal_caps":
        if len(entity1) > 2:
            idx = random.randint(1, len(entity1) - 1)
            text = entity1[:idx] + entity1[idx].upper() + entity1[idx+1:]
        else:
            text = entity1.upper()
        tag = f"B-{entity_type1}"
    
    if not text:
        return "", ""

    return text, tag
