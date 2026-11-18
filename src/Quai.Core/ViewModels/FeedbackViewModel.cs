using CommunityToolkit.Mvvm.ComponentModel;
using Quai.Core.Feedback;
using Quai.Core.Localization;

namespace Quai.Core.ViewModels;

/// <summary>
/// Formulaire « Proposer une idée ou signaler un bug » : rempli dans Quai, il ouvre ensuite la page GitHub d'une
/// nouvelle issue avec tout déjà saisi. Rien ne part sans que l'utilisateur valide lui-même sur GitHub.
/// </summary>
public sealed partial class FeedbackViewModel : ObservableObject
{
    /// <summary>Identifiants des champs des formulaires <c>.github/ISSUE_TEMPLATE/idee.yml</c> et <c>bug.yml</c>.</summary>
    public const string DescriptionField = "description";
    public const string AreaField = "domaine";
    public const string StepsField = "etapes";
    public const string SystemField = "systeme";

    private static readonly string[] AreaKeys =
    [
        "Feedback_AreaConnections",
        "Feedback_AreaTerminal",
        "Feedback_AreaFiles",
        "Feedback_AreaSnippets",
        "Feedback_AreaSettings",
        "Feedback_AreaOther",
    ];

    private readonly IStrings _strings;

    public FeedbackViewModel(IStrings strings, string systemInfo, FeedbackKind kind = FeedbackKind.Idea)
    {
        _strings = strings;
        SystemInfo = systemInfo;
        Areas = [.. AreaKeys.Select(strings.Get)];
        KindIndex = kind == FeedbackKind.Bug ? 1 : 0;
        AreaIndex = -1;
        Title = string.Empty;
        Description = string.Empty;
        Steps = string.Empty;
        IncludeSystemInfo = true;
        ErrorMessage = string.Empty;
    }

    /// <summary>0 : idée, 1 : bug (index du choix dans le formulaire).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Kind), nameof(IsIdea), nameof(IsBug), nameof(TitleHeader), nameof(TitlePlaceholder),
        nameof(DescriptionHeader), nameof(DescriptionPlaceholder))]
    public partial int KindIndex { get; set; }

    public FeedbackKind Kind => KindIndex == 1 ? FeedbackKind.Bug : FeedbackKind.Idea;

    public bool IsIdea => Kind == FeedbackKind.Idea;

    public bool IsBug => Kind == FeedbackKind.Bug;

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; }

    /// <summary>Étapes pour reproduire (bug).</summary>
    [ObservableProperty]
    public partial string Steps { get; set; }

    /// <summary>Partie de Quai concernée (idée) ; -1 si rien n'est choisi (facultatif).</summary>
    [ObservableProperty]
    public partial int AreaIndex { get; set; }

    public IReadOnlyList<string> Areas { get; }

    /// <summary>Joindre <see cref="SystemInfo"/> (affiché tel quel dans le formulaire).</summary>
    [ObservableProperty]
    public partial bool IncludeSystemInfo { get; set; }

    public string SystemInfo { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; }

    public bool HasError => ErrorMessage.Length > 0;

    public string TitleHeader => _strings.Get(IsBug ? "Feedback_BugTitle" : "Feedback_IdeaTitle");

    public string TitlePlaceholder => _strings.Get(IsBug ? "Feedback_BugTitlePlaceholder" : "Feedback_IdeaTitlePlaceholder");

    public string DescriptionHeader => _strings.Get(IsBug ? "Feedback_BugDescription" : "Feedback_IdeaDescription");

    public string DescriptionPlaceholder =>
        _strings.Get(IsBug ? "Feedback_BugDescriptionPlaceholder" : "Feedback_IdeaDescriptionPlaceholder");

    /// <summary>Vérifie le formulaire ; en cas d'erreur, <see cref="ErrorMessage"/> l'explique.</summary>
    public bool Validate()
    {
        ErrorMessage = string.IsNullOrWhiteSpace(Title)
            ? _strings.Get("Feedback_TitleRequired")
            : string.IsNullOrWhiteSpace(Description)
                ? _strings.Get("Feedback_DescriptionRequired")
                : string.Empty;
        return !HasError;
    }

    /// <summary>Adresse de la nouvelle issue GitHub, avec les champs du formulaire choisi.</summary>
    public FeedbackLink BuildLink()
    {
        List<FeedbackField> fields = [new(DescriptionField, DescriptionHeader, Description)];
        if (IsBug)
        {
            fields.Add(new(StepsField, _strings.Get("Feedback_BugSteps"), Steps));
        }
        else if (AreaIndex >= 0 && AreaIndex < Areas.Count)
        {
            fields.Add(new(AreaField, _strings.Get("Feedback_IdeaArea"), Areas[AreaIndex], CanShorten: false));
        }

        if (IncludeSystemInfo)
        {
            fields.Add(new(SystemField, _strings.Get("Feedback_SystemHeading"), SystemInfo, CanShorten: false));
        }

        return FeedbackIssue.Build(Kind, Title, fields);
    }
}
