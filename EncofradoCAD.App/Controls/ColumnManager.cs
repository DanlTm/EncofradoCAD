using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;
using EncofradoCAD.App.Views.Dialogs;

namespace EncofradoCAD.App.Controls
{
    public class ColumnManager
    {
        private readonly DrawingCanvas _canvas;
        private readonly MeasurementGuideManager _measurementGuideManager;
        private readonly ColumnInteractionHandler _interactionHandler;

        private readonly Dictionary<Border, EstadoColumnaUI> _estadosColumnas = new Dictionary<Border, EstadoColumnaUI>();
        private readonly List<Border> _columnasVisuales = new List<Border>();
        private EstadoColumnaUI? _columnaSeleccionada = null;

        private DatosColumna? _columnEnPreview;
        private Border? _previewColumnaVisual;
        private bool _enModoInsertarColumna = false;
        private double _ultimaEscala = 1.0;

        private static readonly Cursor CursorArrow = new Cursor(StandardCursorType.Arrow);
        private static readonly Cursor CursorCross = new Cursor(StandardCursorType.Cross);

        public bool EnModoInsertarColumna => _enModoInsertarColumna;
        public DatosColumna? ColumnEnPreview => _columnEnPreview;
        public List<Border> ColumnasVisuales => _columnasVisuales;
        public IEnumerable<EstadoColumnaUI> EstadosColumnas => _estadosColumnas.Values;
        public EstadoColumnaUI? ColumnaSeleccionada => _columnaSeleccionada;

        public ColumnManager(DrawingCanvas canvas, MeasurementGuideManager measurementGuideManager)
        {
            _canvas = canvas;
            _measurementGuideManager = measurementGuideManager;
            _interactionHandler = new ColumnInteractionHandler(canvas, measurementGuideManager, this);
        }

        public async Task IniciarModoInsercion(double posXMetro, double posYMetro, double anchoCm = 30, double largoCm = 30)
        {
            var dialog = new ColumnaDialog(posXMetro, posYMetro, anchoCm, largoCm, SugerirNombreColumna());
            var owner = TopLevel.GetTopLevel(_canvas) as Window;

            bool aceptado = owner != null && await dialog.ShowDialog<bool>(owner);
            if (aceptado && dialog.Resultado != null)
            {
                _columnEnPreview = dialog.Resultado;
                _enModoInsertarColumna = true;
                _canvas.Focus();
                _canvas.Cursor = CursorCross;
            }
        }

        public string MoverPreviewColumna(Point mousePos)
        {
            if (_previewColumnaVisual == null && _columnEnPreview != null)
            {
                double anchoPx = _columnEnPreview.Ancho * _canvas.PixelesPorMetro * _canvas.FactorVisualizacionColumna;
                double largoPx = _columnEnPreview.Largo * _canvas.PixelesPorMetro * _canvas.FactorVisualizacionColumna;

                _previewColumnaVisual = new Border
                {
                    Width = anchoPx,
                    Height = largoPx,
                    Background = new SolidColorBrush(Color.FromArgb(120, 52, 152, 219)),
                    BorderBrush = Brushes.LimeGreen,
                    BorderThickness = new Thickness(3),
                    CornerRadius = new CornerRadius(4),
                    RenderTransform = new ScaleTransform(1.0 / _ultimaEscala, 1.0 / _ultimaEscala),
                    RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                };
                ToolTip.SetTip(_previewColumnaVisual, $"{_columnEnPreview.Nombre}\n({(_columnEnPreview.Ancho * 100):0}cm x {(_columnEnPreview.Largo * 100):0}cm)");

                _canvas.Children.Add(_previewColumnaVisual);
            }

            if (_previewColumnaVisual != null)
            {
                Canvas.SetLeft(_previewColumnaVisual, mousePos.X - (_previewColumnaVisual.Width / 2));
                Canvas.SetTop(_previewColumnaVisual, mousePos.Y - (_previewColumnaVisual.Height / 2));
            }

            double xMetros = mousePos.X / _canvas.PixelesPorMetro;
            double yMetros = mousePos.Y / _canvas.PixelesPorMetro;

            return $"X: {xMetros:0.00}m\nY: {yMetros:0.00}m\n(Click para colocar)";
        }

        public void LimpiarPreviewColumna()
        {
            if (_previewColumnaVisual != null)
            {
                _canvas.Children.Remove(_previewColumnaVisual);
                _previewColumnaVisual = null;
            }
        }

        public void ColocarColumna(Point clickPoint)
        {
            if (_columnEnPreview == null) return;

            var col = _columnEnPreview;
            col.PosX = clickPoint.X / _canvas.PixelesPorMetro;
            col.PosY = clickPoint.Y / _canvas.PixelesPorMetro;

            DibujarColumna(col, _canvas.PolygonManager.LosaPath, _canvas.PolygonManager.PuntoInicial, _canvas.PolygonManager.Tramos);

            LimpiarPreviewColumna();
            _enModoInsertarColumna = false;
            _columnEnPreview = null;
            _canvas.TooltipCoordenadas.IsVisible = false;
            _canvas.Cursor = CursorArrow;
        }

        public void SeleccionarColumna(EstadoColumnaUI? estado)
        {
            if (_columnaSeleccionada != null && _columnaSeleccionada != estado)
            {
                _columnaSeleccionada.IsSelected = false;
                _columnaSeleccionada.Visual.BorderBrush = Brushes.LimeGreen;
                _columnaSeleccionada.OcultarGuias();
            }

            _columnaSeleccionada = estado;
            if (estado != null)
            {
                estado.IsSelected = true;
                estado.Visual.BorderBrush = Brushes.White;
                if (!estado.IsLocked)
                {
                    _measurementGuideManager.ActualizarGuias(estado, _canvas.PolygonManager.LosaPath, _canvas.PolygonManager.PuntoInicial, _canvas.PolygonManager.Tramos, _columnasVisuales, _canvas.PixelesPorMetro);
                }
            }
        }

        public void DeseleccionarColumna() => SeleccionarColumna(null);

        public void DibujarColumna(
            DatosColumna col,
            Path losaPath,
            Point? puntoInicial,
            List<TramoLosa> tramos)
        {
            EstadoColumnaUI estado = ColumnVisualBuilder.ConstruirColumnaCompleta(col, _canvas);
            ActualizarEscalaVisualIndividual(estado, _ultimaEscala);

            double posXpx = col.PosX * _canvas.PixelesPorMetro;
            double posYpx = col.PosY * _canvas.PixelesPorMetro;

            Canvas.SetLeft(estado.Visual, posXpx - (estado.Visual.Width / 2));
            Canvas.SetTop(estado.Visual, posYpx - (estado.Visual.Height / 2));

            _canvas.Children.Add(estado.Visual);
            _columnasVisuales.Add(estado.Visual);

            _canvas.Children.Add(estado.GuiaX);
            _canvas.Children.Add(estado.GuiaXDer);
            _canvas.Children.Add(estado.GuiaY);
            _canvas.Children.Add(estado.GuiaYAbj);
            _canvas.Children.Add(estado.TextoXIzq);
            _canvas.Children.Add(estado.TextoXDer);
            _canvas.Children.Add(estado.TextoYArr);
            _canvas.Children.Add(estado.TextoYAbj);

            _estadosColumnas[estado.Visual] = estado;

            _interactionHandler.AdjuntarEventos(estado);
        }

        public EstadoColumnaUI? ObtenerEstado(Border visual)
        {
            _estadosColumnas.TryGetValue(visual, out var estado);
            return estado;
        }

        public void EliminarColumna(EstadoColumnaUI estado)
        {
            if (estado == null) return;

            if (_columnaSeleccionada == estado)
            {
                _columnaSeleccionada = null;
            }

            _canvas.Children.Remove(estado.Visual);
            _canvas.Children.Remove(estado.GuiaX);
            _canvas.Children.Remove(estado.GuiaY);
            _canvas.Children.Remove(estado.GuiaXDer);
            _canvas.Children.Remove(estado.GuiaYAbj);
            _canvas.Children.Remove(estado.TextoXIzq);
            _canvas.Children.Remove(estado.TextoXDer);
            _canvas.Children.Remove(estado.TextoYArr);
            _canvas.Children.Remove(estado.TextoYAbj);

            _columnasVisuales.Remove(estado.Visual);
            _estadosColumnas.Remove(estado.Visual);
        }

        public EstadoColumnaUI? DuplicarColumna(EstadoColumnaUI estado)
        {
            if (estado == null) return null;

            const double offsetMetros = 0.30;
            var copia = new DatosColumna
            {
                Nombre = GenerarNombreDuplicado(estado.Datos.Nombre),
                PosX = estado.Datos.PosX + offsetMetros,
                PosY = estado.Datos.PosY + offsetMetros,
                Ancho = estado.Datos.Ancho,
                Largo = estado.Datos.Largo,
            };

            DibujarColumna(copia, _canvas.PolygonManager.LosaPath, _canvas.PolygonManager.PuntoInicial, _canvas.PolygonManager.Tramos);

            var estadoDuplicado = _estadosColumnas.Values.LastOrDefault(e => ReferenceEquals(e.Datos, copia));
            if (estadoDuplicado != null)
            {
                SeleccionarColumna(estadoDuplicado);
            }

            return estadoDuplicado;
        }

        /// <summary>Sugiere el siguiente nombre secuencial: "Columna 1", "Columna 2", … según las columnas existentes.</summary>
        private string SugerirNombreColumna()
        {
            int maximo = 0;
            foreach (var estado in _estadosColumnas.Values)
            {
                string nombre = estado.Datos.Nombre?.Trim() ?? string.Empty;
                if (nombre.StartsWith("Columna ", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(nombre.Substring("Columna ".Length).Trim(), out int n) &&
                    n > maximo)
                {
                    maximo = n;
                }
            }

            return $"Columna {maximo + 1}";
        }

        private string GenerarNombreDuplicado(string nombreBase)
        {
            if (string.IsNullOrWhiteSpace(nombreBase))
            {
                nombreBase = "Columna";
            }

            string candidato = $"{nombreBase} copia";
            int indice = 2;
            var nombresExistentes = new HashSet<string>(
                _estadosColumnas.Values.Select(e => e.Datos.Nombre),
                StringComparer.OrdinalIgnoreCase);

            while (nombresExistentes.Contains(candidato))
            {
                candidato = $"{nombreBase} copia {indice}";
                indice++;
            }

            return candidato;
        }

        public void Limpiar()
        {
            foreach (var colState in _estadosColumnas.Values)
            {
                _canvas.Children.Remove(colState.Visual);
                _canvas.Children.Remove(colState.GuiaX);
                _canvas.Children.Remove(colState.GuiaY);
                _canvas.Children.Remove(colState.GuiaXDer);
                _canvas.Children.Remove(colState.GuiaYAbj);
                _canvas.Children.Remove(colState.TextoXIzq);
                _canvas.Children.Remove(colState.TextoXDer);
                _canvas.Children.Remove(colState.TextoYArr);
                _canvas.Children.Remove(colState.TextoYAbj);
            }
            _columnasVisuales.Clear();
            _estadosColumnas.Clear();
            _columnaSeleccionada = null;
        }

        public bool HandleColumnMovementKeys(Key key)
        {
            return _interactionHandler.HandleColumnMovementKeys(key, _columnaSeleccionada);
        }

        public void ActualizarEscalaVisual(double escala)
        {
            if (escala <= 0) return;
            _ultimaEscala = escala;

            foreach (var estado in _estadosColumnas.Values)
            {
                ActualizarEscalaVisualIndividual(estado, escala);
            }
        }

        private void ActualizarEscalaVisualIndividual(EstadoColumnaUI estado, double escala)
        {
            double invScale = 1.0 / escala;

            if (estado.BtnLock.RenderTransform is ScaleTransform stBtn)
            {
                stBtn.ScaleX = invScale;
                stBtn.ScaleY = invScale;
            }

            ActualizarEscalaTexto(estado.TextoXIzq, invScale);
            ActualizarEscalaTexto(estado.TextoXDer, invScale);
            ActualizarEscalaTexto(estado.TextoYArr, invScale);
            ActualizarEscalaTexto(estado.TextoYAbj, invScale);

            double strokeThk = Math.Max(0.5, 1.0 / escala);
            estado.GuiaX.StrokeThickness = strokeThk;
            estado.GuiaXDer.StrokeThickness = strokeThk;
            estado.GuiaY.StrokeThickness = strokeThk;
            estado.GuiaYAbj.StrokeThickness = strokeThk;
        }

        private void ActualizarEscalaTexto(TextBlock txt, double invScale)
        {
            if (txt != null && txt.RenderTransform is ScaleTransform st)
            {
                st.ScaleX = invScale;
                st.ScaleY = invScale;
            }
        }
    }
}
