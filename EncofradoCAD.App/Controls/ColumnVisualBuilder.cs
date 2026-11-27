using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using EncofradoCAD.App.Views.Dialogs;

namespace EncofradoCAD.App.Controls
{
    public class ColumnVisualBuilder
    {
        private static AvaloniaList<double> Dash() => new AvaloniaList<double> { 4, 4 };

        private static TextBlock GuiaTexto() => new TextBlock
        {
            Foreground = Brushes.Cyan,
            Background = new SolidColorBrush(Color.FromArgb(200, 30, 30, 30)),
            FontSize = 11,
            Padding = new Thickness(2),
            IsVisible = false,
            IsHitTestVisible = false,
            RenderTransform = new ScaleTransform(1, 1),
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        };

        private static Line GuiaLinea() => new Line
        {
            Stroke = Brushes.Cyan,
            StrokeThickness = 1,
            StrokeDashArray = Dash(),
            IsVisible = false,
            IsHitTestVisible = false,
        };

        public static EstadoColumnaUI ConstruirColumnaCompleta(DatosColumna col, DrawingCanvas canvas)
        {
            double pixelesPorMetro = canvas.PixelesPorMetro;
            double factorVisualizacionColumna = canvas.FactorVisualizacionColumna;

            double anchoPx = col.Ancho * pixelesPorMetro * factorVisualizacionColumna;
            double largoPx = col.Largo * pixelesPorMetro * factorVisualizacionColumna;

            Border colVisual = new Border
            {
                Width = anchoPx,
                Height = largoPx,
                Background = new SolidColorBrush(Color.FromArgb(120, 52, 152, 219)),
                BorderBrush = Brushes.LimeGreen,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Cursor = new Cursor(StandardCursorType.SizeAll),
            };
            ToolTip.SetTip(colVisual, $"{col.Nombre}\nColumna: {col.Ancho:0.00} × {col.Largo:0.00} m ({(col.Ancho * 100):0} × {(col.Largo * 100):0} cm)\nPosición: X={col.PosX:0.00}m, Y={col.PosY:0.00}m");
            ToolTip.SetShowDelay(colVisual, 250); // aparece pronto al acercar el cursor a la columna

            Grid gridInterior = new Grid();
            Button btnLock = new Button
            {
                Content = "🔓",
                FontSize = 10,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(0, -2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                RenderTransform = new ScaleTransform(1, 1),
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            };

            gridInterior.Children.Add(btnLock);
            colVisual.Child = gridInterior;

            Line guiaX = GuiaLinea();
            Line guiaXDer = GuiaLinea();
            Line guiaY = GuiaLinea();
            Line guiaYAbj = GuiaLinea();
            TextBlock textoXIzq = GuiaTexto();
            TextBlock textoXDer = GuiaTexto();
            TextBlock textoYArr = GuiaTexto();
            TextBlock textoYAbj = GuiaTexto();

            return new EstadoColumnaUI
            {
                Visual = colVisual,
                Datos = col,
                GuiaX = guiaX,
                GuiaXDer = guiaXDer,
                GuiaY = guiaY,
                GuiaYAbj = guiaYAbj,
                TextoXIzq = textoXIzq,
                TextoXDer = textoXDer,
                TextoYArr = textoYArr,
                TextoYAbj = textoYAbj,
                BtnLock = btnLock,
                IsLocked = false,
                IsSelected = false,
            };
        }
    }
}
