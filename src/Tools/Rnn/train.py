import os
import random
import time
import torch
import torch.nn as nn
import torch.optim as optim
from torch.utils.data import Dataset, DataLoader, random_split
import pandas as pd
from tokenizers import Tokenizer
import numpy as np
from sklearn.metrics import f1_score

# --- Configuration ---
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# The NER model requires token-level labels, so we will use a single CSV file.
DATA_FILE = os.path.join(SCRIPT_DIR, "data", "dataset.csv") 
TOKENIZER_PATH = os.path.join(SCRIPT_DIR, "custom-bpe-tokenizer.json")
ONNX_EXPORT_PATH = os.path.join(SCRIPT_DIR, "name_classifier.onnx")

# --- Labeling Scheme for NER ---
# Define the tags for our NER task
TAG_MAP = {
    "O": 0,
    "B-PER": 1, "I-PER": 2,
    "B-NICK": 3, "I-NICK": 4,
    "B-ORG": 5, "I-ORG": 6,
    "B-LOC": 7, "I-LOC": 8,
    "B-TIT": 9, "I-TIT": 10,
}
NUM_TAGS = len(TAG_MAP)


# Model Hyperparameters
# VOCAB_SIZE is determined dynamically from the tokenizer; keep constant for legacy reference only
VOCAB_SIZE = None  # resolved at runtime via tokenizer.get_vocab_size()
EMBEDDING_DIM = 128      # Increased from 32
HIDDEN_DIM = 256        # Increased from 64
OUTPUT_DIM = NUM_TAGS   # Output dimension is now the number of NER tags
MAX_LEN = 128           # Max sequence length (from your requirement)
NUM_LAYERS = 2          ### NEW ### Number of GRU layers
DROPOUT = 0.3           ### NEW ### Dropout probability

# Byte-CNN hyperparameters
CHAR_MAX_LEN = 24       # number of bytes per token we keep
CHAR_EMBED_DIM = 16
CHAR_CHANNELS = 64
CHAR_DROPOUT = 0.1

# Training Hyperparameters
LEARNING_RATE = 0.0005   # This is now the *initial* learning rate
WEIGHT_DECAY = 0.001     ### NEW ### A common value for AdamW
BATCH_SIZE = 32         # Increased from 8
EPOCHS = 1             # Train for more epochs on this small dataset
def normalize_text(text: str) -> str:
    s = text.strip()
    if not s:
        return s
    s = " ".join(s.split())
    return s.casefold()


def _format_seconds(seconds: float) -> str:
    seconds = int(max(0, seconds))
    h = seconds // 3600
    m = (seconds % 3600) // 60
    s = seconds % 60
    if h > 0:
        return f"{h:d}:{m:02d}:{s:02d}"
    return f"{m:d}:{s:02d}"


def load_ner_data_from_csv(path: str):
    """
    Loads NER data from a CSV.
    The CSV must have a 'text' column and a 'tags' column.
    The 'tags' column should contain space-separated IOB tags.
    """
    df = pd.read_csv(path)
    # Ensure 'tags' column is treated as a string, even if it's empty
    texts = df['text'].tolist()
    tags = df['tags'].astype(str).tolist()
    return texts, tags


# --- Model Definition ---
class DepthwiseSeparableConvBlock(nn.Module):
    def __init__(self, channels: int, kernel_size: int = 5, dilation: int = 1, dropout: float = 0.1):
        super().__init__()
        # Padding to preserve sequence length
        padding = (kernel_size // 2) * dilation
        self.depthwise = nn.Conv1d(
            in_channels=channels,
            out_channels=channels,
            kernel_size=kernel_size,
            padding=padding,
            dilation=dilation,
            groups=channels,
            bias=False,
        )
        self.bn_dw = nn.BatchNorm1d(channels)
        self.pointwise = nn.Conv1d(
            in_channels=channels,
            out_channels=channels,
            kernel_size=1,
            bias=False,
        )
        self.bn_pw = nn.BatchNorm1d(channels)
        self.activation = nn.ReLU()
        self.dropout = nn.Dropout(dropout)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        # x: [batch, channels, seq_len]
        residual = x
        y = self.depthwise(x)
        y = self.bn_dw(y)
        y = self.activation(y)
        y = self.pointwise(y)
        y = self.bn_pw(y)
        y = self.dropout(y)
        return y + residual


class ByteCNN(nn.Module):
    """Small CNN over UTF-8 bytes per token.
    Input: byte_ids [B, L, C]
    Output: per-token vector [B, L, CHAR_CHANNELS]
    """
    def __init__(self, num_bytes: int = 256):
        super().__init__()
        self.byte_embed = nn.Embedding(num_bytes, CHAR_EMBED_DIM)
        self.conv1 = nn.Conv1d(CHAR_EMBED_DIM, CHAR_CHANNELS, kernel_size=3, padding=1)
        self.act = nn.ReLU()
        self.bn1 = nn.BatchNorm1d(CHAR_CHANNELS)
        self.dropout = nn.Dropout(CHAR_DROPOUT)

    def forward(self, byte_ids: torch.Tensor) -> torch.Tensor:
        # byte_ids: [B, L, C]
        B, L, C = byte_ids.shape
        x = byte_ids.view(B * L, C)  # [B*L, C]
        x = self.byte_embed(x)       # [B*L, C, E]
        x = x.transpose(1, 2)        # [B*L, E, C]
        x = self.conv1(x)            # [B*L, CHAR_CHANNELS, C]
        x = self.bn1(self.act(x))
        x = torch.amax(x, dim=2)     # global max pool over char-length -> [B*L, CHAR_CHANNELS]
        x = self.dropout(x)
        x = x.view(B, L, CHAR_CHANNELS)
        return x


class NerClassifier(nn.Module):
    def __init__(self, vocab_size, embedding_dim, hidden_dim, output_dim, n_layers, dropout):
        super().__init__()
        # Embedding and positional encoding
        self.token_embedding = nn.Embedding(vocab_size, embedding_dim)
        self.positional_embedding = nn.Embedding(MAX_LEN, embedding_dim)
        self.byte_cnn = ByteCNN()
        # Project embeddings to CNN channel width (reuse hidden_dim as channels)
        self.input_projection = nn.Linear(embedding_dim + CHAR_CHANNELS, hidden_dim)
        # Depthwise-separable CNN stack with dilations
        blocks = []
        # Cycle dilations [1, 2, 4, 8] to expand receptive field efficiently
        dilation_cycle = [1, 2, 4, 8]
        for i in range(max(1, int(n_layers))):
            dilation = dilation_cycle[i % len(dilation_cycle)]
            blocks.append(DepthwiseSeparableConvBlock(hidden_dim, kernel_size=5, dilation=dilation, dropout=dropout))
        self.conv_blocks = nn.ModuleList(blocks)
        # Output projection to tag logits per token
        self.output_projection = nn.Linear(hidden_dim, output_dim)

    def forward(self, input_ids: torch.Tensor, byte_ids: torch.Tensor) -> torch.Tensor:
        # input_ids: [batch, seq_len]
        # byte_ids: [batch, seq_len, CHAR_MAX_LEN]
        batch_size, seq_len = input_ids.size(0), input_ids.size(1)
        token_embed = self.token_embedding(input_ids)  # [B, L, E]
        # positions 0..seq_len-1 (cap at MAX_LEN)
        positions = torch.arange(seq_len, device=input_ids.device).clamp(max=MAX_LEN - 1)
        positions = positions.unsqueeze(0).expand(batch_size, -1)  # [B, L]
        pos_embed = self.positional_embedding(positions)  # [B, L, E]
        x_tok = token_embed + pos_embed  # [B, L, E]
        # Byte features
        x_byte = self.byte_cnn(byte_ids)  # [B, L, CHAR_CHANNELS]
        x = torch.cat([x_tok, x_byte], dim=2)
        x = self.input_projection(x)  # [B, L, C]
        x = x.transpose(1, 2)  # [B, C, L]
        for block in self.conv_blocks:
            x = block(x)  # [B, C, L]
        x = x.transpose(1, 2)  # [B, L, C]
        logits = self.output_projection(x)  # [B, L, output_dim]
        return logits


# --- PyTorch Dataset ---
class NerDataset(Dataset):
    def __init__(self, texts, tags, tokenizer, max_len, tag_map):
        self.texts = texts
        self.tags = tags
        self.tokenizer = tokenizer
        self.max_len = max_len
        self.tag_map = tag_map
        self.pad_token_id = tokenizer.token_to_id("[PAD]") or 0

    def __len__(self):
        return len(self.texts)

    def __getitem__(self, idx):
        # Ensure text is a plain string; tokenizer expects str
        raw_text = self.texts[idx]
        text = str(raw_text) if raw_text is not None else ""
        tags = str(self.tags[idx]).split()

        encoding = self.tokenizer.encode(text)
        input_ids = encoding.ids
        tokens = encoding.tokens
        offsets = encoding.offsets
        
        # Align labels with tokens. Label all subtokens for entity words:
        # first subtoken keeps B-/I- tag, subsequent subtokens use the corresponding I- tag.
        # For non-entity (O) words, only the first subtoken is labeled; others are ignored (-100)
        word_ids = encoding.word_ids
        aligned_labels = np.full(len(word_ids), -100, dtype=np.int64)
        
        previous_word_id = None
        for i, word_id in enumerate(word_ids):
            if word_id is None:
                continue # Special token
            if word_id < len(tags):
                tag = tags[word_id]
                if word_id != previous_word_id:
                    # First subtoken: keep original tag
                    aligned_labels[i] = self.tag_map.get(tag, self.tag_map["O"])
                else:
                    # Subsequent subtokens: if entity, force I-<TYPE>; if O, ignore
                    if tag.startswith("B-") or tag.startswith("I-"):
                        base = tag.split("-", 1)[1]
                        i_tag = f"I-{base}"
                        aligned_labels[i] = self.tag_map.get(i_tag, self.tag_map["O"])
            previous_word_id = word_id
        # Build per-token byte sequences using offsets (fallback to token string)
        byte_rows: list[list[int]] = []
        for i in range(len(word_ids)):
            b_start, b_end = (0, 0)
            try:
                b_start, b_end = offsets[i]
            except Exception:
                pass
            if b_end > b_start and b_end <= len(text):
                substr = text[b_start:b_end]
            else:
                tok = tokens[i]
                substr = tok[2:] if tok.startswith("##") else tok
            b = list(substr.encode('utf-8'))[:CHAR_MAX_LEN]
            if len(b) < CHAR_MAX_LEN:
                b += [0] * (CHAR_MAX_LEN - len(b))
            byte_rows.append(b)

        # Pad sequences to max_len
        if len(input_ids) > self.max_len:
            input_ids = input_ids[:self.max_len]
            aligned_labels = aligned_labels[:self.max_len]
            byte_rows = byte_rows[:self.max_len]
        else:
            pad_length = self.max_len - len(input_ids)
            input_ids = input_ids + [self.pad_token_id] * pad_length
            aligned_labels = np.pad(aligned_labels, (0, pad_length), mode='constant', constant_values=-100)
            for _ in range(pad_length):
                byte_rows.append([0] * CHAR_MAX_LEN)
            
        return {
            'ids': torch.tensor(input_ids, dtype=torch.long),
            'byte_ids': torch.tensor(byte_rows, dtype=torch.long),
            'labels': torch.tensor(aligned_labels, dtype=torch.long)
        }


def main():
    import argparse
    parser = argparse.ArgumentParser(description="Train a NER classifier. The input data must be in a CSV file with 'text' and 'tags' columns.")
    parser.add_argument("--use-wandb", action="store_true", help="Enable Weights & Biases logging")
    parser.add_argument("--wandb-project", default="morpheus-ner", help="wandb project name")
    parser.add_argument("--wandb-entity", default=None, help="wandb entity (team)")
    parser.add_argument("--run-name", default=None, help="Optional run name")
    parser.add_argument("--checkpoint-dir", default=os.path.join(SCRIPT_DIR, "checkpoints"), help="Directory to save checkpoints")
    parser.add_argument("--resume", default=None, help="Path to checkpoint to resume from")
    parser.add_argument("--log-train-interval", type=int, default=100, help="Log training loss to wandb every N steps (batches)")
    parser.add_argument("--print-train-interval", type=int, default=200, help="Print training loss/ETA every N steps (batches)")
    parser.add_argument("--early-stop-patience", type=int, default=3, help="Early stop if val loss doesn't improve for this many epochs")
    parser.add_argument("--early-stop-min-delta", type=float, default=0.0, help="Minimum improvement on val loss to reset early stopping patience")
    parser.add_argument("--epochs", type=int, default=3, help="Number of epochs to train (upper bound; early stopping may end sooner)")
    parser.add_argument("--max-steps", type=int, default=None, help="Stop training entirely after this many total optimizer steps")
    parser.add_argument("--max-steps-per-epoch", type=int, default=None, help="Limit number of optimizer steps per epoch")
    parser.add_argument("--val-interval-steps", type=int, default=5000, help="Run a quick validation every N steps (0 to disable)")
    parser.add_argument("--val-max-batches", type=int, default=50, help="Max validation batches to use for quick step validation")
    parser.add_argument("--save-interval-steps", type=int, default=20000, help="Save a rolling checkpoint every N optimizer steps (0 to disable)")
    parser.add_argument("--num-workers", type=int, default=0, help="DataLoader worker processes (use 2-8 on GPU for speed)")
    args = parser.parse_args()

    use_wandb = args.use_wandb
    if use_wandb:
        try:
            import wandb  # type: ignore
        except Exception as e:
            print("wandb not available; proceed without it. Error:", e)
            use_wandb = False
    print("--- Starting NER Model Training ---")
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    print(f"Using device: {device}")

    # 1. Load data and tokenizer
    print(f"Loading NER data from {DATA_FILE}...")
    texts, tags = load_ner_data_from_csv(DATA_FILE)
    tokenizer = Tokenizer.from_file(TOKENIZER_PATH)
    vocab_size = tokenizer.get_vocab_size()

    # 2. Create dataset and split
    dataset = NerDataset(
        texts=texts,
        tags=tags,
        tokenizer=tokenizer,
        max_len=MAX_LEN,
        tag_map=TAG_MAP
    )
    train_size = int(0.8 * len(dataset))
    val_size = len(dataset) - train_size
    train_dataset, val_dataset = random_split(dataset, [train_size, val_size])
    pin = (device.type == "cuda")
    train_loader = DataLoader(train_dataset, batch_size=BATCH_SIZE, shuffle=True,
                              num_workers=args.num_workers, pin_memory=pin,
                              persistent_workers=True if args.num_workers > 0 else False)
    val_loader = DataLoader(val_dataset, batch_size=BATCH_SIZE,
                            num_workers=args.num_workers, pin_memory=pin,
                            persistent_workers=True if args.num_workers > 0 else False)

    # 3. Initialize model, loss, optimizer, and optional wandb
    model = NerClassifier(
        vocab_size=vocab_size,
        embedding_dim=EMBEDDING_DIM,
        hidden_dim=HIDDEN_DIM,
        output_dim=OUTPUT_DIM,
        n_layers=NUM_LAYERS,
        dropout=DROPOUT,
    ).to(device)
    # Use ignore_index to skip padded tokens in loss calculation
    criterion = nn.CrossEntropyLoss(ignore_index=-100)
    
    ### NEW ### Use AdamW optimizer with weight decay
    optimizer = optim.AdamW(model.parameters(), lr=LEARNING_RATE, weight_decay=WEIGHT_DECAY)
    
    ### NEW ### Add a learning rate scheduler
    # This will decay the LR from its initial value down to 0 over the course of all epochs
    num_epochs = args.epochs
    scheduler = optim.lr_scheduler.CosineAnnealingLR(optimizer, T_max=num_epochs)

    # Wandb setup
    if use_wandb:
        config = {
            "vocab_size": vocab_size,
            "embedding_dim": EMBEDDING_DIM,
            "hidden_dim": HIDDEN_DIM,
            "output_dim": OUTPUT_DIM,
            "max_len": MAX_LEN,
            "n_layers": NUM_LAYERS,
            "dropout": DROPOUT,
            "batch_size": BATCH_SIZE,
            "epochs": num_epochs,
            "learning_rate": LEARNING_RATE,
            "weight_decay": WEIGHT_DECAY,
            "optimizer": "AdamW",
            "scheduler": "CosineAnnealingLR",
            "data_source": "csv",
        }
        wandb.init(project=args.wandb_project, entity=args.wandb_entity, name=args.run_name, config=config)
        # Define custom step metrics for nicer charts
        wandb.define_metric("global_step")
        wandb.define_metric("epoch")
        wandb.define_metric("train/*", step_metric="global_step")
        # Log validation against global step so it updates frequently
        wandb.define_metric("val/loss", step_metric="global_step")
        wandb.define_metric("val/token_accuracy", step_metric="global_step")
        wandb.define_metric("val_step/*", step_metric="global_step")
        wandb.define_metric("lr", step_metric="epoch")
        # Additional metrics
        wandb.define_metric("val/token_f1", step_metric="global_step")
        wandb.define_metric("val_step/token_f1", step_metric="global_step")
        # wandb.watch(model, log="gradients", log_freq=200)

    # Resume from checkpoint if provided
    start_epoch = 0
    os.makedirs(args.checkpoint_dir, exist_ok=True)
    if args.resume is not None and os.path.exists(args.resume):
        ckpt = torch.load(args.resume, map_location=device)
        model.load_state_dict(ckpt["model_state_dict"])
        optimizer.load_state_dict(ckpt["optimizer_state_dict"])
        if "scheduler_state_dict" in ckpt:
            scheduler.load_state_dict(ckpt["scheduler_state_dict"])
        start_epoch = int(ckpt.get("epoch", 0)) + 1
        print(f"Resumed from checkpoint: {args.resume} at epoch {start_epoch}")

    # 4. Training loop
    best_val_loss = float("inf")
    best_epoch = -1
    epochs_since_improve = 0
    best_ckpt_path = None
    global_step = 0
    stop_training = False
    try:
        for epoch in range(start_epoch, num_epochs):
            model.train()
            total_train_loss = 0
            epoch_start = time.time()
            last_print = epoch_start
            steps_per_epoch = len(train_loader)
            step_in_epoch = 0
            for batch in train_loader:
                step_in_epoch += 1
                ids = batch['ids'].to(device)
                byte_ids = batch['byte_ids'].to(device)
                labels = batch['labels'].to(device)
                optimizer.zero_grad()
                outputs = model(ids, byte_ids) # Shape: [batch, seq_len, num_tags]
                
                # Reshape for CrossEntropyLoss
                # outputs -> [batch * seq_len, num_tags]
                # labels -> [batch * seq_len]
                loss = criterion(outputs.view(-1, NUM_TAGS), labels.view(-1))
                
                loss.backward()
                optimizer.step()
                total_train_loss += loss.item()
                # Per-step logging
                if use_wandb and (global_step % max(1, args.log_train_interval) == 0):
                    wandb.log({
                        "global_step": global_step,
                        "train/loss": loss.item(),
                    })
                global_step += 1

                # Immediate max-steps check — stop cleanly as soon as we hit the cap
                if args.max_steps is not None and global_step >= args.max_steps:
                    step_latest_path = os.path.join(args.checkpoint_dir, "step_latest.pt")
                    ckpt_step = {
                        "epoch": epoch,
                        "global_step": global_step,
                        "model_state_dict": model.state_dict(),
                        "optimizer_state_dict": optimizer.state_dict(),
                        "scheduler_state_dict": scheduler.state_dict(),
                        "config": {
                            "VOCAB_SIZE": vocab_size,
                            "EMBEDDING_DIM": EMBEDDING_DIM,
                            "HIDDEN_DIM": HIDDEN_DIM,
                            "OUTPUT_DIM": OUTPUT_DIM,
                            "MAX_LEN": MAX_LEN,
                            "NUM_LAYERS": NUM_LAYERS,
                            "DROPOUT": DROPOUT,
                            "TAG_MAP": TAG_MAP,
                        },
                    }
                    torch.save(ckpt_step, step_latest_path)
                    print(f"[checkpoint] Reached max_steps={args.max_steps}, saved {step_latest_path}", flush=True)
                    stop_training = True
                    break

                # Console progress printing (independent of save interval)
                if args.print_train_interval > 0 and ((global_step % args.print_train_interval == 0) or global_step == 1):
                    now = time.time()
                    elapsed = now - epoch_start
                    step_time = (now - last_print) / max(1, args.print_train_interval)
                    if args.max_steps is not None:
                        remaining = max(0, args.max_steps - global_step)
                    else:
                        remaining = max(0, steps_per_epoch - step_in_epoch)
                    eta = remaining * step_time
                    global_cap = str(args.max_steps) if args.max_steps is not None else "-"
                    print(
                        f"Step {global_step}/{global_cap} (epoch {epoch+1}/{num_epochs} {step_in_epoch}/{steps_per_epoch}) | "
                        f"loss={loss.item():.4f} | elapsed={_format_seconds(elapsed)} | eta={_format_seconds(eta)}",
                        flush=True,
                    )
                    last_print = now

                # Quick mid-epoch validation (independent of save interval)
                if args.val_interval_steps and (global_step % args.val_interval_steps == 0):
                    model.eval()
                    quick_val_loss = 0.0
                    quick_val_correct = 0
                    quick_val_total = 0
                    with torch.no_grad():
                        all_true: list[int] = []
                        all_pred: list[int] = []
                        for i, vbatch in enumerate(val_loader):
                            if i >= max(1, args.val_max_batches):
                                break
                            vids = vbatch['ids'].to(device)
                            vbyte = vbatch['byte_ids'].to(device)
                            vlabels = vbatch['labels'].to(device)
                            voutputs = model(vids, vbyte)
                            vloss = criterion(voutputs.view(-1, NUM_TAGS), vlabels.view(-1))
                            quick_val_loss += vloss.item()
                            vpred = torch.argmax(voutputs, dim=2)
                            active_tokens = vlabels != -100
                            quick_val_correct += (vpred[active_tokens] == vlabels[active_tokens]).sum().item()
                            quick_val_total += active_tokens.sum().item()
                            if active_tokens.any():
                                all_true.extend(vlabels[active_tokens].view(-1).detach().cpu().tolist())
                                all_pred.extend(vpred[active_tokens].view(-1).detach().cpu().tolist())
                    denom = max(1, min(len(val_loader), args.val_max_batches))
                    quick_val_loss /= denom
                    quick_val_acc = quick_val_correct / max(1, quick_val_total)
                    quick_val_f1 = f1_score(all_true, all_pred, average='micro') if all_true else 0.0
                    print(f"[val_step] step={global_step} loss={quick_val_loss:.4f} acc={quick_val_acc:.4f} f1={quick_val_f1:.4f}", flush=True)
                    if use_wandb:
                        wandb.log({
                            "global_step": global_step,
                            "val/loss": quick_val_loss,
                            "val/token_accuracy": quick_val_acc,
                            "val_step/loss": quick_val_loss,
                            "val_step/token_accuracy": quick_val_acc,
                            "val_step/token_f1": quick_val_f1,
                        })
                    model.train()

                # Periodic rolling checkpoint to avoid losing progress on long runs
            if args.save_interval_steps and (global_step % max(1, args.save_interval_steps) == 0):
                step_latest_path = os.path.join(args.checkpoint_dir, "step_latest.pt")
                ckpt_step = {
                    "epoch": epoch,
                    "global_step": global_step,
                    "model_state_dict": model.state_dict(),
                    "optimizer_state_dict": optimizer.state_dict(),
                    "scheduler_state_dict": scheduler.state_dict(),
                    "config": {
                        "VOCAB_SIZE": vocab_size,
                        "EMBEDDING_DIM": EMBEDDING_DIM,
                        "HIDDEN_DIM": HIDDEN_DIM,
                        "OUTPUT_DIM": OUTPUT_DIM,
                        "MAX_LEN": MAX_LEN,
                        "NUM_LAYERS": NUM_LAYERS,
                        "DROPOUT": DROPOUT,
                        "TAG_MAP": TAG_MAP,
                    },
                }
                torch.save(ckpt_step, step_latest_path)
                print(f"[checkpoint] Saved {step_latest_path} at step {global_step}", flush=True)
                if use_wandb:
                    try:
                        import wandb
                        wandb.save(step_latest_path)
                    except Exception:
                        pass

                # Console progress printing
                if args.print_train_interval > 0 and ((global_step % args.print_train_interval == 0) or global_step == 1):
                    now = time.time()
                    elapsed = now - epoch_start
                    step_time = (now - last_print) / max(1, args.print_train_interval)
                    # ETA: prefer remaining to global max-steps if provided; otherwise remaining in epoch
                    if args.max_steps is not None:
                        remaining = max(0, args.max_steps - global_step)
                    else:
                        remaining = max(0, steps_per_epoch - step_in_epoch)
                    eta = remaining * step_time
                    global_cap = str(args.max_steps) if args.max_steps is not None else "-"
                    print(
                        f"Step {global_step}/{global_cap} (epoch {epoch+1}/{num_epochs} {step_in_epoch}/{steps_per_epoch}) | "
                        f"loss={loss.item():.4f} | elapsed={_format_seconds(elapsed)} | eta={_format_seconds(eta)}"
                    , flush=True)
                    last_print = now

                # Quick mid-epoch validation
                if args.val_interval_steps and (global_step % args.val_interval_steps == 0):
                    model.eval()
                    quick_val_loss = 0.0
                    quick_val_correct = 0
                    quick_val_total = 0
                    with torch.no_grad():
                        all_true: list[int] = []
                        all_pred: list[int] = []
                        for i, vbatch in enumerate(val_loader):
                            if i >= max(1, args.val_max_batches):
                                break
                            vids = vbatch['ids'].to(device)
                            vbyte = vbatch['byte_ids'].to(device)
                            vlabels = vbatch['labels'].to(device)
                            voutputs = model(vids, vbyte)
                            vloss = criterion(voutputs.view(-1, NUM_TAGS), vlabels.view(-1))
                            quick_val_loss += vloss.item()
                            
                            # Calculate token-level accuracy (ignoring padding)
                            vpred = torch.argmax(voutputs, dim=2)
                            active_tokens = vlabels != -100
                            quick_val_correct += (vpred[active_tokens] == vlabels[active_tokens]).sum().item()
                            quick_val_total += active_tokens.sum().item()
                            if active_tokens.any():
                                all_true.extend(vlabels[active_tokens].view(-1).detach().cpu().tolist())
                                all_pred.extend(vpred[active_tokens].view(-1).detach().cpu().tolist())
                            
                    denom = max(1, min(len(val_loader), args.val_max_batches))
                    quick_val_loss /= denom
                    quick_val_acc = quick_val_correct / max(1, quick_val_total)
                    # F1 (micro)
                    quick_val_f1 = f1_score(all_true, all_pred, average='micro') if all_true else 0.0
                    # Console visibility
                    print(f"[val_step] step={global_step} loss={quick_val_loss:.4f} acc={quick_val_acc:.4f} f1={quick_val_f1:.4f}", flush=True)
                    if use_wandb:
                        wandb.log({
                            "global_step": global_step,
                            "val/loss": quick_val_loss,
                            "val/token_accuracy": quick_val_acc,
                            "val_step/loss": quick_val_loss,
                            "val_step/token_accuracy": quick_val_acc,
                            "val_step/token_f1": quick_val_f1,
                        })
                    model.train()

                # Optional limits
                if args.max_steps is not None and global_step >= args.max_steps:
                    stop_training = True
                    break
                if args.max_steps_per_epoch is not None and step_in_epoch >= args.max_steps_per_epoch:
                    break
        
            # Validation
            model.eval()
            total_val_loss = 0
            total_correct_predictions = 0
            total_tokens = 0
            with torch.no_grad():
                all_true_epoch: list[int] = []
                all_pred_epoch: list[int] = []
                for batch in val_loader:
                    ids = batch['ids'].to(device)
                    byte_batch = batch['byte_ids'].to(device)
                    labels = batch['labels'].to(device)
                    outputs = model(ids, byte_batch)
                    loss = criterion(outputs.view(-1, NUM_TAGS), labels.view(-1))
                    total_val_loss += loss.item()
                    
                    # Calculate token-level accuracy (ignoring padding)
                    predicted = torch.argmax(outputs, dim=2)
                    active_tokens = labels != -100
                    total_correct_predictions += (predicted[active_tokens] == labels[active_tokens]).sum().item()
                    total_tokens += active_tokens.sum().item()
                    if active_tokens.any():
                        all_true_epoch.extend(labels[active_tokens].view(-1).detach().cpu().tolist())
                        all_pred_epoch.extend(predicted[active_tokens].view(-1).detach().cpu().tolist())

        ### NEW ### Update the learning rate at the end of the epoch
        scheduler.step()

        avg_train_loss = total_train_loss / len(train_loader)
        avg_val_loss = total_val_loss / len(val_loader)
        val_accuracy = total_correct_predictions / max(1, total_tokens)
        val_f1 = f1_score(all_true_epoch, all_pred_epoch, average='micro') if all_true_epoch else 0.0
        
        # Get current learning rate to display
        current_lr = optimizer.param_groups[0]['lr']
        
        # Log every epoch
        print(f"Epoch {epoch+1}/{num_epochs} | Train Loss: {avg_train_loss:.4f} | Val Loss: {avg_val_loss:.4f} | Val Acc: {val_accuracy:.4f} | Val F1: {val_f1:.4f} | LR: {current_lr:.6f}")
        if use_wandb:
            wandb.log({
                "global_step": global_step,
                "epoch": epoch + 1,
                "train/epoch_loss": avg_train_loss,
                "val/loss": avg_val_loss,
                "val/token_accuracy": val_accuracy,
                "val/token_f1": val_f1,
                "lr": current_lr,
            })

        # Save checkpoint
        ckpt_path = os.path.join(args.checkpoint_dir, f"checkpoint_epoch_{epoch+1:03d}.pt")
        latest_path = os.path.join(args.checkpoint_dir, "latest.pt")
        ckpt = {
            "epoch": epoch,
            "model_state_dict": model.state_dict(),
            "optimizer_state_dict": optimizer.state_dict(),
            "scheduler_state_dict": scheduler.state_dict(),
            "config": {
                "VOCAB_SIZE": vocab_size,
                "EMBEDDING_DIM": EMBEDDING_DIM,
                "HIDDEN_DIM": HIDDEN_DIM,
                "OUTPUT_DIM": OUTPUT_DIM,
                "MAX_LEN": MAX_LEN,
                "NUM_LAYERS": NUM_LAYERS,
                "DROPOUT": DROPOUT,
                "TAG_MAP": TAG_MAP,
            },
        }
        torch.save(ckpt, ckpt_path)
        torch.save(ckpt, latest_path)
        if use_wandb:
            try:
                import wandb
                wandb.save(ckpt_path)
                wandb.save(latest_path)
            except Exception:
                pass

        # Track best
        if avg_val_loss < (best_val_loss - args.early_stop_min_delta):
            best_val_loss = avg_val_loss
            best_epoch = epoch + 1
            best_ckpt_path = os.path.join(args.checkpoint_dir, "best.pt")
            torch.save(ckpt, best_ckpt_path)
            epochs_since_improve = 0
            print(f"New best val loss {best_val_loss:.6f} at epoch {best_epoch}. Saved {best_ckpt_path}")
            if use_wandb:
                try:
                    wandb.run.summary["best_val_loss"] = best_val_loss
                    wandb.run.summary["best_epoch"] = best_epoch
                    wandb.save(best_ckpt_path)
                except Exception:
                    pass
        else:
            epochs_since_improve += 1
            print(f"No improvement in val loss for {epochs_since_improve}/{args.early_stop_patience} epoch(s)")
            if epochs_since_improve >= args.early_stop_patience:
                print("Early stopping triggered.")
                stop_training = True
            if stop_training:
                print("Reached global stop condition; ending training loop.")
                # Exit outer epoch loop gracefully
                return finalize_and_export(model, best_ckpt_path, best_val_loss, best_epoch, vocab_size)
    except Exception as e:
        # Save a crash checkpoint with latest state
        os.makedirs(args.checkpoint_dir, exist_ok=True)
        crash_path = os.path.join(args.checkpoint_dir, "crash_latest.pt")
        ckpt = {
            "epoch": epoch if 'epoch' in locals() else 0,
            "global_step": global_step if 'global_step' in locals() else 0,
            "model_state_dict": model.state_dict(),
            "optimizer_state_dict": optimizer.state_dict(),
            "scheduler_state_dict": scheduler.state_dict(),
            "config": {
                "VOCAB_SIZE": vocab_size,
                "EMBEDDING_DIM": EMBEDDING_DIM,
                "HIDDEN_DIM": HIDDEN_DIM,
                "OUTPUT_DIM": OUTPUT_DIM,
                "MAX_LEN": MAX_LEN,
                "NUM_LAYERS": NUM_LAYERS,
                "DROPOUT": DROPOUT,
                "TAG_MAP": TAG_MAP,
            },
            "error": str(e),
        }
        torch.save(ckpt, crash_path)
        print(f"Exception occurred: {e}. Saved crash checkpoint to {crash_path}")
        # Proceed to export best available model below

    print("--- Training Finished ---")
    finalize_and_export(model, best_ckpt_path, best_val_loss, best_epoch, vocab_size)

def finalize_and_export(model: nn.Module, best_ckpt_path: str | None, best_val_loss: float, best_epoch: int, vocab_size: int):
    # 5. Export to ONNX
    if best_ckpt_path and os.path.exists(best_ckpt_path):
        print(f"Loading best checkpoint from {best_ckpt_path} for ONNX export (val loss {best_val_loss:.6f} at epoch {best_epoch})")
        best_ckpt = torch.load(best_ckpt_path, map_location="cpu")
        model.load_state_dict(best_ckpt["model_state_dict"])
    print("--- Exporting model to ONNX ---")
    model.eval()
    model.to("cpu")
    dummy_input_ids = torch.randint(0, vocab_size, (1, MAX_LEN), dtype=torch.long)
    dummy_byte_ids = torch.zeros((1, MAX_LEN, CHAR_MAX_LEN), dtype=torch.long)
    input_names = ["input_ids", "byte_ids"]
    output_names = ["logits"]
    torch.onnx.export(model,
                      (dummy_input_ids, dummy_byte_ids),
                      ONNX_EXPORT_PATH,
                      input_names=input_names,
                      output_names=output_names,
                      opset_version=14,
                      dynamic_axes={
                          'input_ids': {0: 'batch_size', 1: 'sequence_length'},
                          'byte_ids': {0: 'batch_size', 1: 'sequence_length', 2: 'char_length'},
                          'logits': {0: 'batch_size', 1: 'sequence_length'}
                      })
    print(f"Model successfully exported to {ONNX_EXPORT_PATH}")


if __name__ == "__main__":
    main()