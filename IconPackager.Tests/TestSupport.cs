using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using PatTech.IcoNet;
using Xunit;

// The tool reports on Console.Error, which is process-wide, so tests that capture it cannot overlap.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PatTech.IconPackager.Tests {
	/// <summary>A folder under the system temp folder that goes away with the test.</summary>
	sealed class TempDir : IDisposable {
		public string Root { get; } = Path.Combine(Path.GetTempPath(), "IconPackager.Tests", Guid.NewGuid().ToString("N"));

		public TempDir() {
			Directory.CreateDirectory(Root);
		}

		/// <summary>The full path of a file in the folder.</summary>
		public string At(string name) => Path.Combine(Root, name);

		/// <summary>Writes a text file into the folder and returns its path.</summary>
		public string Write(string name, string text) {
			string path = At(name);
			File.WriteAllText(path, text);
			return path;
		}

		public void Dispose() {
			try {
				Directory.Delete(Root, true);
			}
			catch (IOException) {
				// A file left open by a failed test is not worth failing the cleanup over.
			}
		}
	}

	/// <summary>Collects what the tool reports on standard error while alive.</summary>
	sealed class ErrorCapture : IDisposable {
		readonly TextWriter previous = Console.Error;
		readonly StringWriter writer = new();

		public ErrorCapture() {
			Console.SetError(writer);
		}

		public string Text => writer.ToString();

		public void Dispose() {
			Console.SetError(previous);
			writer.Dispose();
		}
	}

	/// <summary>Small images and icons for the tests to work on.</summary>
	static class Images {
		/// <summary>A square 32-bit bitmap filled with one colour.</summary>
		public static Bitmap Solid(int size, Color colour) {
			var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
			using var g = Graphics.FromImage(bitmap);
			g.Clear(colour);
			return bitmap;
		}

		/// <summary>Writes a solid PNG into the folder and returns its path.</summary>
		public static string WritePng(TempDir dir, string name, int size, Color colour) {
			using var bitmap = Solid(size, colour);
			string path = dir.At(name);
			bitmap.Save(path, ImageFormat.Png);
			return path;
		}

		/// <summary>The bytes of the icon file <paramref name="icon"/> would write.</summary>
		public static byte[] Bytes(IconBuilder icon) {
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) {
				icon.Write(writer);
			}
			return stream.ToArray();
		}
	}
}
