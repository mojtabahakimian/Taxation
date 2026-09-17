@echo off
setlocal EnableExtensions EnableDelayedExpansion

title Moadian Solutions - Auto Publisher
chcp 65001 >nul

echo ===============================================================================
echo                Moadian Solutions - Auto Publisher
echo ===============================================================================
echo.

:: 1. Working directories
set "ROOT_DIR=%~dp0"
cd /d "%ROOT_DIR%"

set "SETUP_ROOT=E:\prg\MoadianSetup"
set "TEMP_DIR=%ROOT_DIR%_temp_publish"

:: 2. Find MSBuild.exe (Required for Prg_Grpsend due to COMReference)
set "MSBUILD="
if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" (
    for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do (
        if not defined MSBUILD set "MSBUILD=%%i"
    )
)

if not defined MSBUILD (
    if exist "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" (
        set "MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    ) else if exist "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" (
        set "MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
    ) else if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" (
        set "MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
    )
)

if not defined MSBUILD (
    echo [ERROR] MSBuild.exe not found. Visual Studio 2022 is required.
    goto :FAILED
)

echo [INFO] MSBuild: "%MSBUILD%"

:: 3. Detect Version from Prg_Graphicy\Prg_Graphicy.csproj
set "VERSION=%~1"
if "%VERSION%"=="" (
    for /f "usebackq tokens=*" %%v in (`powershell -NoProfile -Command "([xml](Get-Content '%ROOT_DIR%Prg_Graphicy\Prg_Graphicy.csproj')).SelectSingleNode('//FileVersion').InnerText.Trim()"`) do (
        set "VERSION=%%v"
    )
)

if "%VERSION%"=="" (
    echo [WARN] Could not detect FileVersion from Prg_Graphicy.csproj!
    set /p "VERSION=Please enter version (e.g. 8.9.7): "
)

echo [INFO] Detected Version: %VERSION%

:: 4. Target directories
set "TARGET_DIR=%SETUP_ROOT%\Publish_%VERSION%"
if not exist "%SETUP_ROOT%" mkdir "%SETUP_ROOT%"
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"
if not exist "%TEMP_DIR%" mkdir "%TEMP_DIR%"

echo [INFO] Output Folder : "%TARGET_DIR%"
echo.

:: ===============================================================================
:: STEP 1: Prg_Graphicy -> MOADIAN.exe
:: ===============================================================================
echo -------------------------------------------------------------------------------
echo [1/4] Publishing Prg_Graphicy (win-x64 SingleFile)...
echo -------------------------------------------------------------------------------
set "PUB_GRAPHICY=%TEMP_DIR%\Graphicy"
if exist "%PUB_GRAPHICY%" rd /s /q "%PUB_GRAPHICY%"

"%MSBUILD%" "%ROOT_DIR%Prg_Graphicy\Prg_Graphicy.csproj" -restore -t:Publish -p:PublishProfile=FolderProfile -p:PublishDir="%PUB_GRAPHICY%\\" -v:m -nologo
if errorlevel 1 (
    echo [ERROR] Failed to compile Prg_Graphicy!
    goto :FAILED
)

if not exist "%PUB_GRAPHICY%\Prg_Graphicy.exe" (
    echo [ERROR] Output file Prg_Graphicy.exe not found!
    goto :FAILED
)

copy /y "%PUB_GRAPHICY%\Prg_Graphicy.exe" "%TARGET_DIR%\MOADIAN.exe" >nul
copy /y "%PUB_GRAPHICY%\Prg_Graphicy.exe" "%SETUP_ROOT%\MOADIAN.exe" >nul
echo [OK] Published and renamed: MOADIAN.exe
echo.

:: ===============================================================================
:: STEP 2: Prg_Grpsend -> Group Send Tax.exe
:: ===============================================================================
echo -------------------------------------------------------------------------------
echo [2/4] Publishing Prg_Grpsend (win-x64 SingleFile)...
echo -------------------------------------------------------------------------------
set "PUB_GRPSEND=%TEMP_DIR%\Grpsend"
if exist "%PUB_GRPSEND%" rd /s /q "%PUB_GRPSEND%"

"%MSBUILD%" "%ROOT_DIR%Prg_Grpsend\Prg_Grpsend.csproj" -restore -t:Publish -p:PublishProfile=FolderProfile -p:PublishDir="%PUB_GRPSEND%\\" -v:m -nologo
if errorlevel 1 (
    echo [ERROR] Failed to compile Prg_Grpsend!
    goto :FAILED
)

if not exist "%PUB_GRPSEND%\Prg_Grpsend.exe" (
    echo [ERROR] Output file Prg_Grpsend.exe not found!
    goto :FAILED
)

copy /y "%PUB_GRPSEND%\Prg_Grpsend.exe" "%TARGET_DIR%\Group Send Tax.exe" >nul
copy /y "%PUB_GRPSEND%\Prg_Grpsend.exe" "%SETUP_ROOT%\Group Send Tax.exe" >nul
echo [OK] Published and renamed: Group Send Tax.exe
echo.

:: ===============================================================================
:: STEP 3: Prg_TrackSentInvoice (MainTax & SandBoxTax)
:: ===============================================================================
set "TRACK_XAML=%ROOT_DIR%Prg_TrackSentInvoice\MainWindow.xaml"
set "TRACK_BAK=%ROOT_DIR%Prg_TrackSentInvoice\MainWindow.xaml.pubbak"

:: Create backup of original MainWindow.xaml
copy /y "%TRACK_XAML%" "%TRACK_BAK%" >nul

:: --- 3A: Production (MainTax: RD_MAINTAX=True, RD_SANDBOX=False) ---
echo -------------------------------------------------------------------------------
echo [3/4] Publishing Prg_TrackSentInvoice -> MainTax.exe (سامانه اصلی)...
echo -------------------------------------------------------------------------------

powershell -NoProfile -Command "$p = '%TRACK_XAML%'; $c = [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8); $c = $c -replace '<RadioButton\s+IsChecked=.+?\s+x:Name=.RD_MAINTAX.', '<RadioButton IsChecked=\"True\"  x:Name=\"RD_MAINTAX\"' -replace '<RadioButton\s+IsChecked=.+?\s+x:Name=.RD_SANDBOX.', '<RadioButton IsChecked=\"False\" x:Name=\"RD_SANDBOX\"'; [System.IO.File]::WriteAllText($p, $c, [System.Text.Encoding]::UTF8);"

:: Verification check
powershell -NoProfile -Command "if ((Get-Content '%TRACK_XAML%') -match 'IsChecked=\"True\"\s+x:Name=\"RD_MAINTAX\"') { exit 0 } else { exit 1 }"
if errorlevel 1 (
    echo [ERROR] Failed to set RD_MAINTAX to True in MainWindow.xaml!
    goto :FAILED
)

:: Clean intermediate obj to force BAML recompile
if exist "%ROOT_DIR%Prg_TrackSentInvoice\obj" rd /s /q "%ROOT_DIR%Prg_TrackSentInvoice\obj"

set "PUB_MAINTAX=%TEMP_DIR%\MainTax"
if exist "%PUB_MAINTAX%" rd /s /q "%PUB_MAINTAX%"

"%MSBUILD%" "%ROOT_DIR%Prg_TrackSentInvoice\Prg_TrackSentInvoice.csproj" -restore -t:Publish -p:PublishProfile=FolderProfile -p:PublishDir="%PUB_MAINTAX%\\" -v:m -nologo
if errorlevel 1 (
    echo [ERROR] Failed to compile MainTax!
    goto :FAILED
)

if not exist "%PUB_MAINTAX%\Prg_TrackSentInvoice.exe" (
    echo [ERROR] Output file for MainTax not found!
    goto :FAILED
)

copy /y "%PUB_MAINTAX%\Prg_TrackSentInvoice.exe" "%TARGET_DIR%\MainTax.exe" >nul
copy /y "%PUB_MAINTAX%\Prg_TrackSentInvoice.exe" "%SETUP_ROOT%\MainTax.exe" >nul
echo [OK] Published and renamed: MainTax.exe
echo.

:: --- 3B: Sandbox (SandBoxTax: RD_MAINTAX=False, RD_SANDBOX=True) ---
echo -------------------------------------------------------------------------------
echo [4/4] Publishing Prg_TrackSentInvoice -> SandBoxTax.exe (آزمایشی Sandbox)...
echo -------------------------------------------------------------------------------

powershell -NoProfile -Command "$p = '%TRACK_XAML%'; $c = [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8); $c = $c -replace '<RadioButton\s+IsChecked=.+?\s+x:Name=.RD_MAINTAX.', '<RadioButton IsChecked=\"False\"  x:Name=\"RD_MAINTAX\"' -replace '<RadioButton\s+IsChecked=.+?\s+x:Name=.RD_SANDBOX.', '<RadioButton IsChecked=\"True\" x:Name=\"RD_SANDBOX\"'; [System.IO.File]::WriteAllText($p, $c, [System.Text.Encoding]::UTF8);"

:: Verification check
powershell -NoProfile -Command "if ((Get-Content '%TRACK_XAML%') -match 'IsChecked=\"True\"\s+x:Name=\"RD_SANDBOX\"') { exit 0 } else { exit 1 }"
if errorlevel 1 (
    echo [ERROR] Failed to set RD_SANDBOX to True in MainWindow.xaml!
    goto :FAILED
)

:: Clean intermediate obj to force BAML recompile
if exist "%ROOT_DIR%Prg_TrackSentInvoice\obj" rd /s /q "%ROOT_DIR%Prg_TrackSentInvoice\obj"

set "PUB_SANDBOX=%TEMP_DIR%\SandBoxTax"
if exist "%PUB_SANDBOX%" rd /s /q "%PUB_SANDBOX%"

"%MSBUILD%" "%ROOT_DIR%Prg_TrackSentInvoice\Prg_TrackSentInvoice.csproj" -restore -t:Publish -p:PublishProfile=FolderProfile -p:PublishDir="%PUB_SANDBOX%\\" -v:m -nologo
if errorlevel 1 (
    echo [ERROR] Failed to compile SandBoxTax!
    goto :FAILED
)

if not exist "%PUB_SANDBOX%\Prg_TrackSentInvoice.exe" (
    echo [ERROR] Output file for SandBoxTax not found!
    goto :FAILED
)

copy /y "%PUB_SANDBOX%\Prg_TrackSentInvoice.exe" "%TARGET_DIR%\SandBoxTax.exe" >nul
copy /y "%PUB_SANDBOX%\Prg_TrackSentInvoice.exe" "%SETUP_ROOT%\SandBoxTax.exe" >nul
echo [OK] Published and renamed: SandBoxTax.exe
echo.

:: Restore original MainWindow.xaml
if exist "%TRACK_BAK%" (
    copy /y "%TRACK_BAK%" "%TRACK_XAML%" >nul
    del /q "%TRACK_BAK%" >nul
)

:: Clean temporary publish files
if exist "%TEMP_DIR%" rd /s /q "%TEMP_DIR%"

echo ===============================================================================
echo SUCCESS: All 4 files published to:
echo "%TARGET_DIR%"
echo ===============================================================================
dir "%TARGET_DIR%\*.exe"
echo.

:: Open destination folder in Windows Explorer
explorer.exe "%TARGET_DIR%"

pause
exit /b 0

:FAILED
echo.
echo ===============================================================================
echo [ERROR] Publishing process failed!
echo ===============================================================================
if exist "%TRACK_BAK%" (
    copy /y "%TRACK_BAK%" "%TRACK_XAML%" >nul
    del /q "%TRACK_BAK%" >nul
)
if exist "%TEMP_DIR%" rd /s /q "%TEMP_DIR%"
pause
exit /b 1
