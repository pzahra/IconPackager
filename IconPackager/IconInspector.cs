using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using PatTech.IcoNet;

namespace PatTech.IconPackager {
	/// <summary>
	/// Pictures an existing icon: a montage of every frame on one sheet, or each frame exploded into a PNG
	/// of its own. Both are derived from the icon file on disk, so they show what was actually written, and
	/// like the icons themselves they are only rewritten when missing or older than the icon.
	/// </summary>
	static class IconInspector {
		/// <summary>
		/// Writes the montage, <c>name.montage.png</c>, and the exploded frames, <c>name.size-depth.png</c>,
		/// that are asked for, next to the icon or in <paramref name="outputFolder"/>. Frames that Windows
		/// would treat as screen-inverting are painted the default invert key, teal, so that a frame fed back
		/// in with <c>invert #008080</c> reproduces them.
		/// </summary>
		/// <returns>True when everything asked for was written, or was already current.</returns>
		public static bool Inspect(string icoFile, string? outputFolder, bool montage, bool explode) {
			IconReader icon;
			try {
				icon = IconReader.Read(icoFile);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) {
				Report.Error(icoFile, 0, Report.Icon, ex.Message);
				return false;
			}
			string folder = outputFolder ?? Path.GetDirectoryName(Path.GetFullPath(icoFile))!;
			string stem = Path.GetFileNameWithoutExtension(icoFile);
			var written = File.GetLastWriteTimeUtc(icoFile);

			var frames = new List<(string Label, Bitmap? Image)>();
			try {
				bool ok = true;
				for (int i = 0; i < icon.Frames.Count; ++i) {
					var frame = icon.Frames[i];
					try {
						frames.Add((Label(frame), frame.ToBitmap(FrameLoader.DefaultInvert)));
					}
					catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentException) {
						Report.Error(icoFile, 0, Report.Frame, $"frame {i} ({Label(frame)}): {ex.Message}");
						frames.Add((Label(frame), null));
						ok = false;
					}
				}
				if (montage) ok &= WriteMontage(icoFile, frames, Path.Combine(folder, stem + ".montage.png"), written);
				if (explode) ok &= Explode(icoFile, icon, frames, folder, stem, written);
				return ok;
			}
			finally {
				foreach (var (_, image) in frames) image?.Dispose();
			}
		}

		/// <summary>The size and depth of a frame the way a project file would write them: <c>32 pal</c>, <c>256 png</c>.</summary>
		private static string Label(IconReader.Frame frame) {
			string size = frame.Width == frame.Height ? $"{frame.Width}" : $"{frame.Width}x{frame.Height}";
			string depth = frame.IsPng ? "png" : frame.BitCount switch {
				1 => "bw",
				4 => "pal4",
				8 => "pal",
				24 => "rgb",
				32 => "true",
				_ => $"{frame.BitCount}bpp",
			};
			return $"{size} {depth}";
		}

		private static bool WriteMontage(string icoFile, List<(string Label, Bitmap? Image)> frames, string path, DateTime written) {
			if (IsCurrent(path, written)) return true;
			try {
				using var sheet = Montage.Render(frames);
				sheet.Save(path, ImageFormat.Png);
				return true;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException) {
				Report.Error(icoFile, 0, Report.Write, $"{Path.GetFileName(path)}: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// One PNG per frame. A PNG frame is written byte for byte; a DIB frame is decoded. Two frames with
		/// the same size and depth, which a project file cannot express, are numbered.
		/// </summary>
		private static bool Explode(string icoFile, IconReader icon, List<(string Label, Bitmap? Image)> frames, string folder, string stem, DateTime written) {
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			bool ok = true;
			for (int i = 0; i < frames.Count; ++i) {
				var (label, image) = frames[i];
				string name = $"{stem}.{label.Replace(' ', '-')}";
				for (int n = 2; !names.Add(name); ++n) name = $"{stem}.{label.Replace(' ', '-')}-{n}";
				string path = Path.Combine(folder, name + ".png");
				if (IsCurrent(path, written)) continue;
				try {
					if (icon.Frames[i].IsPng) File.WriteAllBytes(path, icon.Frames[i].Data);
					else if (image != null) image.Save(path, ImageFormat.Png);
					else ok = false; // already reported when it failed to decode
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException) {
					Report.Error(icoFile, 0, Report.Write, $"{name}.png: {ex.Message}");
					ok = false;
				}
			}
			return ok;
		}

		/// <summary>A derived file is current when it exists and is no older than the icon it came from.</summary>
		private static bool IsCurrent(string path, DateTime iconWritten) =>
			File.Exists(path) && File.GetLastWriteTimeUtc(path) >= iconWritten;
	}
}
