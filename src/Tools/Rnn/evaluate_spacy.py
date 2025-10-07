"""
Evaluate trained spaCy NER model
Computes per-entity-type metrics and overall performance.
"""
import os
import spacy
from spacy.scorer import Scorer
from spacy.training import Example
from spacy.tokens import DocBin
from collections import defaultdict
import pandas as pd

# Configuration
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
MODEL_PATH = os.path.join(SCRIPT_DIR, "spacy_model", "model-best")
DEV_FILE = os.path.join(SCRIPT_DIR, "spacy_data", "dev.spacy")

ENTITY_LABELS = ["PER", "NICK", "ORG", "LOC", "TIT"]


def evaluate_model():
    """Evaluate the trained spaCy model on dev set."""
    print("=" * 80)
    print("spaCy NER Model Evaluation")
    print("=" * 80)
    
    # Load trained model
    print(f"\nLoading model from {MODEL_PATH}...")
    try:
        nlp = spacy.load(MODEL_PATH)
        print("✓ Model loaded successfully")
    except Exception as e:
        print(f"✗ Error loading model: {e}")
        print("\nMake sure you've trained the model first with run_spacy_train.bat")
        return
    
    # Load dev data
    print(f"\nLoading dev data from {DEV_FILE}...")
    doc_bin = DocBin().from_disk(DEV_FILE)
    docs = list(doc_bin.get_docs(nlp.vocab))
    print(f"✓ Loaded {len(docs):,} dev samples")
    
    # Create examples for scoring
    print("\nRunning predictions on dev set...")
    examples = []
    for gold_doc in docs:
        pred_doc = nlp(gold_doc.text)
        example = Example(pred_doc, gold_doc)
        examples.append(example)
    
    # Compute scores
    print("\nComputing metrics...")
    scorer = Scorer()
    scores = scorer.score(examples)
    
    # Print overall metrics
    print("\n" + "=" * 80)
    print("Overall Metrics")
    print("=" * 80)
    print(f"Entities F1:        {scores['ents_f']*100:.2f}%")
    print(f"Entities Precision: {scores['ents_p']*100:.2f}%")
    print(f"Entities Recall:    {scores['ents_r']*100:.2f}%")
    
    # Per-entity-type metrics
    print("\n" + "=" * 80)
    print("Per-Entity-Type Metrics")
    print("=" * 80)
    print(f"{'Entity':<10} {'Precision':>10} {'Recall':>10} {'F1':>10} {'Count':>10}")
    print("-" * 80)
    
    per_type = scores.get('ents_per_type', {})
    for label in ENTITY_LABELS:
        if label in per_type:
            metrics = per_type[label]
            p = metrics.get('p', 0) * 100
            r = metrics.get('r', 0) * 100
            f = metrics.get('f', 0) * 100
            
            # Count gold entities
            count = sum(1 for doc in docs for ent in doc.ents if ent.label_ == label)
            
            print(f"{label:<10} {p:>9.2f}% {r:>9.2f}% {f:>9.2f}% {count:>10,}")
    
    # Token-level accuracy
    print("\n" + "=" * 80)
    print("Additional Metrics")
    print("=" * 80)
    
    correct_tokens = 0
    total_tokens = 0
    
    for example in examples:
        pred_doc = example.predicted
        gold_doc = example.reference
        
        # Align tokens
        for pred_token, gold_token in zip(pred_doc, gold_doc):
            if pred_token.text == gold_token.text:
                total_tokens += 1
                # Check if both have same entity label
                pred_ent = pred_token.ent_iob_ + "-" + pred_token.ent_type_ if pred_token.ent_iob_ != "O" else "O"
                gold_ent = gold_token.ent_iob_ + "-" + gold_token.ent_type_ if gold_token.ent_iob_ != "O" else "O"
                if pred_ent == gold_ent:
                    correct_tokens += 1
    
    token_acc = (correct_tokens / total_tokens * 100) if total_tokens > 0 else 0
    print(f"Token-level accuracy: {token_acc:.2f}%")
    print(f"Total tokens: {total_tokens:,}")
    
    # Show some example predictions
    print("\n" + "=" * 80)
    print("Example Predictions (first 5 samples)")
    print("=" * 80)
    
    for i, example in enumerate(examples[:5]):
        pred_doc = example.predicted
        gold_doc = example.reference
        
        print(f"\n--- Example {i+1} ---")
        print(f"Text: {gold_doc.text}")
        print(f"Gold: {[(ent.text, ent.label_) for ent in gold_doc.ents]}")
        print(f"Pred: {[(ent.text, ent.label_) for ent in pred_doc.ents]}")
    
    print("\n" + "=" * 80)
    print("✓ Evaluation complete!")
    print("=" * 80)


def interactive_demo():
    """Interactive demo mode."""
    print("\n" + "=" * 80)
    print("Interactive Demo Mode")
    print("=" * 80)
    print("Enter text to analyze (or 'quit' to exit)")
    print("-" * 80)
    
    # Load model
    try:
        nlp = spacy.load(MODEL_PATH)
    except Exception as e:
        print(f"✗ Error loading model: {e}")
        return
    
    while True:
        text = input("\n> ").strip()
        
        if text.lower() in ['quit', 'exit', 'q']:
            break
        
        if not text:
            continue
        
        # Process text
        doc = nlp(text)
        
        # Display results
        if doc.ents:
            print("\nEntities found:")
            for ent in doc.ents:
                print(f"  - {ent.text:<20} [{ent.label_}]")
        else:
            print("\nNo entities found.")


if __name__ == "__main__":
    import sys
    
    if len(sys.argv) > 1 and sys.argv[1] == "--demo":
        # Load model first
        print("Loading model...")
        try:
            nlp = spacy.load(MODEL_PATH)
            print("✓ Model loaded\n")
            interactive_demo()
        except Exception as e:
            print(f"✗ Error: {e}")
    else:
        evaluate_model()

