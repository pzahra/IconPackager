using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Svg;

namespace PatTech.IconPackager {
	/// <summary>Rasterises SVG artwork for a frame: the whole page, one named element, or a region of the page.</summary>
	static class SvgRenderer {
		/// <summary>
		/// Opens the file and rasterises it into a <paramref name="size"/> square. A <c>use</c> element is
		/// drawn on its own and fitted to the square; a <c>snip</c> region selects that part of the page instead.
		/// </summary>
		public static Bitmap Render(string file, IconFrame frame, int size) => Render(SvgDocument.Open(file), frame, size);

		private static Bitmap Render(SvgDocument svg, IconFrame frame, int size) {
			var region = PageRegion(svg);
			if (frame.SvgElement is not null) {
				var element = FindElement(svg, frame.SvgElement)
					?? throw new KeyNotFoundException($"Element '{frame.SvgElement}' not found in {frame.File}");
				Isolate(element);
				region = DocumentBounds(element);
			}
			if (frame.Crop is RectangleF mm) {
				region = MillimetresToUserUnits(svg, mm);
			}
			if (region.Width <= 0 || region.Height <= 0) {
				throw new InvalidOperationException($"Nothing to draw for '{frame.SvgElement}' in {frame.File}");
			}

			// Re-frame the document: the region becomes the viewBox and the viewport becomes the icon square,
			// so the region is scaled uniformly to fit and centred. Drawing the page straight into a square
			// would stretch it, because pages are rarely square.
			svg.ViewBox = new SvgViewBox(region.X, region.Y, region.Width, region.Height);
			svg.AspectRatio = new SvgAspectRatio(SvgPreserveAspectRatio.xMidYMid);
			svg.Width = new SvgUnit(SvgUnitType.Pixel, size);
			svg.Height = new SvgUnit(SvgUnitType.Pixel, size);
			return svg.Draw(size, size);
		}

		/// <summary>The page in user units: its viewBox, or its width and height when it has none.</summary>
		private static RectangleF PageRegion(SvgDocument svg) {
			var box = svg.ViewBox;
			if (box.Width > 0 && box.Height > 0) {
				return new(box.MinX, box.MinY, box.Width, box.Height);
			}
			var dim = svg.GetDimensions();
			return new(0, 0, dim.Width, dim.Height);
		}

		/// <summary>Finds a drawable element by id, or failing that by its Inkscape label (the name shown in
		/// Inkscape's Layers and Objects panel).</summary>
		private static SvgVisualElement? FindElement(SvgDocument svg, string name) {
			if (svg.GetElementById(name) is SvgVisualElement byId) return byId;
			return Descendants(svg).OfType<SvgVisualElement>().FirstOrDefault(e => e.CustomAttributes
				.Any(a => a.Key.EndsWith(":label", StringComparison.Ordinal) && a.Value == name));
		}

		private static IEnumerable<SvgElement> Descendants(SvgElement element) {
			foreach (var child in element.Children) {
				yield return child;
				foreach (var grandchild in Descendants(child)) yield return grandchild;
			}
		}

		/// <summary>
		/// Hides everything that is not the element, one of its descendants or one of its ancestors, so that
		/// overlapping artwork elsewhere on the page does not bleed into the icon. Ancestors are shown so that
		/// an element on a hidden layer still renders. Inherited styles and gradients are left intact.
		/// </summary>
		private static void Isolate(SvgVisualElement element) {
			for (SvgElement current = element; current.Parent is SvgElement parent; current = parent) {
				current.Display = "inline";
				foreach (var sibling in parent.Children.OfType<SvgVisualElement>()) {
					if (sibling != current) sibling.Display = "none";
				}
			}
		}

		/// <summary>
		/// The element's bounds in the document's user space. The library's Bounds include the element's own
		/// transform but not its ancestors', so those are replayed on the way up to the root.
		/// </summary>
		private static RectangleF DocumentBounds(SvgVisualElement element) {
			var bounds = element.Bounds;
			for (var ancestor = element.Parent; ancestor is not null and not SvgDocument; ancestor = ancestor.Parent) {
				if (ancestor.Transforms is not { Count: > 0 }) continue;
				using var matrix = ancestor.Transforms.GetMatrix();
				var corners = new[] {
					new PointF(bounds.Left, bounds.Top), new PointF(bounds.Right, bounds.Top),
					new PointF(bounds.Right, bounds.Bottom), new PointF(bounds.Left, bounds.Bottom),
				};
				matrix.TransformPoints(corners);
				bounds = RectangleF.FromLTRB(
					corners.Min(c => c.X), corners.Min(c => c.Y),
					corners.Max(c => c.X), corners.Max(c => c.Y));
			}
			return bounds;
		}

		/// <summary>
		/// Maps a region given in millimetres of the physical page onto the page's user units. A page without a
		/// physical size (percentage or unitless dimensions) is taken to be 96 dpi pixels, as browsers do.
		/// </summary>
		private static RectangleF MillimetresToUserUnits(SvgDocument svg, RectangleF mm) {
			var page = PageRegion(svg);
			float unitsPerMmX = page.Width / ToMillimetres(svg.Width, page.Width);
			float unitsPerMmY = page.Height / ToMillimetres(svg.Height, page.Height);
			return new(
				page.X + mm.X * unitsPerMmX, page.Y + mm.Y * unitsPerMmY,
				mm.Width * unitsPerMmX, mm.Height * unitsPerMmY);
		}

		private static float ToMillimetres(SvgUnit length, float fallbackPx) {
			const float mmPerPx = 25.4f / 96;
			return length.Type switch {
				SvgUnitType.Percentage or SvgUnitType.Em or SvgUnitType.Ex => fallbackPx * mmPerPx,
				_ => length.ToDeviceValue(null, UnitRenderingType.Horizontal, null) * mmPerPx,
			};
		}
	}
}
