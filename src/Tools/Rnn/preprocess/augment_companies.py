"""
Performs intelligent data augmentation on company name source files.

This script reads one or more source lists of company names, then generates an
augmented list by:
1.  Keeping all original company names.
2.  Stripping common corporate suffixes (e.g., "s.r.o.", "ltd.").
3.  Extracting potential core brand names from longer descriptive names.

Crucially, it uses a 'person-like' heuristic to triage the results. Any
stripped or extracted name that looks like a person's name is discarded to
avoid poisoning the training data. The final, unique, augmented list is
written to a new source file.
"""
import argparse
import os
import sys
from typing import List, Set

# --- Copied constants from strategies/utils.py for standalone script ---
TITLES_BEFORE = [
    "Pan", "Paní", "Bc.", "BcA.", "Ing.", "Ing. arch.", "MUDr.", "MDDr.", "MVDr.",
    "MgA.", "Mgr.", "JUDr.", "PhDr.", "RNDr.", "PharmDr.", "ThLic.", "ThDr.",
    "akad. arch.", "ak. mal.", "ak. soch.", "MSDr.", "PaedDr.", "PhMr.", "RCDr.",
    "RSDr.", "RTDr.", "ThMgr.", "as.", "odb. as.", "doc.", "prof.", "voj.",
    "svob.", "sv.", "des.", "čet.", "rtn.", "rtm.", "nrtm.", "prap.", "nprap.",
    "št. prap.", "šprap.", "por.", "npor.", "kpt.", "mjr.", "pplk.", "plk.",
    "brig.gen.", "genmjr.", "genpor.", "arm.gen.", "ppor.", "škpt.", "šrtm.",
    "gen.", "genplk.", "Rev.", "Very Rev.", "Most Rev.", "Rt. Rev.", "Right Rev.",
    "Fr.", "Father", "Sister", "Br.", "Brother", "Dcn.", "Deacon", "Bp.", "Bishop",
    "Abp.", "Archbishop", "Msgr.", "Monsignor", "Card.", "Cardinal", "Dom", "Abbot",
    "Mother", "Pastor", "Padre", "prom.", "promovaný", "promovaná"
]
COMPANY_SUFFIXES = [
    "s.r.o.", "a.s.", "v.o.s.", "k.s.", "z.s.", "o.p.s.", "spol. s r.o.", "v likvidaci", "družstvo",
    "LLC", "Ltd.", "Inc.", "GmbH", "S.A.", "Corp.", "Limited", "Incorporated"
]

def read_lines(path: str) -> List[str]:
    try:
        with open(path, 'r', encoding='utf-8', errors='ignore') as f:
            return [line.strip() for line in f if line.strip()]
    except FileNotFoundError:
        print(f"Error: File not found at {path}", file=sys.stderr)
        return []

def write_lines(path: str, lines: Set[str]) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        for line in sorted(list(lines)):
            f.write(line + '\n')

def main() -> int:
    parser = argparse.ArgumentParser(
        description="Augment company source files with intelligent stripping and extraction.",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter
    )
    parser.add_argument('--companies', required=True, nargs='+', help='One or more input company source files.')
    parser.add_argument('--names', required=True, help='Input names list to use for filtering heuristics.')
    parser.add_argument('--out', required=True, help='Output path for the new, augmented source file.')
    parser.add_argument('--removed-out', default=None, help='Optional path to write discarded "person-like" names for inspection.')
    args = parser.parse_args()

    all_companies = []
    for company_file in args.companies:
        print(f"Loading companies from: {company_file}")
        companies = read_lines(company_file)
        all_companies.extend(companies)

    print(f"Loading names from: {args.names}")
    names = read_lines(args.names)
    
    if not all_companies or not names:
        print("Error: Input files are empty or could not be read.", file=sys.stderr)
        return 1
        
    print(f"Loaded {len(all_companies):,} total companies and {len(names):,} names.")

    # --- Heuristics setup for 'is_person_like' check ---
    name_token_set = {token for name in names for token in name.lower().split()}
    title_token_set = {title.lower().replace('.', '') for title in TITLES_BEFORE}

    def is_person_like(text: str) -> bool:
        if not text: return False
        tokens = text.lower().split()
        
        if any(token.replace('.', '') in title_token_set for token in tokens):
            return True
        if len(tokens) == 2 and tokens[0] in name_token_set and tokens[1] in name_token_set:
            return True
        if len(tokens) == 1 and len(tokens[0]) > 3 and tokens[0] in name_token_set:
            return True
        return False

    augmented_companies: Set[str] = set()
    discarded_person_like: Set[str] = set()
    sorted_suffixes = sorted(COMPANY_SUFFIXES, key=len, reverse=True)

    total_companies = len(all_companies)
    for i, original_company in enumerate(all_companies):
        if (i + 1) % 100000 == 0:
            print(f"  Processing: {(i+1):,}/{total_companies:,} companies...", flush=True)

        augmented_companies.add(original_company)
        
        # 1. Suffix Stripping & Triage
        stripped_company = original_company
        company_lower = original_company.lower()
        
        for suffix in sorted_suffixes:
            suffix_lower = suffix.lower()
            if company_lower.endswith(f" {suffix_lower}") or company_lower.endswith(f",{suffix_lower}"):
                suffix_start_index = company_lower.rfind(suffix_lower)
                stripped_company = original_company[:suffix_start_index].strip(" ,")
                break
        
        if stripped_company and stripped_company != original_company:
            if not is_person_like(stripped_company):
                augmented_companies.add(stripped_company)
            else:
                discarded_person_like.add(stripped_company)
        
        # 2. Core Brand Extraction & Triage
        separators = [" - ", ":", "|"]
        for sep in separators:
            if sep in original_company:
                core_candidate = original_company.split(sep, 1)[0].strip()
                if core_candidate and core_candidate != original_company:
                    if not is_person_like(core_candidate):
                        augmented_companies.add(core_candidate)
                    else:
                        discarded_person_like.add(core_candidate)

    print("\nAugmentation summary:")
    print(f"  + Original unique companies: {len(set(all_companies)):,}")
    print(f"  + Final unique augmented companies: {len(augmented_companies):,}")
    print(f"  - Discarded {len(discarded_person_like):,} unique 'person-like' variants.")
    
    print(f"\nWriting {len(augmented_companies):,} unique augmented company names to: {args.out}")
    write_lines(args.out, augmented_companies)

    if args.removed_out:
        print(f"Writing {len(discarded_person_like):,} unique discarded names to: {args.removed_out}")
        write_lines(args.removed_out, discarded_person_like)
        
    return 0

if __name__ == '__main__':
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        print("\nInterrupted by user.", file=sys.stderr)
        sys.exit(130)
