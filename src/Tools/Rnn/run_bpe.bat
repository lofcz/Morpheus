@echo off
setlocal enabledelayedexpansion

REM Change to the directory of this script
pushd "%~dp0"

rem Enable ANSI escape sequences (best effort)
for /f "delims=" %%A in ('echo prompt $E^| cmd') do set "ESC=%%A"
if defined ESC (
    set "RESET=!ESC![0m"
    set "BOLD=!ESC![1m"
    set "YELLOW=!ESC![93m"
    set "CYAN=!ESC![96m"
    set "GREEN=!ESC![92m"
) else (
    set "RESET="
    set "BOLD="
    set "YELLOW="
    set "CYAN="
    set "GREEN="
)

echo.
echo !CYAN!=================================================!RESET!
echo !BOLD! TRAIN BPE TOKENIZER!RESET!
echo !CYAN!=================================================!RESET!
echo.

echo !YELLOW![1/2]!RESET! Checking venv and installing requirements...
if not exist "venv" (
    echo Creating virtual environment...
    python -m venv venv
)
call venv\Scripts\activate
pip install -r requirements.txt

echo.
echo !YELLOW![2/2]!RESET! Running bpe.py ...
python bpe.py

echo.
echo !GREEN!!BOLD!Done training tokenizer!RESET!

deactivate
popd
pause
