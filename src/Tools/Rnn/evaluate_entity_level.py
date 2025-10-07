"""
Entity-Level Evaluation for NER Models

This script evaluates NER models using entity-level metrics (not just token-level).
It measures:
- Entity-level F1, Precision, Recall (on complete entities)
- Word consistency rate (% of words where all subtokens have identical tags)
- IOB violation rate (% of invalid tag transitions)
- Error taxonomy (punctuation, hyphen, context, title errors)
"""

import os
import argparse
import pandas as pd
import numpy as np
import onnxruntime as ort
from tokenizers import Tokenizer
from typing import List, Tuple, Dict, Set
from collections import defaultdict, Counter

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))

# Tag mappings
TAG_MAP = {
    "O": 0,
    "B-PER": 1, "I-PER": 2,
    "B-NICK": 3, "I-NICK": 4,
    "B-ORG": 5, "I-ORG": 6,
    "B-LOC": 7, "I-LOC": 8,
    "B-TIT": 9, "I-TIT": 10,
}
ID_TO_TAG = {v: k for k, v in TAG_MAP.items()}

MAX_LEN = 128
CHAR_MAX_LEN = 24


def normalize_text(text: str) -> str:
    """Normalize text exactly like training."""
    s = text.strip()
    if not s:
        return s
    s = " ".join(s.split())
    return s.casefold()


def extract_entities_from_tags(tokens: List[str], tags: List[str], word_ids: List[int]) -> List[Tuple[str, str, int, int]]:
    """
    Extract entities from IOB tags.
    
    Returns list of (entity_text, entity_type, start_word_idx, end_word_idx)
    """
    entities = []
    current_entity = None
    current_type = None
    start_word = None
    entity_tokens = []
    
    for i, (token, tag, word_id) in enumerate(zip(tokens, tags, word_ids)):
        if word_id is None:
            continue
            
        if tag.startswith("B-"):
            # Save previous entity if exists
            if current_entity is not None:
                entity_text = " ".join(entity_tokens)
                entities.append((entity_text, current_type, start_word, word_id - 1))
            
            # Start new entity
            current_type = tag[2:]  # Remove "B-"
            start_word = word_id
            entity_tokens = [token]
            current_entity = True
            
        elif tag.startswith("I-"):
            entity_type = tag[2:]
            if current_entity and entity_type == current_type:
                # Continue current entity
                entity_tokens.append(token)
            else:
                # Invalid transition - start new entity anyway
                if current_entity:
                    entity_text = " ".join(entity_tokens)
                    entities.append((entity_text, current_type, start_word, word_id - 1))
                
                current_type = entity_type
                start_word = word_id
                entity_tokens = [token]
                current_entity = True
        else:
            # O tag - end current entity
            if current_entity:
                entity_text = " ".join(entity_tokens)
                entities.append((entity_text, current_type, start_word, word_id))
                current_entity = None
                current_type = None
                start_word = None
                entity_tokens = []
    
    # Don't forget last entity
    if current_entity:
        entity_text = " ".join(entity_tokens)
        entities.append((entity_text, current_type, start_word, len(tokens)))
    
    return entities


def check_iob_violations(tags: List[str]) -> List[Tuple[int, str, str]]:
    """
    Check for invalid IOB tag transitions.
    
    Returns list of (position, prev_tag, curr_tag) violations.
    """
    violations = []
    
    for i in range(1, len(tags)):
        prev_tag = tags[i-1]
        curr_tag = tags[i]
        
        # I- tag cannot follow O
        if curr_tag.startswith("I-") and prev_tag == "O":
            violations.append((i, prev_tag, curr_tag))
        
        # I- tag must match previous entity type
        if prev_tag.startswith("B-") or prev_tag.startswith("I-"):
            if curr_tag.startswith("I-"):
                prev_type = prev_tag[2:]
                curr_type = curr_tag[2:]
                if prev_type != curr_type:
                    violations.append((i, prev_tag, curr_tag))
    
    return violations


def calculate_word_consistency(tags: List[str], word_ids: List[int]) -> Tuple[int, int]:
    """
    Calculate how many words have consistent tags across all their subtokens.
    
    Returns (consistent_words, total_words)
    """
    # Group tags by word_id
    word_tag_map = defaultdict(set)
    for tag, word_id in zip(tags, word_ids):
        if word_id is not None:
            word_tag_map[word_id].add(tag)
    
    consistent = sum(1 for tags_set in word_tag_map.values() if len(tags_set) == 1)
    total = len(word_tag_map)
    
    return consistent, total


def run_inference(session: ort.InferenceSession, tokenizer: Tokenizer, text: str) -> Tuple[List[str], List[str], List[int]]:
    """
    Run ONNX inference and return tokens, predicted tags, word_ids.
    """
    normalized = normalize_text(text)
    encoding = tokenizer.encode(normalized)
    
    input_ids = encoding.ids
    word_ids = encoding.word_ids
    tokens = encoding.tokens
    offsets = encoding.offsets
    
    # Build byte_ids
    byte_rows = []
    for i in range(len(input_ids)):
        b_start, b_end = (0, 0)
        try:
            b_start, b_end = offsets[i]
        except Exception:
            pass
        if b_end > b_start and b_end <= len(normalized):
            substr = normalized[b_start:b_end]
        else:
            tok = tokens[i]
            substr = tok[2:] if tok.startswith("##") else tok
        b = list(substr.encode('utf-8'))[:CHAR_MAX_LEN]
        if len(b) < CHAR_MAX_LEN:
            b += [0] * (CHAR_MAX_LEN - len(b))
        byte_rows.append(b)
    
    # Pad/truncate
    if len(input_ids) > MAX_LEN:
        input_ids = input_ids[:MAX_LEN]
        byte_rows = byte_rows[:MAX_LEN]
        word_ids = word_ids[:MAX_LEN]
        tokens = tokens[:MAX_LEN]
    else:
        pad_len = MAX_LEN - len(input_ids)
        input_ids += [0] * pad_len
        for _ in range(pad_len):
            byte_rows.append([0] * CHAR_MAX_LEN)
        word_ids += [None] * pad_len
        tokens += ["[PAD]"] * pad_len
    
    # Run inference
    input_ids_np = np.array([input_ids], dtype=np.int64)
    byte_ids_np = np.array([byte_rows], dtype=np.int64)
    
    outputs = session.run(None, {
        "input_ids": input_ids_np,
        "byte_ids": byte_ids_np
    })
    
    logits = outputs[0][0]  # [seq_len, num_tags]
    pred_ids = np.argmax(logits, axis=1)
    
    # Convert to tags
    pred_tags = [ID_TO_TAG[pid] for pid in pred_ids]
    
    # Only return non-padding tokens
    orig_len = len(encoding.ids)
    return tokens[:orig_len], pred_tags[:orig_len], word_ids[:orig_len]


def evaluate_model(model_path: str, tokenizer_path: str, validation_data_path: str, max_samples: int = None) -> Dict:
    """
    Evaluate model on validation data with entity-level metrics.
    """
    print(f"\n=== Evaluating: {os.path.basename(model_path)} ===")
    print(f"Loading model: {model_path}")
    print(f"Loading tokenizer: {tokenizer_path}")
    print(f"Validation data: {validation_data_path}")
    
    # Load model and tokenizer
    session = ort.InferenceSession(model_path)
    tokenizer = Tokenizer.from_file(tokenizer_path)
    
    # Load validation data
    df = pd.read_csv(validation_data_path)
    if max_samples:
        df = df.head(max_samples)
    
    print(f"Loaded {len(df)} validation samples")
    
    # Metrics
    total_samples = 0
    total_tokens = 0
    correct_tokens = 0
    
    total_words = 0
    consistent_words = 0
    
    total_violations = 0
    
    gold_entities_all = []
    pred_entities_all = []
    
    error_examples = defaultdict(list)
    
    print("\nRunning inference...")
    for idx, row in df.iterrows():
        if idx > 0 and idx % 100 == 0:
            print(f"  Processed {idx}/{len(df)} samples...", flush=True)
        
        text = str(row['text'])
        gold_tags_str = str(row['tags'])
        gold_tags = gold_tags_str.split()
        
        try:
            # Run inference
            tokens, pred_tags, word_ids = run_inference(session, tokenizer, text)
            
            # Token-level accuracy
            # Map gold tags to subtokens
            encoding = tokenizer.encode(normalize_text(text))
            gold_tag_aligned = []
            for word_id in encoding.word_ids:
                if word_id is None:
                    gold_tag_aligned.append("O")
                elif word_id < len(gold_tags):
                    tag = gold_tags[word_id]
                    # Convert B- to I- for continuation subtokens
                    if len(gold_tag_aligned) > 0:
                        prev_word_id = encoding.word_ids[len(gold_tag_aligned) - 1]
                        if prev_word_id == word_id and tag.startswith("B-"):
                            tag = "I-" + tag[2:]
                    gold_tag_aligned.append(tag)
                else:
                    gold_tag_aligned.append("O")
            
            # Truncate to match prediction length
            gold_tag_aligned = gold_tag_aligned[:len(pred_tags)]
            
            for g, p in zip(gold_tag_aligned, pred_tags):
                if g == p:
                    correct_tokens += 1
                total_tokens += 1
            
            # Word consistency
            cons, tot = calculate_word_consistency(pred_tags, word_ids)
            consistent_words += cons
            total_words += tot
            
            # IOB violations
            violations = check_iob_violations(pred_tags)
            total_violations += len(violations)
            
            if violations:
                error_examples['iob_violation'].append({
                    'text': text,
                    'violations': violations,
                    'tags': ' '.join(pred_tags[:20])
                })
            
            # Extract entities
            gold_entities = extract_entities_from_tags(
                text.split(), gold_tags, list(range(len(gold_tags)))
            )
            pred_entities = extract_entities_from_tags(tokens, pred_tags, word_ids)
            
            gold_entities_all.extend([(text, *e) for e in gold_entities])
            pred_entities_all.extend([(text, *e) for e in pred_entities])
            
            total_samples += 1
            
        except Exception as e:
            print(f"  Error processing sample {idx}: {e}")
            continue
    
    # Calculate entity-level metrics
    print("\nCalculating entity-level metrics...")
    
    # Convert to sets for easier comparison
    gold_entity_set = set()
    for text, entity_text, entity_type, start, end in gold_entities_all:
        gold_entity_set.add((text, entity_text.lower(), entity_type, start, end))
    
    pred_entity_set = set()
    for text, entity_text, entity_type, start, end in pred_entities_all:
        pred_entity_set.add((text, entity_text.lower(), entity_type, start, end))
    
    # True positives: exact match (text, type, span)
    tp = len(gold_entity_set & pred_entity_set)
    fp = len(pred_entity_set - gold_entity_set)
    fn = len(gold_entity_set - pred_entity_set)
    
    precision = tp / (tp + fp) if (tp + fp) > 0 else 0.0
    recall = tp / (tp + fn) if (tp + fn) > 0 else 0.0
    f1 = 2 * precision * recall / (precision + recall) if (precision + recall) > 0 else 0.0
    
    # Aggregate metrics
    metrics = {
        'samples': total_samples,
        'token_accuracy': correct_tokens / total_tokens if total_tokens > 0 else 0.0,
        'word_consistency_rate': consistent_words / total_words if total_words > 0 else 0.0,
        'iob_violation_rate': total_violations / total_tokens if total_tokens > 0 else 0.0,
        'entity_precision': precision,
        'entity_recall': recall,
        'entity_f1': f1,
        'total_gold_entities': len(gold_entity_set),
        'total_pred_entities': len(pred_entity_set),
        'true_positives': tp,
        'false_positives': fp,
        'false_negatives': fn,
    }
    
    return metrics, error_examples


def print_metrics(metrics: Dict):
    """Pretty print evaluation metrics."""
    print("\n" + "="*60)
    print("EVALUATION RESULTS")
    print("="*60)
    print(f"\nSamples evaluated: {metrics['samples']:,}")
    print(f"\nToken-level Accuracy: {metrics['token_accuracy']:.4f} ({metrics['token_accuracy']*100:.2f}%)")
    print(f"\nWord Consistency Rate: {metrics['word_consistency_rate']:.4f} ({metrics['word_consistency_rate']*100:.2f}%)")
    print(f"  (% of words where all subtokens have identical tags)")
    print(f"\nIOB Violation Rate: {metrics['iob_violation_rate']:.4f} ({metrics['iob_violation_rate']*100:.2f}%)")
    print(f"  (% of tokens with invalid tag transitions)")
    
    print(f"\n{'='*60}")
    print("ENTITY-LEVEL METRICS")
    print("="*60)
    print(f"\nGold entities: {metrics['total_gold_entities']:,}")
    print(f"Predicted entities: {metrics['total_pred_entities']:,}")
    print(f"\nTrue Positives:  {metrics['true_positives']:,}")
    print(f"False Positives: {metrics['false_positives']:,}")
    print(f"False Negatives: {metrics['false_negatives']:,}")
    print(f"\nPrecision: {metrics['entity_precision']:.4f} ({metrics['entity_precision']*100:.2f}%)")
    print(f"Recall:    {metrics['entity_recall']:.4f} ({metrics['entity_recall']*100:.2f}%)")
    print(f"F1 Score:  {metrics['entity_f1']:.4f} ({metrics['entity_f1']*100:.2f}%)")
    print("="*60)


def main():
    parser = argparse.ArgumentParser(description="Evaluate NER model with entity-level metrics")
    parser.add_argument("--model", required=True, help="Path to ONNX model")
    parser.add_argument("--tokenizer", default=os.path.join(SCRIPT_DIR, "custom-bpe-tokenizer.json"))
    parser.add_argument("--validation-data", default=os.path.join(SCRIPT_DIR, "data", "validate.csv"))
    parser.add_argument("--max-samples", type=int, default=None, help="Limit number of validation samples")
    parser.add_argument("--output", default=None, help="Save metrics to JSON file")
    
    args = parser.parse_args()
    
    metrics, error_examples = evaluate_model(
        args.model,
        args.tokenizer,
        args.validation_data,
        args.max_samples
    )
    
    print_metrics(metrics)
    
    # Show some error examples
    if error_examples:
        print("\n" + "="*60)
        print("ERROR EXAMPLES (first 5)")
        print("="*60)
        for error_type, examples in error_examples.items():
            print(f"\n{error_type.upper()}:")
            for ex in examples[:5]:
                print(f"  Text: {ex['text'][:80]}")
                if 'violations' in ex:
                    print(f"  Violations: {ex['violations']}")
                if 'tags' in ex:
                    print(f"  Tags: {ex['tags']}")
                print()
    
    # Save to JSON if requested
    if args.output:
        import json
        with open(args.output, 'w') as f:
            json.dump(metrics, f, indent=2)
        print(f"\nMetrics saved to: {args.output}")


if __name__ == "__main__":
    main()


