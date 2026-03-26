@echo off
cd /d "%~dp0"

where bundle >nul 2>&1
if %ERRORLEVEL% == 0 goto run

if exist "C:\Ruby34-x64\bin\bundle.bat" goto found34
if exist "C:\Ruby33-x64\bin\bundle.bat" goto found33
if exist "C:\Ruby32-x64\bin\bundle.bat" goto found32
if exist "C:\Ruby31-x64\bin\bundle.bat" goto found31
if exist "C:\Ruby30-x64\bin\bundle.bat" goto found30

echo Ruby/Bundler not found.
echo Install Ruby from https://rubyinstaller.org/
pause
exit /b 1

:found34
set "PATH=C:\Ruby34-x64\bin;%PATH%"
goto run
:found33
set "PATH=C:\Ruby33-x64\bin;%PATH%"
goto run
:found32
set "PATH=C:\Ruby32-x64\bin;%PATH%"
goto run
:found31
set "PATH=C:\Ruby31-x64\bin;%PATH%"
goto run
:found30
set "PATH=C:\Ruby30-x64\bin;%PATH%"
goto run

:run
bundle exec jekyll serve --baseurl ""
