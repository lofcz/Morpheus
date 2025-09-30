"""
Name with nickname strategy - places nicknames before/after/within names with various delimiters.
"""
import random
from typing import List, Tuple
from .utils import tag_entity, O


def remix_name_with_nickname(names: List[str], nicknames: List[str]) -> Tuple[str, str]:
    """
    Creates samples with a nickname placed before, inside, or after a real name,
    using a wide variety of delimiters and realistic patterns.
    e.g., 'Matěj (lofcz) Štágl', '"lofcz" Matěj Štágl', 'Matěj Štágl - lofcz'
    """
    if not names or not nicknames:
        return "", ""
    
    name_text = random.choice(names)
    nickname_text = random.choice(nicknames)
    
    # Use simpler, single-word nicknames for cleaner patterns
    if len(nickname_text.split()) > 1:
        # Fallback to just the nickname if it's complex
        _, nick_tags = tag_entity(nickname_text, "NICK")
        return nickname_text, " ".join(nick_tags)

    name_words, name_tags = name_text.split(), tag_entity(name_text, "PER")[1]
    nick_words, nick_tags = nickname_text.split(), tag_entity(nickname_text, "NICK")[1]

    DELIMITER_PAIRS = [
        ('(', ')'), ('[', ']'), ('{', '}'), ('<', '>'),
        ("'", "'"), ('"', '"'), ('„', '"'), 
        ('`', '`'), ('-', '-'), ('_', '_'), ('/', '/'), ('|', '|'),
        ('', '') # No delimiters
    ]
    
    q_start, q_end = random.choice(DELIMITER_PAIRS)
    
    # 20% chance to create mismatched or one-sided delimiters
    if random.random() < 0.2:
        all_delimiters = ['"', "'", '„', '"', '`', '(', ')', '[', ']', '{', '}', '<', '>', '-', '_', '/', '|', '']
        # Randomly pick start and end independently (may mismatch or be one-sided)
        q_start = random.choice(all_delimiters)
        q_end = random.choice(all_delimiters)
    
    # More diverse placement strategies
    placement_options = ["before", "after"]
    
    # Add "inside" options for multi-word names with more variety
    if len(name_words) >= 2:
        placement_options.extend([
            "replace_first",     # Replace first name with nickname
            "replace_last",      # Replace last name with nickname  
            "between_first_last", # Between first and last name
            "after_first",       # After first name (most common in real life)
        ])
    
    placement = random.choice(placement_options)

    final_words, final_tags = [], []

    # Decide spacing: 0 = no spaces, 1 = left space only, 2 = right space only, 3 = both spaces
    spacing_mode = random.choice([0, 1, 2, 3])

    # Helper to add delimiters
    def wrap_with_delimiters(words, tags):
        if not q_start and not q_end:
            return words, tags
        
        # Handle cases where one delimiter is empty (e.g., only a starting quote)
        final_words = []
        final_tags = []
        if q_start:
            final_words.append(q_start)
            final_tags.append(O)
        final_words.extend(words)
        final_tags.extend(tags)
        if q_end:
            final_words.append(q_end)
            final_tags.append(O)
        return final_words, final_tags

    wrapped_nick_words, wrapped_nick_tags = wrap_with_delimiters(nick_words, nick_tags)

    if placement == "before":
        # Nick before name: "[kuma] Jan Novák"
        final_words = wrapped_nick_words + name_words
        final_tags = wrapped_nick_tags + name_tags
    elif placement == "after":
        # Nick after name: "Jan Novák [kuma]"
        final_words = name_words + wrapped_nick_words
        final_tags = name_tags + wrapped_nick_tags
    elif placement == "replace_first":
        # Replace first name: "[kuma] Novák" instead of "Jan Novák"
        final_words = wrapped_nick_words + name_words[1:]
        final_tags = wrapped_nick_tags + name_tags[1:]
    elif placement == "replace_last":
        # Replace last name: "Jan [kuma]" instead of "Jan Novák"
        final_words = name_words[:-1] + wrapped_nick_words
        final_tags = name_tags[:-1] + wrapped_nick_tags
    elif placement == "after_first":
        # After first name: "Jan [kuma] Novák"
        final_words = name_words[:1] + wrapped_nick_words + name_words[1:]
        final_tags = name_tags[:1] + wrapped_nick_tags + name_tags[1:]
    elif placement == "between_first_last":
        # Between first and last (if 3+ words): "Jan Marie [kuma] Novák"
        if len(name_words) >= 3:
            mid = len(name_words) // 2
            final_words = name_words[:mid] + wrapped_nick_words + name_words[mid:]
            final_tags = name_tags[:mid] + wrapped_nick_tags + name_tags[mid:]
        else:
            # Fallback to after_first for 2-word names
            final_words = name_words[:1] + wrapped_nick_words + name_words[1:]
            final_tags = name_tags[:1] + wrapped_nick_tags + name_tags[1:]

    # Apply spacing mode
    if spacing_mode == 0:
        # No spaces at all - concatenate everything
        text = "".join(final_words)
        # When concatenating, produce a single representative tag
        final_tag = final_tags[0] if final_tags else O
        return text, final_tag
    
    # For all other modes, use normal spacing. The complex logic was causing errors.
    return " ".join(final_words), " ".join(final_tags)
