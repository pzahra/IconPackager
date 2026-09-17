using System;
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
		readonly List<IconDef> icons = [];

		public static IconProject FromFile(string file) {
			if (!File.Exists(file)) throw new FileNotFoundException("Project file not found", file);
			var prj = new IconProject();
			Regex rxIcoName = new(@"\[(.*\.ico)\]");
			Regex rxIcoProp = new(@"(source|pack|(16|24|32|48|64|128|256)-(bw|pal|rgb|true))\s*=\s*(.*)");
			// file|param value|param value...  Anchored so that a parameter that fails to parse is reported
			// rather than silently dropped. Values run to the next '|' so element names may contain spaces.
			Regex rxIcoParam = new(@"^([^|]+?)\s*(?:\|\s*(use|snip|mask|invert)\s+([^|]+?)\s*)*$");
			Regex rxCrop = new(@"^(?:(\d+(?:\.\d+)?)(|mm|in|px)(?:[, ]+|$)){4}$");
			IconDef? ico = null;
			string folder = Path.GetDirectoryName(file)!;
			foreach (var line in File.ReadAllLines(file)
				.Select(l => l.Split(';')[0].Trim())
				.Where(l => !string.IsNullOrEmpty(l))
			) {
				var section = rxIcoName.Match(line);
				if (section.Success) {
					ico = new() {
						DestFile = Path.Combine(folder, section.Groups[1].Value),
						LookupFolder = folder,
					};
					prj.icons.Add(ico);
					continue;
				}

				if (ico is not null) {
					var prop = rxIcoProp.Match(line);
					if (!prop.Success) {
						throw new IconProjectParseError("Parse Error: " + line);
					}
					string p = prop.Groups[1].Value;
					string v = prop.Groups[4].Value;
					IconSize size;
					IconDepth depth;
					switch (p) {
						case "source":
							ico.LookupFolder = Path.Combine(ico.LookupFolder, v);
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
						throw new IconProjectParseError("Parse Error: " + v);
					}
					var frame = new IconFrame(param.Groups[1].Value);
					foreach (var (par, val) in param.Groups[2].Captures
						.Select((c, i) => (c.Value, param.Groups[3].Captures[i].Value))) {
						switch (par) {
							case "use":
								frame.SvgElement = val;
								break;
							case "snip":
								var crop = rxCrop.Match(val);
								if (!crop.Success) {
									throw new IconProjectParseError("Parse Error: " + val);
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
					ico.Frames[(size, depth)] = frame;
				}
			}
			return prj;
		}

		private IconProject() { }

		/// <summary>
		/// Renders every icon and writes each one that has at least one frame. Problems are reported on
		/// standard error, one line each, and rendering carries on with the next frame or icon.
		/// </summary>
		/// <returns>True when every frame of every icon was rendered and written.</returns>
		public bool RenderAll() {
			bool ok = true;
			foreach (var icon in icons) {
				var iconFile = new IconBuilder();
				foreach (var ((size, depth), frame) in icon.Frames) {
					try {
						using var img = LoadImage(icon.LookupFolder, frame, (int)size);
						iconFile.Add((int)size, size is IconSize.S256 ? img.GetPngData() : img.GetBmpData());
					}
					catch (Exception ex) {
						Console.Error.WriteLine($"{icon.DestFile}: {(int)size}px frame from {frame.File}: {ex.Message}");
						ok = false;
					}
				}

				if (iconFile.ImageCount == 0) {
					Console.Error.WriteLine($"{icon.DestFile}: no frames rendered, icon not written");
					ok = false;
					continue;
				}
				try {
					using var outfile = new FileStream(icon.DestFile, FileMode.Create);
					using var writer = new BinaryWriter(outfile);
					iconFile.Write(writer);
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
					Console.Error.WriteLine($"{icon.DestFile}: {ex.Message}");
					ok = false;
				}
			}
			return ok;
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

	class IconProjectParseError(string message) : Exception(message) { }

	class IconDef {
		public string DestFile = "";
		public string LookupFolder = "";
		public Dictionary<(IconSize, IconDepth), IconFrame> Frames = [];

	}
	class IconFrame(string source) {
		public string File { get; set; } = source;
		public string? SvgElement { get; set; }
		public RectangleF? Crop { get; set; }
		public Color Mask { get; set; } = Color.Magenta;
		public Color Invert { get; set; } = Color.Teal;
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
