using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PatTech.IcoNet;

namespace PatTech.IconPackager {
	/// <summary>
	/// A frame's pixels, plus an optional plane that is opaque where the frame should invert the screen.
	/// Those pixels are transparent in <see cref="Image"/>, so a PNG made from it shows nothing there.
	/// </summary>
	sealed class Artwork(Bitmap image, Bitmap? invert, Color invertKey) : IDisposable {
		public Bitmap Image => image;

		/// <summary>
		/// Applies colour keys to a 32-bit ARGB bitmap in place: pixels of the mask colour become transparent,
		/// and pixels of the invert colour become transparent too while being recorded on a separate plane.
		/// The bitmap is owned by the returned <see cref="Artwork"/>.
		/// </summary>
		public static Artwork Key(Bitmap image, Color? mask, Color? invert) {
			if (mask is null && invert is null) return new Artwork(image, null, default);
			var pixels = ReadArgb(image);
			int? maskKey = mask?.ToArgb(), invertKey = invert?.ToArgb();
			int[]? plane = invert is null ? null : new int[pixels.Length];
			for (int i = 0; i < pixels.Length; ++i) {
				if (pixels[i] == maskKey) {
					pixels[i] = 0;
				}
				else if (pixels[i] == invertKey) {
					pixels[i] = 0;
					plane![i] = unchecked((int)0xFFFFFFFF);
				}
			}
			WriteArgb(image, pixels);
			Bitmap? planeBitmap = null;
			if (plane is not null) {
				planeBitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
				WriteArgb(planeBitmap, plane);
			}
			return new Artwork(image, planeBitmap, invert ?? default);
		}

		/// <summary>Crops (when <paramref name="crop"/> is given) and scales both layers to a <paramref name="size"/> square.</summary>
		public Artwork Fit(Rectangle? crop, int size) =>
			new(Fit(image, crop, size), invert is null ? null : Fit(invert, crop, size), invertKey);

		/// <summary>Encodes the frame as an icon DIB, with the plane's pixels as screen-inverting pixels.</summary>
		public byte[] GetBmpData(int bitCount) {
			if (invert is null) return image.GetBmpData(bitCount);
			// Painting the inverting pixels in the key colour lets the encoder pick them out exactly.
			Paint(image, invert, invertKey);
			return image.GetBmpData(bitCount, invertKey);
		}

		public void Dispose() {
			image.Dispose();
			invert?.Dispose();
		}

		private static Bitmap Fit(Bitmap bmp, Rectangle? crop, int size) {
			if (crop is not Rectangle rect) return bmp.Resize(new(size, size));
			using var snip = bmp.Crop(rect);
			return snip.Resize(new(size, size));
		}

		/// <summary>Sets <paramref name="target"/> to <paramref name="colour"/> wherever <paramref name="plane"/> is at least half opaque.</summary>
		private static void Paint(Bitmap target, Bitmap plane, Color colour) {
			var pixels = ReadArgb(target);
			var marks = ReadArgb(plane);
			int argb = colour.ToArgb();
			for (int i = 0; i < pixels.Length; ++i) {
				if ((uint)marks[i] >> 24 >= 128) pixels[i] = argb;
			}
			WriteArgb(target, pixels);
		}

		/// <summary>The pixels of a 32-bit ARGB bitmap, top row first. Rows of that format are never padded, so one copy covers them all.</summary>
		private static int[] ReadArgb(Bitmap bmp) {
			var data = bmp.LockBits(new Rectangle(Point.Empty, bmp.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			try {
				var pixels = new int[bmp.Width * bmp.Height];
				Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
				return pixels;
			}
			finally {
				bmp.UnlockBits(data);
			}
		}

		private static void WriteArgb(Bitmap bmp, int[] pixels) {
			var data = bmp.LockBits(new Rectangle(Point.Empty, bmp.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
			try {
				Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
			}
			finally {
				bmp.UnlockBits(data);
			}
		}
	}
}
