"""
Two names strategy - generates samples with two person names connected by various separators.
"""
import random
from typing import List, Tuple
from .utils import tag_entity, O


def remix_two_names(names: List[str]) -> Tuple[str, str]:
    """Creates a sample with two names, e.g., 'John Doe and Jane Smith'."""
    if len(names) < 2:
        return "", ""
    
    name1_text, name2_text = random.sample(names, 2)
    
    # Sometimes shuffle name order (surname first)
    def maybe_shuffle_name(name: str) -> str:
        if random.random() < 0.3 and len(name.split()) >= 2:
            parts = name.split()
            # Reverse: surname first
            return " ".join(reversed(parts))
        return name
    
    name1_text = maybe_shuffle_name(name1_text)
    name2_text = maybe_shuffle_name(name2_text)
    
    name1_words, name1_tags = tag_entity(name1_text, "PER")
    name2_words, name2_tags = tag_entity(name2_text, "PER")
    
    # More variety in separators, avoid repetitive ", " pattern
    separator = random.choice(["a", "and", "&", "+", "/", "|", "-", ","])
    
    # Sometimes concatenate separator without spaces (only for clear separators)
    if separator in ["/", "|", "-", "&"] and random.random() < 0.5:
        # No spaces around separator - merge with adjacent words
        # Only for visual separators that clearly delimit boundaries
        all_words = name1_words[:-1] + [name1_words[-1] + separator + name2_words[0]] + name2_words[1:]
        all_tags = name1_tags + name2_tags[1:]
    else:
        # Normal spacing with separator
        all_words = name1_words + [separator] + name2_words
        all_tags = name1_tags + [O] + name2_tags
    
    return " ".join(all_words), " ".join(all_tags)
