import torch
import torch.nn as nn
import torch.optim as optim
from torch.utils.data import Dataset, DataLoader, random_split
import pandas as pd
from tokenizers import Tokenizer
import numpy as np

# --- Configuration ---
DATA_FILE = "data/dataset.csv"
TOKENIZER_PATH = "custom-bpe-tokenizer.json"
ONNX_EXPORT_PATH = "name_classifier.onnx"

# Model Hyperparameters
VOCAB_SIZE = 1000       # Must match the vocab size in bpe.py
EMBEDDING_DIM = 32      # Low dimension for a tiny model
HIDDEN_DIM = 64         # GRU hidden units
OUTPUT_DIM = 3          # 3 classes (Name, Nickname, Company)
MAX_LEN = 128           # Max sequence length (from your requirement)

# Training Hyperparameters
LEARNING_RATE = 0.005   # This is now the *initial* learning rate
WEIGHT_DECAY = 0.01     ### NEW ### A common value for AdamW
BATCH_SIZE = 8
EPOCHS = 50             # Train for more epochs on this small dataset

# --- Model Definition ---
class TinyClassifier(nn.Module):
    def __init__(self, vocab_size, embedding_dim, hidden_dim, output_dim):
        super().__init__()
        self.embedding = nn.Embedding(vocab_size, embedding_dim)
        self.gru = nn.GRU(embedding_dim, hidden_dim, batch_first=True)
        self.fc = nn.Linear(hidden_dim, output_dim)

    def forward(self, input_ids):
        embedded = self.embedding(input_ids)
        _, hidden = self.gru(embedded)
        return self.fc(hidden.squeeze(0))

# --- PyTorch Dataset ---
class NameDataset(Dataset):
    def __init__(self, texts, labels, tokenizer, max_len):
        self.texts = texts
        self.labels = labels
        self.tokenizer = tokenizer
        self.max_len = max_len
        self.pad_token_id = tokenizer.token_to_id("[PAD]")

    def __len__(self):
        return len(self.texts)

    def __getitem__(self, idx):
        text = self.texts[idx]
        label = self.labels[idx]
        encoding = self.tokenizer.encode(text)
        input_ids = encoding.ids
        if len(input_ids) > self.max_len:
            input_ids = input_ids[:self.max_len]
        else:
            input_ids = input_ids + [self.pad_token_id] * (self.max_len - len(input_ids))
        return {
            'ids': torch.tensor(input_ids, dtype=torch.long),
            'label': torch.tensor(label, dtype=torch.long)
        }

def main():
    print("--- Starting Model Training (with AdamW and LR Scheduler) ---")
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    print(f"Using device: {device}")

    # 1. Load data and tokenizer
    df = pd.read_csv(DATA_FILE)
    tokenizer = Tokenizer.from_file(TOKENIZER_PATH)

    # 2. Create dataset and split
    dataset = NameDataset(
        texts=df['text'].tolist(),
        labels=df['label'].tolist(),
        tokenizer=tokenizer,
        max_len=MAX_LEN
    )
    train_size = int(0.8 * len(dataset))
    val_size = len(dataset) - train_size
    train_dataset, val_dataset = random_split(dataset, [train_size, val_size])
    train_loader = DataLoader(train_dataset, batch_size=BATCH_SIZE, shuffle=True)
    val_loader = DataLoader(val_dataset, batch_size=BATCH_SIZE)

    # 3. Initialize model, loss, and optimizer
    model = TinyClassifier(VOCAB_SIZE, EMBEDDING_DIM, HIDDEN_DIM, OUTPUT_DIM).to(device)
    criterion = nn.CrossEntropyLoss()
    
    ### NEW ### Use AdamW optimizer with weight decay
    optimizer = optim.AdamW(model.parameters(), lr=LEARNING_RATE, weight_decay=WEIGHT_DECAY)
    
    ### NEW ### Add a learning rate scheduler
    # This will decay the LR from its initial value down to 0 over the course of all epochs
    scheduler = optim.lr_scheduler.CosineAnnealingLR(optimizer, T_max=EPOCHS)

    # 4. Training loop
    for epoch in range(EPOCHS):
        model.train()
        total_train_loss = 0
        for batch in train_loader:
            ids = batch['ids'].to(device)
            labels = batch['label'].to(device)
            optimizer.zero_grad()
            outputs = model(ids)
            loss = criterion(outputs, labels)
            loss.backward()
            optimizer.step()
            total_train_loss += loss.item()
        
        # Validation
        model.eval()
        total_val_loss = 0
        correct_predictions = 0
        with torch.no_grad():
            for batch in val_loader:
                ids = batch['ids'].to(device)
                labels = batch['label'].to(device)
                outputs = model(ids)
                loss = criterion(outputs, labels)
                total_val_loss += loss.item()
                _, predicted = torch.max(outputs, 1)
                correct_predictions += (predicted == labels).sum().item()

        ### NEW ### Update the learning rate at the end of the epoch
        scheduler.step()

        avg_train_loss = total_train_loss / len(train_loader)
        avg_val_loss = total_val_loss / len(val_loader)
        val_accuracy = correct_predictions / len(val_dataset)
        
        # Get current learning rate to display
        current_lr = optimizer.param_groups[0]['lr']
        
        if (epoch + 1) % 10 == 0:
            print(f"Epoch {epoch+1}/{EPOCHS} | Train Loss: {avg_train_loss:.4f} | Val Loss: {avg_val_loss:.4f} | Val Acc: {val_accuracy:.4f} | LR: {current_lr:.6f}")

    print("--- Training Finished ---")

    # 5. Export to ONNX
    print("--- Exporting model to ONNX ---")
    model.eval()
    model.to("cpu")
    dummy_input = torch.randint(0, VOCAB_SIZE, (1, MAX_LEN), dtype=torch.long)
    input_names = ["input_ids"]
    output_names = ["logits"]
    torch.onnx.export(model,
                      dummy_input,
                      ONNX_EXPORT_PATH,
                      input_names=input_names,
                      output_names=output_names,
                      opset_version=23,
                      dynamic_axes={'input_ids': {0: 'batch_size', 1: 'sequence_length'}})
    
    print(f"Model successfully exported to {ONNX_EXPORT_PATH}")

if __name__ == "__main__":
    main()