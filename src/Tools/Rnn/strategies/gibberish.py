"""
Gibberish strategy - generates random character strings.
"""
import random
from typing import Tuple
from .utils import B_NICK


def remix_gibberish() -> Tuple[str, str]:
    """Generates a random string of characters."""
    length = random.randint(5, 15)
    chars = "abcdefghijklmnopqrstuvwxyz0123456789_-"
    text = "".join(random.choice(chars) for _ in range(length))
    return text, B_NICK
