# Tasks

Prioritised from the Codex quality review of 2026-09-18, plus gaps noted while packaging. Tick items off
as they land; move anything that turns out to be wrong or unwanted to the bottom with a note.

## P1: fix before publishing 0.1.0

These affect anyone using the build step, and each is a small change.

- [x] **Write outputs atomically.** `AtomicFile.Write` puts the content in a temporary file beside the
  destination and moves it into place once complete; a failure deletes the temporary file and leaves the
  old output untouched. Icons, PNG outputs, montages and exploded frames all go through it.
- [x] **Report bad colours as parse errors.** `ColorTranslator.FromHtml` throws for a bad `mask` or
  `invert` value, which escapes `FromFile` as a plain exception, becomes IP1005 with exit code 3, and
  aborts the remaining project files. Catch it in the parser and throw `IconProjectParseError` with the
  line number, so it is IP1001 and processing continues. `docs/project-format.md` already describes the
  intended behaviour. `IconPackager/ProjectParser.cs`, `ParseKey`.
- [x] **Anchor the project file grammar.** Both regexes are anchored and case-insensitive on names, a line
  before the first section is a parse error, and `ParserTests` covers each case.
- [x] **Rename the MSBuild target.** `BuildIcons` is now `IconPackagerBuildIcons`, so it cannot collide
  with a consumer's own target or another package's.
- [x] **Fix the links in the packaged README.** The two relative links, to `docs/project-format.md` and
  `LICENSE.txt`, are now absolute GitHub URLs, so they resolve both on GitHub and on nuget.org. No change
  to `Directory.Build.props` was needed: the same README serves both, and absolute links work in each.
- [x] **Fill in LICENSE.txt.** `Copyright (c) 2026 Patrick Zahra`.

## P2: robustness and tests

- [x] **Add a test project.** `IconPackager.Tests/`, xunit, in the solution: the parser and its error
  lines, rendering against real timestamps, the atomic write, the pictures, an SVG element, the byte
  layout of a two-frame icon, frame validation, and the reader's decoding.
- [x] **Validate frame data in `IconBuilder`.** A PNG frame must carry the full signature and an IHDR of
  the frame's size; a DIB must have a 40-byte header, the frame's width, twice its height, a depth of 1, 4,
  8, 24 or 32, and enough bytes for its rows and mask.
- [x] **Reject duplicate destinations.** Sections are compared by full path, without regard to case, and
  the second is a parse error.
- [x] **PNG sections should key on size only.** A later line at the same size replaces the earlier one; a
  second size is still a parse error.
- [x] **Move the mutex inside the catch-all.** A mutex failure is now IP1005 with exit code 3.
- [x] **Validate `GetMask` arguments.** A width below 1 is `ArgumentOutOfRangeException`; a buffer that is
  not whole rows is `ArgumentException`.
- [x] **Bound the frame count.** `ImageCount` is a `ushort` written to the 16-bit directory field, and
  `Add` refuses a frame past `IconBuilder.MaxFrames`, set to 32, with `InvalidOperationException`. That is
  far below the format limit but more than any real icon carries.

## P3: improvements

- [ ] **Run the build step once per project, not once per target framework.** A multi-targeted project
  runs the tool in every inner build. Each framework now has its own `obj\...\icons\` folder, so the runs
  no longer write the same files, but every frame is still rendered and every failure reported once per
  framework. Running only in the outer build races with project references that call inner builds
  directly, so the fix is probably a per-run stamp file the target uses as an `Inputs`/`Outputs` marker.
  `IconPackager/build/PatTech.IconPackager.targets`.
- [x] **Let `dotnet clean` remove generated icons.** The build step writes under `obj` and adds the folder
  to `FileWrites`, so `Clean` and `IncrementalClean` remove the icons and montages with everything else.
- [ ] **Decide on path validation.** Section names and `source` accept rooted paths and `..`, so a project
  file can read and write anywhere. The docs describe absolute `source` as a feature; if that stays,
  document that output names may also leave the folder, or restrict output names to below the project file.
- [ ] **Declare Windows support instead of warning about it.** Both projects emit dozens of CA1416
  warnings. An `[assembly: SupportedOSPlatform("windows")]` on `IcoNet` and the tool states the truth and
  gives consumers the analyzer warning where it belongs, in their code.
- [x] **Apply `mask` and `invert`.** Parsed and validated since the first version, still not used when
  rendering raster frames. `IconPackager/FrameLoader.cs` and `IconPackager/Artwork.cs`.
- [x] **Give the packages an icon.** `icon/icons.ini` renders `icon.png` and `icon.ico` from `icon/icon.svg`;
  both packages set `PackageIcon` and the tool embeds the `.ico`. The rendered images are checked in rather
  than built by the build step, since a fresh clone needs them before the tool exists. Rerun the tool by
  hand after editing the drawing.
- [ ] **Drop the apphost from `tools/`.** `IconPackager.exe` is a Windows x64 launcher the target never
  uses; `dotnet IconPackager.dll` does the work. `UseAppHost=false` on publish saves 160 KB per package.
- [ ] **Offer a `dotnet tool` package for scripts.** Running the CLI outside MSBuild currently means
  building the repository. A separate `PackAsTool` package, from the same project, would cover that.
