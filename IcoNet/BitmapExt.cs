using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace IcoNet {
	/// <summary>
	/// Helpers for turning <see cref="System.Drawing"/> images into icon frames for <see cref="IconBuilder"/>.
	/// </summary>
	/// <remarks>Windows only: System.Drawing.Common is not supported on other platforms.</remarks>
	public static class BitmapExt {
		/// <summary>Copies a rectangle of pixels into a new 32-bit ARGB bitmap.</summary>
		/// <param name="image">The source image.</param>
		/// <param name="rect">The region to copy, in pixels of the source.</param>
		public static Bitmap Crop(this Image image, Rectangle rect) {
			var nb = new Bitmap(rect.Width, rect.Height);
			using Graphics g = Graphics.FromImage(nb);
			// Copy by pixel rectangles: drawing at a point would rescale an image whose DPI differs from the target's.
			g.DrawImage(image, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);
			return nb;
		}

		/// <summary>
		/// Scales the image to <paramref name="size"/> into a new 32-bit ARGB bitmap using high-quality bicubic
		/// resampling. The aspect ratio is not preserved: a non-square image becomes a stretched square frame.
		/// </summary>
		/// <param name="image">The source image.</param>
		/// <param name="size">The output size in pixels.</param>
		public static Bitmap Resize(this Image image, Size size) {
			var nb = new Bitmap(size.Width, size.Height);
			using Graphics g = Graphics.FromImage(nb);
			g.CompositingMode = CompositingMode.SourceCopy;
			g.CompositingQuality = CompositingQuality.HighQuality;
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
			g.SmoothingMode = SmoothingMode.HighQuality;
			g.PixelOffsetMode = PixelOffsetMode.HighQuality;
			using var wrapMode = new ImageAttributes();
			wrapMode.SetWrapMode(WrapMode.TileFlipXY);
			var destRect = new Rectangle(0, 0, size.Width, size.Height);
			g.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
			return nb;
		}

		/// <summary>Encodes the image as a complete PNG file, the format used for 256 px icon frames.</summary>
		/// <param name="bmp">The frame to encode.</param>
		public static byte[] GetPngData(this Image bmp) {
			using var stream = new MemoryStream();
			bmp.Save(stream, ImageFormat.Png);
			return stream.ToArray();
		}

		/// <summary>Encodes the bitmap as a 32-bit icon DIB. See <see cref="GetBmpData(Bitmap, int)"/>.</summary>
		/// <param name="bmp">The frame, in any pixel format.</param>
		public static byte[] GetBmpData(this Bitmap bmp) => bmp.GetBmpData(32);

		/// <summary>
		/// Encodes the bitmap as an icon DIB at the given depth: a BITMAPINFOHEADER, the colour table for
		/// palette depths, the pixel rows stored bottom-up, and a 1-bit AND mask (see <see cref="GetMask"/>).
		/// </summary>
		/// <param name="bmp">The frame, in any pixel format.</param>
		/// <param name="bitCount">Bits per pixel: 1 (black and white), 4 or 8 (palette), 24 (RGB) or 32 (ARGB).</param>
		/// <remarks>
		/// At 32 bits the alpha channel is kept and the mask marks only fully transparent pixels. At lower
		/// depths the mask is the only transparency: pixels less than half opaque are transparent, the rest
		/// opaque. Palette depths reserve index 0 for black, which transparent pixels use, and fill the other
		/// entries by median-cut quantisation of the opaque colours weighted by how often each occurs; pixels
		/// then take the nearest palette colour, without dithering. Black and white is decided by luminance.
		/// </remarks>
		public static byte[] GetBmpData(this Bitmap bmp, int bitCount) {
			if (bitCount != 1 && bitCount != 4 && bitCount != 8 && bitCount != 24 && bitCount != 32) {
				throw new ArgumentOutOfRangeException(nameof(bitCount), "Bit count must be 1, 4, 8, 24 or 32");
			}
			int width = bmp.Width, height = bmp.Height;
			byte[] argb = GetArgbRows(bmp);
			byte opaque = bitCount == 32 ? (byte)1 : (byte)128;

			Color[] palette = bitCount switch {
				1 => new[] { Color.Black, Color.White },
				4 => BuildPalette(argb, 16, opaque),
				8 => BuildPalette(argb, 256, opaque),
				_ => Array.Empty<Color>(),
			};
			byte[] pixels = bitCount switch {
				32 => argb,
				24 => PackRgb(argb, width, height, opaque),
				_ => PackIndexed(argb, width, height, bitCount, palette, opaque),
			};
			byte[] mask = GetMask(argb, width, opaque).ToArray();

			using var stream = new MemoryStream();
			using var writer = new BinaryWriter(stream);
			WriteHeader(writer, bmp.Size, bitCount, pixels.Length + mask.Length);
			foreach (var colour in palette) {
				writer.Write(colour.B);
				writer.Write(colour.G);
				writer.Write(colour.R);
				writer.Write((byte)0);
			}
			writer.Write(pixels);
			writer.Write(mask);
			return stream.ToArray();
		}

		/// <summary>
		/// The 1-bit AND mask for 32-bit BGRA rows: a 1 bit where a pixel's alpha is below <paramref name="opaque"/>,
		/// most significant bit first, each row padded to a multiple of 32 bits.
		/// </summary>
		/// <param name="pixels">BGRA pixels, 4 bytes each, in the row order the mask should follow.</param>
		/// <param name="width">Row width in pixels.</param>
		/// <param name="opaque">The lowest alpha that counts as opaque. The default marks only fully transparent pixels.</param>
		public static IEnumerable<byte> GetMask(byte[] pixels, int width, byte opaque = 1) {
			int stride = RowStride(width, 1);
			int height = pixels.Length / (width * 4);
			var mask = new byte[stride * height];
			for (int y = 0; y < height; ++y) {
				for (int x = 0; x < width; ++x) {
					if (pixels[(y * width + x) * 4 + 3] < opaque) {
						mask[y * stride + x / 8] |= (byte)(0x80 >> (x % 8));
					}
				}
			}
			return mask;
		}

		/// <summary>Bytes per row for a DIB of the given width and depth: rows are padded to 32 bits.</summary>
		private static int RowStride(int width, int bitCount) => (width * bitCount + 31) / 32 * 4;

		/// <summary>The bitmap's pixels as 32-bit BGRA rows, bottom row first, with no row padding.</summary>
		private static byte[] GetArgbRows(Bitmap bmp) {
			int width = bmp.Width, height = bmp.Height, rowBytes = width * 4;
			var data = bmp.LockBits(new Rectangle(Point.Empty, bmp.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			try {
				var argb = new byte[rowBytes * height];
				for (int y = 0; y < height; ++y) {
					Marshal.Copy(data.Scan0 + y * data.Stride, argb, (height - 1 - y) * rowBytes, rowBytes);
				}
				return argb;
			}
			finally {
				bmp.UnlockBits(data);
			}
		}

		private static void WriteHeader(BinaryWriter writer, Size size, int bitCount, int imageSize) {
			// BITMAPINFOHEADER only, no file header. The height covers both the pixel rows and the mask rows.
			writer.Write(40); // header length
			writer.Write(size.Width);
			writer.Write(size.Height * 2);
			writer.Write((short)1); // colour planes
			writer.Write((short)bitCount);
			writer.Write(0); // compression (none)
			writer.Write(imageSize); // pixel rows plus mask rows
			writer.Write(0); // horizontal resolution
			writer.Write(0); // vertical resolution
			writer.Write(0); // colours used: 0 means the full table for the depth
			writer.Write(0); // important colours (all)
		}

		/// <summary>24-bit BGR rows padded to 32 bits. Transparent pixels are left black so they XOR to nothing.</summary>
		private static byte[] PackRgb(byte[] argb, int width, int height, byte opaque) {
			int stride = RowStride(width, 24);
			var rows = new byte[stride * height];
			for (int y = 0; y < height; ++y) {
				for (int x = 0; x < width; ++x) {
					int p = (y * width + x) * 4, q = y * stride + x * 3;
					if (argb[p + 3] < opaque) continue;
					rows[q] = argb[p];
					rows[q + 1] = argb[p + 1];
					rows[q + 2] = argb[p + 2];
				}
			}
			return rows;
		}

		/// <summary>
		/// Rows at 1, 4 or 8 bits per pixel padded to 32 bits: index 0 for transparent pixels, otherwise the
		/// nearest palette colour, or at 1 bit white for luminance above the midpoint and black below.
		/// </summary>
		private static byte[] PackIndexed(byte[] argb, int width, int height, int bitCount, Color[] palette, byte opaque) {
			int stride = RowStride(width, bitCount);
			var rows = new byte[stride * height];
			var nearest = new Dictionary<int, int>();
			for (int y = 0; y < height; ++y) {
				for (int x = 0; x < width; ++x) {
					int p = (y * width + x) * 4;
					if (argb[p + 3] < opaque) continue;
					int index;
					if (bitCount == 1) {
						// ITU-R BT.601 luma, scaled by 1000
						index = argb[p + 2] * 299 + argb[p + 1] * 587 + argb[p] * 114 >= 128 * 1000 ? 1 : 0;
					}
					else {
						int rgb = argb[p + 2] << 16 | argb[p + 1] << 8 | argb[p];
						if (!nearest.TryGetValue(rgb, out index)) {
							index = Nearest(palette, argb[p + 2], argb[p + 1], argb[p]);
							nearest[rgb] = index;
						}
					}
					int bit = x * bitCount;
					rows[y * stride + bit / 8] |= (byte)(index << (8 - bitCount - bit % 8));
				}
			}
			return rows;
		}

		private static int Nearest(Color[] palette, int r, int g, int b) {
			int best = 0, bestDistance = int.MaxValue;
			for (int i = 0; i < palette.Length; ++i) {
				int dr = palette[i].R - r, dg = palette[i].G - g, db = palette[i].B - b;
				int distance = dr * dr + dg * dg + db * db;
				if (distance < bestDistance) {
					bestDistance = distance;
					best = i;
				}
			}
			return best;
		}

		/// <summary>
		/// A palette of <paramref name="size"/> entries: black at index 0, the opaque colours reduced by median
		/// cut, then black padding. Transparent pixels must map to black so that they XOR to nothing on screen.
		/// </summary>
		private static Color[] BuildPalette(byte[] argb, int size, byte opaque) {
			var counts = new Dictionary<int, int>();
			for (int i = 0; i < argb.Length; i += 4) {
				if (argb[i + 3] < opaque) continue;
				int rgb = argb[i + 2] << 16 | argb[i + 1] << 8 | argb[i];
				counts.TryGetValue(rgb, out int n);
				counts[rgb] = n + 1;
			}
			counts.Remove(0); // black already sits at index 0
			var colours = counts.Select(kv => (Colour: Color.FromArgb(kv.Key | unchecked((int)0xFF000000)), Count: kv.Value)).ToList();
			var palette = new Color[size];
			int index = 1;
			foreach (var colour in colours.Count < size ? colours.Select(c => c.Colour) : MedianCut(colours, size - 1)) {
				palette[index++] = colour;
			}
			return palette;
		}

		/// <summary>
		/// Reduces weighted colours to at most <paramref name="count"/> by repeatedly splitting the box with the
		/// widest channel range at its population median, then averaging each box.
		/// </summary>
		private static IEnumerable<Color> MedianCut(List<(Color Colour, int Count)> colours, int count) {
			var boxes = new List<List<(Color Colour, int Count)>> { colours };
			while (boxes.Count < count) {
				int widest = -1, range = 0;
				Func<Color, int> channel = c => c.R;
				for (int i = 0; i < boxes.Count; ++i) {
					var box = boxes[i];
					if (box.Count < 2) continue;
					int r = box.Max(c => c.Colour.R) - box.Min(c => c.Colour.R);
					int g = box.Max(c => c.Colour.G) - box.Min(c => c.Colour.G);
					int b = box.Max(c => c.Colour.B) - box.Min(c => c.Colour.B);
					int widestHere = Math.Max(r, Math.Max(g, b));
					if (widestHere <= range) continue;
					range = widestHere;
					widest = i;
					if (r >= g && r >= b) channel = c => c.R;
					else if (g >= b) channel = c => c.G;
					else channel = c => c.B;
				}
				if (widest < 0) break;

				var split = boxes[widest];
				split.Sort((a, b) => channel(a.Colour) - channel(b.Colour));
				int half = split.Sum(c => c.Count) / 2, weight = 0, cut = 0;
				while (cut < split.Count - 1 && weight + split[cut].Count <= half) {
					weight += split[cut++].Count;
				}
				if (cut == 0) cut = 1;
				boxes[widest] = split.GetRange(0, cut);
				boxes.Add(split.GetRange(cut, split.Count - cut));
			}
			return boxes.Select(box => {
				long total = box.Sum(c => (long)c.Count);
				return Color.FromArgb(
					(int)(box.Sum(c => (long)c.Colour.R * c.Count) / total),
					(int)(box.Sum(c => (long)c.Colour.G * c.Count) / total),
					(int)(box.Sum(c => (long)c.Colour.B * c.Count) / total));
			});
		}
	}
}
