"""
Nickname handle strategy - generates handle-style nicknames with separators and digits.
"""
import random
from typing import List, Tuple
from .utils import B_NICK


def remix_nick_handle(nicknames: List[str]) -> Tuple[str, str]:
    """Generate a nickname/handle with separators and digits as a single token."""
    # Prefer using a real nickname base when available
    base = None
    if nicknames:
        cand = random.choice(nicknames).split()
        if cand:
            base = cand[0]
    if base is None:
        letters = "abcdefghijklmnopqrstuvwxyz"
        base = ''.join(random.choice(letters) for _ in range(random.randint(4, 8)))

    # Randomly inject separators and digits
    seps = ['.', '_']
    parts = [base]
    if random.random() < 0.7:
        parts.append(random.choice(seps))
        parts.append(''.join(random.choice(base) for _ in range(random.randint(2, 4))))
    if random.random() < 0.6:
        parts.append(str(random.randint(10, 99)))
    handle = ''.join(parts)
    return handle, B_NICK
