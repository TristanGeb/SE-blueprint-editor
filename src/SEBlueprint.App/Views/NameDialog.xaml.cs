using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SEBlueprint.App.Views;

/// <summary>Asks for a new blueprint name and checks it's usable as a folder name and not taken.</summary>
public partial class NameDialog : Window
{
    private readonly string _parentFolder;

    public NameDialog(string parentFolder, string suggestedName)
    {
        InitializeComponent();
        _parentFolder = parentFolder;
        NameBox.Text = suggestedName;
        NameBox.SelectAll();
    }

    public string BlueprintName => NameBox.Text.Trim();

    private string? Validate()
    {
        var name = BlueprintName;
        if (name.Length == 0)
            return "Enter a name.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
            return "The name can't contain any of these characters:  \\ / : * ? \" < > |";
        if (Directory.Exists(Path.Combine(_parentFolder, name)))
            return "A blueprint with this name already exists.";
        return null;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        var error = Validate();
        ErrorText.Text = error ?? string.Empty;
        ErrorText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        OkButton.IsEnabled = error is null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Validate() is null)
            DialogResult = true;
    }
}
