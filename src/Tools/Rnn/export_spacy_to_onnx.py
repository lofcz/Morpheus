"""
Export trained spaCy model to ONNX format
Exports the transformer + NER head for C# inference via ONNX Runtime.
"""
import os
import spacy
from spacy_transformers.pipeline_component import Transformer
import torch
import numpy as np

# Configuration
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
MODEL_PATH = os.path.join(SCRIPT_DIR, "spacy_model", "model-best")
ONNX_OUTPUT_DIR = os.path.join(SCRIPT_DIR, "spacy_onnx")
TRANSFORMER_ONNX = os.path.join(ONNX_OUTPUT_DIR, "transformer.onnx")
NER_ONNX = os.path.join(ONNX_OUTPUT_DIR, "ner_head.onnx")

ENTITY_LABELS = ["O", "B-PER", "I-PER", "B-NICK", "I-NICK", "B-ORG", "I-ORG", "B-LOC", "I-LOC", "B-TIT", "I-TIT"]


def export_to_onnx():
    """Export spaCy model components to ONNX."""
    print("=" * 80)
    print("Exporting spaCy Model to ONNX")
    print("=" * 80)
    
    # Create output directory
    os.makedirs(ONNX_OUTPUT_DIR, exist_ok=True)
    
    # Load trained model
    print(f"\nLoading model from {MODEL_PATH}...")
    try:
        nlp = spacy.load(MODEL_PATH)
        print("✓ Model loaded successfully")
    except Exception as e:
        print(f"✗ Error loading model: {e}")
        return
    
    # Get transformer component
    print("\nExtracting transformer component...")
    transformer = nlp.get_pipe("transformer")
    
    # Get NER component
    print("Extracting NER component...")
    ner = nlp.get_pipe("ner")
    
    # Create dummy input for tracing
    print("\nPreparing dummy inputs for ONNX export...")
    dummy_text = "Jan Novák pracuje pro Microsoft v Praze."
    doc = nlp.make_doc(dummy_text)
    
    # Get transformer outputs
    transformer_doc = transformer(doc)
    
    # Export transformer
    print(f"\nExporting transformer to {TRANSFORMER_ONNX}...")
    try:
        # Get the underlying transformer model
        trf_model = transformer.model.get_ref("transformer")
        
        # Create dummy inputs matching BERT expected format
        dummy_input_ids = torch.randint(0, 1000, (1, 16), dtype=torch.long)
        dummy_attention_mask = torch.ones((1, 16), dtype=torch.long)
        
        # Export transformer
        torch.onnx.export(
            trf_model,
            (dummy_input_ids, dummy_attention_mask),
            TRANSFORMER_ONNX,
            input_names=["input_ids", "attention_mask"],
            output_names=["last_hidden_state"],
            dynamic_axes={
                "input_ids": {0: "batch_size", 1: "sequence_length"},
                "attention_mask": {0: "batch_size", 1: "sequence_length"},
                "last_hidden_state": {0: "batch_size", 1: "sequence_length"}
            },
            opset_version=14,
            do_constant_folding=True
        )
        print(f"✓ Transformer exported to {TRANSFORMER_ONNX}")
    except Exception as e:
        print(f"✗ Error exporting transformer: {e}")
        print("\nNote: Transformer export requires direct access to the PyTorch model.")
        print("For C# inference, you may need to use the Hugging Face transformers library")
        print("to export BERT separately, then load it in C# via ONNX Runtime.")
    
    # Export NER head
    print(f"\nExporting NER head to {NER_ONNX}...")
    try:
        # The NER component uses TransitionBasedParser which is not directly exportable
        # We need to extract just the scoring layers
        print("⚠ Warning: NER component uses TransitionBasedParser which requires Python runtime.")
        print("For true ONNX export, you need to use spacy.TokenClassifier.v1 architecture.")
        print("\nAlternative approach:")
        print("1. Retrain with TokenClassifier architecture (see spacy_config.cfg comments)")
        print("2. Or use the transformer outputs + custom C# classification layer")
    except Exception as e:
        print(f"✗ Error exporting NER head: {e}")
    
    # Save label mapping
    print("\nSaving label mapping...")
    import json
    
    label_map = {i: label for i, label in enumerate(ENTITY_LABELS)}
    label_map_path = os.path.join(ONNX_OUTPUT_DIR, "label_map.json")
    
    with open(label_map_path, 'w', encoding='utf-8') as f:
        json.dump(label_map, f, indent=2, ensure_ascii=False)
    
    print(f"✓ Label mapping saved to {label_map_path}")
    
    # Save model configuration
    config_path = os.path.join(ONNX_OUTPUT_DIR, "model_config.json")
    config = {
        "transformer_model": "bert-base-multilingual-uncased",
        "max_length": 128,
        "entity_labels": ENTITY_LABELS,
        "num_labels": len(ENTITY_LABELS),
    }
    
    with open(config_path, 'w', encoding='utf-8') as f:
        json.dump(config, f, indent=2)
    
    print(f"✓ Model config saved to {config_path}")
    
    print("\n" + "=" * 80)
    print("Export Notes")
    print("=" * 80)
    print("""
For complete ONNX export compatible with C#:

1. OPTION A: Use Hugging Face transformers directly
   - Export BERT model separately using transformers library
   - Implement token classification in C# on top of BERT outputs
   - This gives you full control and ONNX compatibility

2. OPTION B: Retrain with Token Classification architecture
   - Modify spacy_config.cfg to use spacy.TokenClassifier.v1
   - This architecture is ONNX-exportable
   - Simpler inference pipeline (single forward pass)

3. OPTION C: Use spaCy in Python as service
   - Keep using current TransitionBasedParser architecture
   - Expose spaCy predictions via REST API
   - Call from C# application

Recommended: Option A for best performance and C# integration.
    """)
    
    print("=" * 80)


if __name__ == "__main__":
    export_to_onnx()

