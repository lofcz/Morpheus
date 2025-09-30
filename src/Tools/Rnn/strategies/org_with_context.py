"""
Organization with context strategy - adds contextual words around organization names.
"""
import random
from typing import List, Tuple
from .utils import tag_entity, O, I_ORG


def remix_org_with_context(companies: List[str]) -> Tuple[str, str]:
    """Add light context around an org name to anchor ORG semantics."""
    if not companies:
        return "", ""
    org = random.choice(companies)
    context_prefix = random.choice(["společnost", "fa", "firma", "brand", "organizace"]) if random.random() < 0.1 else None
    context_suffix = random.choice(["v likvidaci", None])
    words, tags = tag_entity(org, "ORG")
    out_w: List[str] = []
    out_t: List[str] = []
    if context_prefix:
        out_w += context_prefix.split()
        out_t += [O] * len(context_prefix.split())
    out_w += words
    out_t += tags
    if context_suffix:
        out_w += context_suffix.split()
        out_t += [I_ORG] * len(context_suffix.split())
    return " ".join(out_w), " ".join(out_t)
