import argparse
import os
import sys
import unicodedata
from typing import List, Set
import re


def normalize_token(token: str) -> str:
    """Lowercase and strip diacritics for robust matching."""
    s = token.strip().lower()
    s = unicodedata.normalize('NFD', s)
    s = ''.join(ch for ch in s if not unicodedata.combining(ch))
    return s


def read_lines(path: str) -> List[str]:
    try:
        with open(path, 'r', encoding='utf-8', errors='ignore') as f:
            return [line.rstrip('\n') for line in f]
    except FileNotFoundError:
        return []


def build_name_token_set(names: List[str]) -> Set[str]:
    """Build a set of normalized unique tokens from names list.
    Splits names by whitespace and punctuation; removes empty tokens.
    """
    token_set: Set[str] = set()
    splitter = re.compile(r"[\s,.;:/\\()\[\]\-_'\"]+")
    for name in names:
        if not name:
            continue
        for tok in splitter.split(name):
            t = normalize_token(tok)
            if t:
                token_set.add(t)
    return token_set


def should_discard_company(line: str, name_tokens: Set[str]) -> bool:
    """Return True if any normalized token in company line matches a name token."""
    splitter = re.compile(r"[\s,.;:/\\()\[\]\-_'\"]+")
    for tok in splitter.split(line):
        if not tok:
            continue
        if normalize_token(tok) in name_tokens:
            return True
    return False


def write_lines(path: str, lines: List[str]) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        for line in lines:
            f.write(line)
            f.write('\n')


def main() -> int:
    parser = argparse.ArgumentParser(description="Filter companies that contain any name token.")
    parser.add_argument('--companies', required=True, help='Input companies list (one per line)')
    parser.add_argument('--names', required=True, help='Input names list (one per line)')
    parser.add_argument('--out', required=True, help='Output filtered companies list')
    parser.add_argument('--removed-out', default=None, help='Optional path to write removed companies')
    args = parser.parse_args()

    print(f"[companies-filter] Loading: companies={args.companies} names={args.names}")
    companies = read_lines(args.companies)
    names = read_lines(args.names)
    print(f"[companies-filter] Loaded {len(companies):,} companies and {len(names):,} names")

    name_tokens = build_name_token_set(names)
    print(f"[companies-filter] Built {len(name_tokens):,} unique name tokens")

    kept: List[str] = []
    removed: List[str] = []
    for line in companies:
        if should_discard_company(line, name_tokens):
            removed.append(line)
        else:
            kept.append(line)

    write_lines(args.out, kept)
    if args.removed_out is not None:
        write_lines(args.removed_out, removed)

    print(f"[companies-filter] Kept {len(kept):,} ({len(kept)/max(1,len(companies)):.1%}), removed {len(removed):,}")
    if removed:
        print(f"[companies-filter] Sample removed: {removed[:10]}")
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        print("Interrupted.")
        sys.exit(130)
