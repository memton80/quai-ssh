using Quai.Core.Models;
using Quai.Core.Tests.Support;
using Quai.Core.ViewModels;

namespace Quai.Core.Tests.ViewModels;

public class ConnectionEditorViewModelTests
{
    private static ConnectionEditorViewModel NewEditor() => new(new KeyStrings())
    {
        Host = "example.org",
        Username = "alice",
    };

    [Fact]
    public void A_new_editor_has_sensible_defaults()
    {
        var editor = new ConnectionEditorViewModel(new KeyStrings());

        Assert.True(editor.IsNew);
        Assert.Equal(22, editor.Port);
        Assert.True(editor.IsPasswordAuthentication);
        Assert.True(editor.SavePassword);
        Assert.Equal("Editor_TitleNew", editor.Title);
    }

    [Fact]
    public void Valid_input_passes_validation()
    {
        var editor = NewEditor();

        Assert.True(editor.Validate());
        Assert.False(editor.HasError);
    }

    [Theory]
    [InlineData("", "alice", 22.0, "Editor_ErrorHostRequired")]
    [InlineData("bad host", "alice", 22.0, "Editor_ErrorHostInvalid")]
    [InlineData("example.org", " ", 22.0, "Editor_ErrorUserRequired")]
    [InlineData("example.org", "alice", 0.0, "Editor_ErrorPortRange")]
    [InlineData("example.org", "alice", 70000.0, "Editor_ErrorPortRange")]
    [InlineData("example.org", "alice", 22.5, "Editor_ErrorPortRange")]
    [InlineData("example.org", "alice", double.NaN, "Editor_ErrorPortRange")]
    public void Invalid_input_is_reported(string host, string user, double port, string expectedError)
    {
        var editor = NewEditor();
        editor.Host = host;
        editor.Username = user;
        editor.Port = port;

        Assert.False(editor.Validate());
        Assert.Contains(expectedError, editor.ErrorMessage);
    }

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("::1")]
    [InlineData("srv-01.intra.example")]
    public void Ip_addresses_and_host_names_are_accepted(string host)
    {
        var editor = NewEditor();
        editor.Host = host;

        Assert.True(editor.Validate());
    }

    [Fact]
    public void Private_key_authentication_requires_a_key_file()
    {
        var editor = NewEditor();
        editor.AuthenticationIndex = (int)AuthenticationKind.PrivateKey;

        Assert.False(editor.Validate());
        Assert.Contains("Editor_ErrorKeyRequired", editor.ErrorMessage);

        editor.PrivateKeyPath = "C:\\keys\\id_ed25519";
        Assert.True(editor.Validate());
    }

    [Fact]
    public void BuildProfile_maps_every_field_and_keeps_the_identity_when_editing()
    {
        var existing = new ConnectionProfile { Name = "Ancien", Host = "old.example.org", Username = "bob" };
        var editor = new ConnectionEditorViewModel(new KeyStrings(), existing)
        {
            Name = " Nouveau ",
            Host = "new.example.org",
            Port = 2200,
            Username = "carol",
            AuthenticationIndex = (int)AuthenticationKind.PrivateKey,
            PrivateKeyPath = "C:\\keys\\id_rsa",
            Folder = "Prod\\Web",
            TagsText = "web, prod",
            Notes = "note",
            Password = "ne-doit-pas-sortir",
            Passphrase = "non-plus",
        };

        var profile = editor.BuildProfile();

        Assert.False(editor.IsNew);
        Assert.Equal(existing.Id, profile.Id);
        Assert.Equal("Nouveau", profile.Name);
        Assert.Equal("new.example.org", profile.Host);
        Assert.Equal(2200, profile.Port);
        Assert.Equal("carol", profile.Username);
        Assert.Equal(AuthenticationKind.PrivateKey, profile.Authentication);
        Assert.Equal("C:\\keys\\id_rsa", profile.PrivateKeyPath);
        Assert.Equal("Prod/Web", profile.Folder);
        Assert.Equal(["web", "prod"], profile.Tags);
        Assert.Equal("note", profile.Notes);
    }

    [Fact]
    public void The_key_path_is_dropped_for_password_authentication()
    {
        var editor = NewEditor();
        editor.PrivateKeyPath = "C:\\keys\\id_rsa";
        editor.AuthenticationIndex = (int)AuthenticationKind.Password;

        Assert.Null(editor.BuildProfile().PrivateKeyPath);
    }

    [Fact]
    public void Stored_secrets_change_the_placeholders()
    {
        var existing = new ConnectionProfile { Host = "h", Username = "u" };
        var editor = new ConnectionEditorViewModel(new KeyStrings(), existing, hasStoredPassword: true);

        Assert.Equal("Editor_SecretUnchanged", editor.PasswordPlaceholder);
        Assert.Equal("Editor_PassphrasePlaceholder", editor.PassphrasePlaceholder);
        Assert.True(editor.SavePassword);
    }
}
