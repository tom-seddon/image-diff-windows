using System.Globalization;

namespace ImageDiff.Core;

public enum ViewMode
{
    Difference,
    SideBySide,
    Slider,
}

public sealed class CommandLineOptions
{
    public string? LeftPath { get; set; }
    public string? RightPath { get; set; }
    public ViewMode Mode { get; set; } = ViewMode.Difference;
    public int Tolerance { get; set; }
    public string? OutputPath { get; set; }
    public bool Show { get; set; }
    public bool ShowHelp { get; set; }
}

public static class CommandLine
{
    public const string GuiUsage =
        """
        Usage: ImageDiff [left-image] [right-image] [options]

        Opens the comparison window. With no images it opens empty; pick images with
        Browse or drag them in. With two images the comparison is already set up.

        Options:
          -m, --mode <diff|side|slider>  View to open in (default: diff)
          -t, --tolerance <0-255>        Ignore channel differences up to this amount (default: 0)
          -o, --output <file.png>        Also save the highlighted difference image
          -h, --help                     Show this help
        """;

    public const string CliUsage =
        """
        Usage: imgdiff <left-image> <right-image> [options]

        Compares two images and prints the result without opening a window.
        Exit code: 0 identical, 1 different, 2 error.

        Options:
          -t, --tolerance <0-255>        Ignore channel differences up to this amount (default: 0)
          -o, --output <file.png>        Save the highlighted difference image
          -s, --show                     Also open the comparison window (ImageDiff.exe)
          -m, --mode <diff|side|slider>  View the window opens in, with --show (default: diff)
          -h, --help                     Show this help
        """;

    /// <summary>Parses arguments. Throws <see cref="CommandLineException"/> on invalid input.</summary>
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        var options = new CommandLineOptions();
        var positional = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "-h":
                case "--help":
                case "/?":
                    options.ShowHelp = true;
                    break;
                case "-m":
                case "--mode":
                    options.Mode = ParseMode(TakeValue(args, ref i, arg));
                    break;
                case "-t":
                case "--tolerance":
                    options.Tolerance = ParseTolerance(TakeValue(args, ref i, arg));
                    break;
                case "-o":
                case "--output":
                    options.OutputPath = TakeValue(args, ref i, arg);
                    break;
                case "-s":
                case "--show":
                    options.Show = true;
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal) || (arg.StartsWith('-') && arg.Length > 1))
                        throw new CommandLineException($"Unknown option '{arg}'.");
                    positional.Add(arg);
                    break;
            }
        }

        if (positional.Count > 2)
            throw new CommandLineException($"Expected at most two image paths, got {positional.Count}.");
        if (positional.Count > 0) options.LeftPath = positional[0];
        if (positional.Count > 1) options.RightPath = positional[1];

        return options;
    }

    /// <summary>Builds the GUI arguments for a comparison, so the CLI can hand it over to the window.</summary>
    public static IReadOnlyList<string> ToGuiArguments(CommandLineOptions options)
    {
        var args = new List<string>();
        if (options.LeftPath != null) args.Add(options.LeftPath);
        if (options.RightPath != null) args.Add(options.RightPath);
        args.Add("--mode");
        args.Add(options.Mode switch
        {
            ViewMode.SideBySide => "side",
            ViewMode.Slider => "slider",
            _ => "diff",
        });
        args.Add("--tolerance");
        args.Add(options.Tolerance.ToString(CultureInfo.InvariantCulture));
        return args;
    }

    public static ViewMode ParseMode(string value) => value.ToLowerInvariant() switch
    {
        "diff" or "difference" or "highlight" => ViewMode.Difference,
        "side" or "sidebyside" or "side-by-side" => ViewMode.SideBySide,
        "slider" or "swipe" => ViewMode.Slider,
        _ => throw new CommandLineException($"Unknown mode '{value}'. Use diff, side or slider."),
    };

    private static int ParseTolerance(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int t) || t < 0 || t > 255)
            throw new CommandLineException($"Tolerance must be a whole number from 0 to 255, got '{value}'.");
        return t;
    }

    private static string TakeValue(IReadOnlyList<string> args, ref int i, string option)
    {
        if (i + 1 >= args.Count)
            throw new CommandLineException($"Option '{option}' needs a value.");
        return args[++i];
    }
}

public sealed class CommandLineException(string message) : Exception(message);
