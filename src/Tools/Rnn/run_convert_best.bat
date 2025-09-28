@echo off
setlocal enabledelayedexpansion

REM Change to the directory of this script
pushd "%~dp0"

echo.
echo =================================================
echo  CONVERT BEST CHECKPOINT TO ONNX
echo =================================================
echo.

echo Activating virtual environment...
if not exist "venv\Scripts\activate.bat" (
    echo Error: Virtual environment not found. Please run training first to create it.
    pause
    exit /b
)
call venv\Scripts\activate

echo.
echo Running conversion script...
python convert.py

echo.
echo =================================================
echo  CONVERSION COMPLETE
echo =================================================
echo.
echo Model saved to: name_classifier.onnx
echo.

deactivate
popd
pause



