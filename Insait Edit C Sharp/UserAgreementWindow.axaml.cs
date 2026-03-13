using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Insait_Edit_C_Sharp.Services;
using System;

namespace Insait_Edit_C_Sharp;

/// <summary>Window displaying the Insait Edit User Agreement / EULA.</summary>
public partial class UserAgreementWindow : Window
{
    private const string EulaText = """
        INSAIT EDIT — END USER LICENSE AGREEMENT (EULA)
        ════════════════════════════════════════════════

        Last updated: March 2026

        Please read this End User License Agreement ("Agreement") carefully before
        using Insait Edit ("the Software"). By installing or using the Software you
        agree to be bound by the terms below.

        1. LICENSE GRANT
        ─────────────────
        Subject to the terms of this Agreement, you are granted a limited,
        non-exclusive, non-transferable license to install and use the Software on
        devices you own or control, solely for your personal or internal business
        development purposes.

        2. RESTRICTIONS
        ─────────────────
        You may not:
          • Reverse-engineer, decompile, or disassemble the Software except to the
            extent permitted by applicable law.
          • Sell, rent, lease, sublicense, or redistribute the Software without
            written permission.
          • Remove or alter any proprietary notices or labels on the Software.

        3. INTELLECTUAL PROPERTY
        ─────────────────────────
        All title, ownership rights, and intellectual property rights in and to the
        Software remain with the Insait Edit authors. The Software is protected by
        copyright laws and international treaty provisions.

        4. THIRD-PARTY COMPONENTS
        ──────────────────────────
        The Software may include open-source components governed by their own
        licenses (e.g. AvaloniaUI — MIT License). Those components are provided
        under the terms of their respective licenses.

        5. DISCLAIMER OF WARRANTIES
        ────────────────────────────
        THE SOFTWARE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
        IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
        FITNESS FOR A PARTICULAR PURPOSE, AND NON-INFRINGEMENT.

        6. LIMITATION OF LIABILITY
        ───────────────────────────
        IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY INDIRECT, INCIDENTAL,
        SPECIAL, OR CONSEQUENTIAL DAMAGES ARISING OUT OF OR RELATING TO THIS
        AGREEMENT OR THE USE OF THE SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY
        OF SUCH DAMAGES.

        7. TERMINATION
        ─────────────────
        This license is effective until terminated. It will terminate automatically
        if you fail to comply with any term of this Agreement. Upon termination you
        must destroy all copies of the Software in your possession.

        8. GOVERNING LAW
        ─────────────────
        This Agreement shall be governed by and construed in accordance with
        applicable law, without regard to its conflict-of-law provisions.


        ════════════════════════════════════════════════
        Thank you for using Insait Edit!
        """;

    public UserAgreementWindow()
    {
        InitializeComponent();
        ApplyLocalization();
        LocalizationService.LanguageChanged += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(ApplyLocalization);
    }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;

        var title = this.FindControl<TextBlock>("TitleText");
        if (title != null) title.Text = L("Tooltip.UserAgreement");
        Title = L("Tooltip.UserAgreement");

        var footer = this.FindControl<TextBlock>("FooterText");
        if (footer != null) footer.Text = L("UserAgreement.Footer");

        var closeText = this.FindControl<TextBlock>("CloseBtnText");
        if (closeText != null) closeText.Text = L("UserAgreement.Close");

        var acceptText = this.FindControl<TextBlock>("AcceptBtnText");
        if (acceptText != null) acceptText.Text = L("UserAgreement.Accept");

        var agreementBlock = this.FindControl<SelectableTextBlock>("AgreementText");
        if (agreementBlock != null) agreementBlock.Text = EulaText;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void Accept_Click(object? sender, RoutedEventArgs e) => Close();
}

