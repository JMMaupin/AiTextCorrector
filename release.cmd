@echo off
rem ===========================================================================
rem  Publication d'une release GitHub de AI Text Corrector.
rem
rem  1. Verifie : outils (dotnet, git, gh), connexion GitHub, arbre de travail
rem     propre, branche poussee.
rem  2. Demande le type de release et en deduit la version :
rem       Correctifs   +0.0.1
rem       Nouveautes   +0.1.0
rem       Majeure      +1.0.0  (gros changement, dont l'interface principale)
rem  3. Ecrit la version dans le csproj, publie l'exe (publish.cmd /nobump) et
rem     calcule son empreinte SHA-256.
rem  4. Apres confirmation seulement : commite la version, cree le tag vX.Y.Z,
rem     pousse branche et tag ensemble, puis cree la release GitHub.
rem     Annulation ou echec avant le commit : le csproj est remis en l'etat.
rem
rem  L'exe est publie sous le nom fixe AiTextCorrector.exe : le lien
rem  .../releases/latest/download/AiTextCorrector.exe vise ainsi toujours la
rem  derniere version.
rem
rem  Aucun caractere accentue : les .cmd s'affichent dans la page de codes OEM
rem  de la console, qui deformerait les accents d'un fichier UTF-8.
rem ===========================================================================
setlocal

pushd "%~dp0"
set "PROJECT=AiTextCorrector\AiTextCorrector.csproj"
set "OUTEXE=AiTextCorrector\bin\Publish\Portable\AiTextCorrector.exe"
rem Chemins complets : cmd ne cherche pas toujours les commandes dans le dossier
rem courant (variable NoDefaultCurrentDirectoryInExePath).
set "BUMP=powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\bump-version.ps1""
set "NOTES=%TEMP%\AiTextCorrector-release-notes.md"
set "BUMPED="

echo.
echo ===============================================
echo   AI Text Corrector - release GitHub
echo ===============================================
echo.

rem --- Outils ------------------------------------------------------------------
for %%t in (dotnet git gh) do (
    where %%t >nul 2>&1
    if errorlevel 1 (
        echo ERREUR : %%t est introuvable dans le PATH.
        goto :fail
    )
)

gh auth status >nul 2>&1
if errorlevel 1 (
    echo ERREUR : gh n'est pas connecte a GitHub. Lance d'abord : gh auth login
    goto :fail
)

rem --- Etat du depot ------------------------------------------------------------
rem La release doit correspondre exactement a un commit present sur GitHub.
set "DIRTY="
for /f "delims=" %%s in ('git status --porcelain') do set "DIRTY=1"
if defined DIRTY (
    echo ERREUR : des modifications ne sont pas commitees :
    git status --short
    echo Commite-les ^(ou mets-les de cote^) avant de publier.
    goto :fail
)

git fetch --quiet origin
if errorlevel 1 (
    echo ERREUR : impossible de joindre le depot distant.
    goto :fail
)

set "AHEAD="
for /f %%n in ('git rev-list --count @{u}..HEAD 2^>nul') do set "AHEAD=%%n"
if not defined AHEAD (
    echo ERREUR : la branche courante ne suit aucune branche distante.
    goto :fail
)
if not "%AHEAD%"=="0" (
    echo ERREUR : %AHEAD% commit^(s^) non pousse^(s^). Lance d'abord : git push
    goto :fail
)

rem --- Type de release et version -------------------------------------------------
set "CURRENT="
for /f "usebackq delims=" %%v in (`dotnet msbuild "%PROJECT%" -getProperty:Version -nologo 2^>nul`) do set "CURRENT=%%v"
set "VPATCH=" & set "VMINOR=" & set "VMAJOR="
for /f "usebackq delims=" %%v in (`%BUMP% -Part patch -DryRun`) do set "VPATCH=%%v"
for /f "usebackq delims=" %%v in (`%BUMP% -Part minor -DryRun`) do set "VMINOR=%%v"
for /f "usebackq delims=" %%v in (`%BUMP% -Part major -DryRun`) do set "VMAJOR=%%v"
if not defined VMAJOR (
    echo ERREUR : version illisible dans %PROJECT%.
    goto :fail
)

echo   Version actuelle : %CURRENT%
echo.
echo   [C] Correctifs uniquement                       -^> %VPATCH%
echo   [N] Nouveautes                                  -^> %VMINOR%
echo   [M] Majeure : gros changement, UI principale    -^> %VMAJOR%
echo   [A] Annuler
echo.
choice /c CNMA /n /m "Type de release ? [C/N/M/A] "
if errorlevel 4 goto :cancel
if errorlevel 3 (set "PART=major" & set "VER=%VMAJOR%" & goto :versionchosen)
if errorlevel 2 (set "PART=minor" & set "VER=%VMINOR%" & goto :versionchosen)
set "PART=patch" & set "VER=%VPATCH%"

:versionchosen
set "TAG=v%VER%"

git rev-parse -q --verify "refs/tags/%TAG%" >nul
if not errorlevel 1 (
    echo ERREUR : le tag %TAG% existe deja en local.
    goto :fail
)
git ls-remote --exit-code --tags origin "refs/tags/%TAG%" >nul 2>&1
if not errorlevel 1 (
    echo ERREUR : le tag %TAG% existe deja sur GitHub.
    goto :fail
)

rem A partir d'ici, le csproj est modifie : :fail et :cancel le remettent en l'etat
rem tant que le commit de version n'est pas fait.
set "WRITTEN="
for /f "usebackq delims=" %%v in (`%BUMP% -Part %PART%`) do set "WRITTEN=%%v"
set "BUMPED=1"
if not "%WRITTEN%"=="%VER%" (
    echo ERREUR : la version ecrite "%WRITTEN%" differe de la version prevue "%VER%".
    goto :fail
)

rem --- Publication de l'exe -------------------------------------------------------
call "%~dp0publish.cmd" /nobump
if errorlevel 1 goto :fail

rem Garde-fou : l'exe produit doit porter la version de la release.
set "EXEVER="
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Get-Item '%OUTEXE%').VersionInfo.ProductVersion.Split('+')[0]"`) do set "EXEVER=%%v"
if not "%EXEVER%"=="%VER%" (
    echo ERREUR : l'exe publie porte la version "%EXEVER%" au lieu de "%VER%".
    goto :fail
)

rem Empreinte calculee par .NET directement : Get-FileHash vit dans un module que
rem Windows PowerShell ne charge pas toujours (PSModulePath herite de pwsh 7).
rem Toute erreur, ou une empreinte malformee, fait echouer la commande.
powershell -NoProfile -Command "$ErrorActionPreference = 'Stop'; $s = [IO.File]::OpenRead('%OUTEXE%'); try { $b = [Security.Cryptography.SHA256]::Create().ComputeHash($s) } finally { $s.Dispose() }; $h = -join ($b | ForEach-Object { $_.ToString('x2') }); if ($h.Length -ne 64) { exit 1 }; [IO.File]::WriteAllText('%OUTEXE%.sha256', $h + '  AiTextCorrector.exe' + [Environment]::NewLine)"
if errorlevel 1 (
    echo ERREUR : calcul de l'empreinte SHA-256 impossible.
    goto :fail
)

rem --- Recapitulatif et confirmation ----------------------------------------------
echo.
echo ===============================================
echo   Pret a publier
echo ===============================================
echo   Version  : %CURRENT% -^> %VER%  ^(tag %TAG%^)
rem Ligne ecrite par git lui-meme : un sujet de commit contenant ^> ou ^& serait
rem interprete par cmd s'il passait par une variable.
git log -1 --format="  Base     : %%h %%s"
echo   Fichiers : AiTextCorrector.exe, AiTextCorrector.exe.sha256
echo.
echo   La release sera PUBLIQUE si le depot est public.
echo.
choice /c ON /n /m "Publier cette release sur GitHub ? [O/N] "
if errorlevel 2 goto :cancel

rem --- Notes de version (completees par la liste des commits) ---------------------
> "%NOTES%" echo ## Install or update
>> "%NOTES%" echo.
>> "%NOTES%" echo Download **AiTextCorrector.exe** below and run it:
>> "%NOTES%" echo.
>> "%NOTES%" echo - not installed yet: it offers to install itself for your account, without administrator rights;
>> "%NOTES%" echo - already installed: it updates the installed version in place.
>> "%NOTES%" echo.
>> "%NOTES%" echo Requires Windows 10 or 11 x64 and the .NET 8 Desktop Runtime; Windows offers to install the runtime if it is missing.
>> "%NOTES%" echo The SHA-256 checksum of the executable is in AiTextCorrector.exe.sha256.

rem --- Commit de version, tag, push, release --------------------------------------
git commit --quiet -m "Version %VER%" -- "%PROJECT%"
if errorlevel 1 goto :fail
set "BUMPED="

git tag -a "%TAG%" -m "AI Text Corrector %VER%"
if errorlevel 1 (
    echo ERREUR : tag impossible. Le commit "Version %VER%" est fait en local.
    goto :fail
)

rem Push atomique : la branche et le tag arrivent sur GitHub ensemble, ou pas du tout.
git push --atomic origin HEAD "%TAG%"
if errorlevel 1 (
    echo ERREUR : push refuse. Le commit et le tag %TAG% existent en local.
    echo Pour reessayer : git push --atomic origin HEAD %TAG%
    echo Pour annuler   : git tag -d %TAG% ^&^& git reset --soft HEAD~1
    goto :fail
)

gh release create "%TAG%" "%OUTEXE%" "%OUTEXE%.sha256" --title "AI Text Corrector %VER%" --notes-file "%NOTES%" --generate-notes --verify-tag
if errorlevel 1 (
    echo.
    echo ERREUR : la release n'a pas ete creee, mais le tag %TAG% est deja sur GitHub.
    echo Pour reessayer, relance seulement :
    echo   gh release create %TAG% "%OUTEXE%" "%OUTEXE%.sha256" --title "AI Text Corrector %VER%" --generate-notes --verify-tag
    goto :fail
)

echo.
echo ===============================================
echo   Release %TAG% publiee
echo ===============================================
gh release view "%TAG%" --json url --jq .url
echo.
set "EXITCODE=0"
goto :end

:cancel
echo.
echo Abandon : rien n'a ete publie.
call :restore
set "EXITCODE=0"
goto :end

:fail
echo.
call :restore
set "EXITCODE=1"
goto :end

rem Remet le csproj en l'etat si la version y a ete ecrite sans etre commitee.
:restore
if defined BUMPED (
    git checkout --quiet -- "%PROJECT%"
    echo Version du csproj remise a %CURRENT%.
    set "BUMPED="
)
exit /b 0

:end
if exist "%NOTES%" del "%NOTES%" >nul 2>&1
popd
rem Pause uniquement en cas de lancement par double-clic (meme logique que publish.cmd).
echo %cmdcmdline% | find /i "%~nx0" >nul
if not errorlevel 1 pause
exit /b %EXITCODE%
