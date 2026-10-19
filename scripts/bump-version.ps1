<#
  Incremente la version (balise <Version>) du csproj, source unique du numero.

    -Part patch   +0.0.1  exe de test publie en cours de debug (corrections, mises au point)
    -Part minor   +0.1.0  release avec des nouveautes
    -Part major   +1.0.0  gros changement, dont l'interface principale

  Avec -DryRun, calcule sans ecrire. Dans tous les cas, la nouvelle version est
  la seule ligne ecrite sur la sortie standard, pour que les .cmd la recuperent.

  Un suffixe de pre-version (1.2.0-beta) est abandonne : on repart de la partie
  numerique (1.2.0 + patch = 1.2.1).

  Compatible Windows PowerShell 5.1 (appele par powershell.exe depuis les .cmd),
  et sans caractere accentue : un .ps1 sans BOM y est lu en ANSI.
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('patch', 'minor', 'major')]
    [string]$Part,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$csproj = Join-Path $PSScriptRoot '..\AiTextCorrector\AiTextCorrector.csproj'
$csproj = [IO.Path]::GetFullPath($csproj)

# Lecture brute : seule la balise est remplacee, le reste du fichier (commentaires,
# fins de ligne CRLF, absence de BOM) reste identique octet pour octet.
$bytes = [IO.File]::ReadAllBytes($csproj)
$hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
$encoding = New-Object System.Text.UTF8Encoding($hasBom)
$content = [IO.File]::ReadAllText($csproj, $encoding)

$match = [regex]::Match($content, '<Version>\s*(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z.-]+)?\s*</Version>')
if (-not $match.Success) {
    throw "Balise <Version> absente ou invalide (attendu X.Y.Z) dans $csproj"
}

$major = [int]$match.Groups[1].Value
$minor = [int]$match.Groups[2].Value
$patch = [int]$match.Groups[3].Value

switch ($Part) {
    'patch' { $patch++ }
    'minor' { $minor++; $patch = 0 }
    'major' { $major++; $minor = 0; $patch = 0 }
}

$newVersion = "$major.$minor.$patch"

if (-not $DryRun) {
    $updated = $content.Remove($match.Index, $match.Length).Insert($match.Index, "<Version>$newVersion</Version>")
    [IO.File]::WriteAllText($csproj, $updated, $encoding)
}

Write-Output $newVersion
