"""
Hyphenated name strategy - creates hyphenated first-first or surname-surname patterns.
"""
import random
from typing import List, Tuple
from .utils import tag_entity


def remix_hyphenated_name(names: List[str]) -> Tuple[str, str]:
    """Create hyphenated given-given or surname-surname strings."""
    if not names:
        return "", ""

    # Try a few times to find a multi-word name to avoid filtering the whole list
    for _ in range(10):
        name = random.choice(names)
        parts = name.split()
        if len(parts) >= 2:
            # Create a hyphenated name from the first two parts
            text = f"{parts[0]}-{parts[1]}"
            words, tags = tag_entity(text, "PER")
            return " ".join(words), " ".join(tags)
    
    # Fallback if no multi-word name was found after 10 tries
    return "", ""
