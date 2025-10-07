"""
Surname-comma-name strategy - generates Czech standard format: "Surname, Name"
This is critical for official document parsing where a single person is written as "Novák, Petr"
"""
import random
from typing import List, Tuple
from .utils import tag_entity, tag_title_text, TITLES_BEFORE, TITLES_AFTER, O


def remix_surname_comma_name(names: List[str]) -> Tuple[str, str]:
    """
    Creates single-person name in Czech official format: "Surname, Name"
    Comprehensive patterns for Czech official documents, academic contexts, and business cards.
    
    Examples:
      - "Novák, Petr" (basic)
      - "Novák-Svoboda, Jan Marie" (compound surname + multiple first names)
      - "Novák, P." (initial)
      - "Novák, P. M." (multiple initials)
      - "Ing. Novák, Petr" (title before)
      - "Novák, Petr, Ph.D." (title after)
      - "Ing. Novák, Petr, Ph.D." (titles both sides)
      - "Nováková (Svobodová), Jana" (maiden name)
      - "Novák, Petr M." (middle initial)
    """
    if not names:
        return "", ""
    
    name_text = random.choice(names)
    parts = name_text.split()
    
    # Need at least 2 parts to create surname,name pattern
    if len(parts) < 2:
        return "", ""
    
    # Weighted pattern distribution
    patterns = [
        ("basic", 0.20),                      # Novák, Petr
        ("multiple_first_names", 0.15),       # Novák, Jan Marie
        ("single_initial", 0.10),             # Novák, P.
        ("multiple_initials", 0.08),          # Novák, P. M.
        ("compound_surname_hyphen", 0.08),    # Novák-Svoboda, Petr
        ("compound_surname_space", 0.05),     # Novák Svoboda, Petr
        ("title_before", 0.10),               # Ing. Novák, Petr
        ("title_after", 0.08),                # Novák, Petr, Ph.D.
        ("title_both_sides", 0.05),           # Ing. Novák, Petr, Ph.D.
        ("middle_initial", 0.05),             # Novák, Petr M.
        ("mixed_initial_name", 0.03),         # Novák, P. Marie
        ("maiden_name", 0.02),                # Nováková (Svobodová), Jana
        ("suffix", 0.01),                     # Novák, Petr Jr.
    ]
    
    pattern_names = [p[0] for p in patterns]
    pattern_weights = [p[1] for p in patterns]
    pattern = random.choices(pattern_names, weights=pattern_weights, k=1)[0]
    
    surname = parts[-1]
    firstname = parts[0]
    middle_parts = parts[1:-1] if len(parts) > 2 else []
    
    # BASIC PATTERNS
    if pattern == "basic":
        text = f"{surname}, {firstname}"
        words, tags = tag_entity(text, "PER")
        return " ".join(words), " ".join(tags)
    
    elif pattern == "multiple_first_names":
        # Use all non-surname parts as first names
        firstnames = " ".join(parts[:-1])
        text = f"{surname}, {firstnames}"
        words, tags = tag_entity(text, "PER")
        return " ".join(words), " ".join(tags)
    
    # INITIAL PATTERNS
    elif pattern == "single_initial":
        initial = firstname[0].upper() + "."
        text = f"{surname}, {initial}"
        words, tags = tag_entity(text, "PER")
        return " ".join(words), " ".join(tags)
    
    elif pattern == "multiple_initials":
        # Create 2-3 initials
        num_initials = random.randint(2, min(3, len(parts)))
        initials = []
        for i in range(num_initials):
            if i < len(parts) - 1:  # Don't use surname
                initials.append(parts[i][0].upper() + ".")
        initials_str = " ".join(initials)
        text = f"{surname}, {initials_str}"
        words, tags = tag_entity(text, "PER")
        return " ".join(words), " ".join(tags)
    
    elif pattern == "middle_initial":
        # First name + middle initial
        if middle_parts:
            middle_initial = middle_parts[0][0].upper() + "."
            text = f"{surname}, {firstname} {middle_initial}"
        else:
            # Generate random middle initial if no middle name
            middle_initial = random.choice("ABCDEFGHIJKLMNOPQRSTUVWXYZ") + "."
            text = f"{surname}, {firstname} {middle_initial}"
        words, tags = tag_entity(text, "PER")
        return " ".join(words), " ".join(tags)
    
    elif pattern == "mixed_initial_name":
        # Initial + full name (e.g., "P. Marie")
        initial = firstname[0].upper() + "."
        if middle_parts:
            text = f"{surname}, {initial} {middle_parts[0]}"
        else:
            # Fallback to basic
            text = f"{surname}, {firstname}"
        words, tags = tag_entity(text, "PER")
        return " ".join(words), " ".join(tags)
    
    # COMPOUND SURNAME PATTERNS
    elif pattern == "compound_surname_hyphen":
        if len(parts) >= 3:
            # Use last two parts as compound surname
            compound = f"{parts[-2]}-{parts[-1]}"
            text = f"{compound}, {firstname}"
        else:
            # Create compound from two random names
            other_name = random.choice(names)
            other_parts = other_name.split()
            if other_parts:
                compound = f"{surname}-{other_parts[-1]}"
            else:
                compound = surname
            text = f"{compound}, {firstname}"
        words, tags = tag_entity(text, "PER")
        return " ".join(words), " ".join(tags)
    
    elif pattern == "compound_surname_space":
        # Multiple surnames without hyphen (e.g., "García Márquez, Gabriel")
        if len(parts) >= 3:
            compound = f"{parts[-2]} {parts[-1]}"
            text = f"{compound}, {firstname}"
        else:
            other_name = random.choice(names)
            other_parts = other_name.split()
            if other_parts:
                compound = f"{surname} {other_parts[-1]}"
            else:
                compound = surname
            text = f"{compound}, {firstname}"
        words, tags = tag_entity(text, "PER")
        return " ".join(words), " ".join(tags)
    
    # TITLE PATTERNS
    elif pattern == "title_before":
        if TITLES_BEFORE:
            title = random.choice(TITLES_BEFORE)
            # Simplify title (remove spaces for common titles)
            if random.random() < 0.5 and " " not in title:
                title = title.replace(".", "")  # "Ing" instead of "Ing."
            title_words, title_tags = tag_title_text(title)
            text = f"{surname}, {firstname}"
            name_words, name_tags = tag_entity(text, "PER")
            final_words = title_words + name_words
            final_tags = title_tags + name_tags
            return " ".join(final_words), " ".join(final_tags)
        else:
            # Fallback to basic
            text = f"{surname}, {firstname}"
            words, tags = tag_entity(text, "PER")
            return " ".join(words), " ".join(tags)
    
    elif pattern == "title_after":
        if TITLES_AFTER:
            title = random.choice(TITLES_AFTER)
            # Sometimes remove dots
            if random.random() < 0.3:
                title = title.replace(".", "")
            text = f"{surname}, {firstname}"
            name_words, name_tags = tag_entity(text, "PER")
            title_words, title_tags = tag_title_text(title)
            # Add comma before title
            final_words = name_words + [","] + title_words
            final_tags = name_tags + [O] + title_tags
            return " ".join(final_words), " ".join(final_tags)
        else:
            text = f"{surname}, {firstname}"
            words, tags = tag_entity(text, "PER")
            return " ".join(words), " ".join(tags)
    
    elif pattern == "title_both_sides":
        if TITLES_BEFORE and TITLES_AFTER:
            title_before = random.choice(TITLES_BEFORE)
            title_after = random.choice(TITLES_AFTER)
            # Sometimes simplify
            if random.random() < 0.3:
                title_before = title_before.replace(".", "")
            if random.random() < 0.3:
                title_after = title_after.replace(".", "")
            tb_words, tb_tags = tag_title_text(title_before)
            text = f"{surname}, {firstname}"
            name_words, name_tags = tag_entity(text, "PER")
            ta_words, ta_tags = tag_title_text(title_after)
            final_words = tb_words + name_words + [","] + ta_words
            final_tags = tb_tags + name_tags + [O] + ta_tags
            return " ".join(final_words), " ".join(final_tags)
        else:
            text = f"{surname}, {firstname}"
            words, tags = tag_entity(text, "PER")
            return " ".join(words), " ".join(tags)
    
    # SPECIAL PATTERNS
    elif pattern == "maiden_name":
        # Female surname with maiden name in parentheses
        # Convert surname to female form (add -ová) if it doesn't end with it
        if not surname.endswith("ová") and not surname.endswith("ová"):
            if surname.endswith("ý"):
                female_surname = surname[:-1] + "á"
            elif surname.endswith("í"):
                female_surname = surname  # No change for -í
            else:
                female_surname = surname + "ová"
        else:
            female_surname = surname
        
        # Create maiden name from another surname
        other_name = random.choice(names)
        other_parts = other_name.split()
        if other_parts:
            maiden_surname = other_parts[-1]
            # Make it female too
            if not maiden_surname.endswith("ová"):
                if maiden_surname.endswith("ý"):
                    maiden_surname = maiden_surname[:-1] + "á"
                elif maiden_surname.endswith("í"):
                    pass
                else:
                    maiden_surname = maiden_surname + "ová"
        else:
            maiden_surname = "Svobodová"
        
        text = f"{female_surname} ({maiden_surname}), {firstname}"
        words, tags = tag_entity(text, "PER")
        return " ".join(words), " ".join(tags)
    
    elif pattern == "suffix":
        # Add suffix like Jr., Sr., III
        suffixes = ["Jr.", "Sr.", "II", "III", "IV"]
        suffix = random.choice(suffixes)
        text = f"{surname}, {firstname} {suffix}"
        words, tags = tag_entity(text, "PER")
        return " ".join(words), " ".join(tags)
    
    # Fallback
    text = f"{surname}, {firstname}"
    words, tags = tag_entity(text, "PER")
    return " ".join(words), " ".join(tags)

