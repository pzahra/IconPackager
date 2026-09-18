using System;

namespace PatTech.IconPackager {
	/// <summary>A project file line that could not be understood. <see cref="Line"/> is 1-based.</summary>
	class IconProjectParseError(string message, int line) : Exception(message) {
		public int Line { get; } = line;
	}
}
