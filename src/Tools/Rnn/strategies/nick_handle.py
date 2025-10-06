"""
Nickname handle strategy - generates handle-style nicknames with separators and digits.
"""
import random
from typing import List, Tuple
from .utils import B_NICK, I_NICK


def remix_nick_handle(nicknames: List[str]) -> Tuple[str, str]:
    """Generate a nickname/handle with separators and digits."""
    base = None
    if nicknames:
        cand = random.choice(nicknames).split()
        if cand:
            base = cand[0]
    if base is None:
        letters = "abcdefghijklmnopqrstuvwxyz"
        base = ''.join(random.choice(letters) for _ in range(random.randint(4, 8)))

    sep = random.choice(['_', '.', '-'])
    suffix = ''.join(random.choice('abcdefghijklmnopqrstuvwxyz0123456789') for _ in range(random.randint(2, 5)))
    
    # Decide pattern
    pattern = random.choice(['sep_last', 'sep_first', 'no_sep'])
    
    if pattern == 'sep_last':
        text = f"{base}{sep}{suffix}"
    elif pattern == 'sep_first':
        text = f"{suffix}{sep}{base}"
    else: # no_sep
        text = f"{base}{suffix}"

    # Always tag as a single entity
    return text, B_NICK
