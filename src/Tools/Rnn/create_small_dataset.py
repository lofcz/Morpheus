import pandas as pd

print("Loading dataset...")
df = pd.read_csv('data/dataset.csv')
print(f"Original size: {len(df):,}")

# Aggressive filtering for practical training
print("\nStep 1: Remove duplicates...")
df = df.drop_duplicates(subset=['text'])
print(f"After dedup: {len(df):,}")

# Step 2: Stratified sampling - take max 5000 per tag pattern
print("\nStep 2: Stratified sampling (5k per pattern)...")
sampled = []
for tag_pattern, group in df.groupby('tags'):
    n = min(5000, len(group))
    sampled.append(group.sample(n=n, random_state=42))

df_small = pd.concat(sampled, ignore_index=True).sample(frac=1, random_state=42)
print(f"After sampling: {len(df_small):,}")

# Step 3: If still too large, cap at 500k
MAX_SAMPLES = 500_000
if len(df_small) > MAX_SAMPLES:
    df_small = df_small.sample(n=MAX_SAMPLES, random_state=42)
    print(f"Capped at: {len(df_small):,}")

# Save
output_path = 'data/dataset_small.csv'
df_small.to_csv(output_path, index=False)

print(f"\n✓ Saved to: {output_path}")
print(f"\nTraining estimates:")
steps_per_epoch = len(df_small) * 0.8 // 32
print(f"  Steps per epoch: {steps_per_epoch:,}")
print(f"  Epochs needed: 5-10 for good coverage")
print(f"  Total steps: {steps_per_epoch * 5:,} - {steps_per_epoch * 10:,}")
print(f"\n  CNN time (5 epochs): ~{steps_per_epoch * 5 * 0.06 / 3600:.1f} hours")
print(f"  Transformer time (5 epochs): ~{steps_per_epoch * 5 * 0.15 / 3600:.1f} hours")


