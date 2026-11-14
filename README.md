# Quai

Gestionnaire de connexions **SSH**, de **ports série** et de transferts **SFTP** pour Windows, avec une interface native **WinUI 3** (look Windows 11). Dans l'esprit de Termius / WinSCP / MobaXterm / PuTTY, en plus léger.

[![Build](https://github.com/memton80/quai-ssh/actions/workflows/build.yml/badge.svg)](https://github.com/memton80/quai-ssh/actions/workflows/build.yml)

## Fonctionnalités

**Carnet de connexions**
- Serveurs SSH et ports série dans le même carnet.
- Créer, modifier, dupliquer, supprimer ; rangement par dossiers (`Prod/Web`) et par tags ; recherche instantanée (nom, hôte, utilisateur, port série, tag, notes — sans tenir compte des accents).
- Authentification par mot de passe ou par clé privée (OpenSSH, PEM, PuTTY ; passphrase prise en charge).

**Terminal SSH**
- Onglets, moteur natif de **Windows Terminal** (le même que l'application de Microsoft : rendu DirectWrite / Direct3D, couleurs, UTF-8, redimensionnement), reconnexion en un clic. Aucun navigateur embarqué.
- Copier / coller à la Windows Terminal : `Ctrl+C` copie s'il y a une sélection (sinon envoie `^C`), `Ctrl+Maj+C` copie, `Ctrl+V` ou `Maj+Inser` colle, clic droit copie ou colle. Collage multiligne protégé quand le shell le permet (« bracketed paste »).
- Historique : molette, barre de défilement, `Ctrl+Maj+↑ / ↓ / Pg.Préc / Pg.Suiv / Début / Fin`.

**Port série** (console de routeur ou de switch, carte Arduino / ESP32 / Raspberry Pi, modem…)
- Port choisi parmi ceux que Windows détecte (bouton « Actualiser » après avoir branché l'adaptateur) ou saisi (`COM3`).
- Vitesse (de 300 à 921 600 bauds, ou toute autre valeur), bits de données, parité, bits d'arrêt, contrôle de flux XON / XOFF ou RTS / CTS. Par défaut, comme PuTTY : 9600 bauds, 8N1, sans contrôle de flux.
- Même terminal à onglets que le SSH (copier / coller, historique). **Signal Break** par clic droit sur l'onglet. Appareil débranché : l'onglet le signale et se rouvre en un clic.

**Explorateur SFTP**
- Double panneau : cet ordinateur à gauche, le serveur à droite, séparateur redimensionnable.
- Envoi et téléchargement de fichiers et de dossiers entiers, par boutons ou par **glisser-déposer** (entre les panneaux, ou depuis l'Explorateur Windows vers le serveur).
- File de transferts avec progression et **annulation** ; confirmation avant tout écrasement.
- Renommer, nouveau dossier, **droits** (cases à cocher + notation octale), supprimer — toujours avec confirmation côté serveur, via la corbeille côté local.
- Clavier : `Entrée` ouvre, `Retour arrière` remonte, `F2` renomme, `F5` actualise, `Suppr` supprime.

**Réglages**
- Thème clair / sombre / identique à Windows (fond Mica sous Windows 11), police et taille du terminal, appliqués immédiatement.

**Fermeture et mises à jour**
- Fermer la fenêtre avec des sessions ouvertes demande confirmation (sessions et transferts en cours listés), puis chaque connexion est fermée proprement : déconnexion SSH, transferts interrompus, fichiers incomplets supprimés.
- Au démarrage, un bandeau signale qu'une nouvelle version est publiée dans les Releases GitHub (désactivable dans les réglages).

## Sécurité

- **Secrets** (mots de passe, passphrases) : uniquement dans le coffre Windows (Gestionnaire d'identification). Jamais dans un fichier, un journal ou un message d'erreur.
- **Clé d'hôte** toujours vérifiée : confiance au premier contact (l'empreinte SHA-256 vous est montrée avant toute saisie de mot de passe), puis alerte explicite si elle change. La vérification ne se désactive pas.
- **Aucune télémétrie**, aucun navigateur embarqué : le terminal est un composant natif. Seule connexion en dehors de vos serveurs : au démarrage, la lecture du numéro de la dernière version sur l'API publique de GitHub (sans compte, rien sur vous ni sur vos connexions) ; désactivable dans les réglages.

## Télécharger et installer

Rendez-vous dans [**Releases**](https://github.com/memton80/quai-ssh/releases) :

| Fichier | Pour qui |
|---|---|
| `Quai-Setup-x64.exe` | **Recommandé.** Installateur pour PC Intel / AMD 64 bits. |
| `Quai-Setup-arm64.exe` | Installateur pour PC ARM64 (Snapdragon…). |
| `Quai-x64.zip`, `Quai-arm64.zip` | Version portable : décompressez et lancez `Quai.exe`. |

L'installateur est un seul fichier qui contient tout (.NET et Windows App SDK compris). Il installe Quai pour votre compte dans `%LOCALAPPDATA%\Programs\Quai`, **sans droits administrateur**, et ajoute un raccourci au menu Démarrer.

> Les exécutables ne sont pas signés : au premier lancement, Windows SmartScreen peut afficher « Windows a protégé votre ordinateur ». Cliquez sur **Informations complémentaires** puis **Exécuter quand même**.

**Désinstaller** : Paramètres de Windows → Applications → Applications installées → Quai → Désinstaller. Le désinstallateur demande s'il faut aussi effacer vos données (connexions, réglages, hôtes connus et secrets du coffre Windows) ; par défaut elles sont conservées.

**Prérequis** : Windows 10 (1809) ou Windows 11, rien d'autre : le moteur du terminal est fourni avec Quai.

## Où sont mes données ?

Dans `%LOCALAPPDATA%\Quai` :

| Fichier | Contenu |
|---|---|
| `connections.json` | Le carnet (aucun secret). |
| `settings.json` | Les réglages. |
| `known_hosts` | Les clés d'hôte acceptées (format OpenSSH). |
| `logs\` | Journaux des 7 derniers jours. |

## Développement

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

Tests :

```bash
dotnet test --project tests/Quai.Core.Tests/Quai.Core.Tests.csproj
```

Les tests d'intégration SSH / SFTP sont ignorés tant que les variables `QUAI_TEST_SSH_*` ne sont pas définies ; `tests/integration/start-sshd.sh` démarre un serveur jetable et les fournit (Linux). Ceux du port série tournent sous Linux dès que `socat` est installé (il crée un port virtuel relié à un appareil d'écho).

Publication (sous Windows) :

```bash
dotnet publish src/Quai.App/Quai.App.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o publish/x64
```

**CI** (`.github/workflows/build.yml`) : à chaque push, build `x64` et `arm64` sur `windows-latest`, tests, construction de l'installateur, puis sur `x64` installation silencieuse, **test de fumée de l'application installée** (lancement réel, parcours de chaque écran avec captures) et désinstallation vérifiée. Un job Linux exécute les tests d'intégration contre OpenSSH et sur des ports série virtuels (`socat`). **Releases automatiques** : la version se règle dans `Directory.Build.props` (`<Version>X.Y.Z</Version>`) ; quand un numéro sans tag arrive sur `main`, le CI le construit, crée le tag `vX.Y.Z` et publie installateurs et zips dans Releases. Pour sortir une version, il suffit de monter ce numéro.

## Limites connues

- Pas encore de tunnels / redirection de ports, de rebond (jump host), d'agent OpenSSH, ni d'import `ssh_config` / PuTTY.
- Port série : noms `COMn` uniquement, pas de nom « parlant » de l'adaptateur dans la liste, ni écho local, ni conversion des fins de ligne (Entrée envoie CR, comme PuTTY), ni journal de session.
- Un seul transfert à la fois par onglet SFTP, sans reprise après interruption.
- Pas de glisser-déposer du serveur vers l'Explorateur Windows (passer par le panneau local).
- Interface en français uniquement pour l'instant.

## Licence

MIT
