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

os.environ["TOKENIZERS_PARALLELISM"] = "false"

# --- Configuration ---
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
DATA_FILE = os.path.join(SCRIPT_DIR, "data", "dataset.csv")
WORDS_FILE = os.path.join(SCRIPT_DIR, "data", "classes", "sources", "words", "cs_raw.txt") # NEW
CORPUS_FILE = os.path.join(SCRIPT_DIR, "data", "corpus.txt")
TOKENIZER_SAVE_PATH = os.path.join(SCRIPT_DIR, "custom-bpe-tokenizer.json")
VOCAB_SIZE = 16000 # Increased vocab size for a more diverse corpus


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

    # 1. Generate a corpus file from the dataset and the raw words file
    print(f"Reading data from {DATA_FILE} and {WORDS_FILE} to generate corpus...")
    df = pd.read_csv(DATA_FILE)
    
    with open(CORPUS_FILE, "w", encoding="utf-8") as f:
        # First, write the synthesized dataset
        for text in df["text"]:
            s = normalize_text(str(text))
            if not s:
                continue
            f.write(s + "\\n")

        # Second, write the raw words corpus
        if os.path.exists(WORDS_FILE):
            with open(WORDS_FILE, "r", encoding="utf-8") as words_f:
                # The raw file is a single line of comma-separated words
                line = words_f.readline()
                words = line.split(',')
                for word in words:
                    s = normalize_text(word)
                    if s:
                        f.write(s + "\\n")
        else:
            print(f"Warning: Words file not found at {WORDS_FILE}")

    print(f"Corpus saved to {CORPUS_FILE}")

    # 2. Initialize a tokenizer
    tokenizer = Tokenizer(BPE(
        unk_token="[UNK]",
        cache_capacity=0
    ))
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