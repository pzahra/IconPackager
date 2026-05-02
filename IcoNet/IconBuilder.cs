using System;
using System.Collections.Generic;
using System.IO;

namespace IcoNet {
	public class IconBuilder {
		public short ImageCount => (short)images.Count;

		private readonly List<IconEntry> images = new List<IconEntry>();
		public void Add(int size, byte[] data) {
			if (size < 1 || size > 256) throw new ArgumentOutOfRangeException(nameof(size), "Size must be between 1 and 256");
			if (size == 256) size = 0;
			images.Add(new IconEntry(this, (byte)size, data));
		}
		public int IndexOf(IconEntry icon) => images.IndexOf(icon);
		public IconEntry this[int index] => images[index];

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

		public enum IconType : short { ICO = 1, CUR = 2 }

		public class IconEntry {
			public byte Width => size;
			public byte Height => size;
			public int Length => data.Length;
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
