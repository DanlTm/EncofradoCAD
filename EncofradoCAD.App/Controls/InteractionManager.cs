using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace EncofradoCAD.App.Controls
{
    public class InteractionManager
    {
        private readonly Canvas _canvas;
        private readonly PolygonManager _polygonManager;
        private readonly double _pixelesPorMetro;

        private readonly PolygonGeometryService _geometryService;
        private readonly PolygonInteractionHandler _interactionHandler;

        // Propiedades de estado de interacción
        private readonly List<Shape> _segmentosGeometricos = new List<Shape>();
        private readonly List<TextBlock> _textosMedidas = new List<TextBlock>();
        private readonly List<Ellipse> _verticesVisuales = new List<Ellipse>();

        // Anotaciones de lados CURVOS: cuerda (recta inicio→fin) semitransparente + su etiqueta de distancia.
        private readonly List<Line> _cuerdasCurvas = new List<Line>();
        private readonly List<TextBlock> _textosCuerda = new List<TextBlock>();

        /// <summary>Si es true, en los lados curvos se dibuja la cuerda semitransparente y su medida recta.</summary>
        public bool MostrarAnotacionesCurva { get; set; } = true;

        public int VerticeEnEdicion { get; set; } = -1;
        public int SegmentoEnMovimiento { get; set; } = -1;
        public Point PosicionInicialSegmento { get; set; }
        public Point PosicionpreviewVertice { get; set; }
        public bool EscalaEstablecida { get; set; } = false;
        private double _ultimaEscala = 1.0;

        public InteractionManager(Canvas canvas, PolygonManager polygonManager, double pixelesPorMetro)
        {
            _canvas = canvas;
            _polygonManager = polygonManager;
            _pixelesPorMetro = pixelesPorMetro;

            _geometryService = new PolygonGeometryService(polygonManager);
            _interactionHandler = new PolygonInteractionHandler(canvas, polygonManager, _geometryService, this, pixelesPorMetro);
        }

        public void Limpiar()
        {
            foreach (var s in _segmentosGeometricos) _canvas.Children.Remove(s);
            foreach (var t in _textosMedidas) _canvas.Children.Remove(t);
            foreach (var v in _verticesVisuales) _canvas.Children.Remove(v);
            foreach (var c in _cuerdasCurvas) _canvas.Children.Remove(c);
            foreach (var tc in _textosCuerda) _canvas.Children.Remove(tc);

            _segmentosGeometricos.Clear();
            _textosMedidas.Clear();
            _verticesVisuales.Clear();
            _cuerdasCurvas.Clear();
            _textosCuerda.Clear();

            EscalaEstablecida = false;
            VerticeEnEdicion = -1;
            SegmentoEnMovimiento = -1;
        }

        public void RedibujarPoligono()
        {
            _polygonManager.LosaPath.IsVisible = false;

            foreach (var s in _segmentosGeometricos) _canvas.Children.Remove(s);
            foreach (var t in _textosMedidas) _canvas.Children.Remove(t);
            foreach (var v in _verticesVisuales) _canvas.Children.Remove(v);
            foreach (var c in _cuerdasCurvas) _canvas.Children.Remove(c);
            foreach (var tc in _textosCuerda) _canvas.Children.Remove(tc);

            _segmentosGeometricos.Clear();
            _textosMedidas.Clear();
            _verticesVisuales.Clear();
            _cuerdasCurvas.Clear();
            _textosCuerda.Clear();

            if (_polygonManager.PuntoInicial == null) return;

            Point pActual = _polygonManager.PuntoInicial.Value;

            for (int i = 0; i < _polygonManager.Tramos.Count; i++)
            {
                Point pSegSiguiente = _polygonManager.Tramos[i].PuntoFinal;
                TipoSegmento tipo = _polygonManager.Tramos[i].Tipo;

                CrearObjetoSegmento(pActual, pSegSiguiente, tipo, i);
                pActual = pSegSiguiente;
            }

            // Conectar con el inicio
            CrearObjetoSegmento(pActual, _polygonManager.PuntoInicial.Value, _polygonManager.ModoTrazoActual, _polygonManager.Tramos.Count);

            if (!_polygonManager.IsDrawingMode)
            {
                DibujarVertices();
            }

            // El voladizo (línea entrecortada) debe recalcularse con la losa: al mover un lado, un vértice o
            // cambiar una medida se pasa por aquí, así el contorno de 20 cm sigue al plano verde editado.
            _polygonManager.ActualizarVoladizoVisual();
        }

        public void RedibujarPoligonoConPreview()
        {
            foreach (var s in _segmentosGeometricos) _canvas.Children.Remove(s);
            foreach (var t in _textosMedidas) _canvas.Children.Remove(t);
            foreach (var v in _verticesVisuales) _canvas.Children.Remove(v);
            foreach (var c in _cuerdasCurvas) _canvas.Children.Remove(c);
            foreach (var tc in _textosCuerda) _canvas.Children.Remove(tc);

            _segmentosGeometricos.Clear();
            _textosMedidas.Clear();
            _verticesVisuales.Clear();
            _cuerdasCurvas.Clear();
            _textosCuerda.Clear();

            List<Point> verticesConPreview = new List<Point> { _polygonManager.PuntoInicial!.Value };
            foreach (var tramo in _polygonManager.Tramos) verticesConPreview.Add(tramo.PuntoFinal);

            verticesConPreview[VerticeEnEdicion] = PosicionpreviewVertice;

            Point pActual = verticesConPreview[0];
            for (int i = 1; i < verticesConPreview.Count; i++)
            {
                Point pSegSiguiente = verticesConPreview[i];
                TipoSegmento tipo = i <= _polygonManager.Tramos.Count ? _polygonManager.Tramos[i - 1].Tipo : _polygonManager.ModoTrazoActual;

                CrearObjetoSegmento(pActual, pSegSiguiente, tipo, i - 1);
                pActual = pSegSiguiente;
            }

            // Conectar con el inicio
            CrearObjetoSegmento(pActual, verticesConPreview[0], _polygonManager.ModoTrazoActual, _polygonManager.Tramos.Count);

            DibujarVerticesConPreview(verticesConPreview);

            // Voladizo en VIVO: se recalcula con los vértices del preview (incluye el que se está arrastrando),
            // así la línea entrecortada acompaña al plano verde durante el arrastre, no solo al soltar.
            _polygonManager.ActualizarVoladizoVisual(verticesConPreview);
        }

        private void CrearObjetoSegmento(Point p1, Point p2, TipoSegmento tipo, int index)
        {
            Shape pathItem = PolygonVisualBuilder.CrearVisualSegmento(p1, p2, tipo, index, _polygonManager);
            _interactionHandler.AdjuntarEventosSegmento(pathItem, p1, p2, tipo, index);

            _canvas.Children.Add(pathItem);
            _segmentosGeometricos.Add(pathItem);

            double distPx = (tipo == TipoSegmento.Recto) ? GeometryHelper.Distancia(p1, p2) : GeometryHelper.CalcularLargoArco(p1, p2, tipo);
            double distMetros = distPx / _pixelesPorMetro;

            Point mid = GeometryHelper.PuntoMedio(p1, p2);
            if (tipo != TipoSegmento.Recto)
            {
                double dx = p2.X - p1.X;
                double dy = p2.Y - p1.Y;
                double nx = dy;
                double ny = -dx;
                double len = Math.Sqrt(nx * nx + ny * ny);
                if (len > 0) { nx /= len; ny /= len; }
                double radioText = tipo == TipoSegmento.MediaLuna ? distPx / 2.0 : distPx * 0.3;
                mid = new Point(mid.X + nx * radioText, mid.Y + ny * radioText);
            }

            TextBlock txt = PolygonVisualBuilder.CrearTextoMedida(mid, distMetros);
            // En lados curvos, la medida principal es la LONGITUD DE ARCO; se marca con ⌒ para distinguirla.
            if (tipo != TipoSegmento.Recto)
            {
                txt.Text = $"⌒ {distMetros:F2}m";
            }
            if (txt.RenderTransform is ScaleTransform st)
            {
                st.ScaleX = 1.0 / _ultimaEscala;
                st.ScaleY = 1.0 / _ultimaEscala;
            }
            _interactionHandler.AdjuntarEventosTexto(txt, p1, p2, tipo, index);

            _canvas.Children.Add(txt);
            _textosMedidas.Add(txt);

            // ── Anotación de CUERDA (solo lados curvos): línea recta inicio→fin + distancia recta ──────────
            if (tipo != TipoSegmento.Recto && MostrarAnotacionesCurva)
            {
                DibujarAnotacionCuerda(p1, p2);
            }
        }

        /// <summary>
        /// Dibuja para un lado curvo la CUERDA semitransparente (recta de inicio a fin) y su etiqueta con la
        /// distancia recta entre el punto donde inicia y donde termina la curva.
        /// </summary>
        private void DibujarAnotacionCuerda(Point p1, Point p2)
        {
            var cuerda = new Line
            {
                StartPoint = p1,
                EndPoint = p2,
                Stroke = new SolidColorBrush(Color.FromArgb(130, 0x00, 0xE5, 0xFF)), // cian semitransparente
                StrokeThickness = Math.Max(0.5, 1.2 / _ultimaEscala),
                StrokeDashArray = new AvaloniaList<double> { 4, 3 },
                IsHitTestVisible = false,
            };
            _canvas.Children.Add(cuerda);
            _cuerdasCurvas.Add(cuerda);

            double cuerdaMetros = GeometryHelper.Distancia(p1, p2) / _pixelesPorMetro;
            Point midCuerda = GeometryHelper.PuntoMedio(p1, p2);

            var txtCuerda = new TextBlock
            {
                Text = $"↔ {cuerdaMetros:F2}m",
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
                Background = new SolidColorBrush(Color.FromArgb(180, 20, 20, 20)),
                Padding = new Thickness(3, 1, 3, 1),
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                IsHitTestVisible = false,
                RenderTransform = new ScaleTransform(1.0 / _ultimaEscala, 1.0 / _ultimaEscala),
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            };
            Canvas.SetLeft(txtCuerda, midCuerda.X - 18);
            Canvas.SetTop(txtCuerda, midCuerda.Y - 8);
            _canvas.Children.Add(txtCuerda);
            _textosCuerda.Add(txtCuerda);
        }

        /// <summary>Muestra u oculta las anotaciones de cuerda de los lados curvos y redibuja.</summary>
        public void AlternarAnotacionesCurva(bool mostrar)
        {
            MostrarAnotacionesCurva = mostrar;
            RedibujarPoligono();
        }

        private void DibujarVertices()
        {
            List<Point> vertices = new List<Point> { _polygonManager.PuntoInicial!.Value };
            foreach (var tramo in _polygonManager.Tramos) vertices.Add(tramo.PuntoFinal);

            foreach (var vertice in vertices)
            {
                Ellipse circle = PolygonVisualBuilder.CrearVisualVertice(vertice, false);
                Canvas.SetLeft(circle, vertice.X - (circle.Width / 2));
                Canvas.SetTop(circle, vertice.Y - (circle.Height / 2));
                _canvas.Children.Add(circle);
                _verticesVisuales.Add(circle);

                double vertexScale = Math.Max(0.5, 1.0 / _ultimaEscala);
                circle.RenderTransform = new ScaleTransform(vertexScale, vertexScale);
                circle.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            }
        }

        private void DibujarVerticesConPreview(List<Point> vertices)
        {
            for (int i = 0; i < vertices.Count; i++)
            {
                Ellipse circle = PolygonVisualBuilder.CrearVisualVertice(vertices[i], i == VerticeEnEdicion);
                Canvas.SetLeft(circle, vertices[i].X - (circle.Width / 2));
                Canvas.SetTop(circle, vertices[i].Y - (circle.Height / 2));
                _canvas.Children.Add(circle);
                _verticesVisuales.Add(circle);

                double vertexScale = Math.Max(0.5, 1.0 / _ultimaEscala);
                circle.RenderTransform = new ScaleTransform(vertexScale, vertexScale);
                circle.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            }
        }

        public string ActualizarMovimientoSegmento(Point mousePos)
        {
            List<Point> vertices = new List<Point> { _polygonManager.PuntoInicial!.Value };
            foreach (var tramo in _polygonManager.Tramos) vertices.Add(tramo.PuntoFinal);

            Point p1 = vertices[SegmentoEnMovimiento];
            Point p2 = vertices[(SegmentoEnMovimiento + 1) % vertices.Count];

            Vector segmentVector = p2 - p1;
            double segLen = Math.Sqrt(segmentVector.X * segmentVector.X + segmentVector.Y * segmentVector.Y);
            if (segLen > 0) segmentVector = new Vector(segmentVector.X / segLen, segmentVector.Y / segLen);

            Vector mouseVector = mousePos - PosicionInicialSegmento;
            double perpComponent = segmentVector.X * mouseVector.Y - segmentVector.Y * mouseVector.X;
            Vector perpUnitVector = new Vector(-segmentVector.Y, segmentVector.X);
            Vector displacement = perpUnitVector * perpComponent;

            if (SegmentoEnMovimiento == 0)
                _polygonManager.PuntoInicial = p1 + displacement;
            else
                _polygonManager.Tramos[SegmentoEnMovimiento - 1].PuntoFinal = p1 + displacement;

            _polygonManager.Tramos[SegmentoEnMovimiento].PuntoFinal = p2 + displacement;
            PosicionInicialSegmento = mousePos;

            double distancia = perpComponent / _pixelesPorMetro;
            RedibujarPoligono();

            return $"Distancia: {distancia:0.00}m\n(Suelta para confirmar)";
        }

        public string ActualizarMovimientoVertice(Point mousePos)
        {
            PosicionpreviewVertice = mousePos;
            RedibujarPoligonoConPreview();

            return ObtenerTextoPosicionVertice();
        }

        public void FinalizarEdicionVertice()
        {
            if (VerticeEnEdicion == -1) return;

            if (VerticeEnEdicion == 0)
                _polygonManager.PuntoInicial = PosicionpreviewVertice;
            else
                _polygonManager.Tramos[VerticeEnEdicion - 1].PuntoFinal = PosicionpreviewVertice;

            VerticeEnEdicion = -1;
            RedibujarPoligono();
        }

        public bool MoverVerticeConTeclado(Key key)
        {
            if (VerticeEnEdicion < 0) return false;

            double movePx = 0.01 * _pixelesPorMetro;
            double deltaX = 0;
            double deltaY = 0;

            if (key == Key.W) deltaY = -movePx;
            else if (key == Key.S) deltaY = movePx;
            else if (key == Key.A) deltaX = -movePx;
            else if (key == Key.D) deltaX = movePx;
            else return false;

            PosicionpreviewVertice = new Point(
                PosicionpreviewVertice.X + deltaX,
                PosicionpreviewVertice.Y + deltaY);

            RedibujarPoligonoConPreview();
            return true;
        }

        public void CancelarEdicionVertice()
        {
            if (VerticeEnEdicion == -1) return;

            VerticeEnEdicion = -1;
            RedibujarPoligono();
        }

        public string ObtenerTextoPosicionVertice()
        {
            double xMetros = PosicionpreviewVertice.X / _pixelesPorMetro;
            double yMetros = PosicionpreviewVertice.Y / _pixelesPorMetro;
            return $"X: {xMetros:0.00}m\nY: {yMetros:0.00}m\n(W/A/S/D mover · Enter fijar)";
        }

        public void ActualizarEscalaVisual(double escala)
        {
            if (escala <= 0) return;
            _ultimaEscala = escala;
            double invScale = 1.0 / escala;

            foreach (var txt in _textosMedidas)
            {
                if (txt.RenderTransform is ScaleTransform st)
                {
                    st.ScaleX = invScale;
                    st.ScaleY = invScale;
                }
            }

            foreach (var txt in _textosCuerda)
            {
                if (txt.RenderTransform is ScaleTransform st)
                {
                    st.ScaleX = invScale;
                    st.ScaleY = invScale;
                }
            }

            double strokeThk = Math.Max(0.5, 1.5 / escala);
            foreach (var s in _segmentosGeometricos)
            {
                s.StrokeThickness = strokeThk;
            }

            double cuerdaThk = Math.Max(0.5, 1.2 / escala);
            foreach (var c in _cuerdasCurvas)
            {
                c.StrokeThickness = cuerdaThk;
            }

            double vertexScale = Math.Max(0.5, 1.0 / escala);
            foreach (var v in _verticesVisuales)
            {
                if (v.RenderTransform is ScaleTransform stV)
                {
                    stV.ScaleX = vertexScale;
                    stV.ScaleY = vertexScale;
                }
                else
                {
                    v.RenderTransform = new ScaleTransform(vertexScale, vertexScale);
                    v.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
                }
            }
        }
    }
}
