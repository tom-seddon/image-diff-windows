using System.Windows;
using ImageDiff.Core;

namespace ImageDiff;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CommandLineOptions options;
        try
        {
            options = CommandLine.Parse(e.Args);
        }
        catch (CommandLineException ex)
        {
            MessageBox.Show(ex.Message + "\n\n" + CommandLine.GuiUsage, "Image Diff", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(2);
            return;
        }

        if (options.ShowHelp)
        {
            MessageBox.Show(CommandLine.GuiUsage, $"Image Diff {AppVersion.Current}", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        var window = new MainWindow(options);
        MainWindow = window;
        window.Show();
    }
}
