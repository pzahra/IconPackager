# IconPackager

IconPackager turns PNG, BMP and SVG artwork into multi-resolution Windows icons (`.ico`) at whatever
sizes you ask for, driven by a short text project file so that it can run unattended in a CI/CD pipeline.
It exists to replace the aged icon tooling that stopped at 16-colour favicons: modern icons need frames
from 16 to 256 pixels with full alpha, and rebuilding them by hand every time the artwork changes does
not scale. You list the frames each icon needs and where the artwork comes from; the tool renders every
frame and packs them into one `.ico`. An SVG can supply a single named element, so one Inkscape drawing
can hold the artwork for several icons.

It ships as two NuGet packages:

| Package        | Kind                                  | Purpose                                                                                   |
| -------------- | ------------------------------------- | ----------------------------------------------------------------------------------------- |
| `IconPackager` | Build step                            | Builds the `.ico` files named in `icons.ini` before your project compiles. Adds no assembly to your project and copies nothing to its output. |
| `IcoNet`       | .NET Standard 2.1 and .NET 10 library | Assembles `.ico` files from encoded frames, for applications that produce icons themselves. |

Most projects want only the first. The second is there for tools that write icons of their own.

## Status

Active development. Windows only, because rendering uses System.Drawing.

Working today:

- Frames from raster images (`.png`, `.bmp` and anything else System.Drawing opens), resized to each frame size.
- Frames from `.svg` files: the whole page, one element chosen by id or Inkscape label (`use`), or a region of the page (`snip`).
- 256 px frames stored PNG-compressed; smaller frames stored as bitmaps with a transparency mask at 1-bit
  black and white (`bw`), 8-bit palette (`pal`), 24-bit RGB (`rgb`) or 32-bit ARGB (`true`).
- Single PNG images at any frame size from the same sources, for NuGet package icons and the like.
- Outputs rebuilt only when the artwork or the project file changed, or on every run, or skipped (`output=`).

Parsed but not yet applied to the output:

- The `mask` and `invert` colours.

## Requirements

- Windows, on the machine that builds the icons
- .NET 10 SDK. The build step runs on the .NET 10 runtime whatever framework your project targets.
- Visual Studio 2026 or VS Code, if you want an IDE

## Quick start

Add the build step to the project that needs the icon:

```
dotnet add package IconPackager
```

Create `icons.ini` anywhere under the project folder, next to your artwork:

```ini
[app.ico]
source=assets
pack=logo.svg|use AppIcon
48-true=logo.svg|use AppIcon
32-true=logo.svg|use AppIcon
16-true=logo.svg|use AppIconSmall
```

Build the project. `app.ico` appears next to `icons.ini` before the compiler runs, so the project can
embed it:

```xml
<PropertyGroup>
  <ApplicationIcon>app.ico</ApplicationIcon>
</PropertyGroup>
```

The generated files belong in `.gitignore`: they are rebuilt whenever the artwork or `icons.ini` changes
and skipped when neither has, so the repository holds only the sources.

## The build step

The `IconPackager` package is a development dependency: it adds one MSBuild target and nothing else. No
assembly is referenced, nothing is copied to your output folder, and the package does not flow to projects
that reference yours. Before `CoreCompile` in every project that references it, the target:

- builds every `icons.ini` under the project folder, ignoring `bin` and `obj`;
- reports problems as build errors that name the `icons.ini` line concerned, so they show in the error list
  and jump to the line;
- fails the build when any icon could not be built in full. An icon whose remaining frames rendered is
  still written, but it is rebuilt and the error repeated on every build until it is fixed.

| In the project file                                             | Effect                                                     |
| --------------------------------------------------------------- | ---------------------------------------------------------- |
| `<EnableDefaultIconProjects>false</EnableDefaultIconProjects>`  | Stop picking up `icons.ini` files automatically.           |
| `<IconProject Include="art\tray.ini" />`                        | Build this project file as well, or instead.               |

Rendering uses System.Drawing, so the step only works on Windows and is an error elsewhere. A project
that also builds on Linux or macOS should condition its `IconProject` items and `EnableDefaultIconProjects`
on `$([MSBuild]::IsOSPlatform('Windows'))`, and check in the icon, or condition `ApplicationIcon` the same way.

The tool can also run by hand, for scripts outside MSBuild. Build this repository and run:

```
IconPackager path\to\icons.ini [more.ini ...]
```

The exit code is 0 only when every icon was built in full, 1 when a project file or frame failed, 2 when
no project file was given and 3 when the tool itself failed.

## Project files

A project file has one section per output file: `[name.ico]` for an icon, or `[name.png]` for a single
PNG image such as the 128 px package icon NuGet recommends. Inside a section:

| Line                   | Meaning                                                                                        |
| ---------------------- | ---------------------------------------------------------------------------------------------- |
| `source=folder`        | Folder the frame files are looked up in, relative to the project file.                         |
| `output=policy`        | When to build the output: `newest` (the default) when it is missing or a source or the project file is newer, `overwrite` every run, `none` skips the section. |
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

For an application that writes icons itself, reference the library instead of the build step:

```
dotnet add package IcoNet
```

`IcoNet` writes the icon; you supply each frame as encoded bytes. Frames below 256 px go in as bitmaps
with a mask (`GetBmpData`, at 32 bits or with a bit count of 1, 4, 8 or 24), and the 256 px frame goes in
as a PNG (`GetPngData`). The builder reads each frame's depth and palette size from the data itself.

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
| `IconPackager/build/`                 | The `.props` and `.targets` the package adds to a consuming project.             |
| `IconPackager/Properties/project.ini` | A sample project file showing the syntax. Its assets are not included.           |
| `IcoNet/`                             | The library: `IconBuilder` writes `.ico` files, `BitmapExt` prepares frames.     |
| `Directory.Build.props`               | Version, author and licence shared by both packages.                             |
| `docs/`                               | The project file format reference.                                               |
| `eel.svg`                             | A sample Inkscape drawing with a `laughing-eel` element to try `use` on.         |

## Building the packages

```
dotnet pack -c Release -o artifacts
```

`IcoNet.<version>.nupkg` is an ordinary library package. `IconPackager.<version>.nupkg` holds the
published tool under `tools/net10.0/` and the MSBuild files under `build/`, with no `lib/` folder and no
dependencies, which is what keeps it out of a consuming project's output. The version is set once in
`Directory.Build.props`.

## License

MIT. See [LICENSE.txt](LICENSE.txt).
