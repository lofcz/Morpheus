"""
Generates examples of two different entities adjacent to each other with complex separators.
"""
import random
from typing import List, Tuple
from .utils import tag_entity, O

def remix_adjacent_entities(names: List[str], companies: List[str], cities: List[str]) -> Tuple[str, str]:
    """
    Creates samples with two different entities next to each other,
    separated by complex patterns like parentheses or commas.
    e.g., "[Jan Novák] (společnost [Microsoft])"
    """
    if not (names and companies and cities):
        return "", ""

    entity_map = {"PER": names, "ORG": companies, "LOC": cities}
    available_entity_types = [t for t, elist in entity_map.items() if elist]
    
    if len(available_entity_types) < 2:
        return "", ""

    # Choose two different entity types
    entity_type1, entity_type2 = random.sample(available_entity_types, 2)
    
    entity1 = random.choice(entity_map[entity_type1])
    entity2 = random.choice(entity_map[entity_type2])

    words1, tags1 = tag_entity(entity1, entity_type1)
    words2, tags2 = tag_entity(entity2, entity_type2)

    # Choose a random separator pattern
    separator_pattern = random.choice([
        ["("] + words2 + [")"], # (Entity2)
        [",", "firma"] + words2, # , firma Entity2
        ["-", "pobočka"] + words2 # - pobočka Entity2
    ])
    
    separator_tags = [O] * len(separator_pattern)
    
    final_words = words1 + separator_pattern
    final_tags = tags1 + separator_tags
        
    return " ".join(final_words), " ".join(final_tags)
