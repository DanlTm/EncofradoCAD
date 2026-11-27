using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace EncofradoCAD.App.Controls
{
    public static class PolygonVisualBuilder
    {
        private static readonly Cursor HandCursor = new Cursor(StandardCursorType.Hand);

        public static Shape CrearVisualSegmento(Point p1, Point p2, TipoSegmento tipo, int index, PolygonManager polygonManager)
        {
            Shape pathItem;
            if (tipo == TipoSegmento.Recto)
            {
                pathItem = new Line { StartPoint = new Point(p1.X, p1.Y), EndPoint = new Point(p2.X, p2.Y) };
            }
            else
            {
                pathItem = new Path { Data = polygonManager.CrearGeometriaSegmento(p1, p2, tipo) };
            }

            pathItem.Stroke = new SolidColorBrush(Color.FromRgb(0xB6, 0xFF, 0x00)); // verde lima neón (línea de losa)
            pathItem.StrokeThickness = 1.4;
            pathItem.Cursor = HandCursor;
            pathItem.Tag = index;

            return pathItem;
        }

        public static TextBlock CrearTextoMedida(Point mid, double distMetros)
        {
            var txt = new TextBlock
            {
                Text = $"{distMetros:F2}m",
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(180, 30, 30, 30)),
                Padding = new Thickness(4, 2, 4, 2),
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Cursor = HandCursor,
                RenderTransform = new ScaleTransform(1, 1),
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            };

            Canvas.SetLeft(txt, mid.X - 20);
            Canvas.SetTop(txt, mid.Y - 10);

            return txt;
        }

        public static Ellipse CrearVisualVertice(Point pos, bool esActivo)
        {
            const double diameter = 14;

            return new Ellipse
            {
                Width = diameter,
                Height = diameter,
                Fill = esActivo ? Brushes.Orange : Brushes.Red,
                Stroke = esActivo ? Brushes.Orange : Brushes.DarkRed,
                StrokeThickness = esActivo ? 3.5 : 2.5,
                Opacity = esActivo ? 0.8 : 1.0,
                Cursor = HandCursor,
            };
        }
    }
}
