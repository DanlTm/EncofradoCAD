using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using EncofradoCAD.App.Infrastructure;
using EncofradoCAD.App.Views.Dialogs;

namespace EncofradoCAD.App.Controls
{
    public enum TipoSegmento { Recto, MediaLuna, Ovalado }

    public class TramoLosa
    {
        public Point PuntoFinal { get; set; }
        public TipoSegmento Tipo { get; set; }
    }

    public class DrawingCanvas : Canvas
    {
        private readonly PolygonManager _polygonManager;
        private readonly InteractionManager _interactionManager;
        private readonly ViewportManager _viewportManager;
        private readonly MeasurementGuideManager _measurementGuideManager;
        private readonly ColumnManager _columnManager;
        private readonly InputHandler _inputHandler;

        private readonly TextBlock _tooltipCoordenadas;
        private readonly MenuItem _miRecto;
        private readonly MenuItem _miMediaLuna;
        private readonly MenuItem _miOvalado;
        private readonly MenuItem _miInsertarColumna;
        private readonly MenuItem _miDuplicarColumna;
        private readonly MenuItem _miEliminarColumna;
        private readonly Separator _sepCanvas;
        private readonly Separator _sepColumna;

        private Point _ultimoClicDerecho;
        private EstadoColumnaUI? _columnaContextual;

        private readonly double _pixelesPorMetro = 50.0;

        // ESCALA REAL de las columnas (1.0). Se dibujan a su tamaño verdadero para que las guías
        // midan borde-a-borde exacto. Para ver columnas pequeñas, usa el zoom (rueda del ratón).
        private const double _factorVisualizacionColumna = 1.0;

        public PolygonManager PolygonManager => _polygonManager;
        public InteractionManager InteractionManager => _interactionManager;
        public ViewportManager ViewportManager => _viewportManager;
        public MeasurementGuideManager MeasurementGuideManager => _measurementGuideManager;
        public ColumnManager ColumnManager => _columnManager;
        public InputHandler InputHandler => _inputHandler;
        public TextBlock TooltipCoordenadas => _tooltipCoordenadas;
        public double PixelesPorMetro => _pixelesPorMetro;
        public double FactorVisualizacionColumna => _factorVisualizacionColumna;

        // Tamaño (cm) con el que se insertará la próxima columna; lo fija el combo "Tipo" de la barra.
        public double ColumnaAnchoCmActual { get; set; } = 30;
        public double ColumnaLargoCmActual { get; set; } = 30;

        public Point PuntoActual => _polygonManager.Tramos.Count > 0
            ? _polygonManager.Tramos[_polygonManager.Tramos.Count - 1].PuntoFinal
            : _polygonManager.PuntoInicial ?? new Point(0, 0);

        public DrawingCanvas()
        {
            Focusable = true;
            Background = Brushes.Transparent;

            _polygonManager = new PolygonManager(this);
            _interactionManager = new InteractionManager(this, _polygonManager, _pixelesPorMetro);
            _viewportManager = new ViewportManager(this);
            _measurementGuideManager = new MeasurementGuideManager(this);
            _columnManager = new ColumnManager(this, _measurementGuideManager);
            _inputHandler = new InputHandler(this, _polygonManager, _interactionManager, _viewportManager, _columnManager, _measurementGuideManager);

            _tooltipCoordenadas = new TextBlock
            {
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                Padding = new Thickness(5, 3, 5, 3),
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                IsVisible = false,
            };
            Children.Add(_tooltipCoordenadas);

            _viewportManager.EscalaCambiada += (s) =>
            {
                _columnManager.ActualizarEscalaVisual(s);
                _interactionManager.ActualizarEscalaVisual(s);

                if (_tooltipCoordenadas.RenderTransform is ScaleTransform st)
                {
                    st.ScaleX = 1.0 / s;
                    st.ScaleY = 1.0 / s;
                }
                else
                {
                    _tooltipCoordenadas.RenderTransform = new ScaleTransform(1.0 / s, 1.0 / s);
                }
            };

            var menu = new ContextMenu();

            _miRecto = new MenuItem { Header = "Trazo Recto (Linea)" };
            _miRecto.Click += (s, e) => _polygonManager.ModoTrazoActual = TipoSegmento.Recto;

            _miMediaLuna = new MenuItem { Header = "Trazo Curvo (Media Luna)" };
            _miMediaLuna.Click += (s, e) => _polygonManager.ModoTrazoActual = TipoSegmento.MediaLuna;

            _miOvalado = new MenuItem { Header = "Trazo Curvo (Ovalado)" };
            _miOvalado.Click += (s, e) => _polygonManager.ModoTrazoActual = TipoSegmento.Ovalado;

            _miInsertarColumna = new MenuItem { Header = "Insertar Columna..." };
            _miInsertarColumna.Click += MenuInsertarColumna_Click;

            _miDuplicarColumna = new MenuItem { Header = "Duplicar columna" };
            _miDuplicarColumna.Click += (s, e) =>
            {
                if (_columnaContextual != null)
                {
                    _columnManager.DuplicarColumna(_columnaContextual);
                }
            };

            _miEliminarColumna = new MenuItem { Header = "Eliminar columna" };
            _miEliminarColumna.Click += (s, e) =>
            {
                if (_columnaContextual != null)
                {
                    _columnManager.EliminarColumna(_columnaContextual);
                    _columnaContextual = null;
                }
            };

            _sepCanvas = new Separator();
            _sepColumna = new Separator();

            menu.Items.Add(_miRecto);
            menu.Items.Add(_miMediaLuna);
            menu.Items.Add(_miOvalado);
            menu.Items.Add(_sepCanvas);
            menu.Items.Add(_miInsertarColumna);
            menu.Items.Add(_sepColumna);
            menu.Items.Add(_miDuplicarColumna);
            menu.Items.Add(_miEliminarColumna);

            menu.Opened += (s, e) =>
            {
                bool sobreColumna = _columnaContextual != null;
                bool mostrarMenuTrazo = _polygonManager.IsDrawingMode && !sobreColumna;
                bool mostrarInsertarColumna = !_polygonManager.IsDrawingMode && !sobreColumna;

                _miRecto.IsVisible = mostrarMenuTrazo;
                _miMediaLuna.IsVisible = mostrarMenuTrazo;
                _miOvalado.IsVisible = mostrarMenuTrazo;
                _miInsertarColumna.IsVisible = mostrarInsertarColumna;
                _miDuplicarColumna.IsVisible = sobreColumna;
                _miEliminarColumna.IsVisible = sobreColumna;

                _sepCanvas.IsVisible = mostrarMenuTrazo && mostrarInsertarColumna;
                _sepColumna.IsVisible = (mostrarMenuTrazo || mostrarInsertarColumna) && sobreColumna;
            };

            ContextMenu = menu;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var props = e.GetCurrentPoint(this).Properties;

            if (props.IsMiddleButtonPressed)
            {
                _viewportManager.HandleMouseDown(e);
                return;
            }

            if (props.IsRightButtonPressed)
            {
                _ultimoClicDerecho = e.GetPosition(this);
                _columnaContextual = ObtenerColumnaDesdeOrigen(e.Source as Visual);
                if (_columnaContextual != null)
                {
                    _columnManager.SeleccionarColumna(_columnaContextual);
                }
                return;
            }

            if (props.IsLeftButtonPressed)
            {
                _inputHandler.HandleMouseLeftButtonDown(e);
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            _inputHandler.HandleMouseMove(e);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (e.InitialPressMouseButton == MouseButton.Middle)
            {
                _viewportManager.HandleMouseUp(e);
                return;
            }

            _inputHandler.HandleMouseLeftButtonUp(e);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            _viewportManager.HandleMouseWheel(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            _inputHandler.HandleKeyDown(e);
        }

        private async void MenuInsertarColumna_Click(object? sender, RoutedEventArgs e)
        {
            double posXMetro = _ultimoClicDerecho.X / _pixelesPorMetro;
            double posYMetro = _ultimoClicDerecho.Y / _pixelesPorMetro;
            await _columnManager.IniciarModoInsercion(posXMetro, posYMetro, ColumnaAnchoCmActual, ColumnaLargoCmActual);
        }

        public async Task AgregarColumnaDesdeBoton()
        {
            if (_polygonManager.IsDrawingMode)
            {
                await MessageService.ShowAsync(TopLevel.GetTopLevel(this) as Window, "CADStudio",
                    "Debe terminar de dibujar la losa (cerrar el poligono) antes de agregar columnas.");
                return;
            }

            double centerX = (Bounds.Width / 2) / _pixelesPorMetro;
            double centerY = (Bounds.Height / 2) / _pixelesPorMetro;

            if (centerX <= 0 || double.IsNaN(centerX)) centerX = 2;
            if (centerY <= 0 || double.IsNaN(centerY)) centerY = 2;

            await _columnManager.IniciarModoInsercion(centerX, centerY, ColumnaAnchoCmActual, ColumnaLargoCmActual);
        }

        public void Limpiar()
        {
            _polygonManager.Limpiar();
            _interactionManager.Limpiar();
            _columnManager.Limpiar();
        }

        // ── Imagen de plano de fondo (referencia para calcar) ────────────────────────────────────────
        private Image? _imagenPlano;

        /// <summary>Coloca (o reemplaza) una imagen de plano como fondo del lienzo 2D, detrás de todo.</summary>
        public void EstablecerImagenPlano(Bitmap bitmap)
        {
            if (_imagenPlano == null)
            {
                _imagenPlano = new Image
                {
                    Stretch = Stretch.Fill,
                    Opacity = 0.65,
                    IsHitTestVisible = false,
                };
                Children.Insert(0, _imagenPlano); // al fondo, detrás de la cuadrícula y del dibujo
            }

            _imagenPlano.Source = bitmap;

            // Tamaño por defecto: el lado largo ocupa ~10 m (ajustable con el zoom), conservando proporción.
            double px = 10.0 * _pixelesPorMetro;
            double w = Math.Max(1, bitmap.PixelSize.Width);
            double h = Math.Max(1, bitmap.PixelSize.Height);
            double factor = px / Math.Max(w, h);
            _imagenPlano.Width = w * factor;
            _imagenPlano.Height = h * factor;
            Canvas.SetLeft(_imagenPlano, 0);
            Canvas.SetTop(_imagenPlano, 0);
        }

        /// <summary>Quita la imagen de plano de fondo, si hay alguna.</summary>
        public void QuitarImagenPlano()
        {
            if (_imagenPlano != null)
            {
                Children.Remove(_imagenPlano);
                _imagenPlano = null;
            }
        }

        public bool TieneImagenPlano => _imagenPlano != null;

        /// <summary>
        /// Carga un polígono (en píxeles de lienzo) como losa TERMINADA, igual que si el usuario lo hubiera
        /// dibujado: lados rectos, cerrada, lista para el 3D y el cálculo. Se usa al vectorizar una imagen.
        /// </summary>
        public void CargarPoligonoLosa(System.Collections.Generic.List<Point> verticesPx)
        {
            if (verticesPx == null || verticesPx.Count < 3)
            {
                return;
            }

            _polygonManager.Limpiar();
            _interactionManager.Limpiar();

            _polygonManager.PuntoInicial = verticesPx[0];
            _polygonManager.Tramos.Clear();
            for (int i = 1; i < verticesPx.Count; i++)
            {
                _polygonManager.Tramos.Add(new TramoLosa { PuntoFinal = verticesPx[i], Tipo = TipoSegmento.Recto });
            }

            _polygonManager.IsDrawingMode = false;
            _polygonManager.ModoTrazoActual = TipoSegmento.Recto;
            _polygonManager.SimplificarPoligono();
            _polygonManager.RegularizarPoligono();
            _polygonManager.ActualizarLosaVisual();
            _interactionManager.RedibujarPoligono();
        }

        /// <summary>
        /// Coloca una columna funcional directamente (sin diálogo), igual que si el usuario la hubiera
        /// insertado. Se usa al convertir un plano en modelo 2D. Centro en píxeles de lienzo; tamaño en metros.
        /// </summary>
        public void AgregarColumnaDirecta(Point centroPx, double anchoMetros, double largoMetros, string nombre)
        {
            var col = new DatosColumna
            {
                Nombre = nombre,
                PosX = centroPx.X / _pixelesPorMetro,
                PosY = centroPx.Y / _pixelesPorMetro,
                Ancho = anchoMetros,
                Largo = largoMetros
            };
            _columnManager.DibujarColumna(
                col,
                _polygonManager.LosaPath,
                _polygonManager.PuntoInicial,
                _polygonManager.Tramos);
        }

        /// <summary>Muestra u oculta las anotaciones de cuerda/medida recta de los lados curvos.</summary>
        public void AlternarMedidasCurva(bool mostrar) => _interactionManager.AlternarAnotacionesCurva(mostrar);

        private EstadoColumnaUI? ObtenerColumnaDesdeOrigen(Visual? origen)
        {
            var actual = origen;

            while (actual != null)
            {
                if (actual is Border borde)
                {
                    var estado = _columnManager.ObtenerEstado(borde);
                    if (estado != null)
                    {
                        return estado;
                    }
                }

                actual = actual.GetVisualParent();
            }

            return null;
        }
    }
}
