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

    # FIXED: Use realistic patterns with separators, not unrealistic concatenations
    pattern = random.choice([
        "entity_space_entity", "entity_with_separator", "internal_caps"
    ])

    entity_type1 = random.choice(available_entity_types)
    entity1 = get_single_word_entity(entity_map[entity_type1])

    if not entity1:
        return "", ""

    text, tag = "", ""

    if pattern == "entity_space_entity":
        # Two entities with a space separator (realistic)
        entity_type2 = random.choice(available_entity_types)
        entity2 = get_single_word_entity(entity_map[entity_type2])
        if not entity2: return "", ""
        
        text = f"{entity1} {entity2}"
        tag = f"B-{entity_type1} B-{entity_type2}"

    elif pattern == "entity_with_separator":
        # Entity with separator (hyphen, comma, period) - realistic
        separator = random.choice(["-", ",", "."])
        word = random.choice(["cz", "com", "praha", "brno", "sk"])
        if random.random() < 0.5:
            text = f"{entity1}{separator} {word}"
            tag = f"B-{entity_type1} O"
        else:
            text = f"{word} {separator}{entity1}"
            tag = f"O B-{entity_type1}"

    elif pattern == "internal_caps":
        # Internal capitals (e.g., iPhone, eBay)
        if len(entity1) > 2:
            idx = random.randint(1, len(entity1) - 1)
            text = entity1[:idx] + entity1[idx].upper() + entity1[idx+1:]
        else:
            text = entity1.upper()
        tag = f"B-{entity_type1}"
    
    if not text:
        return "", ""

    return text, tag
