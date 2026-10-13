using System.Diagnostics;
using System.IO;
using ImageDiff;
using ImageDiff.Core;

const int Identical = 0, Different = 1, Error = 2;

CommandLineOptions options;
try
{
    options = CommandLine.Parse(args);
}
catch (CommandLineException ex)
{
    Console.Error.WriteLine($"imgdiff: {ex.Message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(CommandLine.CliUsage);
    return Error;
}

if (options.ShowHelp)
{
    Console.WriteLine($"imgdiff {AppVersion.Current}");
    Console.WriteLine();
    Console.WriteLine(CommandLine.CliUsage);
    return Identical;
}

if (options.LeftPath == null || options.RightPath == null)
{
    Console.Error.WriteLine("imgdiff: two image paths are needed.");
    Console.Error.WriteLine();
    Console.Error.WriteLine(CommandLine.CliUsage);
    return Error;
}

DiffResult diff;
try
{
    var left = ImageFile.Load(options.LeftPath);
    var right = ImageFile.Load(options.RightPath);
    diff = ImageComparer.Compare(left, right, options.Tolerance);

    Console.WriteLine($"Left:  {options.LeftPath} ({left.Width}x{left.Height})");
    Console.WriteLine($"Right: {options.RightPath} ({right.Width}x{right.Height})");
    Console.WriteLine(diff.Summary);

    if (options.OutputPath != null)
    {
        ImageFile.SavePng(ImageComparer.RenderHighlight(diff, left), options.OutputPath);
        Console.WriteLine($"Difference image saved to {options.OutputPath}");
    }
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
{
    Console.Error.WriteLine($"imgdiff: {ex.Message}");
    return Error;
}

if (options.Show)
{
    string gui = Path.Combine(AppContext.BaseDirectory, "ImageDiff.exe");
    var start = new ProcessStartInfo(gui) { UseShellExecute = false };
    foreach (string arg in CommandLine.ToGuiArguments(options))
        start.ArgumentList.Add(Path.IsPathRooted(arg) || !File.Exists(arg) ? arg : Path.GetFullPath(arg));
    try
    {
        Process.Start(start);
    }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
    {
        Console.Error.WriteLine($"imgdiff: could not open the window ({gui}): {ex.Message}");
    }
}

return diff.AreIdentical ? Identical : Different;
