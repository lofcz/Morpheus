"""
Name and location strategy - combines person names with location names in various patterns.
"""
import random
from typing import List, Tuple
from .utils import tag_entity, O


def remix_name_and_location(names: List[str], cities: List[str]) -> Tuple[str, str]:
    """Creates a sample like 'Jan Novák, Praha' in various permutations with random spacing."""
    if not names or not cities:
        return "", ""
    
    name_text = random.choice(names)
    location_text = random.choice(cities)

    name_words, name_tags = tag_entity(name_text, "PER")
    location_words, location_tags = tag_entity(location_text, "LOC")
    
    pattern = random.choice([
        "name_sep_city", "city_sep_name", "name_city", "name_prep_city"
    ])
    
    # Random spacing mode: 0 = no spaces, 1 = left space, 2 = right space, 3 = both spaces
    spacing_mode = random.choice([0, 1, 2, 3])
    
    final_words, final_tags = [], []

    if pattern == "name_sep_city":
        separator = random.choice([",", "-", "|", "/"])
        final_words = name_words + [separator] + location_words
        final_tags = name_tags + [O] + location_tags
    elif pattern == "city_sep_name":
        separator = random.choice([",", "-", "|", "/"])
        final_words = location_words + [separator] + name_words
        final_tags = location_tags + [O] + name_tags
    elif pattern == "name_city":
        final_words = name_words + location_words
        final_tags = name_tags + location_tags
    elif pattern == "name_prep_city":
        # Handle Czech prepositions "v" and "ve"
        preposition = "v"
        # Use "ve" for words starting with v, f, or specific consonant clusters
        if location_words[0].lower().startswith(('v', 'f', 's', 'z', 'p', 'b', 'm')):
             if len(location_words[0]) > 1 and location_words[0][1].lower() in "sztk":
                 preposition = "ve"
        
        final_words = name_words + [preposition] + location_words
        final_tags = name_tags + [O] + location_tags

    # Apply spacing: 25% no spaces, 75% normal spaces
    if spacing_mode == 0:
        # No spaces: concatenate everything
        text = "".join(final_words)
        # When concatenating, produce a single representative tag
        final_tag = final_tags[0] if final_tags else O
        return text, final_tag
    else:
        # Normal spacing everywhere
        return " ".join(final_words), " ".join(final_tags)
