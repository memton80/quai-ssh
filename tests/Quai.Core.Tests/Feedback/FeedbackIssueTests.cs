using Quai.Core.Feedback;
using Quai.Core.Tests.Support;
using Quai.Core.ViewModels;

namespace Quai.Core.Tests.Feedback;

public class FeedbackIssueTests
{
    private const string NewIssue = "https://github.com/memton80/quai-ssh/issues/new?";

    [Fact]
    public void Short_idea_fills_the_idea_form()
    {
        var link = FeedbackIssue.Build(
            FeedbackKind.Idea,
            "Terminaux côte à côte",
            [new("description", "Description", "Voir deux serveurs en même temps."), new("domaine", "Partie", "Terminal", CanShorten: false)]);

        Assert.False(link.Shortened);
        Assert.Equal(
            NewIssue + "template=idee.yml&title=Terminaux%20c%C3%B4te%20%C3%A0%20c%C3%B4te"
                + "&description=Voir%20deux%20serveurs%20en%20m%C3%AAme%20temps.&domaine=Terminal",
            link.Address.OriginalString);
    }

    [Fact]
    public void Bug_uses_the_bug_form_and_skips_empty_fields()
    {
        var link = FeedbackIssue.Build(
            FeedbackKind.Bug,
            "Plantage",
            [new("description", "Description", "Ça plante."), new("etapes", "Étapes", "   ")]);

        Assert.StartsWith(NewIssue + "template=bug.yml&title=Plantage", link.Address.OriginalString);
        Assert.DoesNotContain("etapes=", link.Address.OriginalString);
    }

    [Fact]
    public void WinUI_line_breaks_become_line_feeds()
    {
        var link = FeedbackIssue.Build(FeedbackKind.Bug, "T", [new("etapes", "Étapes", "1. Ouvrir\r2. Fermer\r\n3. Fin")]);

        Assert.Contains("etapes=1.%20Ouvrir%0A2.%20Fermer%0A3.%20Fin", link.Address.OriginalString);
    }

    [Fact]
    public void Title_is_kept_on_one_line_and_capped()
    {
        Assert.Equal("Une idée sur deux lignes", FeedbackIssue.CleanTitle("  Une idée\r\nsur deux lignes \t"));
        Assert.Equal(FeedbackIssue.MaxTitleLength, FeedbackIssue.CleanTitle(new string('a', 500)).Length);
    }

    [Fact]
    public void Long_text_is_shortened_to_fit_and_the_full_text_is_kept()
    {
        var description = string.Concat(Enumerable.Repeat("Une très longue description, accentuée. ", 200));
        var link = FeedbackIssue.Build(
            FeedbackKind.Idea,
            "Idée",
            [new("description", "Ce que vous aimeriez", description), new("systeme", "Système", "Quai 1.1.0 · x64", CanShorten: false)]);

        Assert.True(link.Shortened);
        Assert.InRange(link.Address.OriginalString.Length, FeedbackIssue.MaxAddressLength - 60, FeedbackIssue.MaxAddressLength);
        Assert.Contains("%0A%0A%5B%E2%80%A6%5D", link.Address.OriginalString);

        // Les champs courts (système) ne sont jamais coupés.
        Assert.EndsWith("&systeme=Quai%201.1.0%20%C2%B7%20x64", link.Address.OriginalString);

        Assert.Equal("Idée\n\n### Ce que vous aimeriez\n" + description.Trim() + "\n\n### Système\nQuai 1.1.0 · x64", link.FullText);
    }

    [Fact]
    public void Longest_field_is_shortened_first()
    {
        var link = FeedbackIssue.Build(
            FeedbackKind.Bug,
            "Bug",
            [new("description", "D", "Court."), new("etapes", "É", new string('x', 5000))],
            maxLength: 600);

        Assert.True(link.Shortened);
        Assert.True(link.Address.OriginalString.Length <= 600);
        Assert.Contains("description=Court.", link.Address.OriginalString);
    }

    [Fact]
    public void Shortening_never_splits_an_emoji()
    {
        var text = string.Concat(Enumerable.Repeat("🚀", 2000));
        var link = FeedbackIssue.Build(FeedbackKind.Idea, "Fusée", [new("description", "D", text)], maxLength: 500);

        Assert.True(link.Shortened);

        // Une paire de substitution coupée donnerait une séquence UTF-8 invalide (exception au décodage).
        var description = Uri.UnescapeDataString(link.Address.OriginalString[(link.Address.OriginalString.IndexOf("description=", StringComparison.Ordinal) + 12)..]);
        Assert.DoesNotContain('�', description);
        Assert.EndsWith(FeedbackIssue.ShortenedMarker, description);
    }

    [Fact]
    public void System_description_names_Quai_and_its_version()
    {
        Assert.StartsWith($"Quai SSH {AppInfo.Version} · ", FeedbackIssue.DescribeSystem());
    }

    [Fact]
    public void View_model_requires_a_title_and_a_description()
    {
        var feedback = new FeedbackViewModel(new KeyStrings(), "Quai 1.1.0");

        Assert.False(feedback.Validate());
        Assert.Equal("Feedback_TitleRequired", feedback.ErrorMessage);

        feedback.Title = "Idée";
        Assert.False(feedback.Validate());
        Assert.Equal("Feedback_DescriptionRequired", feedback.ErrorMessage);

        feedback.Description = "Une description.";
        Assert.True(feedback.Validate());
        Assert.False(feedback.HasError);
    }

    [Fact]
    public void View_model_builds_the_idea_form_with_area_and_system()
    {
        var feedback = new FeedbackViewModel(new KeyStrings(), "Quai 1.1.0") { Title = "Idée", Description = "Texte", AreaIndex = 1 };

        var address = feedback.BuildLink().Address.OriginalString;

        Assert.StartsWith(NewIssue + "template=idee.yml", address);
        Assert.Contains("&domaine=Feedback_AreaTerminal", address);
        Assert.EndsWith("&systeme=Quai%201.1.0", address);
    }

    [Fact]
    public void View_model_builds_the_bug_form_without_system_when_unchecked()
    {
        var feedback = new FeedbackViewModel(new KeyStrings(), "Quai 1.1.0", FeedbackKind.Bug)
        {
            Title = "Bug",
            Description = "Texte",
            Steps = "1. Ouvrir",
            AreaIndex = 2,
            IncludeSystemInfo = false,
        };

        var address = feedback.BuildLink().Address.OriginalString;

        Assert.True(feedback.IsBug);
        Assert.StartsWith(NewIssue + "template=bug.yml", address);
        Assert.Contains("&etapes=1.%20Ouvrir", address);
        Assert.DoesNotContain("domaine=", address);
        Assert.DoesNotContain("systeme=", address);
    }

    [Fact]
    public void Switching_kind_updates_the_headers()
    {
        var feedback = new FeedbackViewModel(new KeyStrings(), "Quai");
        var changed = new List<string?>();
        feedback.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal("Feedback_IdeaTitle", feedback.TitleHeader);
        feedback.KindIndex = 1;

        Assert.Equal("Feedback_BugTitle", feedback.TitleHeader);
        Assert.Contains(nameof(FeedbackViewModel.IsBug), changed);
        Assert.Contains(nameof(FeedbackViewModel.DescriptionHeader), changed);
    }
}
