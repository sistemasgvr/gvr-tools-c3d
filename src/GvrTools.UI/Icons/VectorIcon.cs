using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace GvrTools.UI.Icons
{
    /// <summary>
    /// Helpers for drawing ribbon icons in code as frozen <see cref="DrawingImage"/> instances.
    ///
    /// Vector, not bitmaps: ribbon buttons are requested at both 16px and 32px, a DrawingImage
    /// scales cleanly to either, and the repository stays free of binary assets that cannot be
    /// reviewed in a diff.
    /// </summary>
    public static class VectorIcon
    {
        /// <summary>Canvas every icon is drawn in, matching Revit's 32x32 large-button slot.</summary>
        public const double CanvasSize = 32;

        public static DrawingImage Compose(params Drawing[] drawings)
        {
            var group = new DrawingGroup();
            foreach (Drawing drawing in drawings)
            {
                if (drawing != null) group.Children.Add(drawing);
            }

            group.Freeze();

            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }

        public static GeometryDrawing Rectangle(Rect bounds, Color fill, double cornerRadius = 0) =>
            Freeze(new GeometryDrawing(Brush(fill), null, new RectangleGeometry(bounds, cornerRadius, cornerRadius)));

        public static GeometryDrawing Outline(Rect bounds, Color stroke, double thickness = 1.5, double cornerRadius = 0) =>
            Freeze(new GeometryDrawing(Brushes.Transparent, Pen(stroke, thickness), new RectangleGeometry(bounds, cornerRadius, cornerRadius)));

        public static GeometryDrawing FilledRectangle(Rect bounds, Color fill, Color stroke, double thickness = 1.5, double cornerRadius = 0) =>
            Freeze(new GeometryDrawing(Brush(fill), Pen(stroke, thickness), new RectangleGeometry(bounds, cornerRadius, cornerRadius)));

        /// <summary>Closed polygon through <paramref name="points"/>.</summary>
        public static GeometryDrawing Polygon(Color fill, params Point[] points) =>
            Freeze(new GeometryDrawing(Brush(fill), null, PolygonGeometry(points)));

        public static Geometry PolygonGeometry(params Point[] points)
        {
            var geometry = new StreamGeometry();

            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(points[0], true, true);
                context.PolyLineTo(new List<Point>(points).GetRange(1, points.Length - 1), true, false);
            }

            geometry.Freeze();
            return geometry;
        }

        private static SolidColorBrush Brush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Pen Pen(Color color, double thickness)
        {
            var pen = new Pen(Brush(color), thickness);
            pen.Freeze();
            return pen;
        }

        private static GeometryDrawing Freeze(GeometryDrawing drawing)
        {
            drawing.Freeze();
            return drawing;
        }
    }
}
