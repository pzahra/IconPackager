using System.Drawing;

namespace PatTech.IconPackager {
	/// <summary>One frame of an output: the source file and the options written after it.</summary>
	class IconFrame(string source) {
		public string File { get; set; } = source;
		/// <summary>The project file line the frame is defined on.</summary>
		public int Line { get; set; }
		/// <summary>The SVG element to draw on its own, by id or Inkscape label. Null draws the page.</summary>
		public string? SvgElement { get; set; }
		/// <summary>The region of the source to draw, in millimetres. Null draws it all.</summary>
		public RectangleF? Crop { get; set; }
		/// <summary>The colour drawn as transparent: null when not written, <see cref="Color.Empty"/> for <c>none</c>.</summary>
		public Color? Mask { get; set; }
		/// <summary>The colour drawn as screen-inverting pixels: null when not written, <see cref="Color.Empty"/> for <c>none</c>.</summary>
		public Color? Invert { get; set; }
	}

	/// <summary>Frame sizes a project file may ask for, in pixels.</summary>
	enum IconSize {
		S16 = 16,
		S24 = 24,
		S32 = 32,
		S48 = 48,
		S64 = 64,
		S128 = 128,
		S256 = 256,
	}

	/// <summary>Frame depths a project file may ask for, in bits per pixel.</summary>
	enum IconDepth {
		BlackWhite = 1,
		Palette = 8,
		RGB = 24,
		True = 32,
	}
}
