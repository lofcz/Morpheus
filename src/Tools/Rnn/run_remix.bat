@echo off
set SCRIPT_DIR=%~dp0
set VENV_DIR=%SCRIPT_DIR%venv

:: Default to 100 million samples if no argument is provided
set NUM_SAMPLES=%1
if "%NUM_SAMPLES%"=="" set NUM_SAMPLES=100000000

echo [remix] Activating venv...
call "%VENV_DIR%/Scripts/activate.bat"

echo [remix] Running remix.py to synthesize %NUM_SAMPLES% NER dataset samples...
python "%SCRIPT_DIR%remix.py" --num-samples %NUM_SAMPLES%

echo [remix] Deactivating venv...
call "%VENV_DIR%/Scripts/deactivate.bat"

echo.
echo NER dataset has been synthesized. You can now run bpe.py.
