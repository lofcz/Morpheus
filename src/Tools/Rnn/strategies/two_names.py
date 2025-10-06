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
    
    name1_words = name1_text.split()
    name2_words = name2_text.split()
    
    # 30% chance to create a single hyphenated token from the first word of each name
    if random.random() < 0.3 and name1_words and name2_words:
        text = f"{name1_words[0]}-{name2_words[0]}"
        words, tags = tag_entity(text, "PER")
        return " ".join(words), " ".join(tags)

    # Default case: two names separated by a spaced separator
    name1_words, name1_tags = tag_entity(name1_text, "PER")
    name2_words, name2_tags = tag_entity(name2_text, "PER")
    
    separator = random.choice(["a", "and", "&", "+", ",", "i"])
    
    all_words = name1_words + [separator] + name2_words
    all_tags = name1_tags + [O] + name2_tags
    
    return " ".join(all_words), " ".join(all_tags)
