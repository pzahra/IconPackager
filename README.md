# IconPackager

IconPackager turns PNG, BMP and SVG artwork into multi-resolution Windows icons (`.ico`) at whatever
sizes you ask for, driven by a short text project file so that it can run unattended in a CI/CD pipeline.
It exists to replace the aged icon tooling that stopped at 16-colour favicons: modern icons need frames
from 16 to 256 pixels with full alpha, and rebuilding them by hand every time the artwork changes does
not scale. You list the frames each icon needs and where the artwork comes from; the tool renders every
frame and packs them into one `.ico`. An SVG can supply a single named element, so one Inkscape drawing
can hold the artwork for several icons.

| Project        | Target                                | Purpose                                                                           |
| -------------- | ------------------------------------- | --------------------------------------------------------------------------------- |
| `IconPackager` | .NET 10 console app                   | Reads `.ini` icon projects and writes `.ico` files.                               |
| `IcoNet`       | .NET Standard 2.1 and .NET 10 library | Assembles `.ico` files from encoded frames, for build scripts and your own tools. |

## Status

Active development. Windows only, because rendering uses System.Drawing.

Working today:

- Frames from raster images (`.png`, `.bmp` and anything else System.Drawing opens), resized to each frame size.
- Frames from `.svg` files: the whole page, one element chosen by id or Inkscape label (`use`), or a region of the page (`snip`).
- 256 px frames stored PNG-compressed; smaller frames stored as 32-bit bitmaps with a transparency mask.

Parsed but not yet applied to the output:

- Bit depths `bw`, `pal` and `rgb`. Every frame is currently written at 32 bits per pixel.
- The `mask` and `invert` colours.

## Requirements

- Windows
- .NET 10 SDK
- Visual Studio 2026 or VS Code, if you want an IDE

## Quick start

Build everything:

```
dotnet build
```

Create `icons.ini` next to your artwork:

```ini
[app.ico]
source=assets
pack=logo.svg|use AppIcon
48-true=logo.svg|use AppIcon
32-true=logo.svg|use AppIcon
16-true=logo.svg|use AppIconSmall
```

Run the tool on it:

```
IconPackager path\to\icons.ini
```

`app.ico` appears next to `icons.ini`. Pass more than one project file to process them in one run.

## Use in a build pipeline

IconPackager is meant to run unattended, so icons are rebuilt whenever the artwork changes rather than
checked in as binaries. Keep the project file and artwork in the repository and add a step such as:

```
IconPackager src\icons\icons.ini
```

A parse error in the project file fails the step with a non-zero exit code. A frame that cannot be rendered is reported on standard error but leaves the exit code at zero, so have the step fail on standard-error output if a
missing frame must break the build.

## Project files

A project file has one `[name.ico]` section per icon. Inside a section:

| Line                   | Meaning                                                                                        |
| ---------------------- | ---------------------------------------------------------------------------------------------- |
| `source=folder`        | Folder the frame files are looked up in, relative to the project file.                         |
| `pack=frame`           | A 256 px frame, stored PNG-compressed.                                                         |
| `<size>-<depth>=frame` | A frame of that size. Sizes: 16, 24, 32, 48, 64, 128, 256. Depths: `bw`, `pal`, `rgb`, `true`. |

A frame is a file name followed by options separated by `|`:

| Option   | Example                           | Effect                                                                                 |
| -------- | --------------------------------- | -------------------------------------------------------------------------------------- |
| `use`    | `logo.svg\|use Wordsmith`         | Render only this SVG element, found by id or Inkscape label, scaled to fill the frame. |
| `snip`   | `tray.svg\|snip 15mm,0,12mm,12mm` | Render only this region: x, y, width, height in `mm` (default), `in` or `px`.          |
| `mask`   | `old.bmp\|mask #ff00ff`           | Transparency key colour. Reserved; not applied yet.                                    |
| `invert` | `old.bmp\|invert #008080`         | Invert overlay colour. Reserved; not applied yet.                                      |

The full format, including rendering rules and error behaviour, is in [docs/project-format.md](docs/project-format.md).

## Using the library

`IcoNet` writes the icon; you supply each frame as encoded bytes. Frames below 256 px go in as 32-bit
bitmaps with a mask (`GetBmpData`), and the 256 px frame goes in as a PNG (`GetPngData`). Both helpers
expect a 32-bit ARGB bitmap, which `Resize` and `Crop` produce.

```csharp
using System.Drawing;
using System.IO;
using IcoNet;

using var source = (Bitmap)Image.FromFile("logo.png");
var icon = new IconBuilder();

foreach (var size in new[] { 16, 32, 48 }) {
    using var frame = source.Resize(new Size(size, size));
    icon.Add(size, frame.GetBmpData());
}

using var large = source.Resize(new Size(256, 256));
icon.Add(256, large.GetPngData());

using var writer = new BinaryWriter(File.Create("app.ico"));
icon.Write(writer);
```

Frames are written in the order they are added, and sizes run from 1 to 256 pixels. The public types
carry XML documentation, so IntelliSense describes each member.

## Repository layout

| Path                                  | Contents                                                                         |
| ------------------------------------- | -------------------------------------------------------------------------------- |
| `IconPackager/`                       | The command-line tool. `IconProject.cs` parses project files and renders frames. |
| `IconPackager/Properties/project.ini` | A sample project file showing the syntax. Its assets are not included.           |
| `IcoNet/`                             | The library: `IconBuilder` writes `.ico` files, `BitmapExt` prepares frames.     |
| `docs/`                               | The project file format reference.                                               |
| `eel.svg`                             | A sample Inkscape drawing with a `laughing-eel` element to try `use` on.         |

## License

MIT. See [LICENSE.txt](LICENSE.txt).
