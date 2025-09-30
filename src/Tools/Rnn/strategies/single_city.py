"""
Single city strategy - generates samples with single city names.
"""
import random
from typing import List, Tuple
from .utils import tag_entity


def remix_single_city(cities: List[str]) -> Tuple[str, str]:
    """Creates a sample with a single city name."""
    if not cities:
        return "", ""
    city_text = random.choice(cities)
    _, city_tags = tag_entity(city_text, "LOC")
    return city_text, " ".join(city_tags)
