"""
Hyphenated name strategy - creates hyphenated first-first or surname-surname patterns.
"""
import random
from typing import List, Tuple
from .utils import tag_entity


def remix_hyphenated_name(names: List[str]) -> Tuple[str, str]:
    """Create hyphenated given-given or surname-surname strings."""
    if len(names) < 2:
        return "", ""
    left = random.choice(names).split()
    right = random.choice(names).split()
    if not left or not right:
        return "", ""
    # take first token of each
    text = f"{left[0]}-{right[0]}"
    words, tags = tag_entity(text, "PER")
    return " ".join(words), " ".join(tags)
