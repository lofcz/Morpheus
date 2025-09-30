import argparse
import os
import sys
import unicodedata
from typing import Dict, List, Set
import re


def normalize_text(text: str, *, keep_letters_only: bool = True) -> str:
    """Lowercase, strip diacritics, optionally keep a-z only.
    Collapses spaces, hyphens, and apostrophes for robust matching.
    """
    if text is None:
        return ""
    s = text.strip().lower()
    s = unicodedata.normalize('NFD', s)
    s = ''.join(ch for ch in s if not unicodedata.combining(ch))
    s = s.replace(" ", "").replace("-", "").replace("'", "")
    if keep_letters_only:
        s = ''.join(ch for ch in s if 'a' <= ch <= 'z')
    return s


def build_name_roots(names: List[str], min_root_len: int) -> Set[str]:
    """
    Build roots from the second list. Language-agnostic aggressive mode:
    - For each line, strip diacritics/lowercase
    - Extract all alphabetic tokens (a–z)
    - Add any token with length >= min_root_len as a root
    - Also add the full concatenated normalized string if long enough
    """
    roots: Set[str] = set()
    token_re = re.compile(r"[a-z]+")
    for raw in names:
        if not raw:
            continue
        # Normalize and keep only ASCII letters for tokenization
        s = unicodedata.normalize('NFD', raw.strip().lower())
        s = ''.join(ch for ch in s if not unicodedata.combining(ch))
        # Token roots (e.g., from company names this will include 'salon', 'sro', ...)
        for tok in token_re.findall(s):
            if len(tok) >= min_root_len:
                roots.add(tok)
        # Full concatenated root (aggressive)
        full = ''.join(ch for ch in s if 'a' <= ch <= 'z')
        if len(full) >= max(min_root_len, 2):
            roots.add(full)
    return roots


def index_roots_by_prefix(roots: Set[str], prefix_len: int = 3) -> Dict[str, List[str]]:
    index: Dict[str, List[str]] = {}
    for r in roots:
        key = r[:prefix_len]
        index.setdefault(key, []).append(r)
    for key in index:
        index[key].sort(key=len, reverse=True)
    return index




def should_prune_prefix(word_norm: str, root_index: Dict[str, List[str]], min_root_len: int, prefix_len: int) -> bool:
    if len(word_norm) < min_root_len:
        return False
    key = word_norm[:prefix_len]
    candidates = root_index.get(key)
    if not candidates:
        return False
    for root in candidates:
        if len(root) < min_root_len:
            continue
        if word_norm.startswith(root):
            return True
    return False


def read_lines(path: str) -> List[str]:
    with open(path, 'r', encoding='utf-8', errors='ignore') as f:
        return [line.rstrip('\n') for line in f]


def read_words_flexible(path: str) -> List[str]:
    """Read a word list that may be one-per-line or space-delimited.
    If the file has very few lines, tokenize by letters.
    """
    try:
        with open(path, 'r', encoding='utf-8', errors='ignore') as f:
            data = f.read()
    except FileNotFoundError:
        return []

    newline_count = data.count('\n')
    if newline_count > 10:
        # Likely one-per-line
        return [line.strip() for line in data.splitlines() if line.strip()]
    # Otherwise, extract letter sequences (keeps diacritics)
    tokens = re.findall(r"[A-Za-zÀ-ÖØ-öø-ÿĀ-žŽ]+", data)
    return tokens


def write_lines(path: str, lines: List[str]) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        for line in lines:
            f.write(line)
            f.write('\n')


def main() -> int:
    parser = argparse.ArgumentParser(description="Filter a Czech word list by pruning entries that look like names or their inflected forms.")
    parser.add_argument('--words', required=True, help='Input word list (one word per line)')
    parser.add_argument('--names', required=True, help='Input names list (one per line)')
    parser.add_argument('--out', required=True, help='Output filtered word list')
    parser.add_argument('--removed-out', default=None, help='Optional path to write pruned entries')
    parser.add_argument('--min-root-len', type=int, default=4, help='Minimum length of name root for pruning')
    parser.add_argument('--prefix-len', type=int, default=3, help='Prefix index length for fast matching (2-4 recommended)')
    args = parser.parse_args()

    print(f"[filter] Loading: words={args.words} names={args.names}")
    # Be robust to space-delimited files
    words = read_words_flexible(args.words)
    names = read_lines(args.names)
    print(f"[filter] Loaded {len(words):,} words and {len(names):,} names")

    # Build root index for a very conservative prefix test
    roots = build_name_roots(names, args.min_root_len)
    root_index = index_roots_by_prefix(roots, args.prefix_len)
    print(f"[filter] Built {len(roots):,} name roots; index keys={len(root_index):,}")

    kept: List[str] = []
    removed: List[str] = []
    # Strategy (language-agnostic, permissive):
    # Normalize both lists and prune if a word STARTS WITH any normalized name root.
    # This aggressively removes inflected forms (accepted false positives by design).
    for w in words:
        wn = normalize_text(w)
        if should_prune_prefix(wn, root_index, args.min_root_len, args.prefix_len):
            removed.append(w)
        else:
            kept.append(w)

    # Optional: sort by length then lexicographically for stability
    kept_sorted = sorted(kept, key=lambda s: (len(normalize_text(s)), normalize_text(s)))
    write_lines(args.out, kept_sorted)
    if args.removed_out:
        removed_sorted = sorted(removed, key=lambda s: (len(normalize_text(s)), normalize_text(s)))
        write_lines(args.removed_out, removed_sorted)

    print(f"[filter] Kept {len(kept):,} ({len(kept)/max(1,len(words)):.1%}), removed {len(removed):,}")
    if removed:
        print(f"[filter] Sample removed: {removed[:10]}")
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        print("Interrupted.")
        sys.exit(130)


