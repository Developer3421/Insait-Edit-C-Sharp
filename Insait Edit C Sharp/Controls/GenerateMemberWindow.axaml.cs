using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// Result returned by <see cref="GenerateMemberWindow"/> when a member kind is chosen.
/// Contains the generated code snippet and the target type name for insertion.
/// </summary>
public sealed class GenerateMemberResult
{
    public string Code     { get; init; } = string.Empty;
    public string TypeName { get; init; } = string.Empty;
}

/// <summary>
/// Dialog window for choosing which member to generate on a type
/// (property, method, async method, field, event).
/// Shown when user writes obj.Something and Something doesn't exist (CS1061).
/// </summary>
public partial class GenerateMemberWindow : Window
{
    private readonly string _typeName;
    private readonly string _memberName;

    /// <summary>Fires when user chose a member kind. Carries the generated code text.</summary>
    public event EventHandler<GenerateMemberResult>? MemberGenerated;

    public GenerateMemberWindow() : this("MyClass", "MyMember") { }

    public GenerateMemberWindow(string typeName, string memberName)
    {
        _typeName = typeName;
        _memberName = memberName;
        AvaloniaXamlLoader.Load(this);

        this.FindControl<TextBlock>("TitleText")!.Text =
            $"Generate Member — {typeName}.{memberName}";
        this.FindControl<TextBlock>("SubtitleText")!.Text =
            $"Generate '{memberName}' on type '{typeName}':";

        this.FindControl<TextBlock>("LblProperty")!.Text    = $"Property  {memberName}";
        this.FindControl<TextBlock>("LblMethod")!.Text      = $"Method  {memberName}()";
        this.FindControl<TextBlock>("LblAsyncMethod")!.Text = $"Async Method  {memberName}Async()";
        this.FindControl<TextBlock>("LblField")!.Text       = $"Field  _{ToCamel(memberName)}";
        this.FindControl<TextBlock>("LblEvent")!.Text       = $"Event  {memberName}";

        this.FindControl<Button>("CloseBtn")!.Click       += (_, _) => Close();
        this.FindControl<Button>("BtnProperty")!.Click    += OnMemberClicked;
        this.FindControl<Button>("BtnMethod")!.Click      += OnMemberClicked;
        this.FindControl<Button>("BtnAsyncMethod")!.Click += OnMemberClicked;
        this.FindControl<Button>("BtnField")!.Click       += OnMemberClicked;
        this.FindControl<Button>("BtnEvent")!.Click       += OnMemberClicked;
    }

    private void OnMemberClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string kind) return;

        var code = kind switch
        {
            "property"    => $"\n    public object {_memberName} {{ get; set; }}\n",
            "method"      => $"\n    public void {_memberName}()\n    {{\n        throw new NotImplementedException();\n    }}\n",
            "asyncmethod" => $"\n    public async Task {_memberName}Async()\n    {{\n        throw new NotImplementedException();\n    }}\n",
            "field"       => $"\n    private object _{ToCamel(_memberName)};\n",
            "event"       => $"\n    public event EventHandler? {_memberName};\n",
            _             => $"\n    public object {_memberName} {{ get; set; }}\n",
        };

        MemberGenerated?.Invoke(this, new GenerateMemberResult
        {
            Code = code,
            TypeName = _typeName,
        });
        Close();
    }

    private static string ToCamel(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

