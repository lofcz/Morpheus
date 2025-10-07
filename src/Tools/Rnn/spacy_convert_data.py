"""
Convert CSV dataset to spaCy binary format (.spacy)
Converts IOB-tagged CSV data to spaCy's DocBin format for training.
"""
import os
import pandas as pd
import spacy
from spacy.tokens import DocBin, Doc
from spacy.training import Example
from tqdm import tqdm
import random

# Configuration
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
DATA_FILE = os.path.join(SCRIPT_DIR, "data", "dataset_clean_1m.csv")
OUTPUT_DIR = os.path.join(SCRIPT_DIR, "spacy_data")
TRAIN_FILE = os.path.join(OUTPUT_DIR, "train.spacy")
DEV_FILE = os.path.join(OUTPUT_DIR, "dev.spacy")

# Split ratio
TRAIN_RATIO = 0.8

# Entity label mapping
ENTITY_LABELS = ["PER", "NICK", "ORG", "LOC", "TIT"]


def parse_iob_tags(text: str, tags_str: str):
    """
    Parse IOB tags and convert to entity spans.
    
    Args:
        text: Raw text string
        tags_str: Space-separated IOB tags (e.g., "B-PER I-PER O B-ORG")
    
    Returns:
        List of (start_char, end_char, label) tuples
    """
    words = text.split()
    tags = tags_str.split()
    
    if len(words) != len(tags):
        # Mismatch - skip this sample
        return None
    
    entities = []
    current_entity = None
    char_pos = 0
    
    for i, (word, tag) in enumerate(zip(words, tags)):
        word_start = char_pos
        word_end = char_pos + len(word)
        
        if tag == "O":
            # End current entity if any
            if current_entity:
                entities.append(current_entity)
                current_entity = None
        elif tag.startswith("B-"):
            # Begin new entity
            if current_entity:
                entities.append(current_entity)
            label = tag[2:]  # Remove "B-" prefix
            current_entity = [word_start, word_end, label]
        elif tag.startswith("I-"):
            # Continue current entity
            if current_entity:
                label = tag[2:]  # Remove "I-" prefix
                if current_entity[2] == label:
                    # Extend current entity
                    current_entity[1] = word_end
                else:
                    # Label mismatch - start new entity
                    entities.append(current_entity)
                    current_entity = [word_start, word_end, label]
            else:
                # I- tag without B- - treat as B-
                label = tag[2:]
                current_entity = [word_start, word_end, label]
        
        # Move char position forward (word + space)
        char_pos = word_end + 1
    
    # Add final entity if any
    if current_entity:
        entities.append(current_entity)
    
    return [(start, end, label) for start, end, label in entities]


def create_spacy_doc(nlp, text: str, entities):
    """Create a spaCy Doc with entity annotations."""
    doc = nlp.make_doc(text)
    ents = []
    
    for start, end, label in entities:
        span = doc.char_span(start, end, label=label, alignment_mode="expand")
        if span is not None:
            ents.append(span)
    
    # Filter overlapping spans
    filtered_ents = spacy.util.filter_spans(ents)
    doc.ents = filtered_ents
    
    return doc


def convert_dataset():
    """Convert CSV dataset to spaCy format."""
    print("=" * 80)
    print("Converting CSV dataset to spaCy binary format")
    print("=" * 80)
    
    # Create output directory
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    
    # Load CSV data
    print(f"\nLoading data from {DATA_FILE}...")
    df = pd.read_csv(DATA_FILE)
    print(f"Loaded {len(df):,} samples")
    
    # Initialize blank Czech model
    print("\nInitializing Czech language model...")
    nlp = spacy.blank("cs")
    
    # Add entity labels to NER component
    if "ner" not in nlp.pipe_names:
        ner = nlp.add_pipe("ner")
    else:
        ner = nlp.get_pipe("ner")
    
    for label in ENTITY_LABELS:
        ner.add_label(label)
    
    # Parse all samples
    print("\nParsing IOB tags...")
    docs = []
    skipped = 0
    
    for idx, row in tqdm(df.iterrows(), total=len(df), desc="Processing"):
        text = str(row['text']).strip()
        tags = str(row['tags']).strip()
        
        if not text or not tags:
            skipped += 1
            continue
        
        entities = parse_iob_tags(text, tags)
        
        if entities is None:
            skipped += 1
            continue
        
        try:
            doc = create_spacy_doc(nlp, text, entities)
            docs.append(doc)
        except Exception as e:
            skipped += 1
            continue
    
    print(f"✓ Successfully parsed {len(docs):,} samples")
    print(f"✗ Skipped {skipped:,} samples due to errors")
    
    # Shuffle and split
    print("\nShuffling and splitting data...")
    random.seed(42)
    random.shuffle(docs)
    
    split_idx = int(len(docs) * TRAIN_RATIO)
    train_docs = docs[:split_idx]
    dev_docs = docs[split_idx:]
    
    print(f"Train set: {len(train_docs):,} samples ({TRAIN_RATIO*100:.0f}%)")
    print(f"Dev set: {len(dev_docs):,} samples ({(1-TRAIN_RATIO)*100:.0f}%)")
    
    # Save to .spacy files
    print("\nSaving train.spacy...")
    train_db = DocBin(docs=train_docs)
    train_db.to_disk(TRAIN_FILE)
    print(f"✓ Saved {TRAIN_FILE}")
    
    print("\nSaving dev.spacy...")
    dev_db = DocBin(docs=dev_docs)
    dev_db.to_disk(DEV_FILE)
    print(f"✓ Saved {DEV_FILE}")
    
    # Print statistics
    print("\n" + "=" * 80)
    print("Entity Statistics")
    print("=" * 80)
    
    for split_name, split_docs in [("Train", train_docs), ("Dev", dev_docs)]:
        entity_counts = {label: 0 for label in ENTITY_LABELS}
        total_entities = 0
        
        for doc in split_docs:
            for ent in doc.ents:
                entity_counts[ent.label_] += 1
                total_entities += 1
        
        print(f"\n{split_name} set ({len(split_docs):,} samples):")
        print(f"  Total entities: {total_entities:,}")
        for label in ENTITY_LABELS:
            count = entity_counts[label]
            pct = (count / total_entities * 100) if total_entities > 0 else 0
            print(f"  {label:5s}: {count:8,} ({pct:5.1f}%)")
    
    print("\n" + "=" * 80)
    print("✓ Conversion complete!")
    print("=" * 80)


if __name__ == "__main__":
    convert_dataset()

