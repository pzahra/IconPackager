using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PatTech.IcoNet;

namespace PatTech.IconPackager {
	/// <summary>The outputs a project file describes, and the means to render and write them.</summary>
	class IconProject {
		readonly string file;
		readonly List<IconDef> outputs;

		public IconProject(string file, List<IconDef> outputs) {
			this.file = file;
			this.outputs = outputs;
		}

		/// <summary>
		/// Reads a project file. See <see cref="ProjectParser"/> for what it may contain. Outputs are written
		/// to <paramref name="outputFolder"/>, or next to the project file when that is null.
		/// </summary>
		public static IconProject FromFile(string file, string? outputFolder = null) => ProjectParser.Parse(file, outputFolder);

		/// <summary>
		/// Renders every output that is due under its <c>output</c> policy and writes each one that has at
		/// least one frame, followed by the montage and exploded frames <paramref name="options"/> ask for.
		/// Problems are reported on standard error in MSBuild's format, one line each, and rendering carries
		/// on with the next frame or output.
		/// </summary>
		/// <returns>True when every frame of every output was rendered and written.</returns>
		public bool RenderAll(Options options) {
			bool ok = true;
			foreach (var output in outputs) {
				if (output.Output == OutputMode.None) {
					Console.WriteLine($"{output.Name} skipped (output=none)");
					continue;
				}
				if (IsUpToDate(output)) {
					Console.WriteLine($"{output.Name} is up to date");
					// The pictures may still be missing, if they were asked for after the icon was built.
					ok &= Picture(output, options);
					continue;
				}

				var frames = new List<(int Size, byte[] Data)>();
				bool complete = true;
				foreach (var ((size, depth), frame) in output.Frames) {
					try {
						using var artwork = FrameLoader.Load(output.LookupFolder, frame, (int)size);
						bool asPng = output.Kind == OutputKind.Png || size is IconSize.S256;
						frames.Add(((int)size, asPng ? artwork.Image.GetPngData() : artwork.GetBmpData((int)depth)));
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
					Write(output, frames);
					ok &= Picture(output, options);
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

		/// <summary>Writes the output file: the single PNG, or the frames assembled into an icon.</summary>
		private static void Write(IconDef output, List<(int Size, byte[] Data)> frames) {
			using var outfile = new FileStream(output.DestFile, FileMode.Create);
			if (output.Kind == OutputKind.Png) {
				outfile.Write(frames[0].Data);
				return;
			}
			var iconFile = new IconBuilder();
			foreach (var (size, data) in frames) {
				iconFile.Add(size, data);
			}
			using var writer = new BinaryWriter(outfile);
			iconFile.Write(writer);
		}

		/// <summary>The montage and exploded frames of an icon output, when asked for. A PNG output is its own picture.</summary>
		private static bool Picture(IconDef output, Options options) {
			if (output.Kind != OutputKind.Ico || !(options.Montage || options.Explode)) return true;
			return IconInspector.Inspect(output.DestFile, null, options.Montage, options.Explode);
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
	}
}
