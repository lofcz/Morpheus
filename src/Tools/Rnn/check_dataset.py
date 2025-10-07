import pandas as pd

df = pd.read_csv('data/dataset.csv')
print(f'Dataset size: {len(df):,}')
print(f'Unique texts: {df["text"].nunique():,}')
print(f'Duplication rate: {(1 - df["text"].nunique()/len(df))*100:.1f}%')
print(f'\nTag distribution:')
print(df['tags'].value_counts().head(20))


