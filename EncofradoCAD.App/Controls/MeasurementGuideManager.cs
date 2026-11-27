using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;

namespace EncofradoCAD.App.Controls
{
    public class MeasurementGuideManager
    {
        private readonly Canvas _canvas;

        // Umbral de distancia (en metros) para mostrar guías hacia la pared en columnas bloqueadas.
        public double UmbralProximidadMetros { get; set; } = 1.45;

        public MeasurementGuideManager(Canvas canvas)
        {
            _canvas = canvas;
        }

        public void ActualizarGuias(
            EstadoColumnaUI estado,
            Path? losaPath,
            Point? puntoInicial,
            List<TramoLosa> tramos,
            List<Border> columnasVisuales,
            double pixelesPorMetro,
            bool esParaColumnaBloqueada = false)
        {
            double newLeft = Canvas.GetLeft(estado.Visual);
            double newTop = Canvas.GetTop(estado.Visual);

            if (double.IsNaN(newLeft) || double.IsNaN(newTop) ||
                double.IsInfinity(newLeft) || double.IsInfinity(newTop))
            {
                OcultarTodasLasGuias(estado);
                return;
            }

            double anchoPx = estado.Visual.Width;
            double largoPx = estado.Visual.Height;

            double edgeIzq = newLeft;
            double edgeDer = newLeft + anchoPx;
            double edgeArr = newTop;
            double edgeAbj = newTop + largoPx;

            double centerXpx = newLeft + anchoPx / 2;
            double centerYpx = newTop + largoPx / 2;

            // ── Segmentos del polígono (a partir de los vértices reales, cerrando la figura) ─────────
            // Los lados CURVOS (media luna / ovalado) se aproximan por una polilínea que sigue el arco real,
            // de modo que las guías de medición (líneas entrecortadas) miden la distancia de la columna a la
            // pared curva y no a su cuerda. Los lados rectos aportan un único segmento vértice→vértice.
            var segPared = new List<Tuple<Point, Point>>();
            if (puntoInicial.HasValue)
            {
                var pts = new List<Point> { puntoInicial.Value };
                foreach (var t in tramos) pts.Add(t.PuntoFinal);
                int count = pts.Count;
                for (int i = 0; i < count; i++)
                {
                    Point a = pts[i];
                    Point b = pts[(i + 1) % count];
                    if (!EsPuntoValido(a) || !EsPuntoValido(b)) continue;

                    TipoSegmento tipo = i < tramos.Count ? tramos[i].Tipo : TipoSegmento.Recto;
                    if (tipo == TipoSegmento.Recto)
                    {
                        segPared.Add(new Tuple<Point, Point>(a, b));
                    }
                    else
                    {
                        var cadena = new List<Point> { a };
                        cadena.AddRange(GeometryHelper.PuntosArcoIntermedios(a, b, tipo, 24));
                        cadena.Add(b);
                        for (int k = 0; k < cadena.Count - 1; k++)
                        {
                            if (EsPuntoValido(cadena[k]) && EsPuntoValido(cadena[k + 1]))
                                segPared.Add(new Tuple<Point, Point>(cadena[k], cadena[k + 1]));
                        }
                    }
                }
            }

            // ── 1) Intersecciones con la PARED ──────────────────────────────────────
            Point? xDerPared = null;
            Point? xIzqPared = null;
            Point? yArrPared = null;
            Point? yAbjPared = null;

            foreach (var sec in segPared)
            {
                Point p1 = sec.Item1, p2 = sec.Item2;

                if (Math.Abs(p2.Y - p1.Y) > 0.001)
                {
                    double minY = Math.Min(p1.Y, p2.Y), maxY = Math.Max(p1.Y, p2.Y);
                    if (centerYpx >= minY && centerYpx <= maxY)
                    {
                        double xI = p1.X + (p2.X - p1.X) * ((centerYpx - p1.Y) / (p2.Y - p1.Y));
                        if (!double.IsNaN(xI) && !double.IsInfinity(xI))
                        {
                            if (xI < edgeIzq && (xIzqPared == null || xI > xIzqPared.Value.X))
                                xIzqPared = new Point(xI, centerYpx);
                            else if (xI > edgeDer && (xDerPared == null || xI < xDerPared.Value.X))
                                xDerPared = new Point(xI, centerYpx);
                        }
                    }
                }

                if (Math.Abs(p2.X - p1.X) > 0.001)
                {
                    double minX = Math.Min(p1.X, p2.X), maxX = Math.Max(p1.X, p2.X);
                    if (centerXpx >= minX && centerXpx <= maxX)
                    {
                        double yI = p1.Y + (p2.Y - p1.Y) * ((centerXpx - p1.X) / (p2.X - p1.X));
                        if (!double.IsNaN(yI) && !double.IsInfinity(yI))
                        {
                            if (yI < edgeArr && (yArrPared == null || yI > yArrPared.Value.Y))
                                yArrPared = new Point(centerXpx, yI);
                            else if (yI > edgeAbj && (yAbjPared == null || yI < yAbjPared.Value.Y))
                                yAbjPared = new Point(centerXpx, yI);
                        }
                    }
                }
            }

            // ── 2) Intersecciones con OTRAS COLUMNAS ────────────────────────────────
            Point? xDerColumna = null;
            Point? xIzqColumna = null;
            Point? yArrColumna = null;
            Point? yAbjColumna = null;

            foreach (var otra in columnasVisuales)
            {
                if (otra == estado.Visual) continue;
                double oL = Canvas.GetLeft(otra);
                double oT = Canvas.GetTop(otra);
                if (double.IsNaN(oL) || double.IsNaN(oT)) continue;

                double oW = otra.Width;
                double oH = otra.Height;
                double borDerOtra = oL + oW;
                double borAbjOtra = oT + oH;

                // Medición ENTRE COLUMNAS = EJE A EJE (centro a centro), la convención de planos
                // estructurales. Se guarda el CENTRO de la otra columna como punto de la guía (no su filo);
                // la clasificación izquierda/derecha/arriba/abajo sigue usando los bordes para saber de qué
                // lado queda.
                double centroOtraX = oL + oW / 2;
                double centroOtraY = oT + oH / 2;

                double margenTolPx = 0.05 * pixelesPorMetro;

                bool overlapV = (centerYpx >= oT - margenTolPx) && (centerYpx <= borAbjOtra + margenTolPx);
                bool overlapH = (centerXpx >= oL - margenTolPx) && (centerXpx <= borDerOtra + margenTolPx);

                if (overlapV)
                {
                    if (borDerOtra <= edgeIzq)
                    {
                        if (xIzqColumna == null || centroOtraX > xIzqColumna.Value.X)
                            xIzqColumna = new Point(centroOtraX, centerYpx);
                    }
                    else if (oL >= edgeDer)
                    {
                        if (xDerColumna == null || centroOtraX < xDerColumna.Value.X)
                            xDerColumna = new Point(centroOtraX, centerYpx);
                    }
                }

                if (overlapH)
                {
                    if (borAbjOtra <= edgeArr)
                    {
                        if (yArrColumna == null || centroOtraY > yArrColumna.Value.Y)
                            yArrColumna = new Point(centerXpx, centroOtraY);
                    }
                    else if (oT >= edgeAbj)
                    {
                        if (yAbjColumna == null || centroOtraY < yAbjColumna.Value.Y)
                            yAbjColumna = new Point(centerXpx, centroOtraY);
                    }
                }
            }

            // ── 3) Seleccionar obstáculo final (columna gana si existe) ─────────────
            (Point? ptIzq, bool izqEsColumna) = ElegirObstaculo(xIzqColumna, xIzqPared, prefiereColumna: true);
            (Point? ptDer, bool derEsColumna) = ElegirObstaculo(xDerColumna, xDerPared, prefiereColumna: true);
            (Point? ptArr, bool arrEsColumna) = ElegirObstaculo(yArrColumna, yArrPared, prefiereColumna: true);
            (Point? ptAbj, bool abjEsColumna) = ElegirObstaculo(yAbjColumna, yAbjPared, prefiereColumna: true);

            // ── 4) Filtro de proximidad para columnas bloqueadas ─────────────────────
            double umbralPx = UmbralProximidadMetros * pixelesPorMetro;
            if (esParaColumnaBloqueada)
            {
                ptIzq = FiltrarPorProximidad(ptIzq, izqEsColumna, Math.Abs(edgeIzq - (ptIzq?.X ?? edgeIzq)), umbralPx);
                if (!derEsColumna)
                    ptDer = FiltrarPorProximidad(ptDer, false, Math.Abs((ptDer?.X ?? edgeDer) - edgeDer), umbralPx);
                ptArr = FiltrarPorProximidad(ptArr, arrEsColumna, Math.Abs(edgeArr - (ptArr?.Y ?? edgeArr)), umbralPx);
                ptAbj = FiltrarPorProximidad(ptAbj, abjEsColumna, Math.Abs((ptAbj?.Y ?? edgeAbj) - edgeAbj), umbralPx);
            }

            // ── 5) Dibujar guías ─────────────────────────────────────────────────────
            // TODAS las medidas salen del EJE de la columna, sea cual sea el obstáculo:
            //  • contra otra columna, del centro de ésta al centro de aquélla (eje a eje);
            //  • contra la pared (volado), del centro de la columna al filo de la losa.
            // Así todas las cotas de una misma fila se leen con el mismo criterio y la cadena se suma
            // directamente, sin tener que añadir media columna en cada punta.
            double inicioIzq = centerXpx;
            double inicioDer = centerXpx;
            double inicioArr = centerYpx;
            double inicioAbj = centerYpx;

            DibujarGuiaHorizontal(estado.GuiaX, estado.TextoXIzq,
                ptIzq.HasValue ? ptIzq.Value.X : (double?)null, inicioIzq, centerYpx, pixelesPorMetro);

            DibujarGuiaHorizontal(estado.GuiaXDer, estado.TextoXDer,
                ptDer.HasValue ? ptDer.Value.X : (double?)null, inicioDer, centerYpx, pixelesPorMetro);

            DibujarGuiaVertical(estado.GuiaY, estado.TextoYArr,
                ptArr.HasValue ? ptArr.Value.Y : (double?)null, inicioArr, centerXpx, pixelesPorMetro);

            DibujarGuiaVertical(estado.GuiaYAbj, estado.TextoYAbj,
                ptAbj.HasValue ? ptAbj.Value.Y : (double?)null, inicioAbj, centerXpx, pixelesPorMetro);
        }

        private static bool EsPuntoValido(Point p) =>
            !double.IsNaN(p.X) && !double.IsNaN(p.Y) &&
            !double.IsInfinity(p.X) && !double.IsInfinity(p.Y);

        private static (Point?, bool esColumna) ElegirObstaculo(Point? columna, Point? pared, bool prefiereColumna)
        {
            if (columna.HasValue && pared.HasValue)
                return prefiereColumna ? (columna, true) : (pared, false);
            if (columna.HasValue) return (columna, true);
            if (pared.HasValue) return (pared, false);
            return (null, false);
        }

        private static Point? FiltrarPorProximidad(Point? punto, bool esColumna, double distanciaPx, double umbralPx)
        {
            if (!punto.HasValue) return null;
            if (esColumna) return punto;
            if (distanciaPx <= umbralPx) return punto;
            return null;
        }

        private void DibujarGuiaHorizontal(Line guia, TextBlock texto,
            double? xObstaculo, double xBordeColumna, double centerY, double pixelesPorMetro)
        {
            if (!xObstaculo.HasValue)
            {
                guia.IsVisible = false;
                texto.IsVisible = false;
                return;
            }

            double dist = Math.Abs(xBordeColumna - xObstaculo.Value);
            if (dist < 0.5)
            {
                guia.IsVisible = false;
                texto.IsVisible = false;
                return;
            }

            guia.StartPoint = new Point(xObstaculo.Value, centerY);
            guia.EndPoint = new Point(xBordeColumna, centerY);
            guia.IsVisible = true;

            texto.Text = $"{dist / pixelesPorMetro:0.00}m";
            double midX = (xObstaculo.Value + xBordeColumna) / 2;
            Canvas.SetLeft(texto, midX - 18);
            Canvas.SetTop(texto, centerY - 20);
            texto.IsVisible = true;
        }

        private void DibujarGuiaVertical(Line guia, TextBlock texto,
            double? yObstaculo, double yBordeColumna, double centerX, double pixelesPorMetro)
        {
            if (!yObstaculo.HasValue)
            {
                guia.IsVisible = false;
                texto.IsVisible = false;
                return;
            }

            double dist = Math.Abs(yBordeColumna - yObstaculo.Value);
            if (dist < 0.5)
            {
                guia.IsVisible = false;
                texto.IsVisible = false;
                return;
            }

            guia.StartPoint = new Point(centerX, yObstaculo.Value);
            guia.EndPoint = new Point(centerX, yBordeColumna);
            guia.IsVisible = true;

            texto.Text = $"{dist / pixelesPorMetro:0.00}m";
            double midY = (yObstaculo.Value + yBordeColumna) / 2;
            Canvas.SetLeft(texto, centerX + 5);
            Canvas.SetTop(texto, midY - 8);
            texto.IsVisible = true;
        }

        private static void OcultarTodasLasGuias(EstadoColumnaUI estado)
        {
            if (!estado.IsLocked)
            {
                estado.GuiaX.IsVisible = false;
                estado.GuiaXDer.IsVisible = false;
                estado.GuiaY.IsVisible = false;
                estado.GuiaYAbj.IsVisible = false;
                estado.TextoXIzq.IsVisible = false;
                estado.TextoXDer.IsVisible = false;
                estado.TextoYArr.IsVisible = false;
                estado.TextoYAbj.IsVisible = false;
            }
        }
    }
}
