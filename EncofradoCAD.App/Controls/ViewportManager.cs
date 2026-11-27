using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace EncofradoCAD.App.Controls
{
    public class ViewportManager
    {
        private readonly Canvas _canvas;
        private readonly ScaleTransform _scaleTransform = new ScaleTransform(1, 1);
        private readonly TranslateTransform _translateTransform = new TranslateTransform(0, 0);

        public event Action<double>? EscalaCambiada;

        private bool _isPanning = false;
        private Point _panStart;
        // Mínimo = 100%: el usuario sólo puede ACERCAR (zoom in), nunca alejar por debajo del 100%.
        private const double _zoomMin = 1.0;
        private const double _zoomMax = 10.0;

        // Regla fija (tipo CAD): vive en una capa superpuesta que NO se transforma con el zoom, de modo que
        // los números de metros conservan su tamaño y sólo la figura interior se acerca/aleja.
        private Canvas? _reglaOverlay;
        private double _ppm = 50.0;

        // ── FRANJAS DE LA REGLA ─────────────────────────────────────────────────────────────────────
        // La regla ocupa una franja reservada arriba y otra a la izquierda. El origen del lienzo (0 m) se
        // ancla justo al filo interior de esas franjas y NO puede correrse más allá, así el 0 m de la regla
        // superior y el de la izquierda quedan siempre a ras con la primera línea de la cuadrícula, sin el
        // hueco vacío que aparecía al arrastrar o al hacer zoom fuera.
        private const double ReglaSuperior = 20.0;
        private const double ReglaIzquierda = 34.0;

        /// <summary>Lado de la cuadrícula en unidades de lienzo (va de 0 a este valor).</summary>
        private const double LadoCuadricula = 10000.0;

        private static readonly IBrush ReglaTextoBrush = new SolidColorBrush(Color.FromArgb(220, 200, 200, 200));
        private static readonly IBrush ReglaTickBrush = new SolidColorBrush(Color.FromArgb(120, 150, 150, 150));
        private static readonly IBrush ReglaFondoBrush = new SolidColorBrush(Color.FromArgb(242, 0x16, 0x16, 0x16));
        private static readonly IBrush ReglaFiloBrush = new SolidColorBrush(Color.FromArgb(255, 0x2E, 0x33, 0x3A));

        private static readonly Cursor CursorHand = new Cursor(StandardCursorType.Hand);
        private static readonly Cursor CursorArrow = new Cursor(StandardCursorType.Arrow);

        public ViewportManager(Canvas canvas)
        {
            _canvas = canvas;

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(_scaleTransform);
            transformGroup.Children.Add(_translateTransform);
            _canvas.RenderTransform = transformGroup;
            // WPF aplica RenderTransform desde la esquina (0,0); en Avalonia el origen por defecto es el
            // centro, así que lo fijamos a la esquina para que pan/zoom se comporten igual.
            _canvas.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);

            // Posición de partida: el 0 m del lienzo pegado al filo interior de las dos reglas.
            _translateTransform.X = ReglaIzquierda;
            _translateTransform.Y = ReglaSuperior;
        }

        private Visual Parent => (_canvas.GetVisualParent() as Visual) ?? _canvas;

        /// <summary>
        /// Sujeta el desplazamiento del lienzo para que la cuadrícula no deje NUNCA un hueco vacío: el origen
        /// no puede pasar del filo interior de las reglas (por eso el 0 m queda siempre a ras) ni arrastrarse
        /// más allá del extremo opuesto de la cuadrícula.
        /// </summary>
        private void AjustarLimites()
        {
            double escala = _scaleTransform.ScaleX;
            double w = Parent.Bounds.Width;
            double h = Parent.Bounds.Height;

            double maxX = ReglaIzquierda;
            double maxY = ReglaSuperior;
            double minX = w > 1 ? Math.Min(maxX, w - (LadoCuadricula * escala)) : maxX;
            double minY = h > 1 ? Math.Min(maxY, h - (LadoCuadricula * escala)) : maxY;

            _translateTransform.X = Math.Clamp(_translateTransform.X, minX, maxX);
            _translateTransform.Y = Math.Clamp(_translateTransform.Y, minY, maxY);
        }

        public void HandleMouseDown(PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(_canvas).Properties.IsMiddleButtonPressed)
            {
                _isPanning = true;
                _panStart = e.GetPosition(Parent);
                _canvas.Cursor = CursorHand;
                e.Pointer.Capture(_canvas);
                e.Handled = true;
            }
        }

        public void HandleMouseUp(PointerReleasedEventArgs e)
        {
            if (_isPanning && e.InitialPressMouseButton == MouseButton.Middle)
            {
                _isPanning = false;
                _canvas.Cursor = CursorArrow;
                e.Pointer.Capture(null);
                e.Handled = true;
            }
        }

        public void HandleMouseMove(PointerEventArgs e)
        {
            if (_isPanning)
            {
                Point currentInParent = e.GetPosition(Parent);
                _translateTransform.X += currentInParent.X - _panStart.X;
                _translateTransform.Y += currentInParent.Y - _panStart.Y;
                _panStart = currentInParent;
                AjustarLimites();   // el arrastre no puede despegar la cuadrícula de la regla
                ActualizarReglas(); // la regla sigue el desplazamiento (pan)
            }
        }

        public void HandleMouseWheel(PointerWheelEventArgs e)
        {
            Point mouseInParent = e.GetPosition(Parent);

            double oldScale = _scaleTransform.ScaleX;
            double factor = e.Delta.Y > 0 ? 1.1 : (1.0 / 1.1);
            double newScale = Math.Max(_zoomMin, Math.Min(_zoomMax, oldScale * factor));

            if (Math.Abs(newScale - oldScale) < 0.0001) return;

            double scaleRatio = newScale / oldScale;
            _translateTransform.X = mouseInParent.X - scaleRatio * (mouseInParent.X - _translateTransform.X);
            _translateTransform.Y = mouseInParent.Y - scaleRatio * (mouseInParent.Y - _translateTransform.Y);

            _scaleTransform.ScaleX = newScale;
            _scaleTransform.ScaleY = newScale;

            AjustarLimites();            // al alejar, el origen no puede despegarse del filo de la regla
            ActualizarReglas();          // recalcula la regla fija con la nueva escala
            EscalaCambiada?.Invoke(newScale);

            e.Handled = true;
        }

        public void DibujarCuadricula(double pixelesPorMetro)
        {
            _ppm = pixelesPorMetro;
            double espacioCuadricula = 1.0 * pixelesPorMetro;

            DrawingGroup gridGroup = new DrawingGroup();
            SolidColorBrush colorLinea = new SolidColorBrush(Color.FromArgb(50, 100, 100, 100));
            var pen = new Pen(colorLinea, 1);

            for (double x = 0; x <= 10000; x += espacioCuadricula)
            {
                var linea = new LineGeometry { StartPoint = new Point(x, 0), EndPoint = new Point(x, 10000) };
                gridGroup.Children.Add(new GeometryDrawing { Geometry = linea, Pen = pen });
            }

            for (double y = 0; y <= 10000; y += espacioCuadricula)
            {
                var linea = new LineGeometry { StartPoint = new Point(0, y), EndPoint = new Point(10000, y) };
                gridGroup.Children.Add(new GeometryDrawing { Geometry = linea, Pen = pen });
            }

            Image gridImage = new Image
            {
                Source = new DrawingImage { Drawing = gridGroup },
                Stretch = Stretch.None,
                Width = 10000,
                Height = 10000,
            };

            _canvas.Children.Insert(0, gridImage);
            ActualizarReglas();
        }

        // ── Regla fija tipo CAD ─────────────────────────────────────────────────────────────────────
        /// <summary>Conecta la capa (Canvas no transformado) donde se dibujan las reglas de metros.</summary>
        public void SetReglaOverlay(Canvas overlay)
        {
            _reglaOverlay = overlay;
            ActualizarReglas();
        }

        /// <summary>
        /// Redibuja las reglas superior (eje X) e izquierda (eje Y). Las etiquetas mantienen su tamaño
        /// (viven fuera del RenderTransform del lienzo) y sólo cambian de POSICIÓN y PASO según el zoom/pan,
        /// como en AutoCAD: la figura interior se acerca/aleja pero la regla permanece fija y legible.
        /// </summary>
        public void ActualizarReglas()
        {
            if (_reglaOverlay == null) return;

            double w = _reglaOverlay.Bounds.Width;
            double h = _reglaOverlay.Bounds.Height;
            _reglaOverlay.Children.Clear();
            if (w < 20 || h < 20) return;

            // Al cambiar de tamaño la ventana el desplazamiento pudo quedar fuera de rango.
            AjustarLimites();

            double scale = _scaleTransform.ScaleX;
            double tx = _translateTransform.X;
            double ty = _translateTransform.Y;
            double pxPorMetro = _ppm * scale;
            if (pxPorMetro <= 0.0001) return;

            double step = ElegirPaso(pxPorMetro, 48.0); // ≥ 48 px entre marcas para no solaparse

            DibujarFranjas(w, h);

            // Regla superior (eje X). Arranca en el primer metro que cae DENTRO del área de dibujo, es decir
            // a partir del filo de la regla izquierda: con el lienzo en su sitio, ese primer metro es el 0.
            double mIniX = Math.Max(0, Math.Floor(((ReglaIzquierda - tx) / pxPorMetro) / step) * step);
            for (double m = mIniX; m <= 100000; m += step)
            {
                double sx = (m * pxPorMetro) + tx;
                if (sx > w) break;
                if (sx < ReglaIzquierda - 0.5) continue;
                AgregarMarca(
                    new Point(sx, ReglaSuperior - 6), new Point(sx, ReglaSuperior),
                    FormatoMetros(m), new Point(sx + 3, 3));
            }

            // Regla izquierda (eje Y), con el mismo criterio respecto al filo de la regla superior.
            double mIniY = Math.Max(0, Math.Floor(((ReglaSuperior - ty) / pxPorMetro) / step) * step);
            for (double m = mIniY; m <= 100000; m += step)
            {
                double sy = (m * pxPorMetro) + ty;
                if (sy > h) break;
                if (sy < ReglaSuperior - 0.5) continue;
                AgregarMarca(
                    new Point(ReglaIzquierda - 6, sy), new Point(ReglaIzquierda, sy),
                    FormatoMetros(m), new Point(3, sy + 2));
            }
        }

        /// <summary>
        /// Franjas de la regla: la superior y la izquierda, con su filo interior marcado. Van por encima del
        /// lienzo, así los números se leen limpios y la cuadrícula empieza exactamente en ese filo.
        /// </summary>
        private void DibujarFranjas(double w, double h)
        {
            if (_reglaOverlay == null) return;

            var superior = new Rectangle { Width = w, Height = ReglaSuperior, Fill = ReglaFondoBrush };
            Canvas.SetLeft(superior, 0);
            Canvas.SetTop(superior, 0);
            _reglaOverlay.Children.Add(superior);

            var izquierda = new Rectangle { Width = ReglaIzquierda, Height = h, Fill = ReglaFondoBrush };
            Canvas.SetLeft(izquierda, 0);
            Canvas.SetTop(izquierda, 0);
            _reglaOverlay.Children.Add(izquierda);

            _reglaOverlay.Children.Add(new Line
            {
                StartPoint = new Point(0, ReglaSuperior - 0.5),
                EndPoint = new Point(w, ReglaSuperior - 0.5),
                Stroke = ReglaFiloBrush,
                StrokeThickness = 1,
            });
            _reglaOverlay.Children.Add(new Line
            {
                StartPoint = new Point(ReglaIzquierda - 0.5, 0),
                EndPoint = new Point(ReglaIzquierda - 0.5, h),
                Stroke = ReglaFiloBrush,
                StrokeThickness = 1,
            });
        }

        private void AgregarMarca(Point tickIni, Point tickFin, string texto, Point textoPos)
        {
            if (_reglaOverlay == null) return;

            var tick = new Line
            {
                StartPoint = tickIni,
                EndPoint = tickFin,
                Stroke = ReglaTickBrush,
                StrokeThickness = 1,
            };
            _reglaOverlay.Children.Add(tick);

            var lbl = new TextBlock
            {
                Text = texto,
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = ReglaTextoBrush,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(lbl, textoPos.X);
            Canvas.SetTop(lbl, textoPos.Y);
            _reglaOverlay.Children.Add(lbl);
        }

        private static double ElegirPaso(double pxPorMetro, double minPx)
        {
            double[] pasos = { 1, 2, 5, 10, 20, 50, 100, 200, 500 };
            foreach (double p in pasos)
            {
                if (p * pxPorMetro >= minPx) return p;
            }
            return pasos[pasos.Length - 1];
        }

        private static string FormatoMetros(double m)
        {
            int metros = (int)Math.Round(m);
            return metros.ToString(CultureInfo.InvariantCulture) + "m";
        }
    }
}
