import onnxruntime as ort
import numpy as np
import pandas as pd
from tokenizers import Tokenizer
import torch # Using torch just for the loss function calculation
import torch.nn as nn
from sklearn.metrics import classification_report, accuracy_score
import os

# --- Configuration ---
ONNX_MODEL_PATH = "name_classifier.onnx"
TOKENIZER_PATH = "custom-bpe-tokenizer.json"
VALIDATE_DATA_FILE = "data/validate.csv"
MAX_LEN = 128 # Must match the training configuration

# --- Labeling Scheme for NER (must match training) ---
TAG_MAP = {
    "O": 0,
    "B-PER": 1, "I-PER": 2,
    "B-NICK": 3, "I-NICK": 4,
    "B-ORG": 5, "I-ORG": 6,
    "B-LOC": 7, "I-LOC": 8,
}
ID_TO_TAG = {v: k for k, v in TAG_MAP.items()}

def validate_model():
    """
    Loads a trained ONNX model and evaluates it on a validation dataset for NER.
    """
    print("--- Starting Model Validation ---")

    # 1. Check if model and tokenizer exist
    if not all(os.path.exists(p) for p in [ONNX_MODEL_PATH, TOKENIZER_PATH, VALIDATE_DATA_FILE]):
        print("Error: Model, tokenizer, or validation file not found.")
        print(f"Please ensure '{ONNX_MODEL_PATH}', '{TOKENIZER_PATH}', and '{VALIDATE_DATA_FILE}' exist.")
        print("You may need to run 'run.bat' first to train the model.")
        return

    # 2. Load model, tokenizer, and validation data
    print(f"Loading model from {ONNX_MODEL_PATH}")
    session = ort.InferenceSession(ONNX_MODEL_PATH)
    input_name = session.get_inputs()[0].name

    print(f"Loading tokenizer from {TOKENIZER_PATH}")
    tokenizer = Tokenizer.from_file(TOKENIZER_PATH)
    pad_token_id = tokenizer.token_to_id("[PAD]") or 0

    print(f"Loading validation data from {VALIDATE_DATA_FILE}")
    df = pd.read_csv(VALIDATE_DATA_FILE)

    # 3. Prepare for evaluation
    all_true_labels = []
    all_pred_labels = []
    total_loss = 0
    criterion = nn.CrossEntropyLoss(ignore_index=-100) # Must ignore -100 for token alignment
    tag_names = list(TAG_MAP.keys())

    # 4. Loop through validation data and perform inference
    for index, row in df.iterrows():
        text = str(row.get('text', ''))
        true_tags = str(row.get('tags', '')).split()
        
        if not text:
            continue

        # Tokenize and pad/truncate
        encoding = tokenizer.encode(text)
        input_ids = encoding.ids
        word_ids = encoding.word_ids
        
        # Align true labels with tokens, similar to the training dataset
        aligned_labels = np.full(len(word_ids), -100, dtype=np.int64)
        previous_word_id = None
        for i, word_id in enumerate(word_ids):
            if word_id is None:
                continue
            if word_id != previous_word_id:
                if word_id < len(true_tags):
                    tag = true_tags[word_id]
                    aligned_labels[i] = TAG_MAP.get(tag, TAG_MAP["O"])
            previous_word_id = word_id

        # Pad and truncate
        if len(input_ids) > MAX_LEN:
            input_ids = input_ids[:MAX_LEN]
            aligned_labels = aligned_labels[:MAX_LEN]
        else:
            pad_length = MAX_LEN - len(input_ids)
            input_ids = input_ids + [pad_token_id] * pad_length
            aligned_labels = np.pad(aligned_labels, (0, pad_length), mode='constant', constant_values=-100)
            
        # Create ONNX-compatible input
        onnx_input = {input_name: np.array([input_ids], dtype=np.int64)}

        # Run inference
        logits_onnx = session.run(None, onnx_input)[0] # Shape: (1, MAX_LEN, NUM_TAGS)

        # Calculate loss
        loss = criterion(
            torch.from_numpy(logits_onnx).view(-1, len(TAG_MAP)),
            torch.from_numpy(aligned_labels).view(-1)
        )
        total_loss += loss.item()

        # Get predictions
        pred_indices = np.argmax(logits_onnx, axis=2)[0] # Shape: (MAX_LEN,)

        # Filter out padding and sub-tokens for evaluation
        active_tokens_mask = aligned_labels != -100
        
        all_true_labels.extend(aligned_labels[active_tokens_mask])
        all_pred_labels.extend(pred_indices[active_tokens_mask])
        
    # 5. Report results
    avg_loss = total_loss / max(1, len(df))
    accuracy = accuracy_score(all_true_labels, all_pred_labels)
    
    # Use IDs for the report and map to names for display
    report_labels = [TAG_MAP[name] for name in tag_names]
    
    print("\n--- Validation Results ---")
    print(f"Average Loss: {avg_loss:.4f}")
    print(f"Token-level Accuracy: {accuracy:.4f}")
    print("\nClassification Report (Token-level):")
    print(classification_report(all_true_labels, all_pred_labels, labels=report_labels, target_names=tag_names, zero_division=0))
    print("--- Validation Finished ---")


if __name__ == "__main__":
    validate_model()