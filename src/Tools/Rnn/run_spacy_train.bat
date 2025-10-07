@echo off
echo ========================================
echo spaCy NER Training
echo ========================================
echo.

python -m spacy train spacy_config.cfg ^
    --output ./spacy_model ^
    --paths.train ./spacy_data/train.spacy ^
    --paths.dev ./spacy_data/dev.spacy ^
    --gpu-id 0 ^
    --verbose

echo.
echo ========================================
echo Training complete!
echo Model saved to: spacy_model/model-best
echo ========================================
pause

