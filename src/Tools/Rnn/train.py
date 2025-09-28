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
class NerClassifier(nn.Module):
    def __init__(self, vocab_size, embedding_dim, hidden_dim, output_dim, n_layers, dropout):
        super().__init__()
        self.embedding = nn.Embedding(vocab_size, embedding_dim)
        
        self.gru = nn.GRU(embedding_dim, 
                          hidden_dim, 
                          num_layers=n_layers,
                          batch_first=True, 
                          bidirectional=True,
                          dropout=dropout if n_layers > 1 else 0)
        
        self.dropout = nn.Dropout(dropout)
        
        # This layer maps the concatenated hidden states of the GRU to the number of NER tags
        self.fc = nn.Linear(hidden_dim * 2, output_dim)

    def forward(self, input_ids):
        # input_ids shape: [batch_size, max_len]
        
        # embedded shape: [batch_size, max_len, embedding_dim]
        embedded = self.embedding(input_ids)
        
        # gru_outputs shape: [batch_size, max_len, hidden_dim * 2]
        gru_outputs, _ = self.gru(embedded)
        
        gru_outputs = self.dropout(gru_outputs)
        
        # Pass the GRU output for each token through the final layer
        # logits shape: [batch_size, max_len, output_dim (num_tags)]
        logits = self.fc(gru_outputs)
        
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
        text = self.texts[idx]
        tags = self.tags[idx].split()

        encoding = self.tokenizer.encode(text)
        input_ids = encoding.ids
        
        # Align labels with tokens. This is crucial for NER with subword tokenizers.
        # The strategy is to label only the first sub-token of a word and ignore the rest (-100).
        word_ids = encoding.word_ids
        aligned_labels = np.full(len(word_ids), -100, dtype=np.int64)
        
        previous_word_id = None
        for i, word_id in enumerate(word_ids):
            if word_id is None:
                continue # Special token
            if word_id != previous_word_id:
                # This is the first sub-token of a word.
                if word_id < len(tags):
                    tag = tags[word_id]
                    aligned_labels[i] = self.tag_map.get(tag, self.tag_map["O"])
            previous_word_id = word_id
        
        # Pad sequences to max_len
        if len(input_ids) > self.max_len:
            input_ids = input_ids[:self.max_len]
            aligned_labels = aligned_labels[:self.max_len]
        else:
            pad_length = self.max_len - len(input_ids)
            input_ids = input_ids + [self.pad_token_id] * pad_length
            aligned_labels = np.pad(aligned_labels, (0, pad_length), mode='constant', constant_values=-100)
            
        return {
            'ids': torch.tensor(input_ids, dtype=torch.long),
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
    train_loader = DataLoader(train_dataset, batch_size=BATCH_SIZE, shuffle=True)
    val_loader = DataLoader(val_dataset, batch_size=BATCH_SIZE)

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
            labels = batch['labels'].to(device)
            optimizer.zero_grad()
            outputs = model(ids) # Shape: [batch, seq_len, num_tags]
            
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

            # Console progress printing
            if args.print_train_interval > 0 and (global_step % args.print_train_interval == 0):
                now = time.time()
                elapsed = now - epoch_start
                steps_remaining = steps_per_epoch - step_in_epoch
                step_time = (now - last_print) / max(1, args.print_train_interval)
                eta = steps_remaining * step_time
                print(f"Epoch {epoch+1}/{num_epochs} Step {step_in_epoch}/{steps_per_epoch} | loss={loss.item():.4f} | elapsed={_format_seconds(elapsed)} | eta={_format_seconds(eta)}")
                last_print = now

            # Quick mid-epoch validation
            if args.val_interval_steps and (global_step % args.val_interval_steps == 0):
                model.eval()
                quick_val_loss = 0.0
                quick_val_correct = 0
                quick_val_total = 0
                with torch.no_grad():
                    for i, vbatch in enumerate(val_loader):
                        if i >= max(1, args.val_max_batches):
                            break
                        vids = vbatch['ids'].to(device)
                        vlabels = vbatch['labels'].to(device)
                        voutputs = model(vids)
                        vloss = criterion(voutputs.view(-1, NUM_TAGS), vlabels.view(-1))
                        quick_val_loss += vloss.item()
                        
                        # Calculate token-level accuracy (ignoring padding)
                        vpred = torch.argmax(voutputs, dim=2)
                        active_tokens = vlabels != -100
                        quick_val_correct += (vpred[active_tokens] == vlabels[active_tokens]).sum().item()
                        quick_val_total += active_tokens.sum().item()
                        
                denom = max(1, min(len(val_loader), args.val_max_batches))
                quick_val_loss /= denom
                quick_val_acc = quick_val_correct / max(1, quick_val_total)
                # Console visibility
                print(f"[val_step] step={global_step} loss={quick_val_loss:.4f} acc={quick_val_acc:.4f}")
                if use_wandb:
                    wandb.log({
                        "global_step": global_step,
                        "val/loss": quick_val_loss,
                        "val/token_accuracy": quick_val_acc,
                        "val_step/loss": quick_val_loss,
                        "val_step/token_accuracy": quick_val_acc,
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
            for batch in val_loader:
                ids = batch['ids'].to(device)
                labels = batch['labels'].to(device)
                outputs = model(ids)
                loss = criterion(outputs.view(-1, NUM_TAGS), labels.view(-1))
                total_val_loss += loss.item()
                
                # Calculate token-level accuracy (ignoring padding)
                predicted = torch.argmax(outputs, dim=2)
                active_tokens = labels != -100
                total_correct_predictions += (predicted[active_tokens] == labels[active_tokens]).sum().item()
                total_tokens += active_tokens.sum().item()

        ### NEW ### Update the learning rate at the end of the epoch
        scheduler.step()

        avg_train_loss = total_train_loss / len(train_loader)
        avg_val_loss = total_val_loss / len(val_loader)
        val_accuracy = total_correct_predictions / max(1, total_tokens)
        
        # Get current learning rate to display
        current_lr = optimizer.param_groups[0]['lr']
        
        # Log every epoch
        print(f"Epoch {epoch+1}/{num_epochs} | Train Loss: {avg_train_loss:.4f} | Val Loss: {avg_val_loss:.4f} | Val Acc: {val_accuracy:.4f} | LR: {current_lr:.6f}")
        if use_wandb:
            wandb.log({
                "epoch": epoch + 1,
                "train/epoch_loss": avg_train_loss,
                "val/loss": avg_val_loss,
                "val/token_accuracy": val_accuracy,
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
                break

        if stop_training:
            print("Reached global max steps; stopping training.")
            break

    print("--- Training Finished ---")

    # 5. Export to ONNX
    # Load best checkpoint before exporting ONNX (fall back to latest model if none)
    if best_ckpt_path and os.path.exists(best_ckpt_path):
        print(f"Loading best checkpoint from {best_ckpt_path} for ONNX export (val loss {best_val_loss:.6f} at epoch {best_epoch})")
        best_ckpt = torch.load(best_ckpt_path, map_location="cpu")
        model.load_state_dict(best_ckpt["model_state_dict"])
    print("--- Exporting model to ONNX ---")
    model.eval()
    model.to("cpu")
    dummy_input = torch.randint(0, vocab_size, (1, MAX_LEN), dtype=torch.long)
    input_names = ["input_ids"]
    output_names = ["logits"]
    torch.onnx.export(model,
                      dummy_input,
                      ONNX_EXPORT_PATH,
                      input_names=input_names,
                      output_names=output_names,
                      opset_version=14,
                      dynamic_axes={
                          'input_ids': {0: 'batch_size', 1: 'sequence_length'},
                          'logits': {0: 'batch_size', 1: 'sequence_length'}
                      })
    
    print(f"Model successfully exported to {ONNX_EXPORT_PATH}")

if __name__ == "__main__":
    main()