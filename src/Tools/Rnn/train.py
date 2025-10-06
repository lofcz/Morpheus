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
LEARNING_RATE = 0.0003   # Lower initial LR for single epoch training
WEIGHT_DECAY = 0.00005   # Very low weight decay for single epoch
BATCH_SIZE = 32          # Increased from 8
EPOCHS = 1               # Single epoch (300k steps ~4hrs on RTX 2080)
WARMUP_STEPS = 2000      # Longer warmup for single epoch (0.67% of 300k)
GRADIENT_CLIP = 1.0      # Clip gradients to prevent exploding
LABEL_SMOOTHING = 0.0    # Disabled - too aggressive for NER
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
class SqueezeExcitation(nn.Module):
    """Lightweight channel attention mechanism - adds <1% parameters"""
    def __init__(self, channels: int, reduction: int = 4):
        super().__init__()
        self.fc1 = nn.Linear(channels, channels // reduction)
        self.fc2 = nn.Linear(channels // reduction, channels)
        self.activation = nn.GELU()
        self.gate = nn.Sigmoid()
    
    def forward(self, x: torch.Tensor) -> torch.Tensor:
        # x: [B, C, L]
        B, C, L = x.shape
        # Global average pooling
        squeeze = x.mean(dim=2)  # [B, C]
        # Excitation
        excitation = self.fc1(squeeze)
        excitation = self.activation(excitation)
        excitation = self.fc2(excitation)
        excitation = self.gate(excitation)  # [B, C]
        # Scale
        return x * excitation.unsqueeze(2)


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
        self.ln_dw = nn.LayerNorm(channels)  # LayerNorm instead of BatchNorm
        self.pointwise = nn.Conv1d(
            in_channels=channels,
            out_channels=channels,
            kernel_size=1,
            bias=False,
        )
        self.ln_pw = nn.LayerNorm(channels)  # LayerNorm instead of BatchNorm
        self.activation = nn.GELU()
        self.dropout = nn.Dropout(dropout)
        # Squeeze-and-Excitation for channel attention
        self.se = SqueezeExcitation(channels)
        # Position-wise Feed-Forward Network
        self.ffn = nn.Sequential(
            nn.Linear(channels, channels * 4),
            nn.GELU(),
            nn.Linear(channels * 4, channels),
            nn.Dropout(dropout)
        )
        self.ln_ffn = nn.LayerNorm(channels)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        # x: [batch, channels, seq_len]
        residual = x
        y = self.depthwise(x)
        y = y.transpose(1, 2)  # [B, L, C] for LayerNorm
        y = self.ln_dw(y)
        y = y.transpose(1, 2)  # [B, C, L]
        y = self.activation(y)
        y = self.pointwise(y)
        y = y.transpose(1, 2)  # [B, L, C]
        y = self.ln_pw(y)
        y = y.transpose(1, 2)  # [B, C, L]
        y = self.dropout(y)
        # Apply SE attention
        y = self.se(y)
        y = y + residual

        # Apply Feed-Forward Network
        residual2 = y
        y = y.transpose(1, 2) # [B, L, C]
        y = self.ffn(y)
        y = self.ln_ffn(y)
        y = y.transpose(1, 2) # [B, C, L]
        y = y + residual2

        return y


class ByteCNN(nn.Module):
    """Improved CNN over UTF-8 bytes per token with residual connections.
    Input: byte_ids [B, L, C]
    Output: per-token vector [B, L, CHAR_CHANNELS]
    """
    def __init__(self, num_bytes: int = 256):
        super().__init__()
        self.byte_embed = nn.Embedding(num_bytes, CHAR_EMBED_DIM)
        # First conv layer
        self.conv1 = nn.Conv1d(CHAR_EMBED_DIM, CHAR_CHANNELS, kernel_size=3, padding=1)
        self.ln1 = nn.LayerNorm(CHAR_CHANNELS)  # LayerNorm instead of BatchNorm
        self.act = nn.GELU()  # GELU for consistency
        # Second conv layer with residual
        self.conv2 = nn.Conv1d(CHAR_CHANNELS, CHAR_CHANNELS, kernel_size=3, padding=1)
        self.ln2 = nn.LayerNorm(CHAR_CHANNELS)
        self.dropout = nn.Dropout(CHAR_DROPOUT)

    def forward(self, byte_ids: torch.Tensor) -> torch.Tensor:
        # byte_ids: [B, L, C]
        B, L, C = byte_ids.shape
        x = byte_ids.view(B * L, C)  # [B*L, C]
        x = self.byte_embed(x)       # [B*L, C, E]
        x = x.transpose(1, 2)        # [B*L, E, C]
        
        # First conv
        x = self.conv1(x)            # [B*L, CHAR_CHANNELS, C]
        x = x.transpose(1, 2)        # [B*L, C, CHAR_CHANNELS] for LayerNorm
        x = self.ln1(x)
        x = x.transpose(1, 2)        # [B*L, CHAR_CHANNELS, C]
        x = self.act(x)
        
        # Second conv with residual
        residual = x
        x = self.conv2(x)            # [B*L, CHAR_CHANNELS, C]
        x = x.transpose(1, 2)        # [B*L, C, CHAR_CHANNELS]
        x = self.ln2(x)
        x = x.transpose(1, 2)        # [B*L, CHAR_CHANNELS, C]
        x = self.act(x)
        x = x + residual  # Residual connection
        
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
        
        # Align labels with tokens. A single word can be split into multiple subtokens.
        # The first subtoken gets the B- or I- tag. All subsequent subtokens of the
        # same word get the corresponding I- tag (or O if the word is O).
        word_ids = encoding.word_ids
        aligned_labels = np.full(len(word_ids), -100, dtype=np.int64)
        
        previous_word_id = None
        for i, word_id in enumerate(word_ids):
            if word_id is None:
                continue # Special token like [CLS], [SEP]

            # If this is the first subtoken of a new word, assign its tag
            if word_id != previous_word_id:
                if word_id < len(tags):
                    tag = tags[word_id]
                    aligned_labels[i] = self.tag_map.get(tag, self.tag_map["O"])
            
            # If this is a subsequent subtoken of the same word, convert its tag to I-
            else:
                if word_id < len(tags):
                    tag = tags[word_id]
                    if tag.startswith("B-"):
                        # Convert "B-PER" to "I-PER"
                        base_tag = tag.split("-", 1)[1]
                        i_tag = f"I-{base_tag}"
                        aligned_labels[i] = self.tag_map.get(i_tag, self.tag_map["O"])
                    elif tag.startswith("I-"):
                        # It's already an I- tag, keep it
                        aligned_labels[i] = self.tag_map.get(tag, self.tag_map["O"])
                    else: # O tag
                        # Subsequent subtokens of an O word should also be O
                        aligned_labels[i] = self.tag_map["O"]
                        
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
    parser.add_argument("--epochs", type=int, default=1, help="Number of epochs to train (1 epoch = ~300k steps = ~4hrs on RTX 2080)")
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
    # Add label smoothing for better generalization
    criterion = nn.CrossEntropyLoss(ignore_index=-100, label_smoothing=LABEL_SMOOTHING)
    
    ### NEW ### Use AdamW optimizer with weight decay
    optimizer = optim.AdamW(model.parameters(), lr=LEARNING_RATE, weight_decay=WEIGHT_DECAY)
    
    ### NEW ### Learning rate scheduler optimized for single epoch training
    num_epochs = args.epochs
    steps_per_epoch = len(train_loader)
    total_steps = num_epochs * steps_per_epoch
    
    # For single epoch: use very gentle cosine decay to 70% of peak LR
    # T_max should be the number of steps AFTER warmup
    scheduler_steps = total_steps - WARMUP_STEPS if WARMUP_STEPS > 0 else total_steps
    scheduler = optim.lr_scheduler.CosineAnnealingLR(
        optimizer, 
        T_max=scheduler_steps,
        eta_min=LEARNING_RATE * 0.7
    )
    print(f"Using gentle cosine decay: {WARMUP_STEPS} warmup steps + {scheduler_steps} cosine steps (total {total_steps})")
    print(f"LR schedule: {LEARNING_RATE * 0.1:.6f} → {LEARNING_RATE:.6f} (warmup) → {LEARNING_RATE * 0.7:.6f} (cosine)")
    
    # Track warmup separately for the first WARMUP_STEPS
    warmup_active = WARMUP_STEPS > 0

    # Exponential Moving Average for better generalization
    # EMA maintains a moving average of model parameters
    from torch.optim.swa_utils import AveragedModel
    ema_model = AveragedModel(model, avg_fn=lambda avg, curr, steps: 0.999 * avg + 0.001 * curr)
    print("Using EMA with decay=0.999")
    
    # Mixed precision training - DISABLED by default for NER stability
    # Can cause numerical issues with token-level classification
    use_amp = False  # Set to True and device.type == "cuda" to enable
    scaler = torch.amp.GradScaler('cuda') if use_amp else None
    if use_amp:
        print("Using mixed precision training (AMP)")
    else:
        print("Mixed precision disabled for training stability")

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
            "scheduler": "CosineAnnealingLR+ManualWarmup",
            "warmup_steps": WARMUP_STEPS,
            "gradient_clip": GRADIENT_CLIP,
            "label_smoothing": LABEL_SMOOTHING,
            "char_channels": CHAR_CHANNELS,
            "char_max_len": CHAR_MAX_LEN,
            "data_source": "csv",
            "improvements": "LayerNorm+SE+ImprovedByteCNN+EMA+SingleEpochOptimized",
            "use_ema": True,
            "ema_decay": 0.999,
            "use_amp": use_amp,
            "total_steps": total_steps,
            "notes": "Optimized for single epoch: gentle LR decay (70%), low WD, no label smoothing, AMP disabled",
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
        if "ema_state_dict" in ckpt:
            ema_model.load_state_dict(ckpt["ema_state_dict"])
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
                
                # Manual warmup for first WARMUP_STEPS
                if warmup_active and global_step < WARMUP_STEPS:
                    # Linear warmup from 10% to 100% of initial LR
                    warmup_factor = 0.1 + 0.9 * (global_step / WARMUP_STEPS)
                    for param_group in optimizer.param_groups:
                        param_group['lr'] = LEARNING_RATE * warmup_factor
                
                # Mixed precision training
                if use_amp:
                    with torch.cuda.amp.autocast():
                        outputs = model(ids, byte_ids)
                        loss = criterion(outputs.view(-1, NUM_TAGS), labels.view(-1))
                    scaler.scale(loss).backward()
                    scaler.unscale_(optimizer)
                    torch.nn.utils.clip_grad_norm_(model.parameters(), GRADIENT_CLIP)
                    scaler.step(optimizer)
                    scaler.update()
                else:
                    outputs = model(ids, byte_ids)
                    loss = criterion(outputs.view(-1, NUM_TAGS), labels.view(-1))
                    loss.backward()
                    torch.nn.utils.clip_grad_norm_(model.parameters(), GRADIENT_CLIP)
                    optimizer.step()
                
                # Update learning rate with cosine schedule (but skip during warmup)
                if not warmup_active or global_step >= WARMUP_STEPS:
                    scheduler.step()
                
                # Update EMA model after each step
                ema_model.update_parameters(model)
                
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
                        "ema_state_dict": ema_model.state_dict(),
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
                    # Calculate steps since last print for accurate step_time
                    steps_since_print = global_step if global_step == 1 else args.print_train_interval
                    step_time = (now - last_print) / steps_since_print
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
                        "ema_state_dict": ema_model.state_dict(),
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

                # Optional limits
                if args.max_steps is not None and global_step >= args.max_steps:
                    stop_training = True
                    break
                if args.max_steps_per_epoch is not None and step_in_epoch >= args.max_steps_per_epoch:
                    break
        
            # Validation - use EMA model for better results
            model.eval()
            ema_model.eval()
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
                    # Use EMA model for validation (better generalization)
                    outputs = ema_model.module(ids, byte_batch)
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

            # Epoch-end metrics calculation and logging
            avg_train_loss = total_train_loss / max(1, len(train_loader))
            avg_val_loss = total_val_loss / max(1, len(val_loader)) if val_loader else 0.0
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

            # Save checkpoint (including EMA state)
            ckpt_path = os.path.join(args.checkpoint_dir, f"checkpoint_epoch_{epoch+1:03d}.pt")
            latest_path = os.path.join(args.checkpoint_dir, "latest.pt")
            ckpt = {
                "epoch": epoch,
                "model_state_dict": model.state_dict(),
                "ema_state_dict": ema_model.state_dict(),
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
                break
    except Exception as e:
        # Save a crash checkpoint with latest state
        os.makedirs(args.checkpoint_dir, exist_ok=True)
        crash_path = os.path.join(args.checkpoint_dir, "crash_latest.pt")
        ckpt = {
            "epoch": epoch if 'epoch' in locals() else 0,
            "global_step": global_step if 'global_step' in locals() else 0,
            "model_state_dict": model.state_dict(),
            "ema_state_dict": ema_model.state_dict() if 'ema_model' in locals() else None,
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
    # 5. Export to ONNX - use EMA weights if available for best generalization
    export_model = model
    if best_ckpt_path and os.path.exists(best_ckpt_path):
        print(f"Loading best checkpoint from {best_ckpt_path} for ONNX export (val loss {best_val_loss:.6f} at epoch {best_epoch})")
        best_ckpt = torch.load(best_ckpt_path, map_location="cpu")
        # Prefer EMA weights if available (better generalization)
        if "ema_state_dict" in best_ckpt and best_ckpt["ema_state_dict"] is not None:
            print("Using EMA weights for export")
            # EMA model wraps the actual model, so we need to extract it
            from torch.optim.swa_utils import AveragedModel
            ema_temp = AveragedModel(model)
            ema_temp.load_state_dict(best_ckpt["ema_state_dict"])
            export_model = ema_temp.module  # Extract the underlying model
        else:
            model.load_state_dict(best_ckpt["model_state_dict"])
            export_model = model
    print("--- Exporting model to ONNX ---")
    export_model.eval()
    export_model.to("cpu")
    dummy_input_ids = torch.randint(0, vocab_size, (1, MAX_LEN), dtype=torch.long)
    dummy_byte_ids = torch.zeros((1, MAX_LEN, CHAR_MAX_LEN), dtype=torch.long)
    input_names = ["input_ids", "byte_ids"]
    output_names = ["logits"]
    torch.onnx.export(export_model,
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