"""
Generates realistic, multi-word phrases that are not entities.
"""
import random
from typing import List, Tuple
from .utils import O


def remix_common_phrase(o_phrase_words: List[str]) -> Tuple[str, str]:
    """
    Creates a multi-word phrase of common Czech words, all tagged as O.
    This helps the model learn to distinguish named entities from regular text.
    """
    if not o_phrase_words:
        return "", ""
    
    # Generate phrases of 2 to 5 words
    num_words = random.randint(2, 5)
    
    # Ensure we don't try to sample more words than available
    if len(o_phrase_words) < num_words:
        return "", ""
        
    words = random.sample(o_phrase_words, num_words)
    
    # 10% chance to capitalize the first word to simulate the start of a sentence
    if random.random() < 0.1:
        words[0] = words[0].capitalize()
        
    text = " ".join(words)
    tags = " ".join([O] * num_words)
    
    return text, tags
