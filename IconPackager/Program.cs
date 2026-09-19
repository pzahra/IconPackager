using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace PatTech.IconPackager {
	class Program {
		const string UsageText = "Usage: IconPackager [--out <folder>] [--montage] [--explode] <project.ini | icon.ico> ...";

		/// <summary>
		/// Builds every project file named on the command line and pictures every icon file named on it:
		/// exploded into one PNG per frame, drawn as a montage, or both when neither switch is given.
		/// </summary>
		/// <returns>
		/// 0 when everything was built, 1 when any project file, frame or icon failed, 2 when the command
		/// line was not understood, 3 when the tool itself failed.
		/// </returns>
		static int Main(string[] args) {
			var options = new Options();
			var inputs = new List<string>();
			for (int i = 0; i < args.Length; ++i) {
				switch (args[i]) {
					case "--out":
						if (++i == args.Length) return Usage("--out needs a folder.");
						try {
							options.OutputFolder = Path.GetFullPath(args[i]);
						}
						catch (ArgumentException ex) {
							return Usage($"--out {args[i]}: {ex.Message}");
						}
						break;
					case "--montage":
						options.Montage = true;
						break;
					case "--explode":
						options.Explode = true;
						break;
					default:
						if (args[i].StartsWith('-')) return Usage($"Option not understood: {args[i]}.");
						inputs.Add(args[i]);
						break;
				}
			}
			if (inputs.Count == 0) return Usage("No project or icon file given.");

			// A multi-targeted project builds its frameworks in parallel and runs the tool once for each, so
			// runs are serialised to keep two of them from writing the same icon at the same time.
			using var mutex = new Mutex(false, @"Local\IconPackager");
			try {
				mutex.WaitOne();
			}
			catch (AbandonedMutexException) {
				// The previous holder died; ownership has passed to this run.
			}

			try {
				if (options.OutputFolder != null) {
					try {
						Directory.CreateDirectory(options.OutputFolder);
					}
					catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
						Report.Error(options.OutputFolder, 0, Report.Write, ex.Message);
						return 1;
					}
				}

				bool ok = true;
				foreach (var input in inputs) {
					if (input.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) {
						// An icon named by itself gets both pictures; a switch narrows that to one.
						bool both = !options.Montage && !options.Explode;
						ok &= IconInspector.Inspect(input, options.OutputFolder, options.Montage || both, options.Explode || both);
						continue;
					}
					try {
						ok &= IconProject.FromFile(input, options.OutputFolder).RenderAll(options);
					}
					catch (IconProjectParseError ex) {
						Report.Error(input, ex.Line, Report.Project, ex.Message);
						ok = false;
					}
					catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
						Report.Error(input, 0, Report.Project, ex.Message);
						ok = false;
					}
				}
				return ok ? 0 : 1;
			}
			catch (Exception ex) {
				// Anything else is a bug in the tool. Reported in the same format so a build shows it as one
				// error, with the stack trace following as plain output.
				Report.Error("IconPackager", 0, Report.Unexpected, ex.ToString());
				return 3;
			}
			finally {
				mutex.ReleaseMutex();
			}
		}

		private static int Usage(string problem) {
			Report.Error("IconPackager", 0, Report.Usage, $"{problem} {UsageText}");
			return 2;
		}
	}
}
