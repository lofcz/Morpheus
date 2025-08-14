@echo off
echo.
echo =================================================
echo  VALIDATING TRAINED NAME CLASSIFIER
echo =================================================
echo.

echo [1/3] Checking for Python virtual environment...
if not exist "venv" (
    echo ERROR: Virtual environment 'venv' not found.
    echo Please run 'run.bat' first to set up the environment and train the model.
    pause
    exit /b
)

echo Activating virtual environment...
call venv\Scripts\activate

echo.
echo [2/3] Checking for required files...
if not exist "name_classifier.onnx" (
    echo ERROR: Model 'name_classifier.onnx' not found.
    echo Please run 'run.bat' first to train the model.
    pause
    exit /b
)

echo.
echo [3/3] Running validation script on data/validate.csv...
python validate.py
echo.

echo =================================================
echo  VALIDATION COMPLETE
echo =================================================
echo.

deactivate
pause