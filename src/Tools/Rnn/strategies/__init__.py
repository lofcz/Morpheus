"""
NER Dataset Generation Strategies

This module contains all the individual strategy functions for generating
synthetic NER training data.
"""

# Import all strategies
from .gibberish import remix_gibberish
from .single_city import remix_single_city
from .single_per import remix_single_per
from .hyphenated_name import remix_hyphenated_name
from .o_phrase import remix_o_phrase
from .nick_handle import remix_nick_handle
from .loc_prep_city import remix_loc_prep_city
from .org_with_context import remix_org_with_context
from .single_entity import remix_single_entity
from .name_with_random_words import remix_name_with_random_words
from .name_with_title import remix_name_with_title
from .name_with_nickname import remix_name_with_nickname
from .name_and_location import remix_name_and_location
from .company_with_suffix import remix_company_with_suffix
from .two_names import remix_two_names
from .email import remix_email
from .name_and_company import remix_name_and_company
from .name_company_patterns import remix_name_company_patterns
from .company_with_multiple_names import remix_company_with_multiple_names
from .name_orgtype_company import remix_name_orgtype_company
from .single_org import remix_single_org
from .entity_in_context import remix_entity_in_context
from .common_phrase import remix_common_phrase
from .multi_word_nickname import remix_multi_word_nickname
from .boundary_stress_test import remix_boundary_stress_test
from .adjacent_entities import remix_adjacent_entities
from .surname_comma_name import remix_surname_comma_name

__all__ = [
    'remix_gibberish',
    'remix_single_city',
    'remix_single_per',
    'remix_hyphenated_name',
    'remix_o_phrase',
    'remix_nick_handle',
    'remix_loc_prep_city',
    'remix_org_with_context',
    'remix_single_entity',
    'remix_name_with_random_words',
    'remix_name_with_title',
    'remix_name_with_nickname',
    'remix_name_and_location',
    'remix_company_with_suffix',
    'remix_two_names',
    'remix_email',
    'remix_name_and_company',
    'remix_name_company_patterns',
    'remix_company_with_multiple_names',
    'remix_name_orgtype_company',
    'remix_single_org',
    'remix_entity_in_context',
    'remix_common_phrase',
    'remix_multi_word_nickname',
    'remix_boundary_stress_test',
    'remix_adjacent_entities',
    'remix_surname_comma_name',
]