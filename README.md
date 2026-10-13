# Image Diff

A Windows tool for comparing two images. It has a desktop window and a command line, and a
comparison can be set up from either.

## The window: `ImageDiff.exe`

Start it with no arguments and choose the two images with **Browse…**, type or paste their
paths and press Enter, or drag them onto the window. Or start it with the comparison already
set up:

```
ImageDiff.exe before.png after.png
ImageDiff.exe before.png after.png --mode flip --tolerance 4
```

Views (switch with the buttons or the 1 and 2 keys):

- **Difference**: the left image faded to grey, with every changed pixel in magenta.
- **Flip**: one image at a time, at the same zoom and position. Press **Space**, click the
  image, or press **Show right / Show left** to switch instantly between them, with no
  transition, so you can spot differences by eye. A label in the corner says which image is
  showing. Space in Difference view jumps to Flip.

Other controls:

- **Tolerance** ignores per-channel differences up to the chosen amount (0-255).
- **Zoom**: Ctrl + mouse wheel zooms around the cursor, **Fit** (Ctrl+0), **100%** (Ctrl+1).
- **Pan** by dragging with any mouse button. A left click without dragging flips.
- The status bar shows the pixel under the cursor in both images as `#AARRGGBB`.
- **Swap** exchanges left and right; **F5** reloads both files from disk.
- **Save difference…** writes the highlighted image as a PNG.
- Dropping two files sets both sides. Dropping one file fills an empty side, or replaces the
  side of the window you drop it on. You can also drop a file straight onto a path box.

| Option | Meaning |
| --- | --- |
| `-m`, `--mode <diff\|flip>` | View to open in (default `diff`) |
| `-t`, `--tolerance <0-255>` | Ignore channel differences up to this amount (default 0) |
| `-o`, `--output <file.png>` | Also save the highlighted difference image |
| `-h`, `--help` | Show help |

## The command line: `imgdiff.exe`

Compares without opening a window, prints the result, and sets the exit code, so it works
in scripts and build steps.

```
> imgdiff before.png after.png -o diff.png
Left:  before.png (800x600)
Right: after.png (800x600)
1,234 of 480,000 pixels differ (0.26%), max channel difference 87.
Difference image saved to diff.png
```

| Option | Meaning |
| --- | --- |
| `-t`, `--tolerance <0-255>` | Ignore channel differences up to this amount (default 0) |
| `-o`, `--output <file.png>` | Save the highlighted difference image |
| `-s`, `--show` | Also open the comparison in the window |
| `-m`, `--mode <diff\|flip>` | View the window opens in, with `--show` |

Exit codes: `0` identical, `1` different, `2` error.

## How images are compared

Images are aligned at the top-left corner. A pixel differs when any of its blue, green, red
or alpha values differs by more than the tolerance. If the images are different sizes, every
pixel that exists in only one of them counts as different. Colour profiles are ignored so the
stored pixel values are compared. Any format Windows can decode works: PNG, JPEG, BMP, GIF,
TIFF, ICO and JPEG XR, plus WebP, HEIC and others when their Windows codecs are installed.

## Building

Needs the .NET 8 SDK. Running needs the .NET 8 Desktop Runtime.

```
dotnet test
dotnet publish src/ImageDiff -c Release -r win-x64 --self-contained false -o publish
dotnet publish src/ImageDiff.Cli -c Release -r win-x64 --self-contained false -o publish
```

Publish both into the same folder: `imgdiff --show` looks for `ImageDiff.exe` next to itself.

Projects:

- `src/ImageDiff.Core`: comparison and command-line parsing, no Windows dependencies.
- `src/ImageDiff`: the WPF window (`ImageDiff.exe`).
- `src/ImageDiff.Cli`: the console tool (`imgdiff.exe`).
- `tests/ImageDiff.Core.Tests`: unit tests for the core.
