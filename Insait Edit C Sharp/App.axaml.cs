using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Load the default language dictionary from AXAML resources
        LocalizationService.Initialize();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Start with Welcome Window (like JetBrains Rider)
            desktop.MainWindow = new WelcomeWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}