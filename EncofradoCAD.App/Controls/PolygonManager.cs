using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using EncofradoCAD.Core.Layout;
using Path = Avalonia.Controls.Shapes.Path;
using CoreGeom = EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.App.Controls
{
    public class PolygonManager
    {
        private readonly Canvas _canvas;
        public Point? PuntoInicial { get; set; }
        public List<TramoLosa> Tramos { get; } = new List<TramoLosa>();
        public Path LosaPath { get; private set; }
        public Path TramoTemporal { get; private set; }

        /// <summary>Contorno del voladizo (20 cm hacia afuera del borde de la losa), línea entrecortada gris.</summary>
        public Path VoladizoPath { get; private set; }

        /// <summary>
        /// Si se dibuja o no la línea entrecortada blanca del voladizo al trazar el plano. Por decisión del
        /// usuario queda OCULTA por defecto (el voladizo ahora es variable 5–20 cm, así que ese contorno fijo
        /// de 20 cm ya no representa el resultado). Poner en true si se quisiera volver a mostrar.
        /// </summary>
        public bool MostrarVoladizo { get; set; } = false;

        /// <summary>Voladizo del encofrado por lado (m): rebasa 20 cm el borde de la losa en los 4 lados.</summary>
        public const double VoladizoMetros = 0.20;

        public bool IsDrawingMode { get; set; } = true;
        public TipoSegmento ModoTrazoActual { get; set; } = TipoSegmento.Recto;

        public PolygonManager(Canvas canvas)
        {
            _canvas = canvas;

            LosaPath = new Path
            {
                // Línea de la losa en verde lima neón para que resalte sobre el resto.
                Stroke = new SolidColorBrush(Color.FromRgb(0xB6, 0xFF, 0x00)),
                StrokeThickness = 1.8,
                Fill = new SolidColorBrush(Color.FromArgb(40, 52, 152, 219)),
            };

            TramoTemporal = new Path
            {
                Stroke = Brushes.LightGray,
                StrokeThickness = 1,
                StrokeDashArray = new AvaloniaList<double> { 2, 2 },
                IsVisible = false,
            };

            VoladizoPath = new Path
            {
                // Contorno del voladizo: línea entrecortada blanca/gris alrededor de la losa.
                Stroke = new SolidColorBrush(Color.FromArgb(210, 210, 210, 210)),
                StrokeThickness = 1.2,
                StrokeDashArray = new AvaloniaList<double> { 6, 4 },
                IsVisible = false,
            };

            _canvas.Children.Add(VoladizoPath);
            _canvas.Children.Add(LosaPath);
            _canvas.Children.Add(TramoTemporal);
        }

        /// <summary>Vértices del polígono en píxeles (punto inicial + fin de cada tramo).</summary>
        public List<Point> ObtenerVertices()
        {
            var pts = new List<Point>();
            if (PuntoInicial == null) return pts;
            pts.Add(PuntoInicial.Value);
            foreach (var t in Tramos) pts.Add(t.PuntoFinal);
            return pts;
        }

        /// <summary>
        /// Contorno del polígono en píxeles con los lados CURVOS teselados en puntos (siguiendo el arco real
        /// dibujado). Los lados rectos aportan solo su vértice inicial. Se usa para que el modelo 3D y el
        /// motor de cálculo sigan el contorno curvo (media luna / ovalado) en vez de aproximarlo por la cuerda.
        /// </summary>
        public List<Point> ObtenerVerticesFlatten(int segmentosPorArco = 24)
        {
            var pts = new List<Point>();
            if (PuntoInicial == null) return pts;

            List<Point> verts = ObtenerVertices();
            int n = verts.Count;
            for (int k = 0; k < n; k++)
            {
                Point a = verts[k];
                Point b = verts[(k + 1) % n];
                // El lado k va de verts[k] a verts[k+1]; su tipo es el del tramo que termina en verts[k+1].
                // El lado de cierre (k == Tramos.Count) usa ModoTrazoActual, igual que el redibujo interactivo.
                TipoSegmento tipo = k < Tramos.Count ? Tramos[k].Tipo : ModoTrazoActual;

                pts.Add(a);
                if (tipo != TipoSegmento.Recto)
                {
                    pts.AddRange(GeometryHelper.PuntosArcoIntermedios(a, b, tipo, segmentosPorArco));
                }
            }
            return pts;
        }

        public void ActualizarLosaVisual()
        {
            if (PuntoInicial == null) return;

            var pathFigure = new PathFigure { StartPoint = PuntoInicial.Value, IsClosed = !IsDrawingMode };

            Point pActual = PuntoInicial.Value;
            foreach (var tramo in Tramos)
            {
                PathSegment segmento = CrearPathSegment(pActual, tramo.PuntoFinal, tramo.Tipo);
                pathFigure.Segments!.Add(segmento);
                pActual = tramo.PuntoFinal;
            }

            var pathGeo = new PathGeometry { Figures = new PathFigures { pathFigure } };

            LosaPath.Data = pathGeo;

            ActualizarVoladizoVisual();
        }

        /// <summary>
        /// Dibuja el contorno del voladizo (20 cm hacia afuera del borde de la losa) como línea entrecortada.
        /// Solo se muestra cuando la losa está cerrada (no en pleno trazo). Reutiliza el offset de Core.
        /// </summary>
        public void ActualizarVoladizoVisual(List<Point>? verticesPx = null)
        {
            // Oculta por defecto (MostrarVoladizo = false): ya no se dibuja la línea entrecortada blanca.
            if (!MostrarVoladizo)
            {
                VoladizoPath.IsVisible = false;
                return;
            }

            if (PuntoInicial == null || IsDrawingMode)
            {
                VoladizoPath.IsVisible = false;
                return;
            }

            double ppm = (_canvas as DrawingCanvas)?.PixelesPorMetro ?? 50.0;
            double dPx = VoladizoMetros * ppm;

            // Si se pasan vértices (p. ej. el preview en vivo de una edición), se usan esos para que la línea
            // entrecortada siga al plano verde MIENTRAS el usuario lo mueve; si no, se toma el contorno real.
            List<Point> vpx = verticesPx ?? ObtenerVerticesFlatten();
            if (vpx.Count < 3)
            {
                VoladizoPath.IsVisible = false;
                return;
            }

            List<CoreGeom.Point> core = vpx.Select(p => new CoreGeom.Point(p.X, p.Y)).ToList();
            List<CoreGeom.Point> off = GeometryUtil.OffsetPolygon(core, dPx);
            if (off.Count < 3)
            {
                VoladizoPath.IsVisible = false;
                return;
            }

            var fig = new PathFigure { StartPoint = new Point(off[0].X, off[0].Y), IsClosed = true };
            for (int i = 1; i < off.Count; i++)
            {
                fig.Segments!.Add(new LineSegment { Point = new Point(off[i].X, off[i].Y) });
            }

            VoladizoPath.Data = new PathGeometry { Figures = new PathFigures { fig } };
            VoladizoPath.IsVisible = true;
        }

        public void AgregarPunto(Point clickPoint, bool shiftPressed, Point puntoActual)
        {
            Point point = shiftPressed
                ? GeometryHelper.SnapPunto(clickPoint, puntoActual, PuntoInicial, Tramos.Count)
                : clickPoint;

            if (PuntoInicial == null)
            {
                PuntoInicial = point;
                ActualizarLosaVisual();
            }
            else
            {
                if (Tramos.Count >= 2 && GeometryHelper.Distancia(PuntoInicial.Value, point) < 15)
                {
                    TerminarLosa(null); // El llamador manejará el callback si es necesario
                }
                else
                {
                    Tramos.Add(new TramoLosa { PuntoFinal = point, Tipo = ModoTrazoActual });
                    ActualizarLosaVisual();
                    ModoTrazoActual = TipoSegmento.Recto;
                }
            }
        }

        public void ActualizarPreview(Point mousePos, bool shiftPressed, Point puntoActual)
        {
            if (PuntoInicial == null) return;

            Point snappedPoint = shiftPressed
                ? GeometryHelper.SnapPunto(mousePos, puntoActual, PuntoInicial, Tramos.Count)
                : mousePos;

            TramoTemporal.Data = CrearGeometriaSegmento(puntoActual, snappedPoint, ModoTrazoActual);
            TramoTemporal.IsVisible = true;
        }

        public PathSegment CrearPathSegment(Point p1, Point p2, TipoSegmento tipo)
        {
            if (tipo == TipoSegmento.Recto)
            {
                return new LineSegment { Point = p2 };
            }
            else
            {
                double d = GeometryHelper.Distancia(p1, p2);
                double radio = tipo == TipoSegmento.MediaLuna ? d / 2 : d;

                return new ArcSegment
                {
                    Point = p2,
                    Size = new Size(radio, radio),
                    RotationAngle = 0,
                    IsLargeArc = false,
                    SweepDirection = SweepDirection.Clockwise,
                };
            }
        }

        public Geometry CrearGeometriaSegmento(Point p1, Point p2, TipoSegmento tipo)
        {
            var fig = new PathFigure { StartPoint = p1, IsClosed = false };
            fig.Segments!.Add(CrearPathSegment(p1, p2, tipo));
            var geo = new PathGeometry { Figures = new PathFigures { fig } };
            return geo;
        }

        public void TerminarLosa(Action? onFinished)
        {
            if (Tramos.Count >= 2)
            {
                IsDrawingMode = false;
                TramoTemporal.IsVisible = false;

                SimplificarPoligono();
                RegularizarPoligono();
                onFinished?.Invoke();
                ActualizarLosaVisual();
            }
        }

        public void SimplificarPoligono()
        {
            GeometryHelper.SimplificarPoligono(PuntoInicial, ModoTrazoActual, Tramos, out Point? nuevoPuntoInicial, out TipoSegmento nuevoModoTrazoActual);

            PuntoInicial = nuevoPuntoInicial;
            ModoTrazoActual = nuevoModoTrazoActual;
        }

        /// <summary>
        /// Endereza el polígono si el usuario lo dibujó "casi recto" (ver GeometryHelper.RegularizarPoligono).
        /// La escala px↔m sale del DrawingCanvas; si no está disponible, usa 50 px/m por defecto.
        /// </summary>
        public void RegularizarPoligono()
        {
            double pixelesPorMetro = (_canvas as DrawingCanvas)?.PixelesPorMetro ?? 50.0;

            GeometryHelper.RegularizarPoligono(
                PuntoInicial, ModoTrazoActual, Tramos, pixelesPorMetro,
                out Point? nuevoPuntoInicial, out TipoSegmento nuevoModoTrazoActual);

            PuntoInicial = nuevoPuntoInicial;
            ModoTrazoActual = nuevoModoTrazoActual;
        }

        public void Limpiar()
        {
            PuntoInicial = null;
            Tramos.Clear();
            LosaPath.Data = null;
            LosaPath.IsVisible = true; // Restaurar visibilidad para el nuevo trazo
            VoladizoPath.Data = null;
            VoladizoPath.IsVisible = false;
            TramoTemporal.IsVisible = false;
            IsDrawingMode = true;
            ModoTrazoActual = TipoSegmento.Recto;
        }
    }
}
