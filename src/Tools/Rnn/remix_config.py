"""
Strategy Configuration for NER Dataset Generation

This module defines the available strategies, their weights, and data requirements.
Adjust weights here to change the composition of the generated dataset.
"""

from strategies import (
    remix_gibberish, remix_single_city, remix_single_per,
    remix_hyphenated_name, remix_o_phrase, remix_nick_handle,
    remix_loc_prep_city, remix_org_with_context, remix_single_entity,
    remix_name_with_random_words, remix_name_with_title,
    remix_name_with_nickname, remix_name_and_location,
    remix_company_with_suffix, remix_two_names, remix_email,
    remix_name_and_company, remix_name_company_patterns,
    remix_company_with_multiple_names, remix_name_orgtype_company,
    remix_single_org, remix_entity_in_context, remix_common_phrase,
    remix_multi_word_nickname, remix_boundary_stress_test,
    remix_adjacent_entities
)

# --- Strategy Definitions ---
# Each strategy is a function that returns a (text, tags) tuple.
# The `weight` determines how often each strategy is chosen.
REMIX_STRATEGIES = [
    # --- HIGH-PRIORITY NEGATIVE EXAMPLES & CONTEXT ---
    {
        "name": "remix_common_phrase",
        "weight": 0.25, # NEW: High weight for realistic non-entity phrases
        "func": remix_common_phrase
    },
    {
        "name": "remix_o_phrase",
        "weight": 0.10, # Reduced weight, but still important for single non-entity words
        "func": remix_o_phrase
    },
    {
        "name": "remix_single_org",
        "weight": 0.15, # High weight for clean, single-token orgs
        "func": remix_single_org
    },
    {
        "name": "remix_entity_in_context",
        "weight": 0.15, # High weight for contextualized entities
        "func": remix_entity_in_context
    },

    # --- Foundational single-entity strategies ---
    {
        "name": "remix_single_entity",
        "weight": 0.05,
        "func": remix_single_entity
    },
    {"func": remix_two_names, "weight": 0.08},
    {"func": remix_name_and_company, "weight": 0.10},  # Kept for its unique patterns
    
    # Czech-specific and common variations
    {"func": remix_name_with_title, "weight": 0.08},
    {"func": remix_name_with_nickname, "weight": 0.05},
    {"func": remix_company_with_suffix, "weight": 0.08},  # Kept for person -> org pattern
    {"func": remix_email, "weight": 0.05},
    {"func": remix_name_and_location, "weight": 0.05},
    {"func": remix_gibberish, "weight": 0.03},
    {"func": remix_single_per, "weight": 0.03},
    {"func": remix_hyphenated_name, "weight": 0.03},
    {"func": remix_o_phrase, "weight": 0.03},
    {"func": remix_nick_handle, "weight": 0.03},
    {"func": remix_multi_word_nickname, "weight": 0.05}, # NEW
    {"func": remix_boundary_stress_test, "weight": 0.10}, # NEW
    {"func": remix_adjacent_entities, "weight": 0.10}, # NEW
    {"func": remix_loc_prep_city, "weight": 0.03},
    {"func": remix_org_with_context, "weight": 0.03},
    {"func": remix_name_orgtype_company, "weight": 0.08},  # Name + org type + company
    {"func": remix_name_with_random_words, "weight": 0.10},  # Name with random O words

    # Complex patterns
    {"func": remix_name_company_patterns, "weight": 0.15},
    {"func": remix_company_with_multiple_names, "weight": 0.05},
    {"func": remix_single_city, "weight": 0.03},
]

# --- Strategy Requirements ---
# This dictionary specifies what data each strategy needs to run.
# This allows the main script to dynamically activate/deactivate strategies
# based on the available source data.
STRATEGY_REQUIREMENTS = {
    remix_gibberish: [],
    remix_single_city: ["cities"],
    remix_single_per: ["single_names"],
    remix_hyphenated_name: ["names"],
    remix_o_phrase: ["o_phrase_words"],
    remix_common_phrase: ["o_phrase_words"], # NEW
    remix_nick_handle: ["nicknames"],
    remix_multi_word_nickname: ["nicknames"], # NEW
    remix_boundary_stress_test: ["names", "companies", "cities"], # NEW
    remix_adjacent_entities: ["names", "companies", "cities"], # NEW
    remix_loc_prep_city: ["cities"],
    remix_org_with_context: ["companies"],
    remix_single_entity: ["names", "companies", "nicknames"],
    remix_name_with_random_words: ["names", "o_phrase_words"],
    remix_name_with_title: ["names"],
    remix_name_with_nickname: ["names", "nicknames"],
    remix_name_and_location: ["names", "cities"],
    remix_company_with_suffix: ["names", "companies"],
    remix_two_names: ["names"],
    remix_email: ["names"],
    remix_name_and_company: ["names", "companies"],
    remix_name_company_patterns: ["names", "companies"],
    remix_company_with_multiple_names: ["names", "companies"],
    remix_name_orgtype_company: ["names", "companies", "org_types"],
    remix_single_org: ["companies"],
    remix_entity_in_context: ["names", "companies", "cities"],
}

# Special validation conditions for strategies
# These are checked before the general requirements
STRATEGY_SPECIAL_CONDITIONS = {
    remix_two_names: lambda data: len(data.get("names", [])) >= 2,
    remix_company_with_multiple_names: lambda data: len(data.get("names", [])) >= 2 and bool(data.get("companies")),
    remix_name_with_nickname: lambda data: bool(data.get("names")) and bool(data.get("nicknames")),
    remix_name_and_location: lambda data: bool(data.get("names")) and bool(data.get("cities")),
    remix_single_city: lambda data: bool(data.get("cities")),
    remix_single_per: lambda data: bool(data.get("single_names")),
    remix_hyphenated_name: lambda data: bool(data.get("names")),
    remix_o_phrase: lambda data: bool(data.get("o_phrase_words")),
    remix_common_phrase: lambda data: bool(data.get("o_phrase_words")), # NEW
    remix_nick_handle: lambda data: bool(data.get("nicknames")),
    remix_multi_word_nickname: lambda data: len(data.get("nicknames", [])) >= 2, # NEW
    remix_boundary_stress_test: lambda data: any(data.get(k) for k in ["names", "companies", "cities"]), # NEW
    remix_adjacent_entities: lambda data: all(data.get(k) for k in ["names", "companies", "cities"]), # NEW
    remix_loc_prep_city: lambda data: bool(data.get("cities")),
    remix_org_with_context: lambda data: bool(data.get("companies")),
    remix_name_with_random_words: lambda data: bool(data.get("names")) and bool(data.get("o_phrase_words")),
}


def get_active_strategies(available_data: dict) -> tuple[list, list]:
    """
    Filter strategies based on available data and return active strategies with normalized weights.
    
    Args:
        available_data: Dictionary mapping data keys to their values (e.g., {"names": [...], "companies": [...]})
    
    Returns:
        Tuple of (population, weights) where population is list of strategy functions and weights is list of floats
    """
    active_strategies = []
    
    for strategy_def in REMIX_STRATEGIES:
        func = strategy_def["func"]
        
        # Check special conditions first
        if func in STRATEGY_SPECIAL_CONDITIONS:
            if not STRATEGY_SPECIAL_CONDITIONS[func](available_data):
                continue
        
        # Check general requirements
        required_data = STRATEGY_REQUIREMENTS.get(func, [])
        if all(available_data.get(req) for req in required_data):
            active_strategies.append(strategy_def)
    
    # Normalize weights
    total_weight = sum(s["weight"] for s in active_strategies)
    if total_weight > 0:
        for s in active_strategies:
            s["weight"] /= total_weight
    
    population = [s["func"] for s in active_strategies]
    weights = [s["weight"] for s in active_strategies]
    
    return population, weights
