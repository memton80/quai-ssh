# Guide de Quai

Tout ce que Quai sait faire, en détail. Pour une présentation rapide, voir le [README](../README.md).

## Fonctionnalités

**Carnet de connexions**
- Serveurs SSH et ports série dans le même carnet.
- Créer, modifier, dupliquer, supprimer ; rangement par dossiers (`Prod/Web`, repliables d'un clic sur le chevron ou avec les flèches ← →) et par tags ; recherche instantanée (nom, hôte, utilisateur, port série, tag, notes — sans tenir compte des accents).
- Authentification par mot de passe ou par clé privée (OpenSSH, PEM, PuTTY ; passphrase prise en charge).
- **Connexion rapide** : tapez `utilisateur@hôte:port` dans le champ en haut du carnet puis `Entrée`, sans créer de fiche (sont aussi acceptés `hôte`, `[::1]:22`, `ssh://…` ou une commande collée comme `ssh -p 2222 alice@serveur`). Utilisateur oublié : Quai le demande. Une fois connecté, l'onglet propose **« Ajouter au carnet… »**. Aucun mot de passe n'est mémorisé pour une connexion rapide tant qu'elle n'est pas dans le carnet.

**Terminal SSH**
- Onglets, moteur natif de **Windows Terminal** (le même que l'application de Microsoft : rendu DirectWrite / Direct3D, couleurs, UTF-8, redimensionnement), reconnexion en un clic. Aucun navigateur embarqué.
- **Reconnexion automatique** après une coupure (réseau, Wi-Fi, mise en veille) : six tentatives espacées sur deux minutes environ, compte à rebours affiché, « Reconnecter » pour ne pas attendre, « Arrêter les tentatives » pour abandonner. Un `exit` normal n'est jamais relancé ; un mot de passe refusé arrête les tentatives. L'historique affiché reste en place. Désactivable dans les réglages, comme le **maintien de la connexion** (keepalive, 30 s par défaut).
- **Snippets** : commandes enregistrées (page « Snippets »), envoyées dans le terminal avec **`Ctrl+Maj+P`**, le bouton « Snippets » de la barre d'onglets ou le clic droit sur l'onglet. Filtre au clavier, `Entrée` envoie. Un snippet peut servir à toutes les connexions ou à une seule ; plusieurs lignes = plusieurs commandes ; « Exécuter tout de suite » ajoute `Entrée` à la fin. Ils sont enregistrés en clair : n'y mettez pas de mot de passe.
- **SFTP dans le dossier du shell** : le bouton « Ouvrir le SFTP dans ce dossier » (barre d'onglets ou clic droit) ouvre l'explorateur là où vous êtes dans le terminal, sans rien taper. Quai lit le dossier annoncé par le shell (séquence OSC 7) ou, à défaut, celui du shell lui-même par un canal séparé de la même connexion (serveurs Linux) ; sinon, le dossier personnel. Un explorateur déjà ouvert sur la même connexion est réutilisé.
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
- **Modifier un fichier du serveur dans votre éditeur** (comme WinSCP) : clic droit → « Modifier » (`F4`) ouvre le fichier dans l'application associée (Bloc-notes, VS Code…), « Modifier avec… » laisse choisir. Chaque enregistrement est renvoyé sur le serveur, à sa place (droits conservés). Si le fichier a changé sur le serveur entre-temps, Quai demande avant de l'écraser ; si la connexion est coupée, la modification part à la reconnexion. Le suivi dure tant que l'onglet SFTP reste ouvert (bandeau en haut de l'explorateur) ; la copie locale est ensuite effacée.
- Clavier : `Entrée` ouvre, `Retour arrière` remonte, `F2` renomme, `F4` modifie, `F5` actualise, `Suppr` supprime.

**Réglages**
- Thème clair / sombre / identique à Windows (fond Mica sous Windows 11), fond « Fluent » du terminal (couleur de surface de Windows 11, comme l'onglet actif) ou fond classique de Windows Terminal, police et taille du terminal, appliqués immédiatement.
- Connexions SSH : reconnexion automatique (activée par défaut) et intervalle du maintien de connexion (keepalive, 5 à 600 s, 0 = désactivé ; s'applique aux nouvelles connexions).

**Fermeture et mises à jour**
- Fermer la fenêtre avec des sessions ouvertes demande confirmation (sessions et transferts en cours listés), puis chaque connexion est fermée proprement : déconnexion SSH, transferts interrompus, fichiers incomplets supprimés.
- Au démarrage, un bandeau signale qu'une nouvelle version est publiée dans les Releases GitHub (désactivable dans les réglages). **« Mettre à jour »** fait tout, comme un navigateur : téléchargement de l'installateur (progression, annulable), vérification de son empreinte SHA-256, puis Quai se ferme (en demandant confirmation s'il reste des sessions ouvertes), s'installe en silencieux et redémarre tout seul. Version portable (zip) : le bouton ouvre la page de la release.

## Sécurité

- **Secrets** (mots de passe, passphrases) : uniquement dans le coffre Windows (Gestionnaire d'identification). Jamais dans un fichier, un journal ou un message d'erreur.
- **Clé d'hôte** toujours vérifiée : confiance au premier contact (l'empreinte SHA-256 vous est montrée avant toute saisie de mot de passe), puis alerte explicite si elle change. La vérification ne se désactive pas.
- **Aucune télémétrie**, aucun navigateur embarqué : le terminal est un composant natif. Seule connexion en dehors de vos serveurs : au démarrage, la lecture du numéro de la dernière version sur l'API publique de GitHub (sans compte, rien sur vous ni sur vos connexions) ; désactivable dans les réglages. L'installateur d'une mise à jour n'est téléchargé (depuis GitHub) que si vous cliquez sur « Mettre à jour », et il n'est exécuté que si son empreinte correspond à celle publiée avec la release.

## Télécharger et installer

Rendez-vous dans [**Releases**](https://github.com/memton80/quai-ssh/releases/latest) :

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
| `snippets.json` | Les snippets (en clair). |
| `settings.json` | Les réglages. |
| `known_hosts` | Les clés d'hôte acceptées (format OpenSSH). |
| `logs\` | Journaux des 7 derniers jours (et celui de la dernière mise à jour). |
| `edits\` | Copies locales des fichiers distants ouverts pour modification (effacées à la fermeture de l'onglet SFTP). |

## Limites connues

- Pas encore de tunnels / redirection de ports, de rebond (jump host), d'agent OpenSSH, ni d'import `ssh_config` / PuTTY.
- Port série : noms `COMn` uniquement, pas de nom « parlant » de l'adaptateur dans la liste, ni écho local, ni conversion des fins de ligne (Entrée envoie CR, comme PuTTY), ni journal de session.
- Un seul transfert à la fois par onglet SFTP, sans reprise après interruption.
- Connexion rapide : mot de passe uniquement (pour une clé privée, ajoutez la connexion au carnet). Pas de reconnexion automatique pour un port série.
- « Ouvrir le SFTP dans ce dossier » sans annonce du shell : serveurs Linux seulement (lecture de `/proc`) ; ailleurs, ou dans un shell `sudo`, l'explorateur s'ouvre dans le dossier personnel ou celui du dernier shell lisible.
- Pas de glisser-déposer du serveur vers l'Explorateur Windows (passer par le panneau local).
- Interface en français uniquement pour l'instant.
