# Quai — consignes pour Claude Code

Lis ce fichier en entier avant de toucher au code. Le propriétaire (Alexandre) parle un français décontracté : réponds-lui en français, direct et pratique. Le code, les noms de classes et les commits conventionnels restent en anglais ou en français selon ce qui est le plus clair, mais sois cohérent.

## Objectif

Application Windows native de gestion de connexions SSH et de transferts SFTP (dans l'esprit de Termius / WinSCP / MobaXterm, en plus léger), avec une interface WinUI 3 au look Windows 11.

## Règle n°1 : ne pas réinventer la roue

Avant d'écrire une brique toi-même, cherche une bibliothèque ou un composant existant et maintenu (NuGet, GitHub). Si tu écris quand même ta propre version, note la raison dans `docs/DECISIONS.md` (une ligne par décision).

Briques imposées au départ (à remplacer seulement si tu trouves nettement mieux, avec justification) :

| Besoin | Brique |
|---|---|
| SSH, SFTP, port forwarding, auth mot de passe / clé | `SSH.NET` (NuGet) |
| Émulation de terminal | `xterm.js` (+ addon fit) hébergé dans `WebView2` (`Microsoft.Web.WebView2`), assets embarqués localement, pas de CDN |
| MVVM | `CommunityToolkit.Mvvm` |
| DI, config, logs | `Microsoft.Extensions.*` + `Serilog` (fichier) |
| Contrôles WinUI supplémentaires | `CommunityToolkit.WinUI.Controls.*` |
| Secrets (mots de passe, passphrases) | coffre Windows (`PasswordVault`), jamais dans le JSON |
| Persistance des connexions et réglages | `System.Text.Json`, dossier `%LOCALAPPDATA%\Quai` |
| Tests | `xUnit` |
| Import `~/.ssh/config` | chercher une lib NuGet existante avant d'écrire un parseur |

Note : SSH.NET ne gère pas nativement l'agent OpenSSH de Windows. C'est un sujet pour plus tard, à étudier avec une lib existante.

## Stack et cible

- C#, WinUI 3 / Windows App SDK, application **non packagée** (`WindowsPackageType=None`), self-contained.
- .NET : prends la dernière LTS réellement supportée par la version stable actuelle du Windows App SDK. Vérifie-le, ne suppose pas. Adapte `dotnet-version` dans le workflow en conséquence.
- Architectures : `x64` et `arm64`.
- Interface en français d'abord (ressources `.resw`, prêt pour l'anglais ensuite).

## Structure attendue

```
Quai.sln
src/Quai.Core/          logique sans aucune dépendance UI (modèles, services SSH/SFTP, stockage)
src/Quai.App/           application WinUI 3 (vues, view-models, assets)
tests/Quai.Core.Tests/  tests xUnit sur Quai.Core
docs/DECISIONS.md       journal des choix techniques
```

`Quai.Core` ne référence jamais WinUI. Tout ce qui est testable va dans `Quai.Core`.

## Fonctionnalités, dans cet ordre

1. **Squelette** : solution, projets, fenêtre avec `NavigationView`, Mica, thème système. Le CI doit être vert.
2. **Carnet de connexions** : créer / modifier / supprimer, dossiers et tags, recherche, stockage JSON.
3. **Terminal SSH** : onglets (`TabView`), auth mot de passe et clé privée (avec passphrase), vérification de l'empreinte de l'hôte (confiance au premier contact, `known_hosts` propre à l'app, alerte claire si l'empreinte change).
4. **Explorateur SFTP** : double panneau local / distant, glisser-déposer, upload / download avec progression et annulation, renommer, supprimer (avec confirmation), nouveau dossier, permissions.
5. **Réglages** : thème, police et taille du terminal.
6. **Plus tard** : tunnels / port forwarding, jump host, import `ssh_config` et PuTTY, agent OpenSSH, snippets.

Ne passe à l'étape suivante que quand la précédente fonctionne et que le CI est vert.

## Sécurité (non négociable)

- Aucun secret en clair sur disque, ni dans les logs, ni dans les messages d'erreur.
- Ne jamais désactiver la vérification de l'empreinte de l'hôte, même « temporairement ».
- Confirmation avant toute suppression distante.
- Pas de télémétrie.

## Règle CSS / HTML

Le CSS ne va **jamais** dans le HTML. La page hôte du terminal est composée de fichiers séparés : `terminal.html`, `terminal.css`, `terminal.js`. Pas d'attribut `style=""`, pas de balise `<style>` dans le HTML. (XAML n'est pas concerné, mais préfère les `ResourceDictionary` aux styles répétés.)

## UI

Fluent / Windows 11 : `NavigationView`, `TabView`, Mica, coins arrondis, thèmes clair / sombre / système, navigation au clavier, textes d'accessibilité sur les boutons icônes. Reste sobre, pas de fioritures.

## CI et releases (GitHub Actions uniquement)

- Le build se fait par `.github/workflows/build.yml` sur `windows-latest`. Il se déclenche sur les changements dans `src/`, `tests/`, `Quai.sln` et les fichiers `Directory.*`.
- Le workflow est un point de départ : adapte-le dès que les projets existent (noms des projets, propriétés de publication WinUI 3, `Platforms` / `RuntimeIdentifiers` dans les `.csproj`).
- Après chaque push, vérifie le résultat du run (`gh run watch` ou `gh run list`) et corrige jusqu'à ce que ce soit vert. Ne déclare rien terminé tant que le CI n'est pas vert.
- Release : un tag `vX.Y.Z` publie les zips `x64` et `arm64` dans GitHub Releases.

## Façon de travailler

- Petits commits, messages clairs.
- Un choix structurant non prévu ici (changer de lib majeure, passer en MSIX, etc.) : pose la question à Alexandre avant de le faire.
- Si une action est risquée ou irréversible, préviens avant.
