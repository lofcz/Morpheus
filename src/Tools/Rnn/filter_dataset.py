import pandas as pd
import numpy as np

print("Loading dataset...")
df = pd.read_csv('data/dataset.csv')
print(f"Original size: {len(df):,}")

# Step 1: Remove duplicates
print("\nRemoving duplicates...")
df = df.drop_duplicates(subset=['text'])
print(f"After deduplication: {len(df):,}")

# Step 2: Sample strategically
# For each tag pattern, take max 50k samples to balance
print("\nBalancing tag distribution...")
sampled = []
for tag_pattern, group in df.groupby('tags'):
    # Take at most 50k samples per pattern
    n_samples = min(50000, len(group))
    sampled.append(group.sample(n=n_samples, random_state=42))

df_balanced = pd.concat(sampled, ignore_index=True)
print(f"After balancing: {len(df_balanced):,}")

# Step 3: Shuffle
df_balanced = df_balanced.sample(frac=1, random_state=42).reset_index(drop=True)

# Step 4: Save
output_path = 'data/dataset_filtered.csv'
df_balanced.to_csv(output_path, index=False)
print(f"\nSaved to: {output_path}")
print(f"Reduction: {len(df):,} → {len(df_balanced):,} ({len(df_balanced)/len(df)*100:.1f}%)")
print(f"\nWith batch_size=32:")
print(f"  Steps per epoch: {len(df_balanced)//32:,}")
print(f"  Time estimate: ~{(len(df_balanced)//32 * 0.6)/3600:.1f} hours")


