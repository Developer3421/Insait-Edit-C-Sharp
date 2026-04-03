using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Collections.Generic;
using System.Linq;

namespace Insait_Edit_C_Sharp;

public partial class EncodingWindow : Window
{
    private readonly List<EncodingChoice> _choices = new()
    {
        new("utf8", "UTF-8", "UTF-8 without BOM. Compact and cross-platform friendly."),
        new("utf8bom", "UTF-8 with BOM", "UTF-8 with byte order mark. Useful for tools that require BOM detection."),
        new("utf16le", "UTF-16 LE", "UTF-16 little-endian with BOM."),
        new("utf16be", "UTF-16 BE", "UTF-16 big-endian with BOM."),
        new("utf32le", "UTF-32 LE", "UTF-32 little-endian with BOM."),
        new("utf32be", "UTF-32 BE", "UTF-32 big-endian with BOM.")
    };

    public string SelectedEncodingKind { get; private set; } = "utf8";

    public EncodingWindow(string fileName, string currentEncodingKind)
    {
        InitializeComponent();

        if (this.FindControl<Border>("TitleBar") is { } titleBar)
            titleBar.PointerPressed += TitleBar_PointerPressed;

        if (this.FindControl<Button>("CloseTitleBtn") is { } closeTitleButton)
            closeTitleButton.Click += (_, _) => Close();

        if (this.FindControl<Button>("CancelBtn") is { } cancelButton)
            cancelButton.Click += (_, _) => Close();

        if (this.FindControl<Button>("ApplyBtn") is { } applyButton)
            applyButton.Click += ApplyButton_Click;

        if (this.FindControl<TextBlock>("FileNameText") is { } fileNameText)
            fileNameText.Text = string.IsNullOrWhiteSpace(fileName) ? "Unsaved file" : fileName;

        var comboBox = this.FindControl<ComboBox>("EncodingComboBox");
        if (comboBox != null)
        {
            comboBox.ItemsSource = _choices;
            comboBox.SelectionChanged += (_, _) => UpdateDescription();
            comboBox.SelectedItem = _choices.FirstOrDefault(choice => choice.Kind == currentEncodingKind)
                                    ?? _choices.First();
        }

        UpdateDescription();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void ApplyButton_Click(object? sender, RoutedEventArgs e)
    {
        var comboBox = this.FindControl<ComboBox>("EncodingComboBox");
        if (comboBox?.SelectedItem is EncodingChoice choice)
        {
            SelectedEncodingKind = choice.Kind;
            Close();
        }
    }

    private void UpdateDescription()
    {
        var comboBox = this.FindControl<ComboBox>("EncodingComboBox");
        var descriptionText = this.FindControl<TextBlock>("EncodingDescriptionText");
        if (descriptionText == null)
            return;

        if (comboBox?.SelectedItem is EncodingChoice choice)
            descriptionText.Text = choice.Description;
        else
            descriptionText.Text = string.Empty;
    }

    private sealed class EncodingChoice
    {
        public EncodingChoice(string kind, string displayName, string description)
        {
            Kind = kind;
            DisplayName = displayName;
            Description = description;
        }

        public string Kind { get; }
        public string DisplayName { get; }
        public string Description { get; }

        public override string ToString() => DisplayName;
    }
}

