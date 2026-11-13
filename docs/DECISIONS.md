# Décisions techniques

Une ligne par décision : date, choix, raison.

- 2026-11-13 : SSH/SFTP via SSH.NET, terminal via xterm.js dans WebView2, MVVM via CommunityToolkit.Mvvm — éviter de réécrire protocole et émulation de terminal.
- 2026-11-13 : .NET 10 (`net10.0`, SDK `10.0.x`) — dernière LTS active (.NET 8 est en fin de vie depuis le 2026-11-10, .NET 11 est une STS) ; le Windows App SDK stable (2.6.5) cible `net6.0-windows10.0.17763.0` et s'utilise donc tel quel avec .NET 10.
- 2026-11-13 : Windows App SDK 2.6.5 via le métapaquet `Microsoft.WindowsAppSDK` (et non les paquets composants un à un) — configuration officielle, moins de risque pour le mode non packagé auto-contenu, au prix de ~18 Mo inutilisés (IA, recherche, widgets).
- 2026-11-13 : `TargetFramework` de l'app = `net10.0-windows10.0.19041.0`, minimum Windows 10 1809 (17763) — valeurs du modèle WinUI 3 ; Mica n'apparaît que sous Windows 11.
- 2026-11-13 : tests avec `xunit.v3` sur Microsoft.Testing.Platform (`global.json` → `test.runner`) — le SDK .NET 10 n'accepte plus VSTest pour xunit.v3 ; commande : `dotnet test --project …`.
- 2026-11-13 : versions NuGet centralisées dans `Directory.Packages.props` — un seul endroit à mettre à jour.
- 2026-11-13 : chaînes de l'interface via une extension XAML `{l:Str Key=…}` adossée aux `.resw` plutôt que `x:Uid` — une clé absente affiche la clé au lieu de planter l'app au chargement de la page, et ça marche sur les propriétés attachées (accessibilité, info-bulles).
- 2026-11-13 : les pages sont créées une fois et masquées/affichées (pas de `Frame.Navigate`) — les terminaux et sessions SFTP ouverts survivent au changement de page.
- 2026-11-13 : mode `Quai.exe --smoke-test <dossier>` exécuté par le CI (x64) — seule façon de vérifier que l'app publiée démarre réellement et que chaque page se charge, faute de pouvoir la lancer à la main.
- 2026-11-13 : pas de trimming ni de fichier unique à la publication — WinUI 3 et les liaisons XAML s'appuient sur la réflexion ; un zip à décompresser suffit.
