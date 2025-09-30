import argparse
import os
from typing import List, Tuple

import numpy as np
import onnxruntime as ort
from tokenizers import Tokenizer


# Keep TAG_MAP consistent with training/validation
TAG_MAP = {
    "O": 0,
    "B-PER": 1, "I-PER": 2,
    "B-NICK": 3, "I-NICK": 4,
    "B-ORG": 5, "I-ORG": 6,
    "B-LOC": 7, "I-LOC": 8,
    "B-TIT": 9, "I-TIT": 10,
}
ID_TO_TAG = {v: k for k, v in TAG_MAP.items()}


def enforce_iob_constraints(tags: List[str]) -> List[str]:
    """
    Enforce simple IOB validity on a sequence of tags.
    - I-X cannot start a span; convert to B-X or O depending on previous context.
    - Transitions like I-A following B/ I-B (A != B) are converted to B-A.
    """
    fixed: List[str] = []
    prev_type = None
    inside = False
    for t in tags:
        if t == "O":
            fixed.append("O")
            prev_type = None
            inside = False
            continue
        if "-" not in t:
            fixed.append("O")
            prev_type = None
            inside = False
            continue
        bio, ent = t.split("-", 1)
        if bio == "B":
            fixed.append(f"B-{ent}")
            prev_type = ent
            inside = True
        else:  # I
            if not inside or prev_type != ent:
                fixed.append(f"B-{ent}")
                prev_type = ent
                inside = True
            else:
                fixed.append(f"I-{ent}")
    return fixed


def tokens_to_words(encoding) -> Tuple[List[str], List[int]]:
    """
    Reconstruct word-level tokens from subword tokens using word_ids.
    Returns (words, first_token_indices_per_word)
    """
    tokens = encoding.tokens
    word_ids = encoding.word_ids
    words: List[str] = []
    first_indices: List[int] = []
    current_word_id = None
    current_word_pieces: List[str] = []
    for i, wid in enumerate(word_ids):
        if wid is None:
            continue
        if current_word_id is None:
            current_word_id = wid
            first_indices.append(i)
        if wid != current_word_id:
            words.append(_join_pieces(current_word_pieces))
            current_word_pieces = []
            current_word_id = wid
            first_indices.append(i)
        current_word_pieces.append(tokens[i])
    if current_word_pieces:
        words.append(_join_pieces(current_word_pieces))
    return words, first_indices


def _join_pieces(pieces: List[str]) -> str:
    # HuggingFace tokenizers BPE continuation often prefixed by "##"
    cleaned = [p[2:] if p.startswith("##") else p for p in pieces]
    return "".join(cleaned)


def extract_entities(words: List[str], word_tags: List[str]) -> List[Tuple[str, str]]:
    entities: List[Tuple[str, str]] = []
    current: List[str] = []
    current_type: str | None = None
    for w, t in zip(words, word_tags):
        if t == "O":
            if current:
                entities.append((" ".join(current), current_type or ""))
                current = []
                current_type = None
            continue
        bio, ent = t.split("-", 1) if "-" in t else ("O", "")
        if bio == "B" or (current_type is not None and ent != current_type):
            if current:
                entities.append((" ".join(current), current_type or ""))
            current = [w]
            current_type = ent
        else:  # I
            current.append(w)
            current_type = ent
    if current:
        entities.append((" ".join(current), current_type or ""))
    return entities


def run_inference_loaded(session: ort.InferenceSession, tokenizer: Tokenizer, text: str, max_len: int) -> None:
    encoding = tokenizer.encode(text)
    input_ids = encoding.ids
    word_ids = encoding.word_ids

    pad_token_id = tokenizer.token_to_id("[PAD]") or 0
    if len(input_ids) > max_len:
        input_ids = input_ids[:max_len]
        word_ids = word_ids[:max_len]
    else:
        input_ids = input_ids + [pad_token_id] * (max_len - len(input_ids))
        word_ids = word_ids + [None] * (max_len - len(word_ids))

    # Build byte_ids to match training export
    CHAR_MAX_LEN = 24
    tokens = encoding.tokens
    if len(tokens) > max_len:
        tokens = tokens[:max_len]
    byte_rows = []
    for i in range(max_len):
        piece = tokens[i] if i < len(tokens) else ""
        if piece.startswith("##"):
            piece = piece[2:]
        b = list(piece.encode('utf-8'))[:CHAR_MAX_LEN]
        if len(b) < CHAR_MAX_LEN:
            b += [0] * (CHAR_MAX_LEN - len(b))
        byte_rows.append(b)

    # Feed both inputs in the correct order/names
    input_name_ids = session.get_inputs()[0].name
    input_name_bytes = session.get_inputs()[1].name
    onnx_input = {
        input_name_ids: np.array([input_ids], dtype=np.int64),
        input_name_bytes: np.array([byte_rows], dtype=np.int64),
    }
    logits = session.run(None, onnx_input)[0]  # [1, L, C]
    pred_ids = np.argmax(logits, axis=2)[0].tolist()
    pred_tags = [ID_TO_TAG.get(i, "O") for i in pred_ids]

    # Trim padding
    pred_tags = pred_tags[:len(word_ids)]

    # Enforce IOB
    pred_tags = enforce_iob_constraints(pred_tags)

    # Show subword-level mapping: token[tag] with BPE '##' removed, excluding padding
    pieces = encoding.tokens
    if len(pieces) > max_len:
        pieces = pieces[:max_len]
    pieces_clean = [p[2:] if p.startswith("##") else p for p in pieces]
    active = [wid is not None for wid in word_ids]
    seq_tokens = [t for t, a in zip(pieces_clean, active) if a]
    seq_tags = [t for t, a in zip(pred_tags, active) if a]
    seq_line = " ".join(f"{tok}[{tag}]" for tok, tag in zip(seq_tokens, seq_tags))

    # Also derive word-level entities for convenience
    words, first_indices = tokens_to_words(encoding)
    word_tags = []
    for fi in first_indices:
        if fi < len(pred_tags):
            word_tags.append(pred_tags[fi])
        else:
            word_tags.append("O")
    entities = extract_entities(words, word_tags)

    print("Input:", text)
    print("Sequence:", seq_line)
    if entities:
        print("Entities:")
        for ent_text, ent_type in entities:
            print(f" - {ent_text} [{ent_type}]")
    else:
        print("Entities: (none)")


def run_inference(model_path: str, tokenizer_path: str, text: str, max_len: int) -> None:
    session = ort.InferenceSession(model_path)
    tokenizer = Tokenizer.from_file(tokenizer_path)
    run_inference_loaded(session, tokenizer, text, max_len)


def main():
    parser = argparse.ArgumentParser(description="Run ONNX NER inference on input text.")
    parser.add_argument("--model", default=os.path.join(os.path.dirname(__file__), "name_classifier.onnx"))
    parser.add_argument("--tokenizer", default=os.path.join(os.path.dirname(__file__), "custom-bpe-tokenizer.json"))
    parser.add_argument("--max-len", type=int, default=128)
    parser.add_argument("--text", default=None, help="Input text to analyze; if omitted, starts REPL")
    args = parser.parse_args()

    # If single text provided, run once. Otherwise, start interactive REPL.
    if args.text:
        run_inference(args.model, args.tokenizer, args.text, args.max_len)
        return

    print("--- ONNX NER REPL ---")
    print("Type text and press Enter. Use /exit or Ctrl+C to quit.")
    print(f"Model: {args.model}")
    print(f"Tokenizer: {args.tokenizer}")

    session = ort.InferenceSession(args.model)
    tokenizer = Tokenizer.from_file(args.tokenizer)
    try:
        while True:
            try:
                line = input(">> ").strip()
            except EOFError:
                break
            if not line:
                continue
            if line.lower() in {"/exit", "exit", "quit", "/quit"}:
                break
            run_inference_loaded(session, tokenizer, line, args.max_len)
    except KeyboardInterrupt:
        pass
    print("Bye.")


if __name__ == "__main__":
    main()


