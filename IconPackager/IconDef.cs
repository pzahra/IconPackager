using System.Collections.Generic;

namespace PatTech.IconPackager {
	/// <summary>One output file of a project: an icon holding several frames, or a single PNG image.</summary>
	class IconDef {
		/// <summary>The section name as written in the project file, used in messages.</summary>
		public string Name = "";
		public string DestFile = "";
		public OutputKind Kind = OutputKind.Ico;
		public OutputMode Output = OutputMode.Newest;
		public string LookupFolder = "";
		public Dictionary<(IconSize, IconDepth), IconFrame> Frames = [];
		/// <summary>The project file line the section starts on.</summary>
		public int Line;
	}

	/// <summary>What a section writes, decided by the extension of its name.</summary>
	enum OutputKind {
		/// <summary>A Windows icon holding every frame of the section.</summary>
		Ico,
		/// <summary>A single PNG image from the section's one frame.</summary>
		Png,
	}

	/// <summary>When an output is rendered and written. Set per section with <c>output=</c>.</summary>
	enum OutputMode {
		/// <summary>When it is missing, or older than the project file or any frame source. The default.</summary>
		Newest,
		/// <summary>On every run.</summary>
		Overwrite,
		/// <summary>Never: the section is skipped entirely.</summary>
		None,
	}
}
