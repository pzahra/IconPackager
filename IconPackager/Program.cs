using System;
using System.IO;
using System.Threading;

namespace IconPackager {
	class Program {
		/// <summary>Builds every project file named on the command line.</summary>
		/// <returns>
		/// 0 when everything was built, 1 when any project file or frame failed, 2 when no project file was
		/// given, 3 when the tool itself failed.
		/// </returns>
		static int Main(string[] args) {
			if (args.Length == 0) {
				Report.Error("IconPackager", 0, Report.Usage, "No project file given. Usage: IconPackager <project.ini> [<project.ini> ...]");
				return 2;
			}

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
				bool ok = true;
				foreach (var arg in args) {
					try {
						ok &= IconProject.FromFile(arg).RenderAll();
					}
					catch (IconProjectParseError ex) {
						Report.Error(arg, ex.Line, Report.Project, ex.Message);
						ok = false;
					}
					catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
						Report.Error(arg, 0, Report.Project, ex.Message);
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
	}
}
