# Quai

Gestionnaire de connexions **SSH** et de transferts **SFTP** pour Windows, avec une interface native **WinUI 3** (look Windows 11).

> Statut : projet en démarrage. Le développement est piloté avec Claude Code, voir [`CLAUDE.md`](CLAUDE.md).

## Objectifs

- Carnet de connexions (dossiers, tags, recherche)
- Terminal SSH à onglets
- Explorateur SFTP à double panneau (local / distant)
- Secrets stockés dans le coffre Windows, jamais en clair
- Compilation et releases 100 % via GitHub Actions

## Principe

On ne réinvente pas la roue : le protocole SSH/SFTP, l'émulation de terminal et le MVVM reposent sur des bibliothèques existantes (SSH.NET, xterm.js, CommunityToolkit). Le détail est dans `CLAUDE.md`.

## Télécharger

Les builds se trouvent dans l'onglet **Actions** (artefacts) et, pour les versions taguées `vX.Y.Z`, dans **Releases**.

## Licence

MIT
