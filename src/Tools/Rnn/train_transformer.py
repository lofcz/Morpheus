import os
import time
import torch
import torch.nn as nn
import torch.optim as optim
from torch.utils.data import Dataset, DataLoader, random_split
import pandas as pd
from tokenizers import Tokenizer
import numpy as np
from sklearn.metrics import f1_score
import math

try:
    from TorchCRF import CRF
except ImportError:
    print("WARNING: TorchCRF not found. Install with: pip install TorchCRF")
    print("Falling back to no CRF support.")
    CRF = None

# --- Configuration ---
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
DATA_FILE = os.path.join(SCRIPT_DIR, "data", "dataset_clean_1m.csv")  # UPDATED: Clean dataset with 90% less noise
TOKENIZER_PATH = os.path.join(SCRIPT_DIR, "custom-bpe-tokenizer.json")
ONNX_EXPORT_PATH = os.path.join(SCRIPT_DIR, "name_classifier_transformer.onnx")

# --- Labeling Scheme for NER ---
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
VOCAB_SIZE = None  # resolved at runtime
EMBEDDING_DIM = 128
HIDDEN_DIM = 256       # Transformer hidden dimension
NUM_HEADS = 8          # Multi-head attention
NUM_LAYERS = 4         # Number of transformer layers
DROPOUT = 0.1          # Lower dropout for transformers
FFN_DIM = 1024         # Feed-forward network dimension (4x hidden)
MAX_LEN = 128

# Byte-CNN hyperparameters (reuse from CNN model)
CHAR_MAX_LEN = 24
CHAR_EMBED_DIM = 16
CHAR_CHANNELS = 64
CHAR_DROPOUT = 0.1

# Training Hyperparameters
LEARNING_RATE = 0.0003
WEIGHT_DECAY = 0.00005
BATCH_SIZE = 32
EPOCHS = 1
WARMUP_STEPS = 2000
GRADIENT_CLIP = 1.0
LABEL_SMOOTHING = 0.0


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
    df = pd.read_csv(path)
    texts = df['text'].tolist()
    tags = df['tags'].astype(str).tolist()
    return texts, tags


# --- Byte CNN (reuse from CNN model) ---
class ByteCNN(nn.Module):
    def __init__(self, num_bytes: int = 256):
        super().__init__()
        self.byte_embed = nn.Embedding(num_bytes, CHAR_EMBED_DIM)
        self.conv1 = nn.Conv1d(CHAR_EMBED_DIM, CHAR_CHANNELS, kernel_size=3, padding=1)
        self.ln1 = nn.LayerNorm(CHAR_CHANNELS)
        self.act = nn.GELU()
        self.conv2 = nn.Conv1d(CHAR_CHANNELS, CHAR_CHANNELS, kernel_size=3, padding=1)
        self.ln2 = nn.LayerNorm(CHAR_CHANNELS)
        self.dropout = nn.Dropout(CHAR_DROPOUT)

    def forward(self, byte_ids: torch.Tensor) -> torch.Tensor:
        B, L, C = byte_ids.shape
        x = byte_ids.view(B * L, C)
        x = self.byte_embed(x)
        x = x.transpose(1, 2)
        
        x = self.conv1(x)
        x = x.transpose(1, 2)
        x = self.ln1(x)
        x = x.transpose(1, 2)
        x = self.act(x)
        
        residual = x
        x = self.conv2(x)
        x = x.transpose(1, 2)
        x = self.ln2(x)
        x = x.transpose(1, 2)
        x = self.act(x)
        x = x + residual
        
        x = torch.amax(x, dim=2)
        x = self.dropout(x)
        x = x.view(B, L, CHAR_CHANNELS)
        return x


# --- Positional Encoding ---
class PositionalEncoding(nn.Module):
    def __init__(self, d_model: int, max_len: int = 5000):
        super().__init__()
        pe = torch.zeros(max_len, d_model)
        position = torch.arange(0, max_len, dtype=torch.float).unsqueeze(1)
        div_term = torch.exp(torch.arange(0, d_model, 2).float() * (-math.log(10000.0) / d_model))
        pe[:, 0::2] = torch.sin(position * div_term)
        pe[:, 1::2] = torch.cos(position * div_term)
        pe = pe.unsqueeze(0)
        self.register_buffer('pe', pe)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        # x: [batch, seq_len, d_model]
        return x + self.pe[:, :x.size(1), :]


# --- Transformer Encoder Layer ---
class TransformerEncoderLayer(nn.Module):
    def __init__(self, d_model: int, nhead: int, dim_feedforward: int, dropout: float):
        super().__init__()
        self.self_attn = nn.MultiheadAttention(d_model, nhead, dropout=dropout, batch_first=True)
        self.linear1 = nn.Linear(d_model, dim_feedforward)
        self.dropout = nn.Dropout(dropout)
        self.linear2 = nn.Linear(dim_feedforward, d_model)
        
        self.norm1 = nn.LayerNorm(d_model)
        self.norm2 = nn.LayerNorm(d_model)
        self.dropout1 = nn.Dropout(dropout)
        self.dropout2 = nn.Dropout(dropout)
        self.activation = nn.GELU()

    def forward(self, src: torch.Tensor, src_key_padding_mask=None) -> torch.Tensor:
        # Self-attention
        src2, _ = self.self_attn(src, src, src, key_padding_mask=src_key_padding_mask)
        src = src + self.dropout1(src2)
        src = self.norm1(src)
        
        # Feed-forward
        src2 = self.linear2(self.dropout(self.activation(self.linear1(src))))
        src = src + self.dropout2(src2)
        src = self.norm2(src)
        
        return src


# --- Transformer NER Classifier ---
class TransformerNerClassifier(nn.Module):
    def __init__(self, vocab_size, embedding_dim, hidden_dim, output_dim, num_layers, num_heads, ffn_dim, dropout, use_crf=False):
        super().__init__()
        self.use_crf = use_crf
        
        # Token and byte embeddings
        self.token_embedding = nn.Embedding(vocab_size, embedding_dim)
        self.byte_cnn = ByteCNN()
        
        # Input projection
        self.input_projection = nn.Linear(embedding_dim + CHAR_CHANNELS, hidden_dim)
        
        # Positional encoding
        self.pos_encoder = PositionalEncoding(hidden_dim, max_len=MAX_LEN)
        
        # Transformer encoder layers
        self.transformer_layers = nn.ModuleList([
            TransformerEncoderLayer(hidden_dim, num_heads, ffn_dim, dropout)
            for _ in range(num_layers)
        ])
        
        # Output projection
        self.output_projection = nn.Linear(hidden_dim, output_dim)
        self.dropout = nn.Dropout(dropout)
        
        # Optional CRF layer
        if use_crf:
            if CRF is None:
                raise ImportError("TorchCRF not installed. Install with: pip install TorchCRF")
            self.crf = CRF(output_dim)

    def forward(self, input_ids: torch.Tensor, byte_ids: torch.Tensor, labels=None) -> torch.Tensor:
        # input_ids: [batch, seq_len]
        # byte_ids: [batch, seq_len, CHAR_MAX_LEN]
        # labels: [batch, seq_len] - only needed for CRF training
        
        # Token embeddings
        token_embed = self.token_embedding(input_ids)  # [B, L, E]
        
        # Byte features
        byte_embed = self.byte_cnn(byte_ids)  # [B, L, CHAR_CHANNELS]
        
        # Combine
        x = torch.cat([token_embed, byte_embed], dim=2)  # [B, L, E+CHAR_CHANNELS]
        x = self.input_projection(x)  # [B, L, H]
        x = self.dropout(x)
        
        # Add positional encoding
        x = self.pos_encoder(x)
        
        # Create padding mask (1 for padding, 0 for real tokens)
        # Assuming pad_token_id = 0
        padding_mask = (input_ids == 0)
        
        # Transformer layers
        for layer in self.transformer_layers:
            x = layer(x, src_key_padding_mask=padding_mask)
        
        # Output projection (emissions for CRF, logits otherwise)
        emissions = self.output_projection(x)  # [B, L, output_dim]
        
        if self.use_crf:
            # TorchCRF expects BoolTensor mask: 1 for real tokens, 0 for padding
            mask = (~padding_mask).bool()
            
            if self.training and labels is not None:
                # During training, return CRF NEGATIVE log-likelihood as loss
                # Replace -100 in labels with 0 for CRF (masked tokens won't affect loss)
                labels_crf = labels.clone()
                labels_crf[labels == -100] = 0
                # TorchCRF.forward() returns log-likelihood per sample
                log_likelihood = self.crf(emissions, labels_crf, mask)
                # Return negative mean log-likelihood as loss
                return -log_likelihood.mean()
            else:
                # During inference, return Viterbi-decoded tags as tensor
                best_paths = self.crf.viterbi_decode(emissions, mask)  # Returns list of lists
                # Convert to tensor [batch, seq_len]
                batch_size, seq_len = input_ids.shape
                result = torch.zeros((batch_size, seq_len), dtype=torch.long, device=input_ids.device)
                for i, path in enumerate(best_paths):
                    result[i, :len(path)] = torch.tensor(path, dtype=torch.long, device=input_ids.device)
                return result
        else:
            # Standard classifier: return logits
            return emissions


# --- Dataset (reuse from train.py) ---
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
        raw_text = self.texts[idx]
        text = str(raw_text) if raw_text is not None else ""
        tags = str(self.tags[idx]).split()

        encoding = self.tokenizer.encode(text)
        input_ids = encoding.ids
        tokens = encoding.tokens
        offsets = encoding.offsets
        
        word_ids = encoding.word_ids
        aligned_labels = np.full(len(word_ids), -100, dtype=np.int64)
        
        previous_word_id = None
        for i, word_id in enumerate(word_ids):
            if word_id is None:
                continue
            if word_id != previous_word_id:
                if word_id < len(tags):
                    tag = tags[word_id]
                    aligned_labels[i] = self.tag_map.get(tag, self.tag_map["O"])
            else:
                if word_id < len(tags):
                    tag = tags[word_id]
                    if tag.startswith("B-"):
                        base_tag = tag.split("-", 1)[1]
                        i_tag = f"I-{base_tag}"
                        aligned_labels[i] = self.tag_map.get(i_tag, self.tag_map["O"])
                    elif tag.startswith("I-"):
                        aligned_labels[i] = self.tag_map.get(tag, self.tag_map["O"])
                    else:
                        aligned_labels[i] = self.tag_map["O"]
            previous_word_id = word_id

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
    parser = argparse.ArgumentParser(description="Train a Transformer NER classifier")
    parser.add_argument("--use-wandb", action="store_true", help="Enable Weights & Biases logging")
    parser.add_argument("--wandb-project", default="morpheus-ner-transformer", help="wandb project name")
    parser.add_argument("--wandb-entity", default=None, help="wandb entity (team)")
    parser.add_argument("--run-name", default=None, help="Optional run name")
    parser.add_argument("--checkpoint-dir", default=os.path.join(SCRIPT_DIR, "checkpoints_transformer"), help="Directory to save checkpoints")
    parser.add_argument("--resume", default=None, help="Path to checkpoint to resume from")
    parser.add_argument("--log-train-interval", type=int, default=100, help="Log training loss to wandb every N steps")
    parser.add_argument("--print-train-interval", type=int, default=200, help="Print training loss/ETA every N steps")
    parser.add_argument("--epochs", type=int, default=1, help="Number of epochs to train")
    parser.add_argument("--max-steps", type=int, default=None, help="Stop training after this many steps")
    parser.add_argument("--val-interval-steps", type=int, default=5000, help="Run validation every N steps")
    parser.add_argument("--val-max-batches", type=int, default=50, help="Max validation batches")
    parser.add_argument("--save-interval-steps", type=int, default=20000, help="Save checkpoint every N steps")
    parser.add_argument("--num-workers", type=int, default=0, help="DataLoader worker processes")
    parser.add_argument("--use-crf", action="store_true", help="Use CRF layer to enforce IOB constraints")
    parser.add_argument("--dataset", default=None, help="Override dataset path (default: dataset_small.csv)")
    args = parser.parse_args()

    use_wandb = args.use_wandb
    if use_wandb:
        try:
            import wandb
        except Exception as e:
            print("wandb not available; proceed without it. Error:", e)
            use_wandb = False
    
    print("--- Starting Transformer NER Model Training ---")
    if args.use_crf:
        print("*** CRF ENABLED: Model will enforce IOB sequence constraints ***")
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    print(f"Using device: {device}")

    # Load data
    data_file = args.dataset if args.dataset else DATA_FILE
    print(f"Loading NER data from {data_file}...")
    texts, tags = load_ner_data_from_csv(data_file)
    tokenizer = Tokenizer.from_file(TOKENIZER_PATH)
    vocab_size = tokenizer.get_vocab_size()

    # Create dataset
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

    # Initialize model
    model = TransformerNerClassifier(
        vocab_size=vocab_size,
        embedding_dim=EMBEDDING_DIM,
        hidden_dim=HIDDEN_DIM,
        output_dim=NUM_TAGS,
        num_layers=NUM_LAYERS,
        num_heads=NUM_HEADS,
        ffn_dim=FFN_DIM,
        dropout=DROPOUT,
        use_crf=args.use_crf,
    ).to(device)
    
    # Count parameters
    total_params = sum(p.numel() for p in model.parameters())
    print(f"Model parameters: {total_params:,}")
    
    # Criterion (only used when CRF is disabled)
    criterion = nn.CrossEntropyLoss(ignore_index=-100, label_smoothing=LABEL_SMOOTHING) if not args.use_crf else None
    optimizer = optim.AdamW(model.parameters(), lr=LEARNING_RATE, weight_decay=WEIGHT_DECAY)
    
    # Learning rate scheduler
    num_epochs = args.epochs
    steps_per_epoch = len(train_loader)
    total_steps = num_epochs * steps_per_epoch
    
    scheduler_steps = total_steps - WARMUP_STEPS if WARMUP_STEPS > 0 else total_steps
    scheduler = optim.lr_scheduler.CosineAnnealingLR(
        optimizer, 
        T_max=scheduler_steps,
        eta_min=LEARNING_RATE * 0.7
    )
    print(f"Training schedule: {WARMUP_STEPS} warmup + {scheduler_steps} cosine steps (total {total_steps})")
    
    warmup_active = WARMUP_STEPS > 0

    # EMA
    from torch.optim.swa_utils import AveragedModel
    ema_model = AveragedModel(model, avg_fn=lambda avg, curr, steps: 0.999 * avg + 0.001 * curr)
    print("Using EMA with decay=0.999")

    # Wandb
    if use_wandb:
        config = {
            "architecture": "Transformer",
            "vocab_size": vocab_size,
            "embedding_dim": EMBEDDING_DIM,
            "hidden_dim": HIDDEN_DIM,
            "num_heads": NUM_HEADS,
            "num_layers": NUM_LAYERS,
            "ffn_dim": FFN_DIM,
            "dropout": DROPOUT,
            "batch_size": BATCH_SIZE,
            "epochs": num_epochs,
            "learning_rate": LEARNING_RATE,
            "weight_decay": WEIGHT_DECAY,
            "warmup_steps": WARMUP_STEPS,
            "gradient_clip": GRADIENT_CLIP,
            "total_params": total_params,
        }
        wandb.init(project=args.wandb_project, entity=args.wandb_entity, name=args.run_name, config=config)
        wandb.define_metric("global_step")
        wandb.define_metric("train/*", step_metric="global_step")
        wandb.define_metric("val/*", step_metric="global_step")

    # Resume from checkpoint
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

    # Training loop
    best_val_loss = float("inf")
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
                
                # Manual warmup
                if warmup_active and global_step < WARMUP_STEPS:
                    warmup_factor = 0.1 + 0.9 * (global_step / WARMUP_STEPS)
                    for param_group in optimizer.param_groups:
                        param_group['lr'] = LEARNING_RATE * warmup_factor
                
                # Forward pass
                if args.use_crf:
                    # CRF returns loss directly during training
                    loss = model(ids, byte_ids, labels=labels)
                else:
                    # Standard cross-entropy loss
                    outputs = model(ids, byte_ids)
                    loss = criterion(outputs.view(-1, NUM_TAGS), labels.view(-1))
                
                # Backward pass
                loss.backward()
                torch.nn.utils.clip_grad_norm_(model.parameters(), GRADIENT_CLIP)
                optimizer.step()
                
                # Scheduler step
                if not warmup_active or global_step >= WARMUP_STEPS:
                    scheduler.step()
                
                # EMA update
                ema_model.update_parameters(model)
                
                total_train_loss += loss.item()
                
                # Logging
                if use_wandb and (global_step % max(1, args.log_train_interval) == 0):
                    wandb.log({
                        "global_step": global_step,
                        "train/loss": loss.item(),
                    })
                
                global_step += 1

                # Check max_steps
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
                            "OUTPUT_DIM": NUM_TAGS,
                            "MAX_LEN": MAX_LEN,
                            "NUM_LAYERS": NUM_LAYERS,
                            "NUM_HEADS": NUM_HEADS,
                            "FFN_DIM": FFN_DIM,
                            "DROPOUT": DROPOUT,
                            "TAG_MAP": TAG_MAP,
                        },
                    }
                    torch.save(ckpt_step, step_latest_path)
                    print(f"[checkpoint] Reached max_steps={args.max_steps}, saved {step_latest_path}", flush=True)
                    stop_training = True
                    break

                # Console progress
                if args.print_train_interval > 0 and ((global_step % args.print_train_interval == 0) or global_step == 1):
                    now = time.time()
                    elapsed = now - epoch_start
                    steps_since_print = global_step if global_step == 1 else args.print_train_interval
                    step_time = (now - last_print) / steps_since_print
                    
                    # Epoch ETA
                    remaining_in_epoch = max(0, steps_per_epoch - step_in_epoch)
                    eta_epoch = remaining_in_epoch * step_time
                    
                    # Total ETA
                    if num_epochs > 1:
                        remaining_epochs = max(0, num_epochs - (epoch + 1))
                        remaining_total = remaining_in_epoch + remaining_epochs * steps_per_epoch
                        eta_total = remaining_total * step_time
                    
                    global_cap = str(args.max_steps) if args.max_steps is not None else "-"
                    
                    eta_str = f"eta(epoch)={_format_seconds(eta_epoch)}"
                    if num_epochs > 1:
                        eta_str += f" | eta(total)={_format_seconds(eta_total)}"
                        
                    print(
                        f"Step {global_step}/{global_cap} (epoch {epoch+1}/{num_epochs} {step_in_epoch}/{steps_per_epoch}) | "
                        f"loss={loss.item():.4f} | elapsed={_format_seconds(elapsed)} | {eta_str}",
                        flush=True,
                    )
                    last_print = now

                # Mid-epoch validation
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
                            
                            if args.use_crf:
                                # CRF: get decoded tags directly
                                vpred = model(vids, vbyte)  # Returns decoded tags
                                # Calculate CRF loss for logging (temporarily enable training mode)
                                model.train()
                                vloss = model(vids, vbyte, labels=vlabels)
                                model.eval()
                                quick_val_loss += vloss.item()
                            else:
                                # Standard: get logits and compute loss
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
                            "val/token_f1": quick_val_f1,
                        })
                    model.train()

                # Periodic checkpoint
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
                            "OUTPUT_DIM": NUM_TAGS,
                            "MAX_LEN": MAX_LEN,
                            "NUM_LAYERS": NUM_LAYERS,
                            "NUM_HEADS": NUM_HEADS,
                            "FFN_DIM": FFN_DIM,
                            "DROPOUT": DROPOUT,
                            "TAG_MAP": TAG_MAP,
                        },
                    }
                    torch.save(ckpt_step, step_latest_path)
                    print(f"[checkpoint] Saved {step_latest_path} at step {global_step}", flush=True)
            
            if stop_training:
                break
            
            # Epoch end - skip full validation
            print("Skipping full epoch validation (using mid-epoch validation metrics instead)")
            avg_train_loss = total_train_loss / max(1, len(train_loader))
            current_lr = optimizer.param_groups[0]['lr']
            print(f"Epoch {epoch+1}/{num_epochs} | Train Loss: {avg_train_loss:.4f} | LR: {current_lr:.6f}")
            
            # Save epoch checkpoint
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
                    "OUTPUT_DIM": NUM_TAGS,
                    "MAX_LEN": MAX_LEN,
                    "NUM_LAYERS": NUM_LAYERS,
                    "NUM_HEADS": NUM_HEADS,
                    "FFN_DIM": FFN_DIM,
                    "DROPOUT": DROPOUT,
                    "TAG_MAP": TAG_MAP,
                },
            }
            torch.save(ckpt, ckpt_path)
            torch.save(ckpt, latest_path)
            best_ckpt_path = latest_path

    except Exception as e:
        print(f"Exception occurred: {e}")
        # Save crash checkpoint
        os.makedirs(args.checkpoint_dir, exist_ok=True)
        crash_path = os.path.join(args.checkpoint_dir, "crash_latest.pt")
        torch.save({
            "epoch": epoch if 'epoch' in locals() else 0,
            "global_step": global_step if 'global_step' in locals() else 0,
            "model_state_dict": model.state_dict(),
            "error": str(e),
        }, crash_path)
        print(f"Saved crash checkpoint to {crash_path}")

    print("--- Training Finished ---")
    
    # Export to ONNX
    print("--- Exporting to ONNX ---")
    export_model = model
    if best_ckpt_path and os.path.exists(best_ckpt_path):
        ckpt = torch.load(best_ckpt_path, map_location="cpu")
        if "ema_state_dict" in ckpt and ckpt["ema_state_dict"] is not None:
            print("Using EMA weights for export")
            from torch.optim.swa_utils import AveragedModel
            ema_temp = AveragedModel(model)
            ema_temp.load_state_dict(ckpt["ema_state_dict"])
            export_model = ema_temp.module
        else:
            model.load_state_dict(ckpt["model_state_dict"])
            export_model = model
    
    export_model.eval()
    export_model.to("cpu")
    
    dummy_input_ids = torch.randint(0, vocab_size, (1, MAX_LEN), dtype=torch.long)
    dummy_byte_ids = torch.zeros((1, MAX_LEN, CHAR_MAX_LEN), dtype=torch.long)
    
    # Update export path for CRF models
    onnx_path = ONNX_EXPORT_PATH
    if args.use_crf:
        onnx_path = onnx_path.replace(".onnx", "_crf.onnx")
        print("NOTE: CRF model exports emissions only (no Viterbi decoding in ONNX)")
    
    # For CRF models, we need to export in a special mode that returns emissions
    if args.use_crf:
        # Temporarily disable CRF for export (export emissions only)
        class EmissionsWrapper(nn.Module):
            def __init__(self, base_model):
                super().__init__()
                self.base_model = base_model
            
            def forward(self, input_ids, byte_ids):
                # Get embeddings and transformer output
                token_embed = self.base_model.token_embedding(input_ids)
                byte_embed = self.base_model.byte_cnn(byte_ids)
                x = torch.cat([token_embed, byte_embed], dim=2)
                x = self.base_model.input_projection(x)
                x = self.base_model.dropout(x)
                x = self.base_model.pos_encoder(x)
                padding_mask = (input_ids == 0)
                for layer in self.base_model.transformer_layers:
                    x = layer(x, src_key_padding_mask=padding_mask)
                emissions = self.base_model.output_projection(x)
                return emissions
        
        export_model = EmissionsWrapper(export_model)
    
    torch.onnx.export(
        export_model,
        (dummy_input_ids, dummy_byte_ids),
        onnx_path,
        input_names=["input_ids", "byte_ids"],
        output_names=["logits"],
        opset_version=14,
        dynamic_axes={
            'input_ids': {0: 'batch_size', 1: 'sequence_length'},
            'byte_ids': {0: 'batch_size', 1: 'sequence_length', 2: 'char_length'},
            'logits': {0: 'batch_size', 1: 'sequence_length'}
        }
    )
    print(f"✓ Model successfully exported to {onnx_path}")


if __name__ == "__main__":
    main()

