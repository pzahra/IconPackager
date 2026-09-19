using System;
using System.IO;

namespace PatTech.IconPackager {
	/// <summary>
	/// Writes a file all or nothing. The content goes to a temporary file beside the destination, which
	/// replaces the old file only once the write has finished; a failure leaves the old file as it was and
	/// removes the temporary one. So an interrupted run cannot leave a truncated output with a fresh
	/// timestamp that <c>newest</c> would then take for up to date.
	/// </summary>
	static class AtomicFile {
		/// <param name="path">The file to write.</param>
		/// <param name="write">Writes the content to the stream it is given.</param>
		public static void Write(string path, Action<Stream> write) {
			string temp = path + ".tmp";
			try {
				using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) {
					write(stream);
				}
				File.Move(temp, path, overwrite: true);
			}
			catch {
				try {
					File.Delete(temp);
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
					// The original failure is the one worth reporting.
				}
				throw;
			}
		}
	}
}
