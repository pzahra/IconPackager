using System;
using System.Drawing;
using System.Linq;
using PatTech.IconPackager.Tests;
using Xunit;

namespace PatTech.IcoNet.Tests {
	public class BitmapExtTests {
		[Fact]
		public void MaskMarksPixelsBelowTheOpaqueThreshold() {
			// One row of four BGRA pixels with alphas 0, 255, 127 and 128.
			byte[] pixels = { 0, 0, 0, 0, 0, 0, 0, 255, 0, 0, 0, 127, 0, 0, 0, 128 };
			Assert.Equal(new byte[] { 0x80, 0, 0, 0 }, BitmapExt.GetMask(pixels, 4).ToArray());
			Assert.Equal(new byte[] { 0xA0, 0, 0, 0 }, BitmapExt.GetMask(pixels, 4, 128).ToArray());
		}

		[Fact]
		public void MaskRejectsBadArguments() {
			Assert.Throws<ArgumentOutOfRangeException>(() => BitmapExt.GetMask(new byte[8], 0).ToArray());
			Assert.Throws<ArgumentException>(() => BitmapExt.GetMask(new byte[6], 1).ToArray());
		}

		[Fact]
		public void BmpDataRejectsDepthsIconsDoNotUse() {
			using var art = Images.Solid(4, Color.Red);
			Assert.Throws<ArgumentOutOfRangeException>(() => art.GetBmpData(16));
		}
	}
}
