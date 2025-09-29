@echo off
echo.
echo =================================================
echo  TINY NAME CLASSIFIER TRAINING PIPELINE
echo =================================================
echo.

echo [1/4] Checking for Python virtual environment...
if not exist "venv" (
    echo Creating virtual environment...
    python -m venv venv
)

echo Activating virtual environment...
call venv\Scripts\activate

echo.
echo [2/4] Installing dependencies from requirements.txt...
pip install -r requirements.txt
echo.

echo [3/4] Training BPE Tokenizer...
python bpe.py
echo.

echo [4/4] Training model and exporting to ONNX...
python train.py
echo.

echo =================================================
echo  DONE!
echo =================================================
echo.
echo Your trained model is ready:
echo   - Tokenizer: custom-bpe-tokenizer.json
echo   - Model: name_classifier.onnx
echo.
echo You can now use these two files in your C# application.
echo.

deactivate
pause