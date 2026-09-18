# Icon project file format

An icon project is a plain-text `.ini` file that tells IconPackager which `.ico` files to build, which
frames to put in each, and which single-image `.png` files to write alongside them. Run it with:

```
IconPackager project.ini [more.ini ...]
```

Each project file is processed in turn, and its icons are written next to it.

```ini
; Comments start with a semicolon.
[app.ico]
source=assets
pack=logo.svg|use AppIcon
48-true=logo.svg|use AppIcon
32-true=logo.svg|use AppIcon
16-pal=legacy.bmp|mask #ff00ff

[tray.ico]
source=assets
32-true=tray.svg|snip 0,0,12mm,12mm
16-true=tray.svg|snip 15mm,0,12mm,12mm

[package.png]
source=assets
128-true=logo.svg|use AppIcon
```

## Lines and comments

- The file is read as UTF-8. A byte-order mark is allowed.
- Blank lines are ignored, and so is whitespace at either end of a line.
- A semicolon starts a comment. Everything from the first `;` to the end of the line is dropped, so a `;`
  cannot appear inside a value.

## Sections

A section starts an output file, and its extension says what kind:

- `[name.ico]` builds an icon holding every frame listed in the section.
- `[name.png]` writes a single PNG image, for uses that want one bitmap rather than an icon, such as the
  128 px package icon NuGet recommends. The section takes exactly one frame; listing frames of two
  different sizes is a parse error. The depth is accepted, but the PNG is always 32-bit with alpha.

The name is a path relative to the project file's folder, so `[icons/app.ico]` writes into an `icons`
subfolder, which must already exist.

Lines before the first section are ignored. A line inside a section that is not a recognised property is
a parse error.

## Properties

| Property | Value | Meaning |
|---|---|---|
| `source` | folder | Where the section's frame files are looked up. Relative to the project file, or absolute. Each section starts at the project file's folder, and repeating `source` appends to the previous folder rather than replacing it. |
| `output` | `newest`, `overwrite` or `none` | When the output is built. See below. The default is `newest`. |
| `pack` | frame | A 256 × 256 frame, stored PNG-compressed. Equivalent to `256-true`. |
| `<size>-<depth>` | frame | One frame. `size` is one of 16, 24, 32, 48, 64, 128 or 256. `depth` is `bw`, `pal`, `rgb` or `true`. |

`output` decides whether the section is built at all:

- `newest` renders when the output is missing, or when the project file or any of the section's frame
  files is newer than it. Nothing is rendered, and nothing is reported, when the output is already up to
  date. A frame file that is missing counts as newer, so the error is reported.
- `overwrite` renders on every run.
- `none` skips the section entirely: nothing is rendered, written or checked. Use it to park an output
  without deleting its definition.

Frames are written to the icon in the order they appear. Size and depth together identify a frame: a
second line with the same size and depth replaces the first, while two depths at the same size give two
frames of that size.

The depth sets how the frame is stored:

| Depth | Bits per pixel | Colour |
|---|---|---|
| `bw` | 1 | Black or white, decided by each pixel's luminance. |
| `pal` | 8 | A palette of up to 255 colours chosen from the frame by median cut, plus black. Each pixel takes the nearest palette colour; there is no dithering. |
| `rgb` | 24 | Full colour, no alpha channel. |
| `true` | 32 | Full colour with an alpha channel. |

At `bw`, `pal` and `rgb` transparency is a hard edge: pixels less than half opaque are transparent and the
rest are drawn solid. 256 px frames are always stored as 32-bit PNG whatever depth is written.

## Frames

A frame value is a file name followed by zero or more options, separated by `|`:

```
file|option value|option value
```

- `file` is relative to the section's `source` folder. Spaces around each `|` are ignored.
- A file ending in `.svg` (in any case) is rendered as vector artwork. Any other file is opened as a raster
  image through System.Drawing, which covers PNG, BMP, GIF, JPEG and TIFF.
- An option is its name, a space, then its value. The value runs to the next `|` or the end of the line,
  so it may contain spaces.
- An unknown option, or an option without a value, is a parse error.

### use

```
logo.svg|use AppIcon
```

SVG files only; ignored for raster files. Names one element of the drawing. Only that element and its
children are rendered, scaled uniformly to fill the frame and centred, with transparent margins along the
shorter side.

The name is matched against element ids first, then against Inkscape labels (the `inkscape:label`
attribute, which is the name shown in Inkscape's Layers and Objects panel). If several elements share a
label, the first in document order wins. A layer can be named, in which case the whole layer is rendered.

Artwork outside the element is hidden, so overlapping shapes elsewhere on the page do not bleed into the
frame. The element's parent layers are made visible, so an element on a hidden layer still renders.
Styles inherited from parents, and gradient or pattern definitions, remain in effect. One limitation: an
Inkscape clone whose original lies outside the element renders blank, because the original is hidden
along with everything else.

A name that matches nothing is a frame error: the frame is reported and skipped.

### snip

```
tray.svg|snip 15mm,0,12mm,12mm
sheet.png|snip 0.5in 0.5in 1in 1in
```

Crops to a region given as x, y, width and height. Numbers may have decimals and are separated by commas
or spaces. Each number takes an optional unit: `mm` (the default), `in`, or `px`, where a `px` is 1/96 of
an inch.

- For SVG files the region is measured on the physical page, as sized by the root element's `width` and
  `height`. A page with unitless or percentage dimensions is treated as 96 dpi pixels, the way browsers do.
- For raster files millimetres are converted to pixels using the image's own resolution (its DPI), then
  the region is scaled to the frame.

When `use` and `snip` are combined, `snip` sets the region and `use` still hides everything outside the
named element.

### mask and invert

```
legacy.bmp|mask #ff00ff|invert #008080
```

Reserved for legacy raster artwork. `mask` names the colour that marks transparent pixels and `invert`
the colour of an invert overlay. Colours are HTML colours, either `#rrggbb` or a name such as `Magenta`.
Both are parsed and validated but do not yet affect the output. The defaults are magenta and teal.

## How frames are rendered

- **SVG, whole page** (no `use` or `snip`): the page is fitted inside the square frame with its aspect
  ratio preserved, leaving transparent margins.
- **SVG, element or region**: the element's bounds or the `snip` region is fitted the same way.
- **Raster**: the image, or its `snip` region, is resized to the square frame with bicubic resampling. The
  aspect ratio is not preserved, so a non-square image is stretched. Use `snip` to cut a square region
  first.
- The 256 px frame is stored as PNG. All other frames are stored as bitmaps at the requested depth with a
  1-bit transparency mask derived from the alpha channel.

## Errors and exit code

Problems are reported on standard error, one line each, in the format MSBuild and Visual Studio recognise,
so that a build running the tool lists them as errors and opens the project file at the line concerned:

```
C:\src\app\icons.ini(5): error IP1002: app.ico: 48px frame from logo.svg: Element 'AppIcon' not found in logo.svg
```

| Code     | Meaning                                                                                                              |
| -------- | -------------------------------------------------------------------------------------------------------------------- |
| `IP1000` | No project file was given on the command line.                                                                       |
| `IP1001` | A project file does not exist, cannot be read, or has an unrecognised line, option, crop or `output` value. None of its outputs are written; the other project files on the command line are still processed. |
| `IP1002` | A frame could not be rendered: missing source file, unknown `use` name, empty region. The frame is skipped and the output is still written with its remaining frames. |
| `IP1003` | An output had no frames left to write.                                                                               |
| `IP1004` | An output file could not be written: missing folder, locked file.                                                    |
| `IP1005` | The tool failed with an unexpected exception. The stack trace follows the message.                                   |

An output written without one of its frames is dated back to 1970, so under `output=newest` it never
counts as up to date and the failure is reported again on each run until it is fixed.

The exit code is 0 when every output in every project file was built in full, 1 when any project file,
frame or output failed, 2 when no project file was given, and 3 for `IP1005`.
