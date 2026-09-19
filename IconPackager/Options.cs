namespace PatTech.IconPackager {
	/// <summary>The command-line switches. Each applies to every file named on the command line.</summary>
	sealed class Options {
		/// <summary>Where outputs are written, or null for the folder of the project or icon they come from.</summary>
		public string? OutputFolder { get; set; }
		/// <summary>Write a montage of every frame next to each icon built or given.</summary>
		public bool Montage { get; set; }
		/// <summary>Write each frame of every icon built or given as a PNG of its own.</summary>
		public bool Explode { get; set; }
	}
}
