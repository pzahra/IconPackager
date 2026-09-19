# Icon project file format

An icon project is a plain-text `.ini` file that tells IconPackager which `.ico` files to build, which
frames to put in each, and which single-image `.png` files to write alongside them. Run it with:

```
IconPackager [--out <folder>] [--montage] [--explode] project.ini [more.ini ...]
```

Each project file is processed in turn, and its outputs are written next to it, or into the `--out`
folder, which is created if need be. The other two switches picture the icons built; see
[Pictures of an icon](#pictures-of-an-icon).

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
  128 px package icon NuGet recommends. The section takes one frame: a later line at the same size
  replaces the earlier one, and a second size is a parse error. The depth is accepted, but the PNG is
  always 32-bit with alpha.

The name is a path relative to the project file's folder, or to the `--out` folder when one is given, so
`[icons/app.ico]` writes into an `icons` subfolder, which must already exist. Two sections that name the
same file, however it is spelt, are a parse error on the second.

The first line that is not blank or a comment must start a section. A line before it, or a line inside a
section that is not a recognised property, is a parse error. Section names, property names, depths and
option names are matched without regard to case; values keep theirs.

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

An output is written to a temporary file beside its destination and moved into place once complete, so a
failed or interrupted write leaves the previous file untouched rather than half written with a fresh
timestamp.

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
photo.png|mask none
```

Colour keys for artwork that has no transparency of its own, in the style of the classic icon editors.
`mask` names the colour to draw as transparent. `invert` names the colour to draw as screen-inverting
pixels, which show the inverse of whatever lies beneath the icon so that an outline stays visible on any
background. Colours are HTML colours, `#rrggbb`, `#rgb` or a name such as `Magenta`; anything else is a
parse error. The value `none` switches a key off.

- Pixels are matched exactly, on the source pixels before any cropping or scaling, so a key never bleeds
  into the pixels around it.
- When neither option is written, artwork without an alpha channel (a 24-bit BMP, a JPEG, an opaque PNG)
  gets the classic defaults: magenta (`#ff00ff`) is transparent and teal (`#008080`) inverts. Artwork that
  can express transparency itself, such as a 32-bit PNG or an SVG, is left as drawn unless a key is
  written for it.
- Inverting pixels exist only at the `bw`, `pal` and `rgb` depths. Windows blends 32-bit frames by their
  alpha channel and ignores the invert mechanism, so at `true` depth, in 256 px frames and in `.png`
  outputs those pixels are simply transparent.

## How frames are rendered

- **SVG, whole page** (no `use` or `snip`): the page is fitted inside the square frame with its aspect
  ratio preserved, leaving transparent margins.
- **SVG, element or region**: the element's bounds or the `snip` region is fitted the same way.
- **Raster**: the image, or its `snip` region, is resized to the square frame with bicubic resampling. The
  aspect ratio is not preserved, so a non-square image is stretched. Use `snip` to cut a square region
  first.
- The 256 px frame is stored as PNG. All other frames are stored as bitmaps at the requested depth with a
  1-bit transparency mask derived from the alpha channel.

## Pictures of an icon

Two switches write pictures of an icon beside it, for checking what the depths and masks did to the
artwork. They apply to every icon a project file builds, and to any `.ico` file named on the command line
in place of a project file, which is pictured without being rebuilt. An `.ico` named with neither switch
gets both.

- `--montage` writes `name.montage.png`: every frame in directory order on one sheet, each over a
  checkerboard with its size and depth beneath. Frames up to 128 px are magnified by a whole number so
  that their pixels stay square; the 256 px frame is shown at half size.
- `--explode` writes each frame as `name.<size>-<depth>.png`, `app.32-pal.png` say, with the depth
  written the way a project file would. A PNG-compressed frame, labelled `png`, is copied byte for byte;
  the others are decoded. Two frames of the same size and depth are numbered.

Frames are decoded the way Windows draws them: a 32-bit frame with an alpha channel is blended by it, and
every other frame takes its transparency from the mask. Pixels that would invert the screen are painted
teal (`#008080`), so an exploded frame fed back in with `invert #008080` reproduces them.

The pictures follow the icon's `output` policy in spirit: they are written when missing or older than the
icon, and left alone otherwise. Under `--out` they go to that folder too.

## Errors and exit code

Problems are reported on standard error, one line each, in the format MSBuild and Visual Studio recognise,
so that a build running the tool lists them as errors and opens the project file at the line concerned:

```
C:\src\app\icons.ini(5): error IP1002: app.ico: 48px frame from logo.svg: Element 'AppIcon' not found in logo.svg
```

| Code     | Meaning                                                                                                              |
| -------- | -------------------------------------------------------------------------------------------------------------------- |
| `IP1000` | The command line was not understood: no project or icon file, an unknown switch, or `--out` without a folder. |
| `IP1001` | A project file does not exist, cannot be read, or has an unrecognised line, option, crop or `output` value. None of its outputs are written; the other project files on the command line are still processed. |
| `IP1002` | A frame could not be rendered: missing source file, unknown `use` name, empty region. The frame is skipped and the output is still written with its remaining frames. Also a frame of an existing icon that could not be decoded. |
| `IP1003` | An output had no frames left to write.                                                                               |
| `IP1004` | An output, montage or exploded frame could not be written: missing folder, locked file.                              |
| `IP1005` | The tool failed with an unexpected exception. The stack trace follows the message.                                   |
| `IP1006` | An icon file named on the command line, or one just built, could not be read or is not an icon.                      |

An output written without one of its frames is dated back to 1970, so under `output=newest` it never
counts as up to date and the failure is reported again on each run until it is fixed.

The exit code is 0 when every output in every project file was built in full and every icon pictured, 1
when any project file, frame, output or icon failed, 2 when the command line was not understood, and 3
for `IP1005`.
