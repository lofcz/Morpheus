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

    # REDUCED CORRUPTION: Only corrupt titles 15% of the time instead of 100%
    should_corrupt = random.random() < 0.15
    
    if chosen_pattern == "single_before":
        title = random.choice(TITLES_BEFORE)
        if should_corrupt:
            title = corrupt_title_string(title)
        title_words, title_tags = tag_title_text(title)
        final_words = title_words + name_words
        final_tags = title_tags + name_tags
    
    elif chosen_pattern == "single_after":
        title = random.choice(TITLES_AFTER)
        if should_corrupt:
            title = corrupt_title_string(title)
        title_words, title_tags = tag_title_text(title)
        final_words = name_words + [","] + title_words
        final_tags = name_tags + [O] + title_tags

    elif chosen_pattern == "one_before_one_after":
        if TITLES_BEFORE and TITLES_AFTER:
            t_before = random.choice(TITLES_BEFORE)
            t_after = random.choice(TITLES_AFTER)
            if should_corrupt:
                t_before = corrupt_title_string(t_before)
                t_after = corrupt_title_string(t_after)
            tb_words, tb_tags = tag_title_text(t_before)
            ta_words, ta_tags = tag_title_text(t_after)
            final_words = tb_words + name_words + [","] + ta_words
            final_tags = tb_tags + name_tags + [O] + ta_tags
            
    elif chosen_pattern == "two_before":
        if len(TITLES_BEFORE) > 1:
            title1, title2 = random.sample(TITLES_BEFORE, 2)
            if should_corrupt:
                title1 = corrupt_title_string(title1)
                title2 = corrupt_title_string(title2)
            t1_words, t1_tags = tag_title_text(title1)
            t2_words, t2_tags = tag_title_text(title2)
            final_words = t1_words + t2_words + name_words
            final_tags = t1_tags + t2_tags + name_tags

    elif chosen_pattern == "two_after":
        if len(TITLES_AFTER) > 1:
            title1, title2 = random.sample(TITLES_AFTER, 2)
            if should_corrupt:
                title1 = corrupt_title_string(title1)
                title2 = corrupt_title_string(title2)
            t1_words, t1_tags = tag_title_text(title1)
            t2_words, t2_tags = tag_title_text(title2)
            final_words = name_words + [","] + t1_words + t2_words
            final_tags = name_tags + [O] + t1_tags + t2_tags
            
    elif chosen_pattern == "many_titles":
        # Reduce many_titles from 1-4 to 1-2 for less noise
        before_count = random.randint(1, 2)
        after_count = random.randint(1, 2)
        
        before_words, before_tags = [], []
        if len(TITLES_BEFORE) > 0:
            for _ in range(before_count):
                title = random.choice(TITLES_BEFORE)
                if should_corrupt:
                    title = corrupt_title_string(title)
                tw, tt = tag_title_text(title)
                before_words.extend(tw)
                before_tags.extend(tt)
        
        after_words, after_tags = [], []
        if len(TITLES_AFTER) > 0:
            for _ in range(after_count):
                title = random.choice(TITLES_AFTER)
                if should_corrupt:
                    title = corrupt_title_string(title)
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
                title = random.choice(common_titles)
                if should_corrupt:
                    title = corrupt_title_string(title)
                separator = random.choice(["et", "a"])
                title_words, title_tags = tag_title_text(title)
                final_words = title_words + [separator] + title_words + name_words
                final_tags = title_tags + [O] + title_tags + name_tags
    
    # Final fallback if something went wrong
    if not final_words:
        return name_text, " ".join(name_tags)
    
    # NO MERGING: Removed all merging patterns that caused title+name boundary issues
    # This prevents patterns like "ing. jan" becoming a single TIT entity
    # Commas and separators are kept as separate tokens with O tags
    return " ".join(final_words), " ".join(final_tags)
