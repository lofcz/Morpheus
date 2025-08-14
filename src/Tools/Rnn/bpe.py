import pandas as pd
from tokenizers import Tokenizer
from tokenizers.models import BPE
from tokenizers.trainers import BpeTrainer
from tokenizers.pre_tokenizers import Whitespace
import os

# --- Configuration ---
DATA_FILE = "data/dataset.csv"
CORPUS_FILE = "data/corpus.txt"
TOKENIZER_SAVE_PATH = "custom-bpe-tokenizer.json"
VOCAB_SIZE = 1000 # Keep this small for a tiny model

def train_tokenizer():
    """
    Trains a BPE tokenizer from the text column of our dataset.
    """
    print("--- Starting Tokenizer Training ---")

    # 1. Generate a corpus file from the dataset
    print(f"Reading data from {DATA_FILE} to generate corpus...")
    df = pd.read_csv(DATA_FILE)
    with open(CORPUS_FILE, "w", encoding="utf-8") as f:
        for text in df["text"]:
            f.write(text + "\n")
    print(f"Corpus saved to {CORPUS_FILE}")

    # 2. Initialize a tokenizer
    tokenizer = Tokenizer(BPE(unk_token="[UNK]"))
    tokenizer.pre_tokenizer = Whitespace()

    # 3. Initialize a trainer
    trainer = BpeTrainer(
        vocab_size=VOCAB_SIZE,
        special_tokens=["[UNK]", "[PAD]"] # [UNK] for unknown, [PAD] for padding
    )

    # 4. Train the tokenizer
    print(f"Training tokenizer with vocab size {VOCAB_SIZE}...")
    tokenizer.train([CORPUS_FILE], trainer)
    print("Training complete.")

    # 5. Save the tokenizer
    tokenizer.save(TOKENIZER_SAVE_PATH)
    print(f"Tokenizer saved to {TOKENIZER_SAVE_PATH}")

    # 6. Clean up the temporary corpus file
    os.remove(CORPUS_FILE)
    print(f"Cleaned up {CORPUS_FILE}.")
    print("--- Tokenizer Training Finished ---")

if __name__ == "__main__":
    train_tokenizer()