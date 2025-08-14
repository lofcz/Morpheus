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

def validate_model():
    """
    Loads a trained ONNX model and evaluates it on a validation dataset.
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
    pad_token_id = tokenizer.token_to_id("[PAD]")

    print(f"Loading validation data from {VALIDATE_DATA_FILE}")
    df = pd.read_csv(VALIDATE_DATA_FILE)

    # 3. Prepare for evaluation
    all_labels = []
    all_preds = []
    total_loss = 0
    criterion = nn.CrossEntropyLoss()
    class_labels = ["Name (0)", "Nickname (1)", "Company (2)"]

    # 4. Loop through validation data and perform inference
    for index, row in df.iterrows():
        text = row['text']
        label = row['label']
        all_labels.append(label)

        # Tokenize and pad/truncate
        encoding = tokenizer.encode(text)
        input_ids = encoding.ids
        if len(input_ids) > MAX_LEN:
            input_ids = input_ids[:MAX_LEN]
        else:
            input_ids = input_ids + [pad_token_id] * (MAX_LEN - len(input_ids))

        # Create ONNX-compatible input
        onnx_input = {input_name: np.array([input_ids], dtype=np.int64)}

        # Run inference
        logits_onnx = session.run(None, onnx_input)[0]

        # Calculate prediction
        pred_label = np.argmax(logits_onnx, axis=1)[0]
        all_preds.append(pred_label)

        # Calculate loss (using PyTorch's loss function for consistency)
        loss = criterion(torch.from_numpy(logits_onnx), torch.tensor([label], dtype=torch.long))
        total_loss += loss.item()

    # 5. Report results
    avg_loss = total_loss / len(df)
    accuracy = accuracy_score(all_labels, all_preds)

    print("\n--- Validation Results ---")
    print(f"Average Loss: {avg_loss:.4f}")
    print(f"Overall Accuracy: {accuracy:.4f}")
    print("\nClassification Report:")
    print(classification_report(all_labels, all_preds, target_names=class_labels, zero_division=0))
    print("--- Validation Finished ---")


if __name__ == "__main__":
    validate_model()