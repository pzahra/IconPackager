using System.Drawing;
using System.IO;
using System.Linq;
using Xunit;

namespace PatTech.IconPackager.Tests {
	public class ParserTests {
		static IconProject Parse(TempDir dir, string text) => ProjectParser.Parse(dir.Write("icons.ini", text));

		static IconProjectParseError Error(string text) {
			using var dir = new TempDir();
			string file = dir.Write("icons.ini", text);
			return Assert.Throws<IconProjectParseError>(() => ProjectParser.Parse(file));
		}

		[Fact]
		public void ReadsSectionsAndFrames() {
			using var dir = new TempDir();
			var project = Parse(dir, "; comment\n[app.ico]\nsource=art\npack=logo.svg|use AppIcon\n32-pal=old.bmp ; trailing comment\n\n[pkg.png]\n128-true=logo.svg\n");

			Assert.Equal(2, project.Outputs.Count);
			var app = project.Outputs[0];
			Assert.Equal("app.ico", app.Name);
			Assert.Equal(OutputKind.Ico, app.Kind);
			Assert.Equal(OutputMode.Newest, app.Output);
			Assert.Equal(2, app.Line);
			Assert.Equal(dir.At("app.ico"), app.DestFile);
			Assert.Equal(dir.At("art"), app.LookupFolder);
			var pack = app.Frames[(IconSize.S256, IconDepth.True)];
			Assert.Equal("logo.svg", pack.File);
			Assert.Equal("AppIcon", pack.SvgElement);
			Assert.Equal(4, pack.Line);
			Assert.Equal("old.bmp", app.Frames[(IconSize.S32, IconDepth.Palette)].File);
			Assert.Equal(OutputKind.Png, project.Outputs[1].Kind);
		}

		[Theory]
		[InlineData("[App.ICO]", false)]
		[InlineData("[Pkg.PNG]", true)]
		public void SectionNamesMatchWithoutRegardToCase(string header, bool png) {
			using var dir = new TempDir();
			Assert.Equal(png ? OutputKind.Png : OutputKind.Ico, Parse(dir, header + "\n32-true=a.png\n").Outputs.Single().Kind);
		}

		[Fact]
		public void PropertyAndOptionNamesMatchWithoutRegardToCaseButValuesKeepTheirs() {
			using var dir = new TempDir();
			var output = Parse(dir, "[a.ico]\nOutput=Overwrite\nPACK=a.png\n32-TRUE=b.png|USE Blue Thing\n").Outputs.Single();
			Assert.Equal(OutputMode.Overwrite, output.Output);
			Assert.Equal("a.png", output.Frames[(IconSize.S256, IconDepth.True)].File);
			Assert.Equal("Blue Thing", output.Frames[(IconSize.S32, IconDepth.True)].SvgElement);
		}

		[Theory]
		[InlineData("[app.ico] trailing", 1)]
		[InlineData("32-true=a.png\n[app.ico]", 1)]
		[InlineData("[app.ico]\nboguspack=logo.svg", 2)]
		[InlineData("[app.ico]\noutput=sometimes", 2)]
		[InlineData("[app.ico]\n32-true=a.png|scale 2", 2)]
		[InlineData("[app.ico]\n32-true=a.png|use", 2)]
		[InlineData("[app.ico]\n32-true=a.bmp|mask nosuchcolour", 2)]
		[InlineData("[app.ico]\n32-true=a.svg|snip 1,2,3", 2)]
		[InlineData("[a.ico]\n32-true=a.png\n[./a.ico]\n16-true=a.png", 3)]
		[InlineData("[a.ico]\n32-true=a.png\n[A.ICO]\n16-true=a.png", 3)]
		[InlineData("[a.png]\n48-true=a.png\n32-true=a.png", 3)]
		public void BadLinesAreParseErrorsAtTheirLine(string text, int line) {
			Assert.Equal(line, Error(text).Line);
		}

		[Fact]
		public void PngSectionKeepsTheLastFrameAtItsSize() {
			using var dir = new TempDir();
			var output = Parse(dir, "[a.png]\n48-true=a.png\n48-rgb=b.png\n").Outputs.Single();
			var frame = Assert.Single(output.Frames);
			Assert.Equal((IconSize.S48, IconDepth.RGB), frame.Key);
			Assert.Equal("b.png", frame.Value.File);
		}

		[Fact]
		public void ColourKeysAreRead() {
			using var dir = new TempDir();
			var output = Parse(dir, "[a.ico]\n32-true=a.bmp|mask #ff00ff|invert none\n16-true=a.bmp\n").Outputs.Single();
			var keyed = output.Frames[(IconSize.S32, IconDepth.True)];
			Assert.Equal(Color.FromArgb(255, 255, 0, 255).ToArgb(), keyed.Mask!.Value.ToArgb());
			Assert.Equal(Color.Empty, keyed.Invert);
			var plain = output.Frames[(IconSize.S16, IconDepth.True)];
			Assert.Null(plain.Mask);
			Assert.Null(plain.Invert);
		}

		[Fact]
		public void CropUnitsConvertToMillimetres() {
			using var dir = new TempDir();
			var frame = Parse(dir, "[a.ico]\n32-true=a.svg|snip 1in 2, 96px 4mm\n").Outputs.Single().Frames.Values.Single();
			var crop = frame.Crop!.Value;
			Assert.Equal(25.4f, crop.X, 3);
			Assert.Equal(2f, crop.Y, 3);
			Assert.Equal(25.4f, crop.Width, 3);
			Assert.Equal(4f, crop.Height, 3);
		}

		[Fact]
		public void OutputFolderRedirectsDestinationsButNotSources() {
			using var dir = new TempDir();
			string file = dir.Write("icons.ini", "[a.ico]\nsource=art\n32-true=a.png\n");
			var output = ProjectParser.Parse(file, dir.At("out")).Outputs.Single();
			Assert.Equal(Path.Combine(dir.At("out"), "a.ico"), output.DestFile);
			Assert.Equal(dir.At("art"), output.LookupFolder);
		}

		[Fact]
		public void MissingProjectFileIsFileNotFound() {
			using var dir = new TempDir();
			Assert.Throws<FileNotFoundException>(() => ProjectParser.Parse(dir.At("none.ini")));
		}
	}
}
