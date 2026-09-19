using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PatTech.IconPackager {
	/// <summary>Draws every frame of an icon on one sheet, each over a checkerboard with its size and depth beneath.</summary>
	static class Montage {
		/// <summary>The square each frame is drawn in.</summary>
		const int Cell = 128;
		const int Gap = 10;
		const int LabelHeight = 16;
		const int Check = 8;
		const int Columns = 4;

		/// <summary>
		/// Frames up to the cell size are magnified by a whole number, so that their pixels stay square and
		/// the 1-bit and palette frames can be judged; larger frames are shrunk to fit. A null image, one
		/// that failed to decode, leaves its cell empty.
		/// </summary>
		public static Bitmap Render(IReadOnlyList<(string Label, Bitmap? Image)> frames) {
			int columns = Math.Max(1, Math.Min(Columns, frames.Count));
			int rows = Math.Max(1, (frames.Count + columns - 1) / columns);
			var sheet = new Bitmap(columns * (Cell + Gap) + Gap, rows * (Cell + Gap + LabelHeight) + Gap, PixelFormat.Format32bppArgb);
			using var g = Graphics.FromImage(sheet);
			using var font = new Font(FontFamily.GenericSansSerif, 8);
			using var light = new SolidBrush(Color.FromArgb(0xDC, 0xDC, 0xDC));
			using var dark = new SolidBrush(Color.FromArgb(0xC8, 0xC8, 0xC8));
			g.Clear(Color.White);
			g.PixelOffsetMode = PixelOffsetMode.Half;

			for (int i = 0; i < frames.Count; ++i) {
				int x = Gap + i % columns * (Cell + Gap);
				int y = Gap + i / columns * (Cell + Gap + LabelHeight);
				for (int cy = 0; cy < Cell; cy += Check) {
					for (int cx = 0; cx < Cell; cx += Check) {
						g.FillRectangle((cx + cy) / Check % 2 == 0 ? light : dark, x + cx, y + cy, Check, Check);
					}
				}
				var (label, image) = frames[i];
				if (image != null) {
					int largest = Math.Max(image.Width, image.Height);
					float scale = largest <= Cell ? Cell / largest : (float)Cell / largest;
					int width = (int)(image.Width * scale), height = (int)(image.Height * scale);
					g.InterpolationMode = scale >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
					g.DrawImage(image, new Rectangle(x + (Cell - width) / 2, y + (Cell - height) / 2, width, height));
				}
				g.DrawRectangle(Pens.Black, x, y, Cell, Cell);
				g.DrawString(label, font, Brushes.Black, x, y + Cell + 2);
			}
			return sheet;
		}
	}
}
