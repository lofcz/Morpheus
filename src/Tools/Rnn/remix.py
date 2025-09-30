import os
import random
import argparse
import pandas as pd
from typing import List, Tuple

# Import strategy configuration
from remix_config import get_active_strategies, STRATEGY_REQUIREMENTS

# Import constants from strategies.utils
from strategies.utils import (
    B_PER, I_PER, B_ORG, I_ORG, B_NICK, I_NICK, B_TIT, I_TIT, O,
    cripple_entity, cripple_iy, has_only_allowed_chars, COMPANY_SUFFIXES, TITLES_BEFORE, TITLES_AFTER
)

# --- Configuration ---
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))

# Helper import for title stripping (used below)
import unicodedata

# --- Data Loading Functions ---

def load_entities(path: str, validator: callable = None) -> (List[str], int, int):
    """Loads a list of entities from a file, one per line, with optional validation."""
    if not os.path.exists(path):
        print(f"Warning: Data file not found at {path}. This entity type will be skipped.")
        return [], 0, 0
    
    entities = []
    initial_count = 0
    removed_count = 0
    filename = os.path.basename(path)
    with open(path, "r", encoding="utf-8") as f:
        for i, line in enumerate(f):
            if (i + 1) % 500000 == 0:
                print(f"  Filtering {filename}: processed {(i+1):,} lines...", flush=True)

            initial_count += 1
            entity = line.strip()
            if not entity:
                continue

            if validator and not validator(entity):
                removed_count += 1
                continue
            entities.append(entity)
            
    return entities, initial_count, removed_count

def load_words_corpus() -> List[str]:
    """Load all .txt word lists from data/classes/sources/words and merge them."""
    words_dir = os.path.join(SCRIPT_DIR, "data", "classes", "sources", "words")
    merged: List[str] = []
    if not os.path.isdir(words_dir):
        return merged
    for fname in os.listdir(words_dir):
        if not fname.lower().endswith('.txt'):
            continue
        # Only include filtered lists, skip raw sources
        if "filtered" not in fname.lower():
            continue
        path = os.path.join(words_dir, fname)
        entities, _, _ = load_entities(path)
        merged.extend(entities)
    return merged

def load_org_types() -> List[str]:
    """Load organization type terms (firma, společnost, lékárna, etc.)"""
    path = os.path.join(SCRIPT_DIR, "data", "classes", "sources", "org_types.txt")
    entities, _, _ = load_entities(path)
    return entities

def _normalize_dotless_token(s: str) -> str:
    # Cache normalized tokens to avoid repeated work
    if not hasattr(_normalize_dotless_token, 'cache'):
        _normalize_dotless_token.cache = {}
    if s in _normalize_dotless_token.cache:
        return _normalize_dotless_token.cache[s]
    result = s.strip().lower()
    result = result.replace('.', '')
    result = unicodedata.normalize('NFD', result)
    result = ''.join(ch for ch in result if not unicodedata.combining(ch))
    _normalize_dotless_token.cache[s] = result
    return result


def _build_title_norm() -> set:
    title_tokens: List[str] = []
    for t in TITLES_BEFORE + TITLES_AFTER:
        title_tokens.extend(t.split())
    return {_normalize_dotless_token(tok) for tok in title_tokens if tok}


def load_names_strip_titles(path: str, validator: callable = None) -> (List[str], int, int):
    """Load names and strip any tokens that match known titles (dotless match)."""
    if not os.path.exists(path):
        return [], 0, 0
        
    title_norm = _build_title_norm()
    cleaned: List[str] = []
    stripped = 0
    initial_count = 0
    removed_by_validator = 0
    filename = os.path.basename(path)

    with open(path, "r", encoding="utf-8") as f:
        lines = f.readlines()
        initial_count = len(lines)

    for idx, line in enumerate(lines):
        if (idx + 1) % 100000 == 0 and idx > 0:
            print(f"  Filtering and stripping titles from {filename}: {(idx+1):,}/{initial_count:,} processed...", flush=True)
        
        line = line.strip()
        if not line:
            continue

        if validator and not validator(line):
            removed_by_validator += 1
            continue

        parts = line.split()
        kept_parts = []
        for p in parts:
            pn = _normalize_dotless_token(p)
            if pn in title_norm:
                stripped += 1
                continue
            kept_parts.append(p)
        s = ' '.join(kept_parts).strip()
        if s:
            cleaned.append(s)
            
    if stripped > 0:
        print(f"Stripped {stripped:,} title tokens from {initial_count:,} names.")
    return cleaned, initial_count, removed_by_validator


def main():
    parser = argparse.ArgumentParser(description="Synthesize an NER dataset from clean entity files.")
    parser.add_argument("--names-file", default=os.path.join(SCRIPT_DIR, "data", "classes", "names.txt"))
    parser.add_argument("--companies-file", default=os.path.join(SCRIPT_DIR, "data", "classes", "companies.txt"))
    parser.add_argument("--nicknames-file", default=os.path.join(SCRIPT_DIR, "data", "classes", "nicknames.txt"))
    parser.add_argument("--cities-file", default=os.path.join(SCRIPT_DIR, "data", "classes", "cities.txt"))
    parser.add_argument("--output-file", default=os.path.join(SCRIPT_DIR, "data", "dataset.csv"))
    parser.add_argument("--num-samples", type=int, default=100_000_000, help="Total number of samples to generate.")
    parser.add_argument("--chunk-size", type=int, default=500_000, help="Number of samples to generate and shuffle in memory at a time before writing to disk.")
    parser.add_argument("--seed", type=int, default=42, help="Random seed for reproducibility.")
    args = parser.parse_args()

    random.seed(args.seed)

    print("Loading and filtering source entity files...")
    
    names, names_initial, names_removed = load_names_strip_titles(args.names_file, has_only_allowed_chars)
    companies, companies_initial, companies_removed = load_entities(args.companies_file, has_only_allowed_chars)
    nicknames, nicknames_initial, nicknames_removed = load_entities(args.nicknames_file, has_only_allowed_chars)
    cities, cities_initial, cities_removed = load_entities(args.cities_file, has_only_allowed_chars)

    # --- Report on filtering ---
    print("Filtered out entries with unsupported characters:")
    if names_removed > 0:
        print(f"  - names: {names_initial:,} -> {names_initial - names_removed:,} (removed {names_removed:,})")
    if companies_removed > 0:
        print(f"  - companies: {companies_initial:,} -> {companies_initial - companies_removed:,} (removed {companies_removed:,})")
    if nicknames_removed > 0:
        print(f"  - nicknames: {nicknames_initial:,} -> {nicknames_initial - nicknames_removed:,} (removed {nicknames_removed:,})")
    if cities_removed > 0:
        print(f"  - cities: {cities_initial:,} -> {cities_initial - cities_removed:,} (removed {cities_removed:,})")

    words_corpus = load_words_corpus()
    org_types = load_org_types()

    # Precompute a smaller subset of words for O-phrase generation to avoid slow random.choice on large lists
    if words_corpus:
        o_phrase_words = random.sample(words_corpus, min(10000, len(words_corpus)))
    else:
        o_phrase_words = []

    # Precompute single-word names for remix_single_per strategy
    single_names = [n for n in names if len(n.split()) == 1]

    # --- Noise Injection: Probabilistically strip suffixes from companies ---
    if companies:
        print("Injecting noise: Probabilistically stripping suffixes from company names...")
        processed_companies = []
        # Sort by length, longest first, to match "spol. s r.o." before "s.r.o."
        sorted_suffixes = sorted(COMPANY_SUFFIXES, key=len, reverse=True)
        
        stripped_count = 0
        for company in companies:
            # With a 40% probability, try to strip a suffix
            if random.random() < 0.4:
                original_company = company
                company_lower = company.lower()
                
                for suffix in sorted_suffixes:
                    suffix_lower = suffix.lower()
                    # Check for suffix as a whole word at the end, possibly preceded by a comma
                    if company_lower.endswith(f" {suffix_lower}") or company_lower.endswith(f",{suffix_lower}"):
                        # Find the start of the suffix in the original string to slice correctly
                        suffix_start_index = company_lower.rfind(suffix_lower)
                        company = company[:suffix_start_index].strip(" ,")
                        break # Stop after the first (longest) match
                
                # If after stripping the company name is empty, revert to original
                if not company.strip():
                    company = original_company
                elif company != original_company:
                    stripped_count += 1

            processed_companies.append(company)
        
        companies = processed_companies # Replace the original list
        print(f"Stripped suffixes from {stripped_count:,} company names.")

    # --- Noise Injection: Introduce typos and remove diacritics ---
    if names:
        print("Injecting noise: Introducing typos/diacritic removal into names...")
        crippled_count = 0
        crippled_names_augmented = []
        for name in names:
            # Always keep the original
            crippled_names_augmented.append(name)
            # With a 30% probability, also add a crippled version
            if random.random() < 0.3:
                crippled_name = cripple_entity(name)
                if crippled_name != name:
                    crippled_names_augmented.append(crippled_name)
                    crippled_count += 1
        names = crippled_names_augmented
        print(f"Added {crippled_count:,} crippled name variants to the data pool.")

    # --- Noise Injection: i/y swapping ---
    if names:
        iy_swap_count = 0
        iy_augmented_names = []
        for name in names:
            iy_augmented_names.append(name)
            if random.random() < 0.20:
                iy_swapped_name = cripple_iy(name)
                if iy_swapped_name != name:
                    iy_augmented_names.append(iy_swapped_name)
                    iy_swap_count += 1
        names = iy_augmented_names
        if iy_swap_count > 0:
            print(f"Added {iy_swap_count:,} i/y swapped name variants to the data pool.")

    if companies:
        print("Injecting noise: Introducing typos/diacritic removal into companies...")
        crippled_count = 0
        crippled_companies_augmented = []
        for company in companies:
            # Always keep the original
            crippled_companies_augmented.append(company)
            # With a 30% probability, also add a crippled version
            if random.random() < 0.3:
                crippled_company = cripple_entity(company)
                if crippled_company != company:
                    crippled_companies_augmented.append(crippled_company)
                    crippled_count += 1
        companies = crippled_companies_augmented
        print(f"Added {crippled_count:,} crippled company variants to the data pool.")

    if companies:
        iy_swap_count = 0
        iy_augmented_companies = []
        for company in companies:
            iy_augmented_companies.append(company)
            if random.random() < 0.20:
                iy_swapped_company = cripple_iy(company)
                if iy_swapped_company != company:
                    iy_augmented_companies.append(iy_swapped_company)
                    iy_swap_count += 1
        companies = iy_augmented_companies
        if iy_swap_count > 0:
            print(f"Added {iy_swap_count:,} i/y swapped company variants to the data pool.")

    # --- Prefix Stripping: Remove common prefixes like 't.j.' ---
    if names:
        processed_names = []
        stripped_count = 0
        for name in names:
            if name.lower().startswith("t.j. "):
                new_name = name[4:].strip()
                if new_name:
                    processed_names.append(new_name)
                    stripped_count += 1
            else:
                processed_names.append(name)
        if stripped_count > 0:
            names = processed_names
            print(f"Stripped 't.j.' prefix from {stripped_count:,} names.")
            
    if companies:
        processed_companies = []
        stripped_count = 0
        for company in companies:
            if company.lower().startswith("t.j. "):
                new_company = company[4:].strip()
                if new_company:
                    processed_companies.append(new_company)
                    stripped_count += 1
            else:
                processed_companies.append(company)
        if stripped_count > 0:
            companies = processed_companies
            print(f"Stripped 't.j.' prefix from {stripped_count:,} company names.")


    # Prepare available data for strategy filtering
    available_data = {
        "names": names,
        "companies": companies,
        "nicknames": nicknames,
        "cities": cities,
        "single_names": single_names,
        "o_phrase_words": o_phrase_words,
        "org_types": org_types
    }

    # Get active strategies based on available data
    population, weights = get_active_strategies(available_data)
    if not population:
        print("Error: No remix strategies can be run with the available data. Check source files.")
        return

    # --- Scalable Generation Loop ---
    # Write header first, then append in chunks
    header_df = pd.DataFrame(columns=["text", "tags"])
    header_df.to_csv(args.output_file, index=False)
    
    buffer = []
    total_generated = 0
    
    print(f"Generating {args.num_samples:,} samples in chunks of {args.chunk_size:,}...")

    for i in range(1, args.num_samples + 1):
        strategy = random.choices(population, weights=weights, k=1)[0]
        
        # Build arguments for the chosen strategy dynamically
        required_args = STRATEGY_REQUIREMENTS.get(strategy, [])
        func_args = {arg: available_data[arg] for arg in required_args}
        
        try:
            text, tags = strategy(**func_args)
        except Exception as e:
            # Log strategy crashes but don't stop the whole process
            error_message = str(e)
            if len(error_message) > 500:
                error_message = error_message[:500] + "..."
            print(f"Strategy '{strategy.__name__}' crashed with an error: {error_message}")
            text, tags = "", "" # Continue with empty sample
        
        if text and tags:
            # Lowercase the entire text for consistent training
            text = text.lower()
            
            # Validate: token count == tag count, all tags in allowed set
            tw = text.split()
            tg = tags.split()
            allowed_tags = {O, B_PER, I_PER, B_ORG, I_ORG, B_NICK, I_NICK, "B-LOC", "I-LOC", B_TIT, I_TIT}
            if len(tw) == len(tg) and all(t in allowed_tags for t in tg):
                buffer.append({"text": text, "tags": tags})
            else:
                # Log validation failures to console for debugging, truncating long strings
                text_to_log = text if len(text) <= 500 else text[:500] + "..."
                tags_to_log = tags if len(tags) <= 500 else tags[:500] + "..."
                print(f"Validation failed for strategy '{strategy.__name__}':")
                print(f"  Text ({len(tw)} tokens): '{text_to_log}'")
                print(f"  Tags ({len(tg)} tokens): '{tags_to_log}'")
            
        # Write chunk to disk when buffer is full or at the very end
        if len(buffer) >= args.chunk_size or (i == args.num_samples and buffer):
            total_generated += len(buffer)
            print(f"Shuffling and writing chunk of {len(buffer):,} samples... (Total: {total_generated:,}/{args.num_samples:,})")
            
            random.shuffle(buffer)
            chunk_df = pd.DataFrame(buffer)
            chunk_df.to_csv(args.output_file, mode='a', header=False, index=False)
            
            buffer = [] # Reset buffer

    print(f"Successfully saved {total_generated:,} NER dataset samples to {args.output_file}")


if __name__ == "__main__":
    main()