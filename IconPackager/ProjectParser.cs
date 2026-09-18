using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PatTech.IconPackager {
	/// <summary>
	/// Reads an icon project file into an <see cref="IconProject"/>. The format is described in
	/// docs/project-format.md. Anything that cannot be understood is an <see cref="IconProjectParseError"/>
	/// carrying the line it was found on.
	/// </summary>
	static class ProjectParser {
		static readonly Regex rxIcoName = new(@"\[(.*\.(ico|png))\]");
		static readonly Regex rxIcoProp = new(@"(source|output|pack|(16|24|32|48|64|128|256)-(bw|pal|rgb|true))\s*=\s*(.*)");
		// file|param value|param value...  Anchored so that a parameter that fails to parse is reported
		// rather than silently dropped. Values run to the next '|' so element names may contain spaces.
		static readonly Regex rxIcoParam = new(@"^([^|]+?)\s*(?:\|\s*(use|snip|mask|invert)\s+([^|]+?)\s*)*$");
		static readonly Regex rxCrop = new(@"^(?:(\d+(?:\.\d+)?)(|mm|in|px)(?:[, ]+|$)){4}$");

		public static IconProject Parse(string file) {
			if (!File.Exists(file)) throw new FileNotFoundException("Project file not found", file);
			var outputs = new List<IconDef>();
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
					outputs.Add(ico);
					continue;
				}
				// Lines before the first section are ignored.
				if (ico is null) continue;

				var prop = rxIcoProp.Match(line);
				if (!prop.Success) {
					throw new IconProjectParseError("Parse Error: " + line, lineNo);
				}
				string p = prop.Groups[1].Value;
				string v = prop.Groups[4].Value;
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
				}

				var (size, depth) = p == "pack"
					? (IconSize.S256, IconDepth.True)
					: ((IconSize)int.Parse(prop.Groups[2].Value, CultureInfo.InvariantCulture), ParseDepth(prop.Groups[3].Value));
				var frame = ParseFrame(v, lineNo);
				// A PNG holds one image, so a second frame of a different size has nowhere to go.
				if (ico.Kind == OutputKind.Png && ico.Frames.Count > 0 && !ico.Frames.ContainsKey((size, depth))) {
					throw new IconProjectParseError("Parse Error: a .png output takes a single frame: " + line, lineNo);
				}
				ico.Frames[(size, depth)] = frame;
			}
			return new IconProject(file, outputs);
		}

		private static IconDepth ParseDepth(string token) => token switch {
			"bw" => IconDepth.BlackWhite,
			"pal" => IconDepth.Palette,
			"rgb" => IconDepth.RGB,
			"true" => IconDepth.True,
			_ => throw new UnreachableException(),
		};

		/// <summary>The value of a frame line: a source file followed by <c>|option value</c> pairs.</summary>
		private static IconFrame ParseFrame(string value, int lineNo) {
			var param = rxIcoParam.Match(value);
			if (!param.Success) {
				throw new IconProjectParseError("Parse Error: " + value, lineNo);
			}
			var frame = new IconFrame(param.Groups[1].Value) { Line = lineNo };
			foreach (var (par, val) in param.Groups[2].Captures
				.Select((c, i) => (c.Value, param.Groups[3].Captures[i].Value))) {
				switch (par) {
					case "use":
						frame.SvgElement = val;
						break;
					case "snip":
						frame.Crop = ParseCrop(val, lineNo);
						break;
					case "mask":
						frame.Mask = ParseKey(val, lineNo);
						break;
					case "invert":
						frame.Invert = ParseKey(val, lineNo);
						break;
				}
			}
			return frame;
		}

		/// <summary>
		/// A <c>snip</c> value: x, y, width and height, returned in millimetres. Each number may carry a unit,
		/// <c>mm</c> (the default), <c>in</c>, or <c>px</c> at 96 to the inch.
		/// </summary>
		private static RectangleF ParseCrop(string value, int lineNo) {
			var crop = rxCrop.Match(value);
			if (!crop.Success) {
				throw new IconProjectParseError("Parse Error: " + value, lineNo);
			}
			var mm = new float[4];
			for (int i = 0; i < mm.Length; ++i) {
				mm[i] = float.Parse(crop.Groups[1].Captures[i].Value, CultureInfo.InvariantCulture);
				switch (crop.Groups[2].Captures[i].Value) {
					case "in": mm[i] *= 25.4f; break;
					case "px": mm[i] *= 25.4f / 96; break;
				}
			}
			return new(mm[0], mm[1], mm[2], mm[3]);
		}

		/// <summary>A colour key value: <c>none</c> gives <see cref="Color.Empty"/>, anything else must be an HTML colour.</summary>
		private static Color ParseKey(string value, int lineNo) {
			if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) return Color.Empty;
			try {
				var colour = ColorTranslator.FromHtml(value);
				if (!colour.IsEmpty) return colour;
			}
			catch (Exception) {
				// FromHtml throws for unknown names and bad hex; the parse error below says which value.
			}
			throw new IconProjectParseError("Parse Error: not a colour: " + value, lineNo);
		}
	}
}
