﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IcoNet;
using Svg;

namespace IconPackager {
	class IconProject {
		readonly string file;
		readonly List<IconDef> outputs = [];

		public static IconProject FromFile(string file) {
			if (!File.Exists(file)) throw new FileNotFoundException("Project file not found", file);
			var prj = new IconProject(file);
			Regex rxIcoName = new(@"\[(.*\.(ico|png))\]");
			Regex rxIcoProp = new(@"(source|output|pack|(16|24|32|48|64|128|256)-(bw|pal|rgb|true))\s*=\s*(.*)");
			// file|param value|param value...  Anchored so that a parameter that fails to parse is reported
			// rather than silently dropped. Values run to the next '|' so element names may contain spaces.
			Regex rxIcoParam = new(@"^([^|]+?)\s*(?:\|\s*(use|snip|mask|invert)\s+([^|]+?)\s*)*$");
			Regex rxCrop = new(@"^(?:(\d+(?:\.\d+)?)(|mm|in|px)(?:[, ]+|$)){4}$");
			IconDef? ico = null;
			string folder = Path.GetDirectoryName(file)!;
			// Line numbers are kept so that errors can point at the line they concern.
			foreach (var (line, lineNo) in File.ReadAllLines(file)
				.Select((l, i) => (Text: l.Split(';')[0].Trim(), No: i + 1))
				.Where(l => !string.IsNullOrEmpty(l.Text))
			) {
				var section = rxIcoName.Match(line);
				if (section.Success) {
					ico = new() {
						Name = section.Groups[1].Value,
						DestFile = Path.Combine(folder, section.Groups[1].Value),
						Kind = section.Groups[2].Value == "png" ? OutputKind.Png : OutputKind.Ico,
						LookupFolder = folder,
						Line = lineNo,
					};
					prj.outputs.Add(ico);
					continue;
				}

				if (ico is not null) {
					var prop = rxIcoProp.Match(line);
					if (!prop.Success) {
						throw new IconProjectParseError("Parse Error: " + line, lineNo);
					}
					string p = prop.Groups[1].Value;
					string v = prop.Groups[4].Value;
					IconSize size;
					IconDepth depth;
					switch (p) {
						case "source":
							ico.LookupFolder = Path.Combine(ico.LookupFolder, v);
							continue;

						case "output":
							ico.Output = v switch {
								"newest" => OutputMode.Newest,
								"overwrite" => OutputMode.Overwrite,
								"none" => OutputMode.None,
								_ => throw new IconProjectParseError("Parse Error: output must be newest, overwrite or none: " + v, lineNo),
							};
							continue;

						case "pack":
							size = IconSize.S256;
							depth = IconDepth.True;
							break;
						default:
							size = (IconSize)Convert.ToInt32(prop.Groups[2].Value);
							depth = prop.Groups[3].Value switch {
								"bw" => IconDepth.BlackWhite,
								"pal" => IconDepth.Palette,
								"rgb" => IconDepth.RGB,
								"true" => IconDepth.True,
								_ => throw new UnreachableException(),
							};
							break;
					}

					var param = rxIcoParam.Match(v);
					if (!param.Success) {
						throw new IconProjectParseError("Parse Error: " + v, lineNo);
					}
					var frame = new IconFrame(param.Groups[1].Value) { Line = lineNo };
					foreach (var (par, val) in param.Groups[2].Captures
						.Select((c, i) => (c.Value, param.Groups[3].Captures[i].Value))) {
						switch (par) {
							case "use":
								frame.SvgElement = val;
								break;
							case "snip":
								var crop = rxCrop.Match(val);
								if (!crop.Success) {
									throw new IconProjectParseError("Parse Error: " + val, lineNo);
								}
								// mm default
								// in * 25.4 => mm
								// px * 25.4 / 96 => mm
								var mm = new float[4];
								for (int i = 0; i < mm.Length; ++i) {
									mm[i] = float.Parse(crop.Groups[1].Captures[i].Value, CultureInfo.InvariantCulture);
									switch (crop.Groups[2].Captures[i].Value) {
										case "in": mm[i] *= 25.4f; break;
										case "px": mm[i] *= 25.4f / 96; break;
									}
								}
								frame.Crop = new(mm[0], mm[1], mm[2], mm[3]);
								break;
							case "mask":
								frame.Mask = ColorTranslator.FromHtml(val);
								break;
							case "invert":
								frame.Invert = ColorTranslator.FromHtml(val);
								break;
						}
					}
					// A PNG holds one image, so a second frame of a different size has nowhere to go.
					if (ico.Kind == OutputKind.Png && ico.Frames.Count > 0 && !ico.Frames.ContainsKey((size, depth))) {
						throw new IconProjectParseError("Parse Error: a .png output takes a single frame: " + line, lineNo);
					}
					ico.Frames[(size, depth)] = frame;
				}
			}
			return prj;
		}

		private IconProject(string file) {
			this.file = file;
		}

		/// <summary>
		/// Renders every output that is due under its <c>output</c> policy and writes each one that has at
		/// least one frame. Problems are reported on standard error in MSBuild's format, one line each, and
		/// rendering carries on with the next frame or output.
		/// </summary>
		/// <returns>True when every frame of every output was rendered and written.</returns>
		public bool RenderAll() {
			bool ok = true;
			foreach (var output in outputs) {
				if (output.Output == OutputMode.None) {
					Console.WriteLine($"{output.Name} skipped (output=none)");
					continue;
				}
				if (IsUpToDate(output)) {
					Console.WriteLine($"{output.Name} is up to date");
					continue;
				}

				var frames = new List<(int Size, byte[] Data)>();
				bool complete = true;
				foreach (var ((size, depth), frame) in output.Frames) {
					try {
						using var img = LoadImage(output.LookupFolder, frame, (int)size);
						bool asPng = output.Kind == OutputKind.Png || size is IconSize.S256;
						frames.Add(((int)size, asPng ? img.GetPngData() : img.GetBmpData((int)depth)));
					}
					catch (Exception ex) {
						Report.Error(file, frame.Line, Report.Frame, $"{output.Name}: {(int)size}px frame from {frame.File}: {ex.Message}");
						complete = false;
					}
				}
				ok &= complete;

				if (frames.Count == 0) {
					Report.Error(file, output.Line, Report.Empty, $"{output.Name}: no frames rendered, not written");
					ok = false;
					continue;
				}
				try {
					using (var outfile = new FileStream(output.DestFile, FileMode.Create)) {
						if (output.Kind == OutputKind.Png) {
							outfile.Write(frames[0].Data);
						}
						else {
							var iconFile = new IconBuilder();
							foreach (var (size, data) in frames) {
								iconFile.Add(size, data);
							}
							using var writer = new BinaryWriter(outfile);
							iconFile.Write(writer);
						}
					}
					// An output missing a frame is still useful, but it must not pass as up to date next time,
					// or the failure would go unreported until a source changed. Dating it back guarantees a rerun.
					if (!complete) File.SetLastWriteTimeUtc(output.DestFile, DateTime.UnixEpoch);
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
					Report.Error(file, output.Line, Report.Write, $"{output.Name}: {ex.Message}");
					ok = false;
				}
			}
			return ok;
		}

		/// <summary>
		/// Applies the section's <c>output</c> policy to an existing file: <c>overwrite</c> always renders,
		/// and <c>newest</c> renders when the file is missing or older than the project file or any frame
		/// source. A missing source counts as newer, so its error is reported.
		/// </summary>
		private bool IsUpToDate(IconDef output) {
			if (output.Output == OutputMode.Overwrite || !File.Exists(output.DestFile)) return false;
			var written = File.GetLastWriteTimeUtc(output.DestFile);
			var sources = output.Frames.Values.Select(f => Path.Combine(output.LookupFolder, f.File)).Prepend(file);
			return sources.All(s => File.Exists(s) && File.GetLastWriteTimeUtc(s) <= written);
		}

		private static Bitmap LoadImage(string lookup, IconFrame frame, int size) {
			var file = Path.Combine(lookup, frame.File);
			if (!File.Exists(file)) throw new FileNotFoundException($"Source file not found: {file}");
			if (frame.File.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) {
				return RenderSvg(SvgDocument.Open(file), frame, size);
			}

			// TODO: look into SkiaSharp or something to make this multi-platform.
			using var bmp = (Bitmap)Image.FromFile(file);
			if (frame.Crop is RectangleF mm) {
				// The image's own resolution says how many pixels make up a millimetre.
				float px = bmp.HorizontalResolution / 25.4f;
				float py = bmp.VerticalResolution / 25.4f;
				using var snip = bmp.Crop(Rectangle.Round(new RectangleF(mm.X * px, mm.Y * py, mm.Width * px, mm.Height * py)));
				return snip.Resize(new(size, size));
			}
			return bmp.Resize(new(size, size));
		}

		/// <summary>
		/// Rasterises the document into a <paramref name="size"/> square. A <c>use</c> element is drawn on
		/// its own and fitted to the square; a <c>snip</c> region selects that part of the page instead.
		/// </summary>
		private static Bitmap RenderSvg(SvgDocument svg, IconFrame frame, int size) {
			var region = PageRegion(svg);
			if (frame.SvgElement is not null) {
				var element = FindElement(svg, frame.SvgElement)
					?? throw new KeyNotFoundException($"Element '{frame.SvgElement}' not found in {frame.File}");
				Isolate(element);
				region = DocumentBounds(element);
			}
			if (frame.Crop is RectangleF mm) {
				region = MillimetresToUserUnits(svg, mm);
			}
			if (region.Width <= 0 || region.Height <= 0) {
				throw new InvalidOperationException($"Nothing to draw for '{frame.SvgElement}' in {frame.File}");
			}

			// Re-frame the document: the region becomes the viewBox and the viewport becomes the icon square,
			// so the region is scaled uniformly to fit and centred. Drawing the page straight into a square
			// would stretch it, because pages are rarely square.
			svg.ViewBox = new SvgViewBox(region.X, region.Y, region.Width, region.Height);
			svg.AspectRatio = new SvgAspectRatio(SvgPreserveAspectRatio.xMidYMid);
			svg.Width = new SvgUnit(SvgUnitType.Pixel, size);
			svg.Height = new SvgUnit(SvgUnitType.Pixel, size);
			return svg.Draw(size, size);
		}

		/// <summary>The page in user units: its viewBox, or its width and height when it has none.</summary>
		private static RectangleF PageRegion(SvgDocument svg) {
			var box = svg.ViewBox;
			if (box.Width > 0 && box.Height > 0) {
				return new(box.MinX, box.MinY, box.Width, box.Height);
			}
			var dim = svg.GetDimensions();
			return new(0, 0, dim.Width, dim.Height);
		}

		/// <summary>Finds a drawable element by id, or failing that by its Inkscape label (the name shown in
		/// Inkscape's Layers and Objects panel).</summary>
		private static SvgVisualElement? FindElement(SvgDocument svg, string name) {
			if (svg.GetElementById(name) is SvgVisualElement byId) return byId;
			return Descendants(svg).OfType<SvgVisualElement>().FirstOrDefault(e => e.CustomAttributes
				.Any(a => a.Key.EndsWith(":label", StringComparison.Ordinal) && a.Value == name));
		}

		private static IEnumerable<SvgElement> Descendants(SvgElement element) {
			foreach (var child in element.Children) {
				yield return child;
				foreach (var grandchild in Descendants(child)) yield return grandchild;
			}
		}

		/// <summary>
		/// Hides everything that is not the element, one of its descendants or one of its ancestors, so that
		/// overlapping artwork elsewhere on the page does not bleed into the icon. Ancestors are shown so that
		/// an element on a hidden layer still renders. Inherited styles and gradients are left intact.
		/// </summary>
		private static void Isolate(SvgVisualElement element) {
			for (SvgElement current = element; current.Parent is SvgElement parent; current = parent) {
				current.Display = "inline";
				foreach (var sibling in parent.Children.OfType<SvgVisualElement>()) {
					if (sibling != current) sibling.Display = "none";
				}
			}
		}

		/// <summary>
		/// The element's bounds in the document's user space. The library's Bounds include the element's own
		/// transform but not its ancestors', so those are replayed on the way up to the root.
		/// </summary>
		private static RectangleF DocumentBounds(SvgVisualElement element) {
			var bounds = element.Bounds;
			for (var ancestor = element.Parent; ancestor is not null and not SvgDocument; ancestor = ancestor.Parent) {
				if (ancestor.Transforms is not { Count: > 0 }) continue;
				using var matrix = ancestor.Transforms.GetMatrix();
				var corners = new[] {
					new PointF(bounds.Left, bounds.Top), new PointF(bounds.Right, bounds.Top),
					new PointF(bounds.Right, bounds.Bottom), new PointF(bounds.Left, bounds.Bottom),
				};
				matrix.TransformPoints(corners);
				bounds = RectangleF.FromLTRB(
					corners.Min(c => c.X), corners.Min(c => c.Y),
					corners.Max(c => c.X), corners.Max(c => c.Y));
			}
			return bounds;
		}

		/// <summary>
		/// Maps a region given in millimetres of the physical page onto the page's user units. A page without a
		/// physical size (percentage or unitless dimensions) is taken to be 96 dpi pixels, as browsers do.
		/// </summary>
		private static RectangleF MillimetresToUserUnits(SvgDocument svg, RectangleF mm) {
			var page = PageRegion(svg);
			float unitsPerMmX = page.Width / ToMillimetres(svg.Width, page.Width);
			float unitsPerMmY = page.Height / ToMillimetres(svg.Height, page.Height);
			return new(
				page.X + mm.X * unitsPerMmX, page.Y + mm.Y * unitsPerMmY,
				mm.Width * unitsPerMmX, mm.Height * unitsPerMmY);
		}

		private static float ToMillimetres(SvgUnit length, float fallbackPx) {
			const float mmPerPx = 25.4f / 96;
			return length.Type switch {
				SvgUnitType.Percentage or SvgUnitType.Em or SvgUnitType.Ex => fallbackPx * mmPerPx,
				_ => length.ToDeviceValue(null, UnitRenderingType.Horizontal, null) * mmPerPx,
			};
		}
	}

	/// <summary>A project file line that could not be understood. <see cref="Line"/> is 1-based.</summary>
	class IconProjectParseError(string message, int line) : Exception(message) {
		public int Line { get; } = line;
	}

	/// <summary>One output file of a project: an icon holding several frames, or a single PNG image.</summary>
	class IconDef {
		/// <summary>The section name as written in the project file, used in messages.</summary>
		public string Name = "";
		public string DestFile = "";
		public OutputKind Kind = OutputKind.Ico;
		public OutputMode Output = OutputMode.Newest;
		public string LookupFolder = "";
		public Dictionary<(IconSize, IconDepth), IconFrame> Frames = [];
		/// <summary>The project file line the section starts on.</summary>
		public int Line;
	}
	class IconFrame(string source) {
		public string File { get; set; } = source;
		/// <summary>The project file line the frame is defined on.</summary>
		public int Line { get; set; }
		public string? SvgElement { get; set; }
		public RectangleF? Crop { get; set; }
		public Color Mask { get; set; } = Color.Magenta;
		public Color Invert { get; set; } = Color.Teal;
	}
	enum OutputKind {
		Ico,
		Png,
	}
	/// <summary>When an output is rendered and written. Set per section with <c>output=</c>.</summary>
	enum OutputMode {
		/// <summary>When it is missing, or older than the project file or any frame source. The default.</summary>
		Newest,
		/// <summary>On every run.</summary>
		Overwrite,
		/// <summary>Never: the section is skipped entirely.</summary>
		None,
	}
	enum IconSize {
		S16 = 16,
		S24 = 24,
		S32 = 32,
		S48 = 48,
		S64 = 64,
		S128 = 128,
		S256 = 256,
	}
	enum IconDepth {
		BlackWhite = 1,
		Palette = 8,
		RGB = 24,
		True = 32,
	}
}
