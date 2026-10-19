# AI Text Corrector V1

Application Windows WPF (.NET 8) avec support multi-provider OpenAI + Anthropic/Claude pour corriger un texte saisi ou collé, puis l'envoyer en texte brut au presse-papiers.

## Nouveautés V1

- Support OpenAI et Anthropic/Claude
- Sélection du provider dans l'interface
- Lecture de `OPENAI_API_KEY` et `AITEXTCORRECTOR_ANTHROPIC_API_KEY`
- Choix du modèle OpenAI et du modèle Claude via `appsettings.json`
- Saisie directe ou collage dans la fenêtre
- Copie automatique après correction
- Sortie presse-papiers en texte brut Unicode
- Option ASCII simple sans accents
- Option de recollage automatique dans l'application d'origine
- Réduction dans la zone de notification

## Ouvrir dans VS Code

```bash
cd AiTextCorrector
code .
```

## Ouvrir dans Visual Studio

Ouvre `AiTextCorrector.sln`.

## Prérequis

- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 ou VS Code + extension C#

## Installation

```bash
cd AiTextCorrector
dotnet restore
```

## Configuration OpenAI

```powershell
$Env:OPENAI_API_KEY="ta_cle_openai"
```

## Configuration Anthropic

```powershell
$Env:AITEXTCORRECTOR_ANTHROPIC_API_KEY="ta_cle_anthropic"
```

> Remarque : on utilise `AITEXTCORRECTOR_ANTHROPIC_API_KEY` (et non `ANTHROPIC_API_KEY`)
> pour éviter tout conflit avec d'autres outils qui utilisent `ANTHROPIC_API_KEY`,
> comme Claude Code.

Tu peux aussi renseigner `ApiKey` et `AnthropicApiKey` dans `appsettings.json`.

## Modèles

- OpenAI: champ `Model`
- Claude: champ `AnthropicModel`

## Exemple de config Claude

```json
{
  "Provider": "Anthropic",
  "AnthropicModel": "claude-sonnet-4-6"
}
```

## Lancer

```bash
dotnet run
```

## Publier

```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

## Générer l'exe portable

Produit un unique `.exe` léger (framework-dependent) via le profil `Portable` :

```bash
dotnet publish -c Release /p:PublishProfile=Portable
```

L'exe est généré dans `bin/Publish/Portable/AiTextCorrector.exe`.

- Nécessite le **.NET 8 Desktop Runtime** sur le PC cible.
- Si le runtime est absent, l'exe affiche automatiquement une boîte de dialogue
  Windows invitant à l'installer.
- Le seul `.exe` suffit à distribuer l'application (le fichier `appsettings.json`
  est recréé au premier lancement et les clés API sont stockées dans le registre).

## Installation et mise à jour sur un poste

L'exe publié s'installe et se met à jour lui-même ; il suffit de le lancer :

- **Pas encore installé** : une fenêtre propose l'installation (option « Start with
  Windows ») ou un lancement sans installation. L'installation se fait pour
  l'utilisateur, sans droits administrateur, dans
  `%LOCALAPPDATA%\Programs\AiTextCorrector`, avec un raccourci dans le menu
  Démarrer et une entrée dans « Applications installées ».
- **Déjà installé, exe plus récent** (ou même version mais autre build) : mise à
  jour automatique. L'instance en cours est fermée, l'exe remplacé, la nouvelle
  version relancée ; une bulle de notification le confirme.
- **Déjà installé, exe identique ou plus ancien** : la version installée est
  lancée, jamais rétrogradée.
- **Désinstallation** : depuis « Applications installées » de Windows (ou
  `AiTextCorrector.exe --uninstall`). Les réglages et clés API sont conservés,
  sauf demande contraire.

Les builds de développement (`dotnet run`, `bin\Debug`) ne sont pas concernés.

## Flux d'utilisation

1. Choisis le provider dans la liste.
2. Copie un texte ou saisis-le directement dans la zone de gauche.
3. Appuie sur `Ctrl+Alt+Space` pour ouvrir la fenêtre.
4. Clique sur un mode de correction.
5. Le résultat s'affiche à droite.
6. Le texte corrigé est copié automatiquement dans le presse-papiers en texte brut.
7. Si l'option est activée, il est recollé dans l'application d'origine.

## Traduction

- Choisis la langue cible dans la liste à côté de **Translate**, puis clique sur **Translate** :
  le texte corrigé (ou, à défaut, le texte source) est traduit et remplace le résultat.
  Copie et recollage automatiques s'appliquent comme pour une correction.
- **+** ajoute une langue : tape les premières lettres de son nom en anglais
  (« ger »), dans la langue elle-même (« deu ») ou dans celle des formats régionaux
  de Windows (« all »), puis Entrée ou clic sur une suggestion. Un nom absent du
  catalogue est accepté tel quel (« Brazilian Portuguese »).
- La corbeille retire la langue sélectionnée (la dernière ne peut pas l'être).
- Liste et dernière langue choisie sont mémorisées (`TranslationLanguages`,
  `TranslationLanguage`) ; le prompt se règle dans `Prompts.Translation`, où
  `{language}` est remplacé par la langue cible.

## Notes

- Avec l'API Claude directe, l'authentification repose sur une clé API Anthropic.
- Le SDK C# Anthropic gère automatiquement les détails d'en-têtes requis par l'API.
- Le mode Unicode texte brut conserve les accents.
- Le mode ASCII retire les accents et filtre les caractères non ASCII.
