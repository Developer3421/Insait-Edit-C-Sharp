using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Insait_Edit_C_Sharp.Services;
using System.IO;
using System.Linq;

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
            // Check if a file path was passed via command-line args (e.g. Windows "Open With")
            var fileArg = desktop.Args?
                .FirstOrDefault(a => !a.StartsWith("-") && File.Exists(a));

            if (!string.IsNullOrEmpty(fileArg))
            {
                // Open MainWindow directly with the single file in Zen mode
                var mainWindow = new MainWindow(null, singleFilePath: fileArg);
                desktop.MainWindow = mainWindow;
            }
            else
            {
                // Start with Welcome Window (like JetBrains Rider)
                desktop.MainWindow = new WelcomeWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}