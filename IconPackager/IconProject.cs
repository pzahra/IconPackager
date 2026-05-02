using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IcoNet;
using Svg;

namespace IconPackager {
	class IconProject {
		readonly List<IconDef> icons = [];

		public static IconProject FromFile(string file) {
			if (!File.Exists(file)) throw new FileNotFoundException();
			var prj = new IconProject();
			Regex rxIcoName = new(@"\[(.*\.ico)\]");
			Regex rxIcoProp = new(@"(source|pack|(16|24|32|48|64|128|256)-(bw|pal|rgb|true))\s*=\s*(.*)");
			Regex rxIcoParam = new(@"(\w+:?[^|]+)(?:\|(use|snip|mask|invert) ([#a-z\d,]+))*");
			Regex rxCrop = new(@"(?:(\d+)(|mm|in|px)(?:[, ]+|$)){4}");
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
									mm[i] = Convert.ToSingle(crop.Groups[1].Captures[i].Value);
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

		public void RenderAll() {
			foreach (var icon in icons) {
				var iconFile = new IconBuilder();
				foreach (var ((size,depth),frame) in icon.Frames) {
					using var img = LoadImage(icon.LookupFolder, frame, (int)size);
					if (img is null) continue;
					iconFile.Add((int)size, size is IconSize.S256 ? img.GetPngData() : img.GetBmpData());
				}

				using var outfile = new FileStream(icon.DestFile, FileMode.Create);
				using var writer = new BinaryWriter(outfile);
				iconFile.Write(writer);
			}
		}

		private static Bitmap? LoadImage(string lookup, IconFrame frame, int size) {
			try {
				Bitmap img;
				var file = Path.Combine(lookup, frame.File);
				if (frame.File.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) {
					var svg = SvgDocument.Open(file);
					// TODO:
					// select an element by name or crop region
					img = svg.Draw(size, size);
				}
				else {
					// TODO: look into SkiaSharp or something to make this multi-platform.
					using var bmp = (Bitmap)Image.FromFile(file);
					img = bmp.Resize(new(size, size));
				}
				return img;
			}
			catch(Exception ex) {
				Console.Error.WriteLine(ex.Message);
				return null;
			}
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
