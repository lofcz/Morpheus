"""
Data Quality Audit for NER Training Dataset

This script audits the training dataset to identify quality issues:
1. Label consistency: Same text with different tags
2. Augmentation noise: Title corruption, boundary stress tests
3. Entity distribution: Single vs multi-word entities, over-represented patterns
"""

import os
import argparse
import pandas as pd
import re
from collections import Counter, defaultdict
from typing import Dict, List, Tuple

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))


def check_label_consistency(df: pd.DataFrame) -> Dict:
    """
    Find cases where the same text has different tag sequences.
    """
    print("\n" + "="*60)
    print("LABEL CONSISTENCY ANALYSIS")
    print("="*60)
    
    # Group by text, collect unique tag sequences
    text_tags_map = defaultdict(set)
    for _, row in df.iterrows():
        text = str(row['text']).strip().lower()
        tags = str(row['tags']).strip()
        text_tags_map[text].add(tags)
    
    # Find inconsistencies
    inconsistent_texts = {text: tags for text, tags in text_tags_map.items() if len(tags) > 1}
    
    total_unique_texts = len(text_tags_map)
    inconsistent_count = len(inconsistent_texts)
    inconsistency_rate = inconsistent_count / total_unique_texts if total_unique_texts > 0 else 0.0
    
    print(f"\nTotal unique texts: {total_unique_texts:,}")
    print(f"Inconsistent texts: {inconsistent_count:,}")
    print(f"Inconsistency rate: {inconsistency_rate:.4f} ({inconsistency_rate*100:.2f}%)")
    
    # Show examples
    print("\nExample inconsistencies (first 10):")
    for i, (text, tag_sets) in enumerate(list(inconsistent_texts.items())[:10]):
        print(f"\n  Text: '{text}'")
        for tags in list(tag_sets)[:3]:  # Show up to 3 different tag sequences
            print(f"    Tags: {tags}")
    
    return {
        'total_unique_texts': total_unique_texts,
        'inconsistent_texts': inconsistent_count,
        'inconsistency_rate': inconsistency_rate,
        'examples': dict(list(inconsistent_texts.items())[:100])
    }


def analyze_punctuation_issues(df: pd.DataFrame, sample_size: int = 10000) -> Dict:
    """
    Analyze samples with punctuation to detect title corruption issues.
    """
    print("\n" + "="*60)
    print("PUNCTUATION & TITLE CORRUPTION ANALYSIS")
    print("="*60)
    
    # Sample texts with punctuation
    punct_samples = df[df['text'].str.contains(r'[.,;:]', regex=True, na=False)]
    if len(punct_samples) > sample_size:
        punct_samples = punct_samples.sample(sample_size, random_state=42)
    
    print(f"\nSampling {len(punct_samples):,} examples with punctuation...")
    
    # Patterns to detect problematic splits
    issues = defaultdict(int)
    problem_examples = defaultdict(list)
    
    for _, row in punct_samples.iterrows():
        text = str(row['text'])
        tags = str(row['tags']).split()
        words = text.split()
        
        if len(words) != len(tags):
            issues['length_mismatch'] += 1
            continue
        
        for i, (word, tag) in enumerate(zip(words, tags)):
            # Detect isolated punctuation with entity tags
            if word in ['.', ',', ';', ':', '-', '_']:
                if tag != 'O':
                    issues['punct_as_entity'] += 1
                    if len(problem_examples['punct_as_entity']) < 10:
                        problem_examples['punct_as_entity'].append((text, tags))
            
            # Detect split titles like "ph . d" or "p . h . d"
            if word == '.' and i > 0:
                prev_word = words[i-1]
                if len(prev_word) <= 3:  # Likely part of title abbreviation
                    issues['split_title'] += 1
                    if len(problem_examples['split_title']) < 10:
                        problem_examples['split_title'].append((text, tags))
            
            # Detect periods attached to words but tagged separately
            if '.' in word and len(word) > 1:
                # e.g., "ing." or "ph.d."
                if tag.endswith('TIT'):
                    # Check if it's split across multiple tokens
                    if i + 1 < len(words) and words[i+1].startswith('.'):
                        issues['fragmented_title'] += 1
                        if len(problem_examples['fragmented_title']) < 10:
                            problem_examples['fragmented_title'].append((text, tags))
    
    total_punct_samples = len(punct_samples)
    
    print(f"\nIssues found in {total_punct_samples:,} punctuation samples:")
    for issue_type, count in issues.items():
        rate = count / total_punct_samples if total_punct_samples > 0 else 0.0
        print(f"  {issue_type}: {count:,} ({rate*100:.2f}%)")
    
    print("\nExamples:")
    for issue_type, examples in problem_examples.items():
        print(f"\n  {issue_type.upper()} (showing {len(examples)} examples):")
        for text, tags in examples[:5]:
            print(f"    Text: {text}")
            print(f"    Tags: {' '.join(tags) if isinstance(tags, list) else tags}")
    
    return {
        'total_samples': total_punct_samples,
        'issues': dict(issues),
        'examples': {k: v[:20] for k, v in problem_examples.items()}
    }


def analyze_entity_distribution(df: pd.DataFrame, sample_size: int = 50000) -> Dict:
    """
    Analyze entity distribution in the dataset.
    """
    print("\n" + "="*60)
    print("ENTITY DISTRIBUTION ANALYSIS")
    print("="*60)
    
    # Sample for efficiency
    sample_df = df.sample(min(sample_size, len(df)), random_state=42)
    
    print(f"\nAnalyzing {len(sample_df):,} samples...")
    
    # Track entity statistics
    entity_counts = Counter()
    entity_lengths = defaultdict(list)  # entity_type -> [lengths]
    subtoken_counts = []  # number of subtokens per entity
    tag_patterns = Counter()  # unique tag sequences
    
    for _, row in sample_df.iterrows():
        text = str(row['text'])
        tags = str(row['tags']).split()
        words = text.split()
        
        if len(words) != len(tags):
            continue
        
        # Count tag pattern
        tag_pattern = ' '.join(tags)
        tag_patterns[tag_pattern] += 1
        
        # Extract entities
        current_entity_type = None
        current_entity_words = []
        
        for word, tag in zip(words, tags):
            if tag.startswith('B-'):
                # Save previous entity
                if current_entity_type:
                    entity_counts[current_entity_type] += 1
                    entity_lengths[current_entity_type].append(len(current_entity_words))
                
                # Start new entity
                current_entity_type = tag[2:]
                current_entity_words = [word]
                
            elif tag.startswith('I-'):
                entity_type = tag[2:]
                if current_entity_type == entity_type:
                    current_entity_words.append(word)
                else:
                    # Inconsistent I- tag, treat as new entity
                    if current_entity_type:
                        entity_counts[current_entity_type] += 1
                        entity_lengths[current_entity_type].append(len(current_entity_words))
                    current_entity_type = entity_type
                    current_entity_words = [word]
            else:
                # O tag
                if current_entity_type:
                    entity_counts[current_entity_type] += 1
                    entity_lengths[current_entity_type].append(len(current_entity_words))
                    current_entity_type = None
                    current_entity_words = []
        
        # Don't forget last entity
        if current_entity_type:
            entity_counts[current_entity_type] += 1
            entity_lengths[current_entity_type].append(len(current_entity_words))
    
    # Print statistics
    print("\nEntity type distribution:")
    total_entities = sum(entity_counts.values())
    for entity_type, count in entity_counts.most_common():
        rate = count / total_entities if total_entities > 0 else 0.0
        print(f"  {entity_type}: {count:,} ({rate*100:.2f}%)")
    
    print("\nEntity length statistics (words per entity):")
    for entity_type in sorted(entity_lengths.keys()):
        lengths = entity_lengths[entity_type]
        if lengths:
            avg_len = sum(lengths) / len(lengths)
            single_word = sum(1 for l in lengths if l == 1)
            multi_word = len(lengths) - single_word
            print(f"  {entity_type}:")
            print(f"    Average length: {avg_len:.2f} words")
            print(f"    Single-word: {single_word:,} ({single_word/len(lengths)*100:.1f}%)")
            print(f"    Multi-word: {multi_word:,} ({multi_word/len(lengths)*100:.1f}%)")
    
    print(f"\nMost common tag patterns (top 20):")
    for pattern, count in tag_patterns.most_common(20):
        rate = count / len(sample_df) if len(sample_df) > 0 else 0.0
        print(f"  {count:,} ({rate*100:.2f}%): {pattern[:80]}")
    
    return {
        'total_entities': total_entities,
        'entity_counts': dict(entity_counts),
        'entity_avg_lengths': {
            etype: sum(lengths) / len(lengths) if lengths else 0.0
            for etype, lengths in entity_lengths.items()
        },
        'top_patterns': dict(tag_patterns.most_common(100))
    }


def analyze_boundary_stress_tests(df: pd.DataFrame, sample_size: int = 10000) -> Dict:
    """
    Detect samples from boundary_stress_test strategy that might be unrealistic.
    """
    print("\n" + "="*60)
    print("BOUNDARY STRESS TEST ANALYSIS")
    print("="*60)
    
    # Look for patterns: single-word samples, concatenations, etc.
    single_word_samples = df[df['text'].str.split().str.len() == 1]
    
    print(f"\nTotal single-word samples: {len(single_word_samples):,} ({len(single_word_samples)/len(df)*100:.2f}%)")
    
    # Sample and check for concatenation patterns
    if len(single_word_samples) > sample_size:
        sample = single_word_samples.sample(sample_size, random_state=42)
    else:
        sample = single_word_samples
    
    # Look for suspicious patterns
    issues = defaultdict(int)
    examples = defaultdict(list)
    
    for _, row in sample.iterrows():
        text = str(row['text']).strip()
        tags = str(row['tags']).strip()
        
        # Detect concatenated words (no spaces, mixed case, etc.)
        if len(text) > 15 and ' ' not in text:
            issues['long_concatenation'] += 1
            if len(examples['long_concatenation']) < 10:
                examples['long_concatenation'].append((text, tags))
        
        # Detect mixed case (boundary stress internal_caps pattern)
        if any(c.isupper() for c in text[1:]):  # Capital letter not at start
            issues['internal_capitals'] += 1
            if len(examples['internal_capitals']) < 10:
                examples['internal_capitals'].append((text, tags))
        
        # Detect punctuation-only or very short
        if len(text) <= 2 and text not in ['cz', 'eu', 'uk']:
            issues['very_short'] += 1
            if len(examples['very_short']) < 10:
                examples['very_short'].append((text, tags))
    
    print(f"\nIssues in {len(sample):,} single-word samples:")
    for issue_type, count in issues.items():
        rate = count / len(sample) if len(sample) > 0 else 0.0
        print(f"  {issue_type}: {count:,} ({rate*100:.2f}%)")
    
    print("\nExamples:")
    for issue_type, ex_list in examples.items():
        print(f"\n  {issue_type.upper()}:")
        for text, tags in ex_list[:5]:
            print(f"    '{text}' -> {tags}")
    
    return {
        'single_word_samples': len(single_word_samples),
        'issues': dict(issues),
        'examples': {k: v[:20] for k, v in examples.items()}
    }


def main():
    parser = argparse.ArgumentParser(description="Audit NER training data quality")
    parser.add_argument("--dataset", default=os.path.join(SCRIPT_DIR, "data", "dataset_small.csv"))
    parser.add_argument("--output", default=None, help="Save audit results to JSON file")
    parser.add_argument("--max-samples", type=int, default=None, help="Limit dataset size for faster audit")
    
    args = parser.parse_args()
    
    print(f"\n{'='*60}")
    print(f"DATASET QUALITY AUDIT")
    print(f"{'='*60}")
    print(f"Dataset: {args.dataset}")
    
    # Load dataset
    print("\nLoading dataset...")
    df = pd.read_csv(args.dataset)
    if args.max_samples:
        df = df.sample(min(args.max_samples, len(df)), random_state=42)
    print(f"Loaded {len(df):,} samples")
    
    # Run all audits
    results = {}
    
    results['label_consistency'] = check_label_consistency(df)
    results['punctuation_analysis'] = analyze_punctuation_issues(df)
    results['entity_distribution'] = analyze_entity_distribution(df)
    results['boundary_stress_tests'] = analyze_boundary_stress_tests(df)
    
    # Summary
    print("\n" + "="*60)
    print("AUDIT SUMMARY")
    print("="*60)
    print(f"\nDataset size: {len(df):,} samples")
    print(f"Inconsistency rate: {results['label_consistency']['inconsistency_rate']*100:.2f}%")
    print(f"Single-word samples: {results['boundary_stress_tests']['single_word_samples']:,}")
    
    # Save results
    if args.output:
        import json
        # Convert to JSON-serializable format
        json_results = {}
        for key, value in results.items():
            json_results[key] = {
                k: v for k, v in value.items()
                if k != 'examples'  # Skip examples for JSON (too large)
            }
        with open(args.output, 'w') as f:
            json.dump(json_results, f, indent=2)
        print(f"\nAudit results saved to: {args.output}")


if __name__ == "__main__":
    main()


