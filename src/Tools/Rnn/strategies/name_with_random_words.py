"""
Name with random words strategy - adds random O-tagged words around/between name parts.
"""
import random
from typing import List, Tuple
from .utils import tag_entity, O


def remix_name_with_random_words(names: List[str], o_phrase_words: List[str]) -> Tuple[str, str]:
    """
    Adds random O words around/between name parts.
    10% chance to trigger, then 30% chance to keep adding more words iteratively.
    Supports multi-word names with various patterns (surname first, multiple names, etc.)
    """
    if not names or not o_phrase_words:
        return "", ""
    
    # Pick a name and potentially create variations
    name_text = random.choice(names)
    name_parts = name_text.split()
    
    # Generate different name patterns (similar to remix_single_entity)
    pattern_choice = random.random()
    if len(name_parts) == 1:
        # Single word name - use as is
        final_name = name_text
    elif pattern_choice < 0.25:
        # Surname only (last word)
        final_name = name_parts[-1]
    elif pattern_choice < 0.50:
        # First name only (first word)
        final_name = name_parts[0]
    elif pattern_choice < 0.65:
        # Surname first (reversed)
        final_name = " ".join(reversed(name_parts))
    elif pattern_choice < 0.80 and len(name_parts) >= 2:
        # Two first names (if we have at least 2 parts, use first two)
        final_name = " ".join(name_parts[:2])
    else:
        # Full name as-is
        final_name = name_text
    
    # Tag the name
    name_words, name_tags = tag_entity(final_name, "PER")
    
    final_words = list(name_words)
    final_tags = list(name_tags)
    
    # Keep adding random O words with 30% chance each iteration
    while random.random() < 0.3:
        # Ensure random_word is a string
        random_word = str(random.choice(o_phrase_words))
        position = random.choice(["before", "after", "between"])
        
        if position == "before":
            # Add word at the beginning
            final_words.insert(0, random_word)
            final_tags.insert(0, O)
        elif position == "after":
            # Add word at the end
            final_words.append(random_word)
            final_tags.append(O)
        elif position == "between" and len(final_words) > 1:
            # Add word between name parts
            insert_pos = random.randint(1, len(final_words) - 1)
            final_words.insert(insert_pos, random_word)
            final_tags.insert(insert_pos, O)
    
    return " ".join(final_words), " ".join(final_tags)
