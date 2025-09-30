"""
Single person name strategy - generates single-word person names.
"""
import random
from typing import List, Tuple
from .utils import tag_entity


def remix_single_per(single_names: List[str]) -> Tuple[str, str]:
    """Single-word PER to strengthen single token people names."""
    if not single_names:
        return "", ""
    w = random.choice(single_names)
    words, tags = tag_entity(w, "PER")
    return " ".join(words), " ".join(tags)
