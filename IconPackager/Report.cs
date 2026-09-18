using System;

namespace IconPackager {
	/// <summary>
	/// Writes diagnostics to standard error in MSBuild's canonical format, <c>file(line): error CODE: text</c>,
	/// so that a build step running the tool shows them in the error list and jumps to the offending line.
	/// </summary>
	static class Report {
		/// <summary>The tool was not given a project file.</summary>
		public const string Usage = "IP1000";
		/// <summary>A project file could not be read or parsed.</summary>
		public const string Project = "IP1001";
		/// <summary>A frame could not be rendered.</summary>
		public const string Frame = "IP1002";
		/// <summary>An output was not written because none of its frames rendered.</summary>
		public const string Empty = "IP1003";
		/// <summary>An output file could not be written.</summary>
		public const string Write = "IP1004";
		/// <summary>The tool failed with an exception it did not expect.</summary>
		public const string Unexpected = "IP1005";

		/// <param name="origin">The file the problem concerns, or the tool name when there is none.</param>
		/// <param name="line">The 1-based line within <paramref name="origin"/>, or 0 when not applicable.</param>
		public static void Error(string origin, int line, string code, string message) {
			string where = line > 0 ? $"{origin}({line})" : origin;
			Console.Error.WriteLine($"{where}: error {code}: {message}");
		}
	}
}
