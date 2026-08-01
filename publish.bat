@echo off
rem ===========================================================================
rem  Publication de la version portable de AI Text Corrector.
rem
rem  Produit un fichier .exe unique, framework-dependent : le .NET 8 Desktop
rem  Runtime doit etre present sur le PC cible (sinon Windows propose de
rem  l'installer au premier lancement).
rem
rem  Ce script n'utilise volontairement aucun caractere accentue : les fichiers
rem  .bat s'affichent dans la page de codes OEM de la console, qui deformerait
rem  les accents d'un fichier enregistre en UTF-8.
rem ===========================================================================
setlocal

rem Chemins relatifs a l'emplacement de ce script : il fonctionne quel que soit
rem le repertoire courant depuis lequel on l'appelle.
set "PROJECT=%~dp0AiTextCorrector\AiTextCorrector.csproj"
set "OUTDIR=%~dp0AiTextCorrector\bin\Publish\Portable"
set "OUTEXE=%OUTDIR%\AiTextCorrector.exe"

echo.
echo ===============================================
echo   AI Text Corrector - publication portable
echo ===============================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERREUR : le SDK .NET est introuvable dans le PATH.
    echo Installe-le depuis https://dotnet.microsoft.com/download
    goto :fail
)

if not exist "%PROJECT%" (
    echo ERREUR : projet introuvable :
    echo   %PROJECT%
    goto :fail
)

rem Repart d'un dossier vide : sans cela, les fichiers d'une publication
rem precedente (autre version, autre profil) subsistent et on ne sait plus ce
rem qui vient d'etre produit.
if exist "%OUTDIR%" rd /s /q "%OUTDIR%"

dotnet publish "%PROJECT%" -c Release /p:PublishProfile=Portable --nologo
if errorlevel 1 (
    echo.
    echo ERREUR : la publication a echoue ^(voir les messages ci-dessus^).
    goto :fail
)

rem dotnet publish peut renvoyer 0 sans rien produire si le profil change :
rem on verifie explicitement que le binaire attendu existe.
if not exist "%OUTEXE%" (
    echo.
    echo ERREUR : executable absent apres publication :
    echo   %OUTEXE%
    goto :fail
)

rem Version reelle lue dans le binaire produit, et non redeclaree ici : le
rem csproj reste la source unique. On retire le "+<sha git>" ajoute par le SDK.
set "VER=inconnue"
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Get-Item '%OUTEXE%').VersionInfo.ProductVersion.Split('+')[0]" 2^>nul`) do set "VER=%%v"

for %%f in ("%OUTEXE%") do set "SIZE=%%~zf"

echo.
echo ===============================================
echo   Publication terminee
echo ===============================================
echo   Version : %VER%
echo   Taille  : %SIZE% octets
echo   Fichier : %OUTEXE%
echo.
echo   Seul AiTextCorrector.exe est necessaire : tout est embarque dedans.
echo   Les autres fichiers du dossier sont des sous-produits du SDK, a ignorer.
echo.
echo   Pour installer, dans une invite ADMINISTRATEUR :
echo     copy /y "%OUTEXE%" "%%ProgramFiles(x86)%%\AiTextCorrector\"
echo.
echo   Fermer l'application avant la copie, sinon le fichier est verrouille.
echo.

set "EXITCODE=0"
goto :end

:fail
echo.
set "EXITCODE=1"

:end
rem Pause uniquement en cas de lancement par double-clic, sinon la fenetre se
rem refermerait avant lecture. Lance depuis une console deja ouverte, on rend
rem la main immediatement.
echo %cmdcmdline% | find /i "%~nx0" >nul
if not errorlevel 1 pause
exit /b %EXITCODE%
