@echo off
setlocal enabledelayedexpansion

REM Change to the directory of this script
pushd "%~dp0"

rem Enable ANSI escape sequences (best effort)
for /f "delims=" %%A in ('echo prompt $E^| cmd') do set "ESC=%%A"
if defined ESC (
    set "RESET=!ESC![0m"
    set "BOLD=!ESC![1m"
    set "DIM=!ESC![2m"
    set "RED=!ESC![91m"
    set "GREEN=!ESC![92m"
    set "YELLOW=!ESC![93m"
    set "BLUE=!ESC![94m"
    set "MAGENTA=!ESC![95m"
    set "CYAN=!ESC![96m"
) else (
    set "RESET="
    set "BOLD="
    set "DIM="
    set "RED="
    set "GREEN="
    set "YELLOW="
    set "BLUE="
    set "MAGENTA="
    set "CYAN="
)

echo.
echo !CYAN!=================================================!RESET!
echo !BOLD! FORGE CLASS FILES (names / companies / nicknames)!RESET!
echo !CYAN!=================================================!RESET!
echo.

echo !YELLOW![1/3]!RESET! Checking for Python virtual environment...
if not exist "venv" (
    echo !BLUE!Creating virtual environment...!RESET!
    python -m venv venv
)

echo !YELLOW!Activating virtual environment...!RESET!
call venv\Scripts\activate

echo.
echo !YELLOW![2/3]!RESET! Installing dependencies from requirements.txt (safe to skip if already installed)...
pip install -r requirements.txt

echo.
echo !YELLOW![3/3]!RESET! Running !BOLD!forge.py!RESET! to build class files from !BOLD!mixture.txt!RESET! ...
python forge.py

echo.
echo !GREEN!=================================================!RESET!
echo !GREEN!!BOLD! DONE FORGING!RESET!
echo !GREEN!=================================================!RESET!
echo.
echo Outputs:
	echo   !DIM!- data\classes\names.txt!RESET!
	echo   !DIM!- data\classes\companies.txt!RESET!
	echo   !DIM!- data\classes\nicknames.txt!RESET!

echo.

deactivate
popd
pause

