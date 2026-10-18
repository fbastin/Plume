@echo off
rem Construit Plume avec le compilateur C# fourni avec Windows (.NET Framework 4.8) : rien a installer.
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
set ROOT=%~dp0
set OUT=%ROOT%bin
if not exist "%OUT%\data" mkdir "%OUT%\data"

"%FW%\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /warn:4 ^
  /out:"%OUT%\Plume.exe" /win32manifest:"%ROOT%src\app.manifest" /win32icon:"%ROOT%src\plume.ico" ^
  /lib:"%FW%\WPF" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:System.Speech.dll /r:Accessibility.dll /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll ^
  "%ROOT%src\*.cs"
if errorlevel 1 exit /b 1

copy /y "%ROOT%src\app.config" "%OUT%\Plume.exe.config" >nul
copy /y "%ROOT%data\*.plm" "%OUT%\data\" >nul

rem Programme d'installation en un seul fichier : Plume et ses dictionnaires y sont integres.
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /warn:4 ^
  /out:"%OUT%\PlumeSetup.exe" /win32manifest:"%ROOT%setup\setup.manifest" /win32icon:"%ROOT%src\plume.ico" ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /resource:"%OUT%\Plume.exe",Plume.exe /resource:"%OUT%\Plume.exe.config",Plume.exe.config ^
  /resource:"%ROOT%data\fr.plm",data/fr.plm /resource:"%ROOT%data\en.plm",data/en.plm ^
  "%ROOT%setup\Setup.cs" "%ROOT%src\Installer.cs" "%ROOT%src\Strings.cs"
if errorlevel 1 exit /b 1

if /i "%1"=="test" (
  "%FW%\csc.exe" /nologo /target:exe /optimize+ /codepage:65001 /out:"%OUT%\PlumeTests.exe" ^
    "%ROOT%tests\EngineTests.cs" "%ROOT%src\Tokenizer.cs" "%ROOT%src\TextUtil.cs" "%ROOT%src\LanguageModel.cs" ^
    "%ROOT%src\UserModel.cs" "%ROOT%src\Predictor.cs" "%ROOT%src\Settings.cs" "%ROOT%src\Strings.cs" "%ROOT%src\Terminals.cs"
  if errorlevel 1 exit /b 1
  "%OUT%\PlumeTests.exe" "%OUT%\data"
  if errorlevel 1 exit /b 1
)
if /i "%1"=="e2e" (
  rem Ouvre une fenetre de test, y tape du texte et verifie ce que Plume insere.
  "%FW%\csc.exe" /nologo /target:exe /optimize+ /codepage:65001 /out:"%OUT%\PlumeE2E.exe" ^
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "%ROOT%tests\E2ETest.cs"
  if errorlevel 1 exit /b 1
  "%OUT%\PlumeE2E.exe" "%OUT%" "%OUT%\captures"
  if errorlevel 1 exit /b 1
)
echo Plume.exe et PlumeSetup.exe sont prets dans %OUT%
