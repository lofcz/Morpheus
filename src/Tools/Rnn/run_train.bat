@echo off
echo.
echo =================================================
echo  TINY NAME CLASSIFIER TRAINING PIPELINE
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

echo [3/3] Training model and exporting to ONNX...

:menu
echo.
echo Select the number of training steps:
echo 1 - 20k (tiny)
echo 2 - 50k (small)
echo 3 - 100k (medium)
echo 4 - 300k (huge)
echo 5 - full (no step limit)
echo.

set "STEPS_ARG="
set /p "choice=Enter your choice (1-5): "

if "%choice%"=="1" set "STEPS_ARG=--max-steps=20000"
if "%choice%"=="2" set "STEPS_ARG=--max-steps=50000"
if "%choice%"=="3" set "STEPS_ARG=--max-steps=100000"
if "%choice%"=="4" set "STEPS_ARG=--max-steps=300000"
if "%choice%"=="5" set "STEPS_ARG="

set "VALID_CHOICE="
if "%choice%"=="1" set VALID_CHOICE=1
if "%choice%"=="2" set VALID_CHOICE=1
if "%choice%"=="3" set VALID_CHOICE=1
if "%choice%"=="4" set VALID_CHOICE=1
if "%choice%"=="5" set VALID_CHOICE=1

if not defined VALID_CHOICE (
    echo Invalid choice. Please try again.
    goto menu
)

echo.
python train.py --use-wandb --epochs=1 %STEPS_ARG%
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