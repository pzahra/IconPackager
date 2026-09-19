using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace PatTech.IcoNet {
	/// <summary>
	/// Reads a Windows icon (<c>.ico</c>) into its frames: the counterpart of <see cref="IconBuilder"/>.
	/// </summary>
	/// <remarks>
	/// Each frame keeps its encoded data, a PNG file or a device-independent bitmap with an AND mask, and
	/// decodes itself to a 32-bit bitmap on request with <see cref="Frame.ToBitmap"/>. Frames are listed in
	/// directory order. Reading checks only the file structure; a frame that cannot be decoded throws when
	/// it is decoded, so one bad frame does not hide the others.
	/// </remarks>
	public sealed class IconReader {
		/// <summary>The frames in directory order.</summary>
		public IReadOnlyList<Frame> Frames { get; }

		private IconReader(List<Frame> frames) {
			Frames = frames;
		}

		/// <summary>Reads an icon file.</summary>
		/// <exception cref="InvalidDataException">The file is not an icon, or its directory points outside the file.</exception>
		public static IconReader Read(string path) => Read(File.ReadAllBytes(path));

		/// <summary>Reads an icon from the current position of <paramref name="stream"/> to its end.</summary>
		/// <exception cref="InvalidDataException">The data is not an icon, or its directory points outside the data.</exception>
		public static IconReader Read(Stream stream) {
			using var buffer = new MemoryStream();
			stream.CopyTo(buffer);
			return Read(buffer.ToArray());
		}

		/// <summary>Reads an icon from the bytes of an icon file.</summary>
		/// <exception cref="InvalidDataException">The data is not an icon, or its directory points outside the data.</exception>
		public static IconReader Read(byte[] file) {
			if (file.Length < 6 || BitConverter.ToInt16(file, 0) != 0 || BitConverter.ToInt16(file, 2) != (short)IconBuilder.IconType.ICO) {
				throw new InvalidDataException("Not an icon file");
			}
			int count = BitConverter.ToUInt16(file, 4);
			if (file.Length < 6 + count * 16) throw new InvalidDataException("The icon directory is truncated");
			var frames = new List<Frame>(count);
			for (int i = 0; i < count; ++i) {
				int entry = 6 + i * 16;
				int length = BitConverter.ToInt32(file, entry + 8);
				int offset = BitConverter.ToInt32(file, entry + 12);
				if (length < 0 || offset < 0 || offset > file.Length - length) {
					throw new InvalidDataException($"Frame {i} lies outside the file");
				}
				var data = new byte[length];
				Array.Copy(file, offset, data, 0, length);
				frames.Add(new Frame(file[entry], file[entry + 1], file[entry + 2], BitConverter.ToInt16(file, entry + 6), data));
			}
			return new IconReader(frames);
		}

		/// <summary>One frame of an icon: its directory entry and its encoded data.</summary>
		public sealed class Frame {
			/// <summary>Width in pixels.</summary>
			public int Width { get; }
			/// <summary>Height in pixels.</summary>
			public int Height { get; }
			/// <summary>Bits per pixel: from the bitmap header of a DIB frame, or 32 for a PNG frame.</summary>
			public int BitCount { get; }
			/// <summary>Palette size from the directory entry: 0 when there is no palette, or when it has 256 entries.</summary>
			public byte ColorCount { get; }
			/// <summary>True when the frame is a PNG file rather than a DIB.</summary>
			public bool IsPng { get; }
			/// <summary>The encoded frame as stored in the file.</summary>
			public byte[] Data { get; }

			internal Frame(byte width, byte height, byte colorCount, short bitCount, byte[] data) {
				Width = width == 0 ? 256 : width;
				Height = height == 0 ? 256 : height;
				ColorCount = colorCount;
				Data = data;
				IsPng = data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
				// The directory's depth is advisory and some writers leave it 0; the bitmap header is authoritative.
				BitCount = IsPng ? 32 : data.Length >= 16 ? BitConverter.ToInt16(data, 14) : bitCount;
			}

			/// <summary>
			/// Decodes the frame to a 32-bit bitmap with alpha. A PNG frame is decoded as it is. A DIB frame
			/// takes its colours from the XOR bitmap and its transparency from the AND mask, except that a
			/// 32-bit DIB with any alpha in it is blended by that alpha and the mask is ignored, as Windows
			/// does. A pixel the mask marks transparent but whose colour is not black inverts the screen
			/// under Windows; it is painted <paramref name="invert"/>, or left transparent when that is null.
			/// </summary>
			/// <param name="invert">The colour to paint screen-inverting pixels, or null to make them transparent.</param>
			/// <exception cref="InvalidDataException">The DIB is truncated or its header is not valid.</exception>
			/// <exception cref="NotSupportedException">The DIB is compressed, or of a depth other than 1, 4, 8, 16, 24 or 32.</exception>
			public Bitmap ToBitmap(Color? invert = null) {
				if (IsPng) {
					using var stream = new MemoryStream(Data);
					using var image = Image.FromStream(stream);
					return new Bitmap(image);
				}
				return DecodeDib(Data, invert);
			}

			private static Bitmap DecodeDib(byte[] d, Color? invert) {
				if (d.Length < 40) throw new InvalidDataException("The bitmap header is truncated");
				int headerSize = BitConverter.ToInt32(d, 0);
				int width = BitConverter.ToInt32(d, 4);
				int height = BitConverter.ToInt32(d, 8) / 2; // the XOR bitmap and the AND mask, stacked
				int bpp = BitConverter.ToInt16(d, 14);
				int compression = BitConverter.ToInt32(d, 16);
				int coloursUsed = BitConverter.ToInt32(d, 32);
				if (headerSize < 40 || width <= 0 || height <= 0) throw new InvalidDataException("The bitmap header is not valid");
				if (compression != 0) throw new NotSupportedException("Compressed bitmap frames are not supported");
				if (bpp != 1 && bpp != 4 && bpp != 8 && bpp != 16 && bpp != 24 && bpp != 32) {
					throw new NotSupportedException($"{bpp}-bit frames are not supported");
				}

				int paletteCount = bpp > 8 ? 0 : coloursUsed > 0 ? Math.Min(coloursUsed, 1 << bpp) : 1 << bpp;
				int xorOffset = headerSize + paletteCount * 4;
				int xorStride = (width * bpp + 31) / 32 * 4;
				int andOffset = xorOffset + xorStride * height;
				int andStride = (width + 31) / 32 * 4;
				if (d.Length < andOffset) throw new InvalidDataException("The bitmap data is truncated");
				bool hasMask = d.Length >= andOffset + andStride * height;

				var palette = new int[paletteCount];
				for (int i = 0; i < paletteCount; ++i) {
					palette[i] = Opaque | (BitConverter.ToInt32(d, headerSize + i * 4) & 0xFFFFFF);
				}

				var pixels = new int[width * height];
				bool anyAlpha = false;
				for (int y = 0; y < height; ++y) {
					int row = xorOffset + (height - 1 - y) * xorStride; // rows are stored bottom-up
					for (int x = 0; x < width; ++x) {
						int argb;
						switch (bpp) {
							case 1:
								argb = Lookup(palette, (d[row + x / 8] >> (7 - x % 8)) & 1);
								break;
							case 4:
								argb = Lookup(palette, x % 2 == 0 ? d[row + x / 2] >> 4 : d[row + x / 2] & 0xF);
								break;
							case 8:
								argb = Lookup(palette, d[row + x]);
								break;
							case 16: {
								int v = BitConverter.ToUInt16(d, row + x * 2); // 5 bits each of red, green and blue
								argb = Opaque | Expand5(v >> 10) << 16 | Expand5(v >> 5) << 8 | Expand5(v);
								break;
							}
							case 24:
								argb = Opaque | d[row + x * 3 + 2] << 16 | d[row + x * 3 + 1] << 8 | d[row + x * 3];
								break;
							default:
								argb = BitConverter.ToInt32(d, row + x * 4);
								anyAlpha |= (argb >> 24) != 0;
								break;
						}
						pixels[y * width + x] = argb;
					}
				}

				// Windows blends a 32-bit frame by its alpha channel when it has one. Every other frame is
				// opaque wherever the mask does not cut it out.
				if (!(bpp == 32 && anyAlpha)) {
					for (int i = 0; i < pixels.Length; ++i) pixels[i] |= Opaque;
					if (hasMask) {
						int paint = invert.HasValue ? invert.Value.ToArgb() : 0;
						for (int y = 0; y < height; ++y) {
							int row = andOffset + (height - 1 - y) * andStride;
							for (int x = 0; x < width; ++x) {
								if ((d[row + x / 8] & (0x80 >> (x % 8))) == 0) continue;
								int i = y * width + x;
								pixels[i] = (pixels[i] & 0xFFFFFF) == 0 ? 0 : paint;
							}
						}
					}
				}

				var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
				var bits = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
				try {
					for (int y = 0; y < height; ++y) {
						Marshal.Copy(pixels, y * width, bits.Scan0 + y * bits.Stride, width);
					}
				}
				finally {
					bitmap.UnlockBits(bits);
				}
				return bitmap;
			}

			private const int Opaque = unchecked((int)0xFF000000);

			private static int Lookup(int[] palette, int index) => index < palette.Length ? palette[index] : Opaque;

			private static int Expand5(int v) => (v & 31) * 255 / 31;
		}
	}
}
