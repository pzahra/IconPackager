using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using PatTech.IconPackager.Tests;
using Xunit;

namespace PatTech.IcoNet.Tests {
	public class IconReaderTests {
		[Fact]
		public void DecodesAPaletteFrameWithItsMaskAndInvertingPixels() {
			using var art = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
			for (int y = 0; y < 4; ++y) {
				art.SetPixel(0, y, Color.Transparent);
				art.SetPixel(1, y, Color.Red);
				art.SetPixel(2, y, Color.Red);
				art.SetPixel(3, y, Color.Teal);
			}
			var icon = new IconBuilder();
			icon.Add(4, art.GetBmpData(8, Color.Teal));
			var frame = Assert.Single(IconReader.Read(Images.Bytes(icon)).Frames);

			Assert.Equal(4, frame.Width);
			Assert.Equal(8, frame.BitCount);
			Assert.False(frame.IsPng);
			using (var painted = frame.ToBitmap(Color.Magenta)) {
				Assert.Equal(0, painted.GetPixel(0, 0).A);
				Assert.Equal(Color.Red.ToArgb(), painted.GetPixel(1, 0).ToArgb());
				Assert.Equal(Color.Magenta.ToArgb(), painted.GetPixel(3, 0).ToArgb());
			}
			using (var plain = frame.ToBitmap()) {
				Assert.Equal(0, plain.GetPixel(3, 0).A);
			}
		}

		[Fact]
		public void ThirtyTwoBitFramesKeepTheirAlpha() {
			using var art = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
			art.SetPixel(1, 1, Color.FromArgb(128, 255, 0, 0));
			art.SetPixel(2, 2, Color.Blue);
			var icon = new IconBuilder();
			icon.Add(4, art.GetBmpData());
			using var frame = IconReader.Read(Images.Bytes(icon)).Frames[0].ToBitmap();

			Assert.Equal(0, frame.GetPixel(0, 0).A);
			Assert.Equal(128, frame.GetPixel(1, 1).A);
			Assert.Equal(Color.Blue.ToArgb(), frame.GetPixel(2, 2).ToArgb());
		}

		[Fact]
		public void DecodesPngFrames() {
			using var art = Images.Solid(256, Color.Blue);
			var icon = new IconBuilder();
			icon.Add(256, art.GetPngData());
			var frame = IconReader.Read(Images.Bytes(icon)).Frames[0];

			Assert.True(frame.IsPng);
			Assert.Equal(256, frame.Width);
			using var image = frame.ToBitmap();
			Assert.Equal(256, image.Height);
			Assert.Equal(Color.Blue.ToArgb(), image.GetPixel(100, 100).ToArgb());
		}

		[Fact]
		public void RejectsDataThatIsNotAnIcon() {
			Assert.Throws<InvalidDataException>(() => IconReader.Read(new byte[] { 1, 2, 3 }));
			Assert.Throws<InvalidDataException>(() => IconReader.Read(Encoding.ASCII.GetBytes("[app.ico]\n32-true=a.png\n")));
		}

		[Fact]
		public void RejectsAFrameThatLiesOutsideTheFile() {
			// One entry whose data is said to start at byte 22 and run for 100 bytes, in a 22-byte file.
			byte[] file = { 0, 0, 1, 0, 1, 0, 16, 16, 0, 0, 1, 0, 32, 0, 100, 0, 0, 0, 22, 0, 0, 0 };
			Assert.Throws<InvalidDataException>(() => IconReader.Read(file));
		}
	}
}
