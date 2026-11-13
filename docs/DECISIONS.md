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
- 2026-11-13 : `ISecretStore` défini dans `Quai.Core`, implémenté dans l'app avec `PasswordVault` (ressource « Quai ») — `PasswordVault` est une API WinRT, la garder hors de Core laisse Core portable et testable ; le modèle `ConnectionProfile` n'a aucun champ secret, le JSON ne peut donc pas en contenir.
- 2026-11-13 : view-models dans `Quai.Core` (CommunityToolkit.Mvvm ne dépend pas de WinUI) avec `IDialogService` / `IStrings` implémentés par l'app — toute la logique du carnet est couverte par des tests xUnit sans interface graphique.
- 2026-11-13 : dossiers = simple chemin « A/B » porté par chaque connexion, filtres dossiers / tags dans un volet à gauche (pas d'arborescence glisser-déposer) — sobre, et un dossier disparaît tout seul quand il est vide.
- 2026-11-13 : interface forcée en `fr-FR` (`ApplicationLanguages.PrimaryLanguageOverride`) tant que seul le français existe — sinon les textes fournis par WinUI (boutons, menus) restent dans la langue de Windows.
- 2026-11-13 : `known_hosts` propre à l'app (`%LOCALAPPDATA%\Quai\known_hosts`) au format OpenSSH non haché, écrit par nos soins — aucune lib NuGet maintenue ne gère ce fichier, le format tient en une ligne par hôte ; comparaison stricte : toute clé différente de celle enregistrée pour `hôte:port` = « empreinte modifiée ».
- 2026-11-13 : clé d'hôte inconnue ou modifiée → la connexion est refusée dans le rappel SSH.NET (`CanTrust = false`), puis on demande à l'utilisateur et on se reconnecte — pas de boîte de dialogue bloquant le fil réseau (délais d'expiration), et la vérification n'est jamais contournée : la seconde connexion repasse par la même comparaison.
- 2026-11-13 : sans mot de passe en coffre, une première connexion « sonde » sans authentification valide la clé d'hôte avant de demander le mot de passe — même ordre qu'OpenSSH : on ne saisit rien pour un serveur non vérifié.
- 2026-11-13 : empreinte modifiée = alerte rouge, « Annuler la connexion » par défaut ; « Remplacer la clé enregistrée » reste proposé (serveur réinstallé) mais demande un clic volontaire.
- 2026-11-13 : terminal = xterm.js 6 + addon fit copiés dans `Assets/Terminal/vendor` (pas de CDN, CSP `default-src 'none'`), servis à WebView2 par un hôte virtuel local ; échanges en messages JSON, sortie en base64 (octets bruts, l'UTF-8 est décodé par xterm.js).
- 2026-11-13 : copier / coller du terminal via le presse-papiers Windows côté C# (Ctrl+C avec sélection, Ctrl+V, clic droit, comme Windows Terminal) — évite les demandes d'autorisation du navigateur embarqué.
- 2026-11-13 : le shell est lu par une boucle `ShellStream.Read` (et non l'événement `DataReceived`) — sinon le tampon interne de SSH.NET grossit sans fin sur les longues sessions.
- 2026-11-13 : tests d'intégration SSH contre un vrai OpenSSH (`tests/integration/start-sshd.sh`, job `integration` sur `ubuntu-latest`) ; sans variables `QUAI_TEST_SSH_*` ils sont ignorés — le protocole est testé pour de vrai, pas simulé.
