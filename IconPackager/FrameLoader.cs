using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace PatTech.IconPackager {
	/// <summary>
	/// Renders one frame from its source file at the frame's size: SVG through <see cref="SvgRenderer"/>,
	/// anything else through System.Drawing, with the frame's colour keys and crop applied.
	/// </summary>
	static class FrameLoader {
		/// <summary>The colours classic icon editors used to mark transparent and screen-inverting pixels.</summary>
		static readonly Color DefaultMask = Color.Magenta, DefaultInvert = Color.Teal;

		public static Artwork Load(string lookup, IconFrame frame, int size) {
			var file = Path.Combine(lookup, frame.File);
			if (!File.Exists(file)) throw new FileNotFoundException($"Source file not found: {file}");
			if (frame.File.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) {
				// Vector artwork carries its own transparency, so keys apply only when written.
				var (mask, invert) = ResolveKeys(frame, hasAlpha: true);
				return Artwork.Key(SvgRenderer.Render(file, frame, size), mask, invert);
			}

			// TODO: look into SkiaSharp or something to make this multi-platform.
			using var source = (Bitmap)Image.FromFile(file);
			var (rasterMask, rasterInvert) = ResolveKeys(frame, HasAlpha(source));
			Rectangle? crop = frame.Crop is RectangleF mm ? Rectangle.Round(ToPixels(mm, source)) : null;
			// Keys are applied to the source pixels, before scaling can blend the key colours into their
			// neighbours, and on a 32-bit copy so every source format is handled the same way.
			using var keyed = Artwork.Key(new Bitmap(source), rasterMask, rasterInvert);
			return keyed.Fit(crop, size);
		}

		/// <summary>A region given in millimetres of the image, in its pixels. The image's own resolution says how many make up a millimetre.</summary>
		private static RectangleF ToPixels(RectangleF mm, Bitmap image) {
			float px = image.HorizontalResolution / 25.4f;
			float py = image.VerticalResolution / 25.4f;
			return new(mm.X * px, mm.Y * py, mm.Width * px, mm.Height * py);
		}

		/// <summary>
		/// The colour keys for a frame. A key written in the project file applies to any source, and <c>none</c>
		/// (stored as <see cref="Color.Empty"/>) disables it. When nothing is written, artwork that has no
		/// transparency of its own gets the classic defaults, magenta for transparent and teal for inverting
		/// pixels; artwork with an alpha channel is left as drawn.
		/// </summary>
		private static (Color? Mask, Color? Invert) ResolveKeys(IconFrame frame, bool hasAlpha) =>
			(Resolve(frame.Mask, DefaultMask, hasAlpha), Resolve(frame.Invert, DefaultInvert, hasAlpha));

		private static Color? Resolve(Color? key, Color fallback, bool hasAlpha) => key switch {
			null => hasAlpha ? null : fallback,
			{ IsEmpty: true } => null,
			_ => key,
		};

		/// <summary>Whether the image can express transparency itself: an alpha channel, or a palette entry that is not opaque.</summary>
		private static bool HasAlpha(Bitmap bmp) =>
			Image.IsAlphaPixelFormat(bmp.PixelFormat)
			|| (bmp.PixelFormat & PixelFormat.Indexed) != 0 && bmp.Palette.Entries.Any(c => c.A < 255);
	}
}
