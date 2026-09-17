using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace IcoNet {
	public static class BitmapExt {
		public static Bitmap Crop(this Image image, Rectangle rect) {
			var nb = new Bitmap(rect.Width, rect.Height);
			using Graphics g = Graphics.FromImage(nb);
			// Copy by pixel rectangles: drawing at a point would rescale an image whose DPI differs from the target's.
			g.DrawImage(image, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);
			return nb;
		}

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

		public static byte[] GetPngData(this Image bmp) {
			using var stream = new MemoryStream();
			bmp.Save(stream, ImageFormat.Png);
			return stream.ToArray();
		}

		public static byte[] GetBmpData(this Bitmap bmp) {
			bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
			var data = bmp.LockBits(
				new Rectangle(Point.Empty, bmp.Size),
				ImageLockMode.ReadWrite,
				bmp.PixelFormat);
			var argb = new byte[data.Stride * data.Height];
			System.Runtime.InteropServices.Marshal.Copy(data.Scan0, argb, 0, argb.Length);
			bmp.UnlockBits(data);
			bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);

			using var stream = new MemoryStream();
			using var writer = new BinaryWriter(stream);
			WriteHeader(writer, bmp.Size);
			writer.Write(argb);
			writer.Write(GetMask(argb, bmp.Width).ToArray());
			return stream.ToArray();
		}

		private static void WriteHeader(BinaryWriter writer, Size size) {
			//bitmap info header, not bitmap file header
			//height must be double actual height
			int dataLength = size.Width * size.Height * 4;
			int maskWidth = size.Width + size.Width % 32;
			int maskLength = maskWidth / 8 * size.Height;

			writer.Write(40); // header length
			writer.Write(size.Width);
			writer.Write(size.Height * 2);
			writer.Write((short)1); // colour planes
			writer.Write((short)32); // bit depth
			writer.Write(0); // compression type (none)
			writer.Write(dataLength + maskLength);
			writer.Write(0); // horizontal resolution
			writer.Write(0); // vertical resolution
			writer.Write(0); // palette length if present
			writer.Write(0); // number of important palette colours (unused)
		}

		public static IEnumerable<byte> GetMask(byte[] pixels, int width) {
			int pad = width % 32;
			byte threshold = 1;
			int x = 32;
			int p = 0;
			uint m = 0;
			for (int l = 3; l < pixels.Length; l += 4) {
				if (pixels[l] < threshold) ++m;
				if (++p % width == 0) {
					m <<= pad;
					x = 1;
				}
				if (--x == 0) {
					var b = BitConverter.GetBytes(m);
					yield return b[3];
					yield return b[2];
					yield return b[1];
					yield return b[0];
					m = 0;
					x = 32;
				}
				m <<= 1;
			}
		}
	}
}
