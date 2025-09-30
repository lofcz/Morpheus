"""
O-phrase strategy - generates phrases tagged entirely as O (outside entities).
"""
import random
from typing import List, Tuple
from .utils import O


def remix_o_phrase(o_phrase_words: List[str]) -> Tuple[str, str]:
    """Compose 1-3 word O-only phrase from a precomputed smaller wordlist."""
    if not o_phrase_words:
        return "", ""
    k = random.choice([1, 2, 3])
    # Use random indices on the smaller precomputed list
    indices = [random.randint(0, len(o_phrase_words) - 1) for _ in range(k)]
    toks = [str(o_phrase_words[i]) for i in indices]
    return " ".join(toks), " ".join([O] * k)
