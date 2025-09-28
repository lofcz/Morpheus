import os
import io
import pandas as pd
from tokenizers import Tokenizer
from tokenizers.models import BPE
from tokenizers.trainers import BpeTrainer
from tokenizers.pre_tokenizers import Whitespace, BertPreTokenizer
from tokenizers.normalizers import Sequence as NormSequence, NFKC, Lowercase

"""
Train a BPE tokenizer from the synthesized dataset.csv.
"""

# --- Configuration ---
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# This script now exclusively uses the synthesized dataset from remix.py
DATA_FILE = os.path.join(SCRIPT_DIR, "data", "dataset.csv")
CORPUS_FILE = os.path.join(SCRIPT_DIR, "data", "corpus.txt")
TOKENIZER_SAVE_PATH = os.path.join(SCRIPT_DIR, "custom-bpe-tokenizer.json")
VOCAB_SIZE = 8000  # Larger vocab to learn meaningful word/subword units


def normalize_text(text: str) -> str:
    s = text.strip()
    if not s:
        return s
    s = " ".join(s.split())
    return s.casefold()


def train_tokenizer():
    """
    Trains a BPE tokenizer from the text column of our dataset.
    """
    print("--- Starting Tokenizer Training ---")
    
    if not os.path.exists(DATA_FILE):
        print(f"Error: Dataset file not found at {DATA_FILE}")
        print("Please run 'run_remix.bat' first to synthesize the dataset.")
        return

    # 1. Generate a corpus file from the dataset
    print(f"Reading data from {DATA_FILE} to generate corpus...")
    df = pd.read_csv(DATA_FILE)
    with open(CORPUS_FILE, "w", encoding="utf-8") as f:
        for text in df["text"]:
            s = normalize_text(str(text))
            if not s:
                continue
            f.write(s + "\n")
    print(f"Corpus saved to {CORPUS_FILE}")

    # 2. Initialize a tokenizer
    tokenizer = Tokenizer(BPE(unk_token="[UNK]"))
    tokenizer.normalizer = NormSequence([NFKC()])
    # Split on whitespace and punctuation like BERT, improves word boundaries
    tokenizer.pre_tokenizer = BertPreTokenizer()

    # 3. Initialize a trainer
    trainer = BpeTrainer(
        vocab_size=VOCAB_SIZE,
        min_frequency=2,
        continuing_subword_prefix="##",
        special_tokens=["[UNK]", "[PAD]"]
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