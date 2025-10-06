"""
Generates multi-word nicknames.
"""
import random
from typing import List, Tuple
from .utils import tag_entity


def remix_multi_word_nickname(nicknames: List[str]) -> Tuple[str, str]:
    """
    Creates a multi-word nickname by combining two single-word nicknames.
    This helps the model distinguish multi-word nicknames from organizations.
    """
    if not nicknames or len(nicknames) < 2:
        return "", ""
        
    # Pick two random nicknames and take the first word of each
    nick1_full = random.choice(nicknames)
    nick2_full = random.choice(nicknames)
    
    nick1 = nick1_full.split()[0]
    nick2 = nick2_full.split()[0]
    
    text = f"{nick1} {nick2}"
    
    words, tags = tag_entity(text, "NICK")
    
    return " ".join(words), " ".join(tags)
