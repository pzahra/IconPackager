# IconPackager

IconPackager is a .NET library and tooling set for creating and packaging
icon sets for Windows applications. It aims to simplify producing multi-resolution
.ICO assets.

## Status
Active development. Targets libraries/projects built for:
- .NET Standard 2.1 (library)
- .NET 10 (command line app)

## Features
- Generate multi-resolution icon sets from raster images (.BMP, .PNG, etc)
- Include elements from .SVG images (future)
- Package icon sets for Windows targets
- Command-line tooling and library API for integration in build pipelines

## Requirements
- .NET 10 SDK
- Visual Studio 2026 or VS Code

## CLI usage (examples)
Create a project.ini file containing icon definitions
```
[app.ico]
source=folder/with/assets
pack=large-hires.png
16-bw=16px-monochrome.bmp|invert #008080|mask #ff00ff
32-pal=32px 8-bit palette.png
64-true=large-hires.png
```
- `pack` will save a large .PNG image as-is.
- `size-depth` options accept sizes of 16,24,32,48,64,128,256 pixels, and bitdepths of:
    - bw (monochrome)
	- pal (8-bit paletted)
	- rgb (24-bit rgb)
	- true (32-bit argb)
- Additional options include
    - mask {$html-colour} to set the transparency mask.
	- invert {$html-colour} to set the invert-colour overlay.
	- use {$xml-element} for selecting named elements from .SVG files (future).
	- snip {$x},{$y},{$width},{$height} for using a subselection of the image. Units can be px,in,mm (default mm) (future).

## API usage (library)
- Instantiate the IconBuilder class.
- Use GetBmpData extension method to convert Bitmap image to bytes.
- Add each image to IconBuilder along with its size (1-255 px square).
- Write from IconBuilder to output stream.

## License
- MIT