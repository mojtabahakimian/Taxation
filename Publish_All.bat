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

:: 2B. Find Rar.exe (WinRAR) for SFX packaging
set "RAR_EXE="
if exist "C:\Program Files\WinRAR\Rar.exe" set "RAR_EXE=C:\Program Files\WinRAR\Rar.exe"
if not defined RAR_EXE if exist "%ProgramFiles%\WinRAR\Rar.exe" set "RAR_EXE=%ProgramFiles%\WinRAR\Rar.exe"
if not defined RAR_EXE if exist "%ProgramFiles(x86)%\WinRAR\Rar.exe" set "RAR_EXE=%ProgramFiles(x86)%\WinRAR\Rar.exe"

if defined RAR_EXE (
    echo [INFO] WinRAR : "%RAR_EXE%"
) else (
    echo [WARN] WinRAR not found. SFX archive will be skipped.
)

:: 3. Detect Version from Prg_Graphicy\Prg_Graphicy.csproj
set "VERSION=%~1"
if "%VERSION%"=="" (
    for /f "usebackq tokens=*" %%v in (`powershell -NoProfile -NoLogo -Command "([xml](Get-Content '%ROOT_DIR%Prg_Graphicy\Prg_Graphicy.csproj')).SelectSingleNode('//FileVersion').InnerText.Trim()"`) do (
        set "VERSION=%%v"
    )
)

if "%VERSION%"=="" (
    echo [WARN] Could not detect Version from project files!
    set /p "VERSION=Please enter version (e.g. 8.9.9): "
)

:: Trim spaces
if not "%VERSION%"=="" set "VERSION=%VERSION: =%"

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
echo [1/5] Publishing Prg_Graphicy (win-x64 SingleFile)...
echo -------------------------------------------------------------------------------
set "PUB_GRAPHICY=%TEMP_DIR%\Graphicy"
if exist "%PUB_GRAPHICY%" rd /s /q "%PUB_GRAPHICY%"

"%MSBUILD%" "%ROOT_DIR%Prg_Graphicy\Prg_Graphicy.csproj" -restore -t:Publish -p:PublishProfile=FolderProfile -p:PublishDir="%PUB_GRAPHICY%\\" -p:FileVersion=%VERSION% -p:AssemblyVersion=%VERSION% -v:m -nologo
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
echo [2/5] Publishing Prg_Grpsend (win-x64 SingleFile)...
echo -------------------------------------------------------------------------------
set "PUB_GRPSEND=%TEMP_DIR%\Grpsend"
if exist "%PUB_GRPSEND%" rd /s /q "%PUB_GRPSEND%"

"%MSBUILD%" "%ROOT_DIR%Prg_Grpsend\Prg_Grpsend.csproj" -restore -t:Publish -p:PublishProfile=FolderProfile -p:PublishDir="%PUB_GRPSEND%\\" -p:FileVersion=%VERSION% -p:AssemblyVersion=%VERSION% -v:m -nologo
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
echo [3/5] Publishing Prg_TrackSentInvoice -> MainTax.exe (سامانه اصلی)...
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

"%MSBUILD%" "%ROOT_DIR%Prg_TrackSentInvoice\Prg_TrackSentInvoice.csproj" -restore -t:Publish -p:PublishProfile=FolderProfile -p:PublishDir="%PUB_MAINTAX%\\" -p:FileVersion=%VERSION% -p:AssemblyVersion=%VERSION% -v:m -nologo
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
echo [4/5] Publishing Prg_TrackSentInvoice -> SandBoxTax.exe (آزمایشی Sandbox)...
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

"%MSBUILD%" "%ROOT_DIR%Prg_TrackSentInvoice\Prg_TrackSentInvoice.csproj" -restore -t:Publish -p:PublishProfile=FolderProfile -p:PublishDir="%PUB_SANDBOX%\\" -p:FileVersion=%VERSION% -p:AssemblyVersion=%VERSION% -v:m -nologo
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

:: ===============================================================================
:: STEP 5: Create SFX Archive (Profile: MoadianSFX) -> Moadian %VERSION%.exe
:: ===============================================================================
echo -------------------------------------------------------------------------------
echo [5/5] Creating SFX Archive (Moadian %VERSION%.exe)...
echo -------------------------------------------------------------------------------

if not defined RAR_EXE (
    echo [WARN] Rar.exe not found! Skipping SFX archive creation.
    goto :SKIP_SFX
)

set "SFX_NAME=Moadian %VERSION%.exe"
set "SFX_DESKTOP=%USERPROFILE%\Desktop\%SFX_NAME%"
set "SFX_CMT=%TEMP_DIR%\sfx_comment.txt"

:: Prepare SFX script comments
if exist "%ROOT_DIR%MoadianSFX_Comment.txt" (
    copy /y "%ROOT_DIR%MoadianSFX_Comment.txt" "%SFX_CMT%" >nul
) else (
    (
        echo ;The comment below contains SFX script commands
        echo.
        echo Path=C:\CORRECT\
        echo Overwrite=1
        echo Title=استعلام مودیان
        echo Text
        echo {
        echo Moadian
        echo }
    ) > "%SFX_CMT%"
)

if exist "%SFX_DESKTOP%" del /f /q "%SFX_DESKTOP%" >nul

pushd "%TARGET_DIR%"
"%RAR_EXE%" a -sfx -s -m5 -md1024m -rr -k -t -z"%SFX_CMT%" -ep "%SFX_DESKTOP%" "Group Send Tax.exe" "MainTax.exe" "MOADIAN.exe" "SandBoxTax.exe"
if errorlevel 1 (
    popd
    echo [ERROR] Failed to create SFX archive!
    goto :FAILED
)
popd

if not exist "%SFX_DESKTOP%" (
    echo [ERROR] SFX output file not found: "%SFX_DESKTOP%"!
    goto :FAILED
)

:: Copy SFX to Setup Root and Publish folder
copy /y "%SFX_DESKTOP%" "%SETUP_ROOT%\%SFX_NAME%" >nul
copy /y "%SFX_DESKTOP%" "%TARGET_DIR%\%SFX_NAME%" >nul

echo.
echo [OK] Created SFX Archive: "%SFX_DESKTOP%"
echo [OK] Copied SFX Archive to: "%SETUP_ROOT%\%SFX_NAME%"
echo [OK] Copied SFX Archive to: "%TARGET_DIR%\%SFX_NAME%"
echo.

:SKIP_SFX

:: Clean temporary publish files
if exist "%TEMP_DIR%" rd /s /q "%TEMP_DIR%"

echo ===============================================================================
echo SUCCESS: All files published and packaged!
echo Target Folder: "%TARGET_DIR%"
if exist "%SFX_DESKTOP%" echo SFX Package  : "%SFX_DESKTOP%"
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
