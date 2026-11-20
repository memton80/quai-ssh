# Développement de Quai SSH

[![Build](https://github.com/memton80/quai-ssh/actions/workflows/build.yml/badge.svg)](https://github.com/memton80/quai-ssh/actions/workflows/build.yml)

Application Windows native (WinUI 3) de gestion de connexions SSH, de ports série et de transferts SFTP. Les choix techniques et leurs raisons sont tenus à jour dans [DECISIONS.md](DECISIONS.md) ; le détail des fonctionnalités est dans le [guide](GUIDE.md).

## Organisation

```
Quai.sln
src/Quai.Core/          logique sans dépendance UI : modèles, stockage, SSH / SFTP, port série, view-models
src/Quai.App/           application WinUI 3 : vues, dialogues, coffre Windows, hébergement du terminal natif
tests/Quai.Core.Tests/  tests xUnit (unitaires + intégration contre un vrai OpenSSH et de vrais ports série virtuels)
tests/integration/      script qui démarre le serveur OpenSSH de test
installer/Quai.iss      script Inno Setup de l'installateur
docs/DECISIONS.md       journal des choix techniques
```

Briques : .NET 10, Windows App SDK 2 (WinUI 3, non packagé, auto-contenu), [SSH.NET](https://github.com/sshnet/SSH.NET), `System.IO.Ports` (port série, paquet officiel de .NET), le moteur de [Windows Terminal](https://github.com/microsoft/terminal) (`Microsoft.Terminal.Control.dll`, licence MIT), CommunityToolkit (MVVM et contrôles), Serilog, Inno Setup.

## Tests

```bash
dotnet test --project tests/Quai.Core.Tests/Quai.Core.Tests.csproj
```

Les tests d'intégration SSH / SFTP sont ignorés tant que les variables `QUAI_TEST_SSH_*` ne sont pas définies ; `tests/integration/start-sshd.sh` démarre un serveur jetable et les fournit (Linux). Ceux du port série tournent sous Linux dès que `socat` est installé (il crée un port virtuel relié à un appareil d'écho).

## Publication (sous Windows)

```bash
dotnet publish src/Quai.App/Quai.App.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o publish/x64
```

## Intégration continue et releases

`.github/workflows/build.yml` : à chaque push sur `main` (et à la demande sur une branche), build `x64` et `arm64` sur `windows-latest`, tests, construction de l'installateur, puis sur `x64` :

- installation silencieuse ;
- **test de fumée de l'application installée** (lancement réel, parcours de chaque écran avec captures, animations de Windows activées comme sur un poste) ;
- **mise à jour en place de bout en bout** (Quai SSH télécharge et vérifie l'installateur du run, s'installe en silencieux et redémarre) ;
- désinstallation vérifiée.

Un job Linux exécute les tests d'intégration contre OpenSSH et sur des ports série virtuels (`socat`).

**Releases automatiques** : la version se règle dans `Directory.Build.props` (`<Version>X.Y.Z</Version>`) ; quand un numéro sans tag arrive sur `main`, le CI le construit, crée le tag `vX.Y.Z` et publie installateurs et zips dans Releases. Pour sortir une version, il suffit de monter ce numéro.
