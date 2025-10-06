"""
Name with title strategy - adds academic/professional titles before and/or after names.
"""
import random
from typing import List, Tuple
from .utils import tag_entity, tag_title_text, corrupt_title_string, TITLES_BEFORE, TITLES_AFTER, O


def remix_name_with_title(names: List[str]) -> Tuple[str, str]:
    """
    Adds one or more titles to a name, before and/or after, with corruption and wrong placements.
    Titles are tagged as TIT, names as PER. Separators and commas are O.
    """
    if not names:
        return "", ""
    name_text = random.choice(names)
    name_words, name_tags = tag_entity(name_text, "PER")
    
    # Define a clearer set of patterns with explicit weights
    patterns = [
        ("single_before", 0.35),
        ("single_after", 0.25),
        ("one_before_one_after", 0.15),
        ("two_before", 0.05),
        ("two_after", 0.05),
        ("many_titles", 0.10),
        ("duplicated_title", 0.05),
    ]
    
    # Filter out patterns that can't be generated with the available titles
    available_patterns = []
    if TITLES_BEFORE:
        available_patterns.extend([p for p in patterns if "before" in p[0] or "duplicated" in p[0]])
    if TITLES_AFTER:
        available_patterns.extend([p for p in patterns if "after" in p[0]])
    if len(TITLES_BEFORE) > 1 and len(TITLES_AFTER) > 1:
        available_patterns.append(("many_titles", 0.10))
    
    # Remove duplicates and get unique patterns
    available_patterns = sorted(list(set(available_patterns)))
    
    if not available_patterns:
        return name_text, " ".join(name_tags)
        
    pattern_names = [p[0] for p in available_patterns]
    pattern_weights = [p[1] for p in available_patterns]
    
    # Normalize weights to sum to 1
    total_weight = sum(pattern_weights)
    if total_weight > 0:
        pattern_weights = [w / total_weight for w in pattern_weights]
    
    chosen_pattern = random.choices(pattern_names, weights=pattern_weights, k=1)[0]

    final_words = []
    final_tags = []

    if chosen_pattern == "single_before":
        title = corrupt_title_string(random.choice(TITLES_BEFORE))
        title_words, title_tags = tag_title_text(title)
        final_words = title_words + name_words
        final_tags = title_tags + name_tags
    
    elif chosen_pattern == "single_after":
        title = corrupt_title_string(random.choice(TITLES_AFTER))
        title_words, title_tags = tag_title_text(title)
        final_words = name_words + [","] + title_words
        final_tags = name_tags + [O] + title_tags

    elif chosen_pattern == "one_before_one_after":
        if TITLES_BEFORE and TITLES_AFTER:
            t_before = corrupt_title_string(random.choice(TITLES_BEFORE))
            t_after = corrupt_title_string(random.choice(TITLES_AFTER))
            tb_words, tb_tags = tag_title_text(t_before)
            ta_words, ta_tags = tag_title_text(t_after)
            final_words = tb_words + name_words + [","] + ta_words
            final_tags = tb_tags + name_tags + [O] + ta_tags
            
    elif chosen_pattern == "two_before":
        if len(TITLES_BEFORE) > 1:
            title1, title2 = random.sample(TITLES_BEFORE, 2)
            t1_words, t1_tags = tag_title_text(corrupt_title_string(title1))
            t2_words, t2_tags = tag_title_text(corrupt_title_string(title2))
            final_words = t1_words + t2_words + name_words
            final_tags = t1_tags + t2_tags + name_tags

    elif chosen_pattern == "two_after":
        if len(TITLES_AFTER) > 1:
            title1, title2 = random.sample(TITLES_AFTER, 2)
            t1_words, t1_tags = tag_title_text(corrupt_title_string(title1))
            t2_words, t2_tags = tag_title_text(corrupt_title_string(title2))
            final_words = name_words + [","] + t1_words + t2_words
            final_tags = name_tags + [O] + t1_tags + t2_tags
            
    elif chosen_pattern == "many_titles":
        # Similar logic as before, but now it's an explicit choice
        before_count = random.randint(1, 4)
        after_count = random.randint(1, 4)
        
        before_words, before_tags = [], []
        if len(TITLES_BEFORE) > 0:
            for _ in range(before_count):
                title = corrupt_title_string(random.choice(TITLES_BEFORE))
                tw, tt = tag_title_text(title)
                before_words.extend(tw)
                before_tags.extend(tt)
        
        after_words, after_tags = [], []
        if len(TITLES_AFTER) > 0:
            for _ in range(after_count):
                title = corrupt_title_string(random.choice(TITLES_AFTER))
                tw, tt = tag_title_text(title)
                after_words.extend(tw)
                after_tags.extend(tt)

        final_words = before_words + name_words + ([","] if after_words else []) + after_words
        final_tags = before_tags + name_tags + ([O] if after_words else []) + after_tags
        
    elif chosen_pattern == "duplicated_title":
        if TITLES_BEFORE:
            # Use a broader list of common titles for duplication
            common_titles = [t for t in TITLES_BEFORE if len(t) < 5 and "." in t]
            if common_titles:
                title = corrupt_title_string(random.choice(common_titles))
                separator = random.choice(["et", "a"])
                title_words, title_tags = tag_title_text(title)
                final_words = title_words + [separator] + title_words + name_words
                final_tags = title_tags + [O] + title_tags + name_tags
    
    # Final fallback if something went wrong
    if not final_words:
        return name_text, " ".join(name_tags)
    
    # New, robust logic to simulate spacing/comma typos by merging list items
    merged_words = []
    merged_tags = []
    i = 0
    while i < len(final_words):
        current_word = final_words[i]
        current_tag = final_tags[i]

        # Check for merge patterns
        merged = False
        if i + 1 < len(final_words):
            next_word = final_words[i+1]
            next_tag = final_tags[i+1]

            # Pattern 1: `word` + `,` -> `word,` (50% chance)
            if next_word == ',' and random.random() < 0.5:
                merged_words.append(current_word + next_word)
                merged_tags.append(current_tag)
                i += 2
                merged = True
            
            # Pattern 2: `,` + `word` -> `,word` (50% chance)
            elif current_word == ',' and random.random() < 0.5:
                merged_words.append(current_word + next_word)
                merged_tags.append(O) # Always tag comma-prefixed words as O
                i += 2
                merged = True
            
            # Pattern 3: `title` + `name`/`title` -> `titlename`/`titletitle` (20% chance)
            elif current_tag.endswith("TIT") and random.random() < 0.2:
                merged_words.append(current_word + next_word)
                merged_tags.append(current_tag)
                i += 2
                merged = True

        if not merged:
            merged_words.append(current_word)
            merged_tags.append(current_tag)
            i += 1

    return " ".join(merged_words), " ".join(merged_tags)
