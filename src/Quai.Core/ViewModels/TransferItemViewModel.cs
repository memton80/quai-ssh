using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quai.Core.Sftp;

namespace Quai.Core.ViewModels;

public enum TransferDirection
{
    Upload,
    Download,
}

public enum TransferStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>Un transfert (fichier ou dossier entier) dans la file de l'explorateur.</summary>
public sealed partial class TransferItemViewModel : ObservableObject
{
    private readonly CancellationTokenSource _cancellation = new();

    /// <param name="cancellable">
    /// Faux pour le renvoi d'un fichier modifié : interrompu, il laisserait sur le serveur un fichier tronqué à la
    /// place de l'original.
    /// </param>
    public TransferItemViewModel(
        TransferDirection direction,
        string name,
        string source,
        string destinationDirectory,
        RemoteEntry? remote = null,
        bool cancellable = true)
    {
        Direction = direction;
        Name = name;
        Source = source;
        DestinationDirectory = destinationDirectory;
        Remote = remote;
        IsCancellable = cancellable;
        Status = TransferStatus.Pending;
        Detail = string.Empty;
        CancelCommand = new RelayCommand(Cancel, () => IsActive && IsCancellable);
    }

    public bool IsCancellable { get; }

    public TransferDirection Direction { get; }

    public string Name { get; }

    /// <summary>Chemin local (envoi) ou distant (récupération) de l'élément transféré.</summary>
    public string Source { get; }

    public string DestinationDirectory { get; }

    /// <summary>Entrée distante à récupérer (récupérations uniquement).</summary>
    public RemoteEntry? Remote { get; }

    public IRelayCommand CancelCommand { get; }

    public CancellationToken CancellationToken => _cancellation.Token;

    /// <summary>Glyphe Segoe Fluent Icons : envoi ou récupération.</summary>
    public string Glyph => Direction == TransferDirection.Upload ? "" : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(Percent))]
    public partial TransferStatus Status { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Percent))]
    public partial long TotalBytes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Percent))]
    public partial long TransferredBytes { get; set; }

    /// <summary>Texte d'état : « 12 Mo / 45 Mo », « Terminé », message d'erreur…</summary>
    [ObservableProperty]
    public partial string Detail { get; set; }

    /// <summary>En attente ou en cours : peut encore être annulé.</summary>
    public bool IsActive => Status is TransferStatus.Pending or TransferStatus.Running;

    public bool IsRunning => Status == TransferStatus.Running;

    public double Percent => Status == TransferStatus.Completed
        ? 100
        : TotalBytes <= 0 ? 0 : Math.Clamp(TransferredBytes * 100.0 / TotalBytes, 0, 100);

    public void Cancel()
    {
        if (!IsActive || !IsCancellable)
        {
            return;
        }

        _cancellation.Cancel();
        if (Status == TransferStatus.Pending)
        {
            Status = TransferStatus.Cancelled;
        }
    }

    partial void OnStatusChanged(TransferStatus value) => CancelCommand.NotifyCanExecuteChanged();
}
