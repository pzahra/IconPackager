# Tasks

Prioritised from the Codex quality review of 2026-09-18, plus gaps noted while packaging. Tick items off
as they land; move anything that turns out to be wrong or unwanted to the bottom with a note.

## P1: fix before publishing 0.1.0

These affect anyone using the build step, and each is a small change.

- [ ] **Write outputs atomically.** `RenderAll` truncates the destination with `FileMode.Create` and then
  writes into it. An interrupted or failed write leaves a corrupt file with a fresh timestamp, which
  `newest` then treats as up to date forever. Write to a temporary file beside the destination and move it
  over the old one on success; on failure delete the temporary file and leave the old output untouched.
  `IconPackager/IconProject.cs`, `RenderAll` and `IsUpToDate`.
- [ ] **Report bad colours as parse errors.** `ColorTranslator.FromHtml` throws for a bad `mask` or
  `invert` value, which escapes `FromFile` as a plain exception, becomes IP1005 with exit code 3, and
  aborts the remaining project files. Catch it in the parser and throw `IconProjectParseError` with the
  line number, so it is IP1001 and processing continues. `docs/project-format.md` already describes the
  intended behaviour. `IconPackager/IconProject.cs`, the `mask` and `invert` cases.
- [ ] **Anchor the project file grammar.** `rxIcoName` and `rxIcoProp` are unanchored, so
  `[app.ico] trailing text` and `boguspack=logo.svg` are accepted, and `[App.ICO]` before the first
  section is silently ignored rather than rejected. Anchor both with `^…$`, match extensions and property
  names case-insensitively, and add a test for each case. `IconPackager/IconProject.cs:19-20`.
- [ ] **Rename the MSBuild target.** `BuildIcons` is generic enough to collide with a consumer's own target
  or another package's, and MSBuild silently replaces a same-named target. Rename it
  `IconPackagerBuildIcons`; the properties are already prefixed. `IconPackager/build/IconPackager.targets`.
- [ ] **Fix the links in the packaged README.** The README goes into both packages, but its relative links
  to `docs/project-format.md` and `LICENSE.txt` point nowhere on nuget.org. Make them absolute GitHub URLs.
  `README.md`, `Directory.Build.props`.
- [ ] **Fill in LICENSE.txt.** It still reads `Copyright (c) [year] [fullname]`.

## P2: robustness and tests

- [ ] **Add a test project.** Nothing is covered today. Start with the parser (line numbers on errors,
  `output=` values, each error code), `IsUpToDate` against real timestamps, the atomic write, and the byte
  layout `IconBuilder` produces for a two-frame icon. xunit, `IconPackager.Tests/`, added to the solution.
- [ ] **Validate frame data in `IconBuilder`.** `IconEntry` accepts any buffer over 40 bytes as a DIB and
  any buffer starting with four PNG bytes as a PNG, so corrupt data or a frame whose encoded size disagrees
  with `size` is written without complaint. Check the full 8-byte PNG signature and the IHDR dimensions,
  and for a DIB check the header size, `biWidth == size`, `biHeight == 2 * size` and a bit count of 1, 4,
  8, 24 or 32. Then the `ArgumentException` promised in the XML docs is true. `IcoNet/IconBuilder.cs:89-104`.
- [ ] **Reject duplicate destinations.** Two sections that resolve to the same file, such as `[app.ico]`
  twice or `[a.ico]` and `[./a.ico]`, are not detected: the second may be skipped as up to date or overwrite
  the first depending on `output=`. Make it a parse error on the second section. `IconPackager/IconProject.cs`.
- [ ] **PNG sections should key on size only.** A `.png` section rejects a second frame whose (size, depth)
  key differs, so `48-true` followed by `48-rgb` is an error even though the docs say depth is accepted and
  only a second size is rejected. Compare sizes, and let the later line replace the earlier one.
  `IconPackager/IconProject.cs`, the "a .png output takes a single frame" check.
- [ ] **Move the mutex inside the catch-all.** `new Mutex` and `WaitOne` run before the `try`, so an ACL or
  platform failure escapes as an unhandled exception instead of IP1005 with exit code 3.
  `IconPackager/Program.cs`.
- [ ] **Validate `GetMask` arguments.** A zero `width` divides by zero and a `pixels` buffer that is not a
  whole number of BGRA rows is silently truncated. Throw `ArgumentException` for both; it is public API.
  `IcoNet/BitmapExt.cs`, `GetMask`.
- [ ] **Bound the frame count.** `ImageCount` casts a list count to `short`; the ICO header field is an
  unsigned 16-bit count. Use `ushort` and refuse to add a frame beyond that. `IcoNet/IconBuilder.cs:17`.

## P3: improvements

- [ ] **Run the build step once per project, not once per target framework.** A multi-targeted project
  runs the tool in every inner build; the mutex keeps them from colliding but `output=overwrite` renders
  everything twice and failures are reported twice. Running only in the outer build races with project
  references that call inner builds directly, so the fix is probably a per-run stamp file the target uses
  as an `Inputs`/`Outputs` marker. `IconPackager/build/IconPackager.targets`.
- [ ] **Let `dotnet clean` remove generated icons.** Outputs are not in `FileWrites` and there is no clean
  target, so they linger after a clean. Cheapest route: a `--list` mode in the tool that prints the outputs
  a project file would write, and a target that deletes them. Decide first whether that is wanted, since
  the icons sit in the source tree by design.
- [ ] **Decide on path validation.** Section names and `source` accept rooted paths and `..`, so a project
  file can read and write anywhere. The docs describe absolute `source` as a feature; if that stays,
  document that output names may also leave the folder, or restrict output names to below the project file.
- [ ] **Declare Windows support instead of warning about it.** Both projects emit dozens of CA1416
  warnings. An `[assembly: SupportedOSPlatform("windows")]` on `IcoNet` and the tool states the truth and
  gives consumers the analyzer warning where it belongs, in their code.
- [ ] **Apply `mask` and `invert`.** Parsed and validated since the first version, still not used when
  rendering raster frames. `IconPackager/IconProject.cs`, `LoadImage`.
- [ ] **Give the packages an icon.** Add an `icons.ini` with a `[icon.png]` section rendered from `eel.svg`
  and set `PackageIcon`, using the build step on its own repository.
- [ ] **Drop the apphost from `tools/`.** `IconPackager.exe` is a Windows x64 launcher the target never
  uses; `dotnet IconPackager.dll` does the work. `UseAppHost=false` on publish saves 160 KB per package.
- [ ] **Offer a `dotnet tool` package for scripts.** Running the CLI outside MSBuild currently means
  building the repository. A separate `PackAsTool` package, from the same project, would cover that.
