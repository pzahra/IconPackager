using System;
using System.Drawing;
using System.IO;
using PatTech.IcoNet;
using Xunit;

namespace PatTech.IconPackager.Tests {
	public class RenderTests {
		static readonly Options Plain = new();

		[Fact]
		public void BuildsAnIconThenSkipsItWhileUpToDate() {
			using var dir = new TempDir();
			Images.WritePng(dir, "a.png", 64, Color.Red);
			var project = ProjectParser.Parse(dir.Write("icons.ini", "[a.ico]\n32-true=a.png\n16-bw=a.png\n"));
			Assert.True(project.RenderAll(Plain));

			string ico = dir.At("a.ico");
			var icon = IconReader.Read(ico);
			Assert.Equal(2, icon.Frames.Count);
			Assert.Equal(32, icon.Frames[0].Width);
			Assert.Equal(32, icon.Frames[0].BitCount);
			Assert.Equal(16, icon.Frames[1].Width);
			Assert.Equal(1, icon.Frames[1].BitCount);
			using (var frame = icon.Frames[0].ToBitmap()) {
				Assert.Equal(Color.Red.ToArgb(), frame.GetPixel(16, 16).ToArgb());
			}
			Assert.False(File.Exists(ico + ".tmp"));

			// Nothing changed, so nothing is rewritten.
			var written = File.GetLastWriteTimeUtc(ico);
			Assert.True(project.RenderAll(Plain));
			Assert.Equal(written, File.GetLastWriteTimeUtc(ico));

			// A newer source is.
			File.SetLastWriteTimeUtc(dir.At("a.png"), written.AddSeconds(5));
			Assert.True(project.RenderAll(Plain));
			Assert.True(File.GetLastWriteTimeUtc(ico) > written);
		}

		[Fact]
		public void MissingSourceIsReportedAndTheOutputDatedBack() {
			using var dir = new TempDir();
			Images.WritePng(dir, "a.png", 64, Color.Red);
			var project = ProjectParser.Parse(dir.Write("icons.ini", "[a.ico]\n32-true=a.png\n16-true=missing.png\n"));
			using var errors = new ErrorCapture();

			Assert.False(project.RenderAll(Plain));
			Assert.Contains("icons.ini(3): error IP1002:", errors.Text);
			string ico = dir.At("a.ico");
			Assert.Single(IconReader.Read(ico).Frames);
			Assert.Equal(DateTime.UnixEpoch, File.GetLastWriteTimeUtc(ico));
		}

		[Fact]
		public void NothingIsWrittenWhenNoFrameRenders() {
			using var dir = new TempDir();
			var project = ProjectParser.Parse(dir.Write("icons.ini", "[a.ico]\n32-true=missing.png\n"));
			using var errors = new ErrorCapture();

			Assert.False(project.RenderAll(Plain));
			Assert.Contains("error IP1002:", errors.Text);
			Assert.Contains("icons.ini(1): error IP1003:", errors.Text);
			Assert.False(File.Exists(dir.At("a.ico")));
		}

		[Fact]
		public void FailedWriteLeavesTheOldOutputUntouched() {
			using var dir = new TempDir();
			Images.WritePng(dir, "a.png", 64, Color.Red);
			var project = ProjectParser.Parse(dir.Write("icons.ini", "[a.ico]\n32-true=a.png\n"));
			Assert.True(project.RenderAll(Plain));
			string ico = dir.At("a.ico");
			byte[] before = File.ReadAllBytes(ico);
			File.SetLastWriteTimeUtc(dir.At("a.png"), DateTime.UtcNow.AddMinutes(1));

			using var errors = new ErrorCapture();
			using (File.Open(ico, FileMode.Open, FileAccess.Read, FileShare.None)) {
				Assert.False(project.RenderAll(Plain));
			}
			Assert.Contains("icons.ini(1): error IP1004:", errors.Text);
			Assert.Equal(before, File.ReadAllBytes(ico));
			Assert.False(File.Exists(ico + ".tmp"));
		}

		[Fact]
		public void WritesPngOutputsAndSkipsOutputNone() {
			using var dir = new TempDir();
			Images.WritePng(dir, "src.png", 64, Color.Blue);
			var project = ProjectParser.Parse(dir.Write("icons.ini", "[a.png]\n128-true=src.png\n[b.ico]\noutput=none\n32-true=src.png\n"));
			Assert.True(project.RenderAll(Plain));

			using (var image = Image.FromFile(dir.At("a.png"))) {
				Assert.Equal(128, image.Width);
				Assert.Equal(128, image.Height);
			}
			Assert.False(File.Exists(dir.At("b.ico")));
		}

		[Fact]
		public void PicturesAreWrittenBesideTheIconAndFollowIt() {
			using var dir = new TempDir();
			Images.WritePng(dir, "src.png", 64, Color.Blue);
			Directory.CreateDirectory(dir.At("out"));
			var project = ProjectParser.Parse(dir.Write("icons.ini", "[a.ico]\n32-true=src.png\n16-bw=src.png\n"), dir.At("out"));
			var options = new Options { Montage = true, Explode = true };
			Assert.True(project.RenderAll(options));

			string montage = Path.Combine(dir.At("out"), "a.montage.png");
			Assert.True(File.Exists(montage));
			Assert.True(File.Exists(Path.Combine(dir.At("out"), "a.32-true.png")));
			Assert.True(File.Exists(Path.Combine(dir.At("out"), "a.16-bw.png")));
			using (var image = Image.FromFile(Path.Combine(dir.At("out"), "a.16-bw.png"))) {
				Assert.Equal(16, image.Width);
			}

			var written = File.GetLastWriteTimeUtc(montage);
			Assert.True(project.RenderAll(options));
			Assert.Equal(written, File.GetLastWriteTimeUtc(montage));
		}

		[Fact]
		public void RendersAnSvgElementOrTheWholePage() {
			using var dir = new TempDir();
			dir.Write("a.svg",
				"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:inkscape=\"http://www.inkscape.org/namespaces/inkscape\" width=\"20\" height=\"10\" viewBox=\"0 0 20 10\">" +
				"<rect x=\"0\" y=\"0\" width=\"10\" height=\"10\" fill=\"#0000ff\"/>" +
				"<rect inkscape:label=\"Box\" x=\"10\" y=\"0\" width=\"10\" height=\"10\" fill=\"#ff0000\"/></svg>");
			var project = ProjectParser.Parse(dir.Write("icons.ini", "[a.ico]\n32-true=a.svg|use Box\n[b.ico]\n32-true=a.svg\n"));
			Assert.True(project.RenderAll(Plain));

			using (var box = IconReader.Read(dir.At("a.ico")).Frames[0].ToBitmap()) {
				Assert.Equal(Color.Red.ToArgb(), box.GetPixel(2, 16).ToArgb());
				Assert.Equal(Color.Red.ToArgb(), box.GetPixel(29, 16).ToArgb());
			}
			using (var page = IconReader.Read(dir.At("b.ico")).Frames[0].ToBitmap()) {
				// The 2:1 page is fitted to the square frame, so it spans the width and leaves the top clear.
				Assert.Equal(Color.Blue.ToArgb(), page.GetPixel(8, 16).ToArgb());
				Assert.Equal(Color.Red.ToArgb(), page.GetPixel(24, 16).ToArgb());
				Assert.Equal(0, page.GetPixel(16, 2).A);
			}
		}
	}
}
