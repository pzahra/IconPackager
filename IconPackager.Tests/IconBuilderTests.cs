using System;
using System.Drawing;
using PatTech.IconPackager.Tests;
using Xunit;

namespace PatTech.IcoNet.Tests {
	public class IconBuilderTests {
		[Fact]
		public void WritesTheDirectoryAndFramesInOrder() {
			using var small = Images.Solid(16, Color.Red);
			using var big = Images.Solid(256, Color.Blue);
			byte[] dib = small.GetBmpData(), png = big.GetPngData();
			var icon = new IconBuilder();
			icon.Add(16, dib);
			icon.Add(256, png);
			byte[] file = Images.Bytes(icon);

			// Header: reserved, type 1 (icon), count.
			Assert.Equal(0, BitConverter.ToInt16(file, 0));
			Assert.Equal(1, BitConverter.ToInt16(file, 2));
			Assert.Equal(2, BitConverter.ToUInt16(file, 4));
			// First entry: 16 px, no palette, one plane, 32 bits, then length and offset just past the directory.
			Assert.Equal(16, file[6]);
			Assert.Equal(16, file[7]);
			Assert.Equal(0, file[8]);
			Assert.Equal(0, file[9]);
			Assert.Equal(1, BitConverter.ToInt16(file, 10));
			Assert.Equal(32, BitConverter.ToInt16(file, 12));
			Assert.Equal(dib.Length, BitConverter.ToInt32(file, 14));
			Assert.Equal(38, BitConverter.ToInt32(file, 18));
			// Second entry: 256 px is stored as 0, and the PNG follows the DIB.
			Assert.Equal(0, file[22]);
			Assert.Equal(0, file[23]);
			Assert.Equal(32, BitConverter.ToInt16(file, 28));
			Assert.Equal(png.Length, BitConverter.ToInt32(file, 30));
			Assert.Equal(38 + dib.Length, BitConverter.ToInt32(file, 34));
			Assert.Equal(38 + dib.Length + png.Length, file.Length);
			Assert.Equal(dib, file[38..(38 + dib.Length)]);
			Assert.Equal(png, file[(38 + dib.Length)..]);
		}

		[Fact]
		public void EntriesRecordTheDepthAndPaletteSizeOfTheFrame() {
			using var art = Images.Solid(16, Color.Red);
			var icon = new IconBuilder();
			icon.Add(16, art.GetBmpData(1));
			icon.Add(16, art.GetBmpData(8));
			icon.Add(16, art.GetBmpData(24));
			Assert.Equal(1, icon[0].BitCount);
			Assert.Equal(2, icon[0].ColorCount);
			Assert.Equal(8, icon[1].BitCount);
			Assert.Equal(0, icon[1].ColorCount); // 256 entries do not fit the byte
			Assert.Equal(24, icon[2].BitCount);
			Assert.Equal(0, icon[2].ColorCount);
		}

		[Fact]
		public void FramesOfAnotherSizeAreRejected() {
			using var art = Images.Solid(16, Color.Red);
			byte[] dib = art.GetBmpData(), png = art.GetPngData();
			var icon = new IconBuilder();
			Assert.Throws<ArgumentException>(() => icon.Add(32, dib));
			Assert.Throws<ArgumentException>(() => icon.Add(32, png));
			Assert.Equal(0, icon.ImageCount);
		}

		[Fact]
		public void DataThatIsNotAFrameIsRejected() {
			using var art = Images.Solid(16, Color.Red);
			byte[] dib = art.GetBmpData();
			var icon = new IconBuilder();
			Assert.Throws<ArgumentException>(() => icon.Add(16, new byte[10]));
			Assert.Throws<ArgumentException>(() => icon.Add(16, new byte[100]));
			Assert.Throws<ArgumentException>(() => icon.Add(16, dib[..^8])); // mask cut short
			byte[] odd = (byte[])dib.Clone();
			odd[14] = 16; // a depth icons do not use
			Assert.Throws<ArgumentException>(() => icon.Add(16, odd));
			byte[] png = art.GetPngData();
			png[12] = (byte)'X'; // no IHDR
			Assert.Throws<ArgumentException>(() => icon.Add(16, png));
		}

		[Theory]
		[InlineData(0)]
		[InlineData(257)]
		public void SizesOutsideTheFormatAreRejected(int size) {
			using var art = Images.Solid(16, Color.Red);
			var icon = new IconBuilder();
			Assert.Throws<ArgumentOutOfRangeException>(() => icon.Add(size, art.GetBmpData()));
		}

		[Fact]
		public void TheFrameCountIsBounded() {
			using var dot = Images.Solid(1, Color.Black);
			byte[] dib = dot.GetBmpData(1);
			var icon = new IconBuilder();
			for (int i = 0; i < IconBuilder.MaxFrames; ++i) {
				icon.Add(1, dib);
			}
			Assert.Equal(IconBuilder.MaxFrames, icon.ImageCount);
			Assert.Throws<InvalidOperationException>(() => icon.Add(1, dib));
		}
	}
}
