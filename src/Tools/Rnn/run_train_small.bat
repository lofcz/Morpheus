@echo off
echo.
echo =================================================
echo  TRANSFORMER+CRF NER TRAINING ON CLEAN DATASET
echo =================================================
echo.

echo [1/3] Checking for Python virtual environment...
if not exist "venv" (
    echo Creating virtual environment...
    python -m venv venv
)

echo Activating virtual environment...
call venv\Scripts\activate

echo.
echo [2/3] Installing dependencies from requirements.txt...
pip install -r requirements.txt
echo.

echo [3/3] Training Transformer+CRF model on clean dataset...
echo.
echo Dataset: dataset_clean_1m.csv (1M samples, 90%% less noise)
echo Model: Transformer + CRF (enforces IOB constraints)
echo Training: 1 epoch (~1.5 hours on RTX 2080)
echo.
echo Starting training...
echo.
python train_transformer.py --epochs=1 --use-crf --use-wandb
echo.

echo =================================================
echo  DONE!
echo =================================================
echo.
echo Your trained Transformer+CRF model is ready:
echo   - Tokenizer: custom-bpe-tokenizer.json
echo   - Model: name_classifier_transformer_crf.onnx
echo   - IOB constraints: ENFORCED (no invalid tag sequences)
echo.
echo Next steps:
echo   1. Run entity-level evaluation to measure performance
echo   2. Test with inference scripts if results are good
echo.

deactivate
pause

