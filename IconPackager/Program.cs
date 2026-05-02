namespace IconPackager {
	class Program {
		static void Main(string[] args) {
			foreach (var arg in args) {
				IconProject.FromFile(arg).RenderAll();
			}
		}
	}
}
