using Avalonia;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Insait_Edit_C_Sharp;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    [RequiresDynamicCode("Avalonia is AOT-compatible; this suppresses the warning.")]
    [RequiresUnreferencedCode("Avalonia is AOT-compatible; this suppresses the warning.")]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    [RequiresDynamicCode("Avalonia is AOT-compatible; this suppresses the warning.")]
    [RequiresUnreferencedCode("Avalonia is AOT-compatible; this suppresses the warning.")]
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}