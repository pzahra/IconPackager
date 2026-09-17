using System;
using System.IO;

namespace IconPackager {
	class Program {
		/// <summary>Builds every project file named on the command line.</summary>
		/// <returns>0 when everything was built, 1 when any project file or frame failed, 2 when no project file was given.</returns>
		static int Main(string[] args) {
			if (args.Length == 0) {
				Console.Error.WriteLine("Usage: IconPackager <project.ini> [<project.ini> ...]");
				return 2;
			}

			bool ok = true;
			foreach (var arg in args) {
				try {
					ok &= IconProject.FromFile(arg).RenderAll();
				}
				catch (Exception ex) when (ex is IconProjectParseError or IOException) {
					Console.Error.WriteLine($"{arg}: {ex.Message}");
					ok = false;
				}
			}
			return ok ? 0 : 1;
		}
	}
}
