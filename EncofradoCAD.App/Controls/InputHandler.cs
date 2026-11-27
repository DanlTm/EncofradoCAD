using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace EncofradoCAD.App.Controls
{
    public class InputHandler
    {
        private readonly DrawingCanvas _canvas;
        private readonly PolygonManager _polygonManager;
        private readonly InteractionManager _interactionManager;
        private readonly ViewportManager _viewportManager;
        private readonly ColumnManager _columnManager;
        private readonly MeasurementGuideManager _measurementGuideManager;

        public InputHandler(
            DrawingCanvas canvas,
            PolygonManager polygonManager,
            InteractionManager interactionManager,
            ViewportManager viewportManager,
            ColumnManager columnManager,
            MeasurementGuideManager measurementGuideManager)
        {
            _canvas = canvas;
            _polygonManager = polygonManager;
            _interactionManager = interactionManager;
            _viewportManager = viewportManager;
            _columnManager = columnManager;
            _measurementGuideManager = measurementGuideManager;
        }

        public void HandleMouseLeftButtonDown(PointerPressedEventArgs e)
        {
            _canvas.Focus();
            Point clickPoint = e.GetPosition(_canvas);

            if (_columnManager.EnModoInsertarColumna && _columnManager.ColumnEnPreview != null)
            {
                _columnManager.ColocarColumna(clickPoint);
                e.Handled = true;
                return;
            }

            if (!_polygonManager.IsDrawingMode && _interactionManager.VerticeEnEdicion >= 0)
            {
                _interactionManager.FinalizarEdicionVertice();
                _canvas.TooltipCoordenadas.IsVisible = false;
                e.Handled = true;
                return;
            }

            if (!_polygonManager.IsDrawingMode)
            {
                if (DetectarYEmpezarEdicionVertice(clickPoint))
                {
                    return;
                }
            }

            if (_polygonManager.IsDrawingMode)
            {
                _polygonManager.AgregarPunto(clickPoint, e.KeyModifiers.HasFlag(KeyModifiers.Shift), _canvas.PuntoActual);

                if (!_polygonManager.IsDrawingMode)
                {
                    _interactionManager.RedibujarPoligono();
                }
            }
        }

        public void HandleMouseMove(PointerEventArgs e)
        {
            _viewportManager.HandleMouseMove(e);
            Point mousePos = e.GetPosition(_canvas);
            bool leftPressed = e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed;

            if (_columnManager.EnModoInsertarColumna && _columnManager.ColumnEnPreview != null)
            {
                string tooltipText = _columnManager.MoverPreviewColumna(mousePos);
                ShowTooltip(mousePos, tooltipText);
                return;
            }

            if (!_polygonManager.IsDrawingMode)
            {
                if (leftPressed && _interactionManager.SegmentoEnMovimiento >= 0)
                {
                    string text = _interactionManager.ActualizarMovimientoSegmento(mousePos);
                    ShowTooltip(mousePos, text);
                    return;
                }

                // El nodo sigue al mouse SOLO mientras se arrastra (botón izquierdo presionado).
                if (leftPressed && _interactionManager.VerticeEnEdicion >= 0)
                {
                    string text = _interactionManager.ActualizarMovimientoVertice(mousePos);
                    ShowTooltip(mousePos, text);
                    return;
                }
            }

            if (_polygonManager.IsDrawingMode && _polygonManager.PuntoInicial != null)
            {
                _polygonManager.ActualizarPreview(mousePos, e.KeyModifiers.HasFlag(KeyModifiers.Shift), _canvas.PuntoActual);
            }
            else
            {
                _polygonManager.TramoTemporal.IsVisible = false;
                if (_interactionManager.VerticeEnEdicion < 0)
                {
                    _canvas.TooltipCoordenadas.IsVisible = false;
                }
            }
        }

        public void HandleMouseLeftButtonUp(PointerReleasedEventArgs e)
        {
            if (!_polygonManager.IsDrawingMode)
            {
                if (_interactionManager.SegmentoEnMovimiento >= 0)
                {
                    _interactionManager.SegmentoEnMovimiento = -1;
                    _canvas.TooltipCoordenadas.IsVisible = false;
                    _interactionManager.RedibujarPoligono();
                }
            }
        }

        public void HandleKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HandleEscape();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (_interactionManager.VerticeEnEdicion >= 0)
                {
                    _interactionManager.FinalizarEdicionVertice();
                    _canvas.TooltipCoordenadas.IsVisible = false;
                    e.Handled = true;
                }

                return;
            }

            if (_columnManager.HandleColumnMovementKeys(e.Key))
            {
                e.Handled = true;
                return;
            }

            if (_interactionManager.MoverVerticeConTeclado(e.Key))
            {
                ShowTooltip(_interactionManager.PosicionpreviewVertice, _interactionManager.ObtenerTextoPosicionVertice());
                e.Handled = true;
            }
        }

        private bool DetectarYEmpezarEdicionVertice(Point clickPoint)
        {
            List<Point> vertices = new List<Point>();
            if (_polygonManager.PuntoInicial.HasValue) vertices.Add(_polygonManager.PuntoInicial.Value);
            foreach (var tramo in _polygonManager.Tramos) vertices.Add(tramo.PuntoFinal);

            for (int i = 0; i < vertices.Count; i++)
            {
                if (GeometryHelper.Distancia(clickPoint, vertices[i]) < 12)
                {
                    if (_columnManager.ColumnaSeleccionada != null)
                    {
                        _columnManager.DeseleccionarColumna();
                    }

                    _interactionManager.VerticeEnEdicion = i;
                    _interactionManager.PosicionpreviewVertice = vertices[i];
                    _interactionManager.RedibujarPoligonoConPreview();
                    ShowTooltip(vertices[i], _interactionManager.ObtenerTextoPosicionVertice());
                    return true;
                }
            }

            return false;
        }

        private void ShowTooltip(Point mousePos, string text)
        {
            _canvas.TooltipCoordenadas.Text = text;
            _canvas.TooltipCoordenadas.IsVisible = true;
            Canvas.SetLeft(_canvas.TooltipCoordenadas, mousePos.X + 10);
            Canvas.SetTop(_canvas.TooltipCoordenadas, mousePos.Y + 10);
        }

        private void HandleEscape()
        {
            if (_interactionManager.VerticeEnEdicion >= 0)
            {
                _interactionManager.CancelarEdicionVertice();
                _canvas.TooltipCoordenadas.IsVisible = false;
            }
            else if (_columnManager.ColumnaSeleccionada != null)
            {
                _columnManager.DeseleccionarColumna();
            }
            else if (_polygonManager.IsDrawingMode)
            {
                _polygonManager.Limpiar();
            }
        }
    }
}
