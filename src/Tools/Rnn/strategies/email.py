"""
Email/dotted name strategy - generates dotted name patterns like jan.novak.
"""
import random
from typing import List, Tuple
from .utils import B_PER, I_PER, O


def remix_email(names: List[str]) -> Tuple[str, str]:
    """
    Generates dotted name patterns like 'jan.novak' without email domains.
    Tags as [name O name] where O is the dot separator.
    Spaces around dots only 20% of the time, shuffles name parts 30% of the time.
    """
    if not names:
        return "", ""
    
    name_text = random.choice(names)
    parts = name_text.lower().split()
    
    if len(parts) < 2:
        # Need at least two parts to create a dotted pattern
        return "", ""
    
    # Shuffle name parts 30% of the time
    if random.random() < 0.3:
        random.shuffle(parts)
    
    # Choose separator: 80% dot, 20% alternative (dash, underscore, slash)
    if random.random() < 0.2:
        separator = random.choice(["-", "_", "/", "\\"])
    else:
        separator = "."
    
    # Build words and tags
    words = []
    tags = []
    
    for i, part in enumerate(parts):
        words.append(part)
        tags.append(B_PER if i == 0 else I_PER)
        
        # Add separator between parts (but not after the last one)
        if i < len(parts) - 1:
            words.append(separator)
            tags.append(O)
    
    # 20% chance to add spaces around separator, 80% concatenate without spaces
    if random.random() < 0.2:
        # With spaces: "jan . novák" → "B-PER O I-PER"
        return " ".join(words), " ".join(tags)
    else:
        # No spaces: "jan.novák" → "B-PER" (concatenated text, single tag)
        text = "".join(words)  # Concatenate without spaces
        return text, B_PER  # Single token gets a single B-PER tag
