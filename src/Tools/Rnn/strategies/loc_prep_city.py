"""
Location with preposition strategy - generates city names with Czech prepositions.
"""
import random
from typing import List, Tuple
from .utils import tag_entity, O


def remix_loc_prep_city(cities: List[str]) -> Tuple[str, str]:
    """Produce prepositional LOC patterns to reduce bare-LOC bias."""
    if not cities:
        return "", ""
    city = random.choice(cities)
    prep = random.choice(["v", "ve", "do", "z"])
    words = prep.split() + city.split()
    tags = [O] + tag_entity(city, "LOC")[1]
    return " ".join(words), " ".join(tags)
