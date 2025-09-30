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
    
    # --- Decide on the complexity of the title pattern ---
    pattern_roll = random.random()

    final_words = []
    final_tags = []

    # Case 1: Simple, single title (most common) - 40% chance
    if pattern_roll < 0.6:
        place_it_wrong = random.random() < 0.20
        place_before = random.random() < 0.7 

        if not place_it_wrong:
            if place_before and TITLES_BEFORE:
                title = corrupt_title_string(random.choice(TITLES_BEFORE))
                title_words, title_tags = tag_title_text(title)
                # 20% chance to add comma after title before name (e.g., "Prof., Jan Novák")
                if random.random() < 0.2:
                    final_words = title_words + [","] + name_words
                    final_tags = title_tags + [O] + name_tags
                else:
                    final_words = title_words + name_words
                    final_tags = title_tags + name_tags
            elif TITLES_AFTER:
                title = corrupt_title_string(random.choice(TITLES_AFTER))
                title_words, title_tags = tag_title_text(title)
                final_words = name_words + [","] + title_words
                final_tags = name_tags + [O] + title_tags
        else: # Intentionally place it wrong
            if place_before and TITLES_BEFORE:
                # Wrongly place before-name title after the name
                title = corrupt_title_string(random.choice(TITLES_BEFORE))
                title_words, title_tags = tag_title_text(title)
                final_words = name_words + [","] + title_words
                final_tags = name_tags + [O] + title_tags
            elif TITLES_AFTER:
                # Wrongly place after-name title before the name
                title = corrupt_title_string(random.choice(TITLES_AFTER))
                title_words, title_tags = tag_title_text(title)
                final_words = title_words + name_words
                final_tags = title_tags + name_tags

    # Case 2: Two titles (less common) - 30% chance
    elif pattern_roll < 0.9 and len(TITLES_BEFORE) > 1 and len(TITLES_AFTER) > 1:
        sub_pattern = random.choice(["before_before", "after_after", "before_after"])

        if sub_pattern == "before_before":
            title1, title2 = random.sample(TITLES_BEFORE, 2)
            t1_words, t1_tags = tag_title_text(corrupt_title_string(title1))
            t2_words, t2_tags = tag_title_text(corrupt_title_string(title2))
            final_words = t1_words + t2_words + name_words
            final_tags = t1_tags + t2_tags + name_tags
        elif sub_pattern == "after_after":
            title1, title2 = random.sample(TITLES_AFTER, 2)
            t1_words, t1_tags = tag_title_text(corrupt_title_string(title1))
            t2_words, t2_tags = tag_title_text(corrupt_title_string(title2))
            final_words = name_words + [","] + t1_words + t2_words
            final_tags = name_tags + [O] + t1_tags + t2_tags
        else: # "before_after"
            title1 = random.choice(TITLES_BEFORE)
            title2 = random.choice(TITLES_AFTER)
            t1_words, t1_tags = tag_title_text(corrupt_title_string(title1))
            t2_words, t2_tags = tag_title_text(corrupt_title_string(title2))
            final_words = t1_words + name_words + [","] + t2_words
            final_tags = t1_tags + name_tags + [O] + t2_tags
            
    # Case 3: Many titles (3–8 total), mix before and after, commas optional - 8% chance
    elif pattern_roll < 0.98:
        # Decide how many total titles
        total_titles = random.randint(3, 8)
        # Prefer both sides populated
        before_count = random.randint(1, max(1, total_titles - 1))
        after_count = total_titles - before_count

        # Build before titles (mostly from TITLES_BEFORE, with some wrong placements)
        before_words: list[str] = []
        before_tags: list[str] = []
        for _ in range(before_count):
            src_list = TITLES_BEFORE if random.random() > 0.2 else TITLES_AFTER
            title = corrupt_title_string(random.choice(src_list))
            tw, tt = tag_title_text(title)
            # Occasionally separate multiple titles with comma
            if before_words and random.random() < 0.3:
                before_words += [","]
                before_tags += [O]
            before_words += tw
            before_tags += tt

        # Build after titles (mostly from TITLES_AFTER, with some wrong placements)
        after_words: list[str] = []
        after_tags: list[str] = []
        for _ in range(after_count):
            src_list = TITLES_AFTER if random.random() > 0.2 else TITLES_BEFORE
            title = corrupt_title_string(random.choice(src_list))
            tw, tt = tag_title_text(title)
            # Separate multiple after titles optionally with comma
            if after_words and random.random() < 0.6:
                after_words += [","]
                after_tags += [O]
            after_words += tw
            after_tags += tt

        # Optionally place a comma between name and after titles (but sometimes not, to allow patterns like 'ing. name phd')
        place_name_after_comma = random.random() < 0.6

        final_words = before_words + name_words + ([","] if (after_words and place_name_after_comma) else []) + after_words
        final_tags = before_tags + name_tags + ([O] if (after_words and place_name_after_comma) else []) + after_tags

    # Case 4: Duplicated title (e.g., Mgr. et Mgr.) - remaining chance
    else:
        if TITLES_BEFORE:
            title = corrupt_title_string(random.choice(["Mgr.", "Ing."]))
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
                merged_tags.append(next_tag)
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
