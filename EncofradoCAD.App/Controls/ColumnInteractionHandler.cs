using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace EncofradoCAD.App.Controls
{
    public class ColumnInteractionHandler
    {
        private readonly DrawingCanvas _canvas;
        private readonly MeasurementGuideManager _measurementGuideManager;
        private readonly ColumnManager _columnManager;

        private static readonly Cursor CursorArrow = new Cursor(StandardCursorType.Arrow);
        private static readonly Cursor CursorSizeAll = new Cursor(StandardCursorType.SizeAll);

        public ColumnInteractionHandler(DrawingCanvas canvas, MeasurementGuideManager measurementGuideManager, ColumnManager columnManager)
        {
            _canvas = canvas;
            _measurementGuideManager = measurementGuideManager;
            _columnManager = columnManager;
        }

        public void AdjuntarEventos(EstadoColumnaUI estado)
        {
            estado.BtnLock.Click += (s, e) => HandleBtnLockClick(estado, e);

            bool isDragging = false;
            Point offset = new Point(0, 0);

            estado.Visual.PointerPressed += (s, e) =>
            {
                var props = e.GetCurrentPoint(estado.Visual).Properties;

                if (props.IsRightButtonPressed)
                {
                    _columnManager.SeleccionarColumna(estado);
                    return;
                }

                if (!props.IsLeftButtonPressed) return;

                _columnManager.SeleccionarColumna(estado);

                if (estado.IsLocked) return;

                isDragging = true;
                offset = e.GetPosition(estado.Visual);
                e.Pointer.Capture(estado.Visual);

                e.Handled = true;
            };

            estado.Visual.PointerMoved += (s, e) =>
            {
                if (isDragging && !estado.IsLocked)
                {
                    Point push = e.GetPosition(_canvas);
                    double newLeft = push.X - offset.X;
                    double newTop = push.Y - offset.Y;

                    Canvas.SetLeft(estado.Visual, newLeft);
                    Canvas.SetTop(estado.Visual, newTop);

                    ActualizarTodasLasGuias(estado, actuanteEstaBloqueada: false);
                }
            };

            estado.Visual.PointerReleased += (s, e) =>
            {
                if (!isDragging) return;
                isDragging = false;
                e.Pointer.Capture(null);

                double finalLeft = Canvas.GetLeft(estado.Visual);
                double finalTop = Canvas.GetTop(estado.Visual);

                double anchoPx = estado.Visual.Width;
                double largoPx = estado.Visual.Height;

                estado.Datos.PosX = (finalLeft + (anchoPx / 2)) / _canvas.PixelesPorMetro;
                estado.Datos.PosY = (finalTop + (largoPx / 2)) / _canvas.PixelesPorMetro;
                ToolTip.SetTip(estado.Visual, $"{estado.Datos.Nombre}\nColumna: {estado.Datos.Ancho:0.00} × {estado.Datos.Largo:0.00} m ({(estado.Datos.Ancho * 100):0} × {(estado.Datos.Largo * 100):0} cm)\nPosicion: X={estado.Datos.PosX:0.00}m, Y={estado.Datos.PosY:0.00}m");
            };
        }

        private void ActualizarTodasLasGuias(EstadoColumnaUI actuante, bool actuanteEstaBloqueada = false)
        {
            var losaPath = _canvas.PolygonManager.LosaPath;
            var puntoInicial = _canvas.PolygonManager.PuntoInicial;
            var tramos = _canvas.PolygonManager.Tramos;
            var columnasVisuales = _columnManager.ColumnasVisuales;
            double pixelesPorMetro = _canvas.PixelesPorMetro;

            _measurementGuideManager.ActualizarGuias(
                actuante, losaPath, puntoInicial, tramos, columnasVisuales, pixelesPorMetro,
                esParaColumnaBloqueada: actuanteEstaBloqueada);
        }

        private void HandleBtnLockClick(EstadoColumnaUI estado, RoutedEventArgs e)
        {
            estado.IsLocked = !estado.IsLocked;
            var btnLock = estado.BtnLock;
            var colVisual = estado.Visual;

            if (estado.IsLocked)
            {
                btnLock.Content = "🔒";
                colVisual.Cursor = CursorArrow;

                var brushBloqueado = new SolidColorBrush(Color.FromArgb(255, 0, 255, 0));
                estado.GuiaX.Stroke = brushBloqueado; estado.GuiaX.StrokeDashArray = new AvaloniaList<double> { 4, 4 }; estado.GuiaX.StrokeThickness = 1;
                estado.GuiaXDer.Stroke = brushBloqueado; estado.GuiaXDer.StrokeDashArray = new AvaloniaList<double> { 4, 4 }; estado.GuiaXDer.StrokeThickness = 1;
                estado.GuiaY.Stroke = brushBloqueado; estado.GuiaY.StrokeDashArray = new AvaloniaList<double> { 4, 4 }; estado.GuiaY.StrokeThickness = 1;
                estado.GuiaYAbj.Stroke = brushBloqueado; estado.GuiaYAbj.StrokeDashArray = new AvaloniaList<double> { 4, 4 }; estado.GuiaYAbj.StrokeThickness = 1;
                estado.TextoXIzq.Foreground = brushBloqueado;
                estado.TextoXDer.Foreground = brushBloqueado;
                estado.TextoYArr.Foreground = brushBloqueado;
                estado.TextoYAbj.Foreground = brushBloqueado;

                ActualizarTodasLasGuias(estado, actuanteEstaBloqueada: true);
            }
            else
            {
                btnLock.Content = "🔓";
                colVisual.Cursor = CursorSizeAll;

                estado.GuiaX.Stroke = Brushes.Cyan; estado.GuiaX.StrokeDashArray = new AvaloniaList<double> { 4, 4 }; estado.GuiaX.StrokeThickness = 1;
                estado.GuiaXDer.Stroke = Brushes.Cyan; estado.GuiaXDer.StrokeDashArray = new AvaloniaList<double> { 4, 4 }; estado.GuiaXDer.StrokeThickness = 1;
                estado.GuiaY.Stroke = Brushes.Cyan; estado.GuiaY.StrokeDashArray = new AvaloniaList<double> { 4, 4 }; estado.GuiaY.StrokeThickness = 1;
                estado.GuiaYAbj.Stroke = Brushes.Cyan; estado.GuiaYAbj.StrokeDashArray = new AvaloniaList<double> { 4, 4 }; estado.GuiaYAbj.StrokeThickness = 1;
                estado.TextoXIzq.Foreground = Brushes.Cyan;
                estado.TextoXDer.Foreground = Brushes.Cyan;
                estado.TextoYArr.Foreground = Brushes.Cyan;
                estado.TextoYAbj.Foreground = Brushes.Cyan;

                if (!estado.IsSelected)
                {
                    estado.OcultarGuias();
                }

                ActualizarTodasLasGuias(estado, actuanteEstaBloqueada: false);
            }

            e.Handled = true;
        }

        public bool HandleColumnMovementKeys(Key key, EstadoColumnaUI? columnaSeleccionada)
        {
            if (columnaSeleccionada == null || columnaSeleccionada.IsLocked) return false;

            double movePx = 0.01 * _canvas.PixelesPorMetro;
            bool moved = false;
            var visual = columnaSeleccionada.Visual;

            double left = Canvas.GetLeft(visual);
            double top = Canvas.GetTop(visual);

            if (key == Key.W) { Canvas.SetTop(visual, top - movePx); moved = true; }
            else if (key == Key.S) { Canvas.SetTop(visual, top + movePx); moved = true; }
            else if (key == Key.A) { Canvas.SetLeft(visual, left - movePx); moved = true; }
            else if (key == Key.D) { Canvas.SetLeft(visual, left + movePx); moved = true; }

            if (moved)
            {
                var col = columnaSeleccionada.Datos;
                col.PosX = (Canvas.GetLeft(visual) + (visual.Width / 2)) / _canvas.PixelesPorMetro;
                col.PosY = (Canvas.GetTop(visual) + (visual.Height / 2)) / _canvas.PixelesPorMetro;
                ToolTip.SetTip(visual, $"{col.Nombre}\nColumna: {col.Ancho:0.00} × {col.Largo:0.00} m ({(col.Ancho * 100):0} × {(col.Largo * 100):0} cm)\nPosicion: X={col.PosX:0.00}m, Y={col.PosY:0.00}m");

                ActualizarTodasLasGuias(columnaSeleccionada);
                return true;
            }

            return false;
        }
    }
}
