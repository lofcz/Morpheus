"""
Compare spaCy model vs Custom Transformer model
Side-by-side comparison of predictions and performance.
"""
import os
import time
import spacy
import torch
from tokenizers import Tokenizer
import numpy as np

# Configuration
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
SPACY_MODEL = os.path.join(SCRIPT_DIR, "spacy_model", "model-best")
CUSTOM_MODEL = os.path.join(SCRIPT_DIR, "checkpoints_transformer", "step_latest.pt")
CUSTOM_TOKENIZER = os.path.join(SCRIPT_DIR, "custom-bpe-tokenizer.json")

# Test sentences
TEST_SENTENCES = [
    "Novák, Petr",
    "Ing. Jan Novák, Ph.D.",
    "matěj štágl",
    "scioškola",
    "microsoft",
    "destroyer66",
    "firma kotlárna",
    "matěj štágl incorporated",
    "Petr Novák pracuje pro Microsoft v Praze.",
    "MUDr. Jana Nováková, CSc. z Brna",
]


def load_models():
    """Load both spaCy and custom models."""
    print("Loading models...")
    
    # Load spaCy model
    try:
        spacy_nlp = spacy.load(SPACY_MODEL)
        print("✓ spaCy model loaded")
    except Exception as e:
        print(f"✗ Could not load spaCy model: {e}")
        spacy_nlp = None
    
    # Load custom model (placeholder - implement based on your inference code)
    custom_model = None
    try:
        # TODO: Implement custom model loading
        # This would load your PyTorch transformer model
        print("⚠ Custom model loading not yet implemented")
        print("  (Add your model loading code here)")
    except Exception as e:
        print(f"✗ Could not load custom model: {e}")
    
    return spacy_nlp, custom_model


def predict_spacy(nlp, text):
    """Get spaCy predictions."""
    if nlp is None:
        return None
    
    start = time.time()
    doc = nlp(text)
    elapsed = time.time() - start
    
    entities = [(ent.text, ent.label_, ent.start_char, ent.end_char) for ent in doc.ents]
    
    return {
        "entities": entities,
        "time_ms": elapsed * 1000
    }


def predict_custom(model, text):
    """Get custom model predictions."""
    if model is None:
        return None
    
    # TODO: Implement custom model inference
    # This would use your PyTorch model
    
    return {
        "entities": [],
        "time_ms": 0
    }


def format_entities(entities):
    """Format entities for display."""
    if not entities:
        return "  (no entities)"
    
    lines = []
    for text, label, start, end in entities:
        lines.append(f"  - {text:<20} [{label}] ({start}-{end})")
    return "\n".join(lines)


def compare():
    """Run comparison between models."""
    print("=" * 80)
    print("Model Comparison: spaCy vs Custom Transformer")
    print("=" * 80)
    
    spacy_nlp, custom_model = load_models()
    
    print("\n" + "=" * 80)
    print("Test Predictions")
    print("=" * 80)
    
    for i, text in enumerate(TEST_SENTENCES):
        print(f"\n{'='*80}")
        print(f"Test {i+1}: {text}")
        print(f"{'='*80}")
        
        # spaCy prediction
        print("\n[spaCy Model]")
        spacy_result = predict_spacy(spacy_nlp, text)
        if spacy_result:
            print(format_entities(spacy_result["entities"]))
            print(f"  Time: {spacy_result['time_ms']:.2f}ms")
        else:
            print("  (model not available)")
        
        # Custom model prediction
        print("\n[Custom Transformer]")
        custom_result = predict_custom(custom_model, text)
        if custom_result:
            print(format_entities(custom_result["entities"]))
            print(f"  Time: {custom_result['time_ms']:.2f}ms")
        else:
            print("  (model not available)")
        
        # Comparison
        if spacy_result and custom_result:
            print("\n[Comparison]")
            spacy_ents = set((e[0], e[1]) for e in spacy_result["entities"])
            custom_ents = set((e[0], e[1]) for e in custom_result["entities"])
            
            if spacy_ents == custom_ents:
                print("  ✓ Predictions match!")
            else:
                print("  ✗ Predictions differ:")
                only_spacy = spacy_ents - custom_ents
                only_custom = custom_ents - spacy_ents
                
                if only_spacy:
                    print(f"    Only spaCy: {only_spacy}")
                if only_custom:
                    print(f"    Only custom: {only_custom}")
    
    print("\n" + "=" * 80)
    print("Comparison complete!")
    print("=" * 80)


if __name__ == "__main__":
    compare()

