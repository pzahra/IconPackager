using System;
using System.Collections.Generic;
using System.IO;

namespace IcoNet {
	/// <summary>
	/// Assembles a Windows icon (<c>.ico</c>) from pre-encoded frames and writes it to a stream.
	/// </summary>
	/// <remarks>
	/// Add one frame per size with <see cref="Add"/>, then call <see cref="Write"/>. Frames are written in the
	/// order they were added. Every directory entry is declared as 32 bits per pixel, so frame data should be
	/// 32-bit ARGB: either a complete PNG file (the convention for 256 px frames) or a device-independent
	/// bitmap with an AND mask, as produced by <see cref="BitmapExt.GetBmpData"/>.
	/// </remarks>
	public class IconBuilder {
		/// <summary>Number of frames added so far.</summary>
		public short ImageCount => (short)images.Count;

		private readonly List<IconEntry> images = new List<IconEntry>();

		/// <summary>Adds a square frame.</summary>
		/// <param name="size">Width and height in pixels, from 1 to 256.</param>
		/// <param name="data">The encoded frame: a PNG file, or a DIB from <see cref="BitmapExt.GetBmpData"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is outside 1 to 256.</exception>
		public void Add(int size, byte[] data) {
			if (size < 1 || size > 256) throw new ArgumentOutOfRangeException(nameof(size), "Size must be between 1 and 256");
			if (size == 256) size = 0;
			images.Add(new IconEntry(this, (byte)size, data));
		}
		/// <summary>Position of <paramref name="icon"/> in the directory, or -1 if it does not belong to this icon.</summary>
		public int IndexOf(IconEntry icon) => images.IndexOf(icon);
		/// <summary>The frame at <paramref name="index"/>, in the order the frames were added.</summary>
		public IconEntry this[int index] => images[index];

		/// <summary>Writes the complete icon file: header, directory, then each frame's data.</summary>
		/// <param name="writer">Destination, positioned where the file should begin.</param>
		public void Write(BinaryWriter writer) {
			writer.Write((short)0);
			writer.Write((short)IconType.ICO);
			writer.Write(ImageCount);
			foreach (var icon in images) {
				icon.WriteEntry(writer);
			}
			foreach (var icon in images) {
				icon.WriteData(writer);
			}
		}

		/// <summary>Resource type recorded in the file header.</summary>
		public enum IconType : short {
			/// <summary>An icon.</summary>
			ICO = 1,
			/// <summary>A cursor. Not produced by <see cref="IconBuilder"/>, which always writes icons.</summary>
			CUR = 2,
		}

		/// <summary>One frame of an icon together with its directory entry.</summary>
		public class IconEntry {
			/// <summary>Width in pixels as stored in the directory, where 0 stands for 256.</summary>
			public byte Width => size;
			/// <summary>Height in pixels as stored in the directory, where 0 stands for 256.</summary>
			public byte Height => size;
			/// <summary>Length of the frame data in bytes.</summary>
			public int Length => data.Length;
			/// <summary>Byte offset of the frame data from the start of the file, given the frames added so far.</summary>
			public int Offset {
				get {
					int index = directory.IndexOf(this);
					int dataLength = 0;
					for (int i = 0; i < index; ++i) {
						dataLength += directory[i].Length;
					}
					return 6 + directory.ImageCount * 16 + dataLength;
				}
			}

			private readonly IconBuilder directory;
			private readonly byte size;
			private readonly byte[] data;

			/// <summary>Creates an entry owned by <paramref name="owner"/>. Normally reached through <see cref="IconBuilder.Add"/>.</summary>
			/// <param name="owner">The icon the entry belongs to; used to compute <see cref="Offset"/>.</param>
			/// <param name="size">Width and height in pixels, with 0 standing for 256.</param>
			/// <param name="data">The encoded frame.</param>
			public IconEntry(IconBuilder owner, byte size, byte[] data) {
				directory = owner;
				this.size = size;
				this.data = data;
			}

			internal void WriteEntry(BinaryWriter writer) {
				writer.Write(Width);
				writer.Write(Height);
				writer.Write((byte)0); // palette length
				writer.Write((byte)0); // nothing
				writer.Write((short)1); // colour planes
				writer.Write((short)32); // bit depth
				writer.Write(Length);
				writer.Write(Offset);
			}

			internal void WriteData(BinaryWriter writer) {
				writer.Write(data);
			}
		}
	}
}
