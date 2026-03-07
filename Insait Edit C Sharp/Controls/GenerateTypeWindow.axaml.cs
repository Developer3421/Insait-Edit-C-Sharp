using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// Dialog window for choosing which type to generate (class, struct, interface, enum, record).
/// </summary>
public partial class GenerateTypeWindow : Window
{
    private readonly string _typeName;

    public event EventHandler<string>? TypeGenerated;

    public GenerateTypeWindow() : this("MyType") { }

    public GenerateTypeWindow(string typeName)
    {
        _typeName = typeName;
        AvaloniaXamlLoader.Load(this);

        var titleText = this.FindControl<TextBlock>("TitleText")!;
        titleText.Text = $"Generate Type — {typeName}";

        this.FindControl<TextBlock>("LblClass")!.Text     = $"Class  {typeName}";
        this.FindControl<TextBlock>("LblStruct")!.Text    = $"Struct  {typeName}";
        this.FindControl<TextBlock>("LblInterface")!.Text = $"Interface  I{typeName}";
        this.FindControl<TextBlock>("LblEnum")!.Text      = $"Enum  {typeName}";
        this.FindControl<TextBlock>("LblRecord")!.Text    = $"Record  {typeName}";

        this.FindControl<Button>("CloseBtn")!.Click     += (_, _) => Close();
        this.FindControl<Button>("BtnClass")!.Click     += OnTypeClicked;
        this.FindControl<Button>("BtnStruct")!.Click    += OnTypeClicked;
        this.FindControl<Button>("BtnInterface")!.Click += OnTypeClicked;
        this.FindControl<Button>("BtnEnum")!.Click      += OnTypeClicked;
        this.FindControl<Button>("BtnRecord")!.Click    += OnTypeClicked;
    }

    private void OnTypeClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string kind) return;

        var code = kind switch
        {
            "class"     => $"\n\npublic class {_typeName}\n{{\n    \n}}\n",
            "struct"    => $"\n\npublic struct {_typeName}\n{{\n    \n}}\n",
            "interface" => $"\n\npublic interface I{_typeName}\n{{\n    \n}}\n",
            "enum"      => $"\n\npublic enum {_typeName}\n{{\n    \n}}\n",
            "record"    => $"\n\npublic record {_typeName};\n",
            _           => $"\n\npublic class {_typeName}\n{{\n    \n}}\n",
        };

        TypeGenerated?.Invoke(this, code);
        Close();
    }
}
