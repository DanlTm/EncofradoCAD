using System;
using System.Collections.Generic;
using Avalonia;

namespace EncofradoCAD.App.Controls
{
    public static class GeometryHelper
    {
        public static double Distancia(Point p1, Point p2)
        {
            return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
        }

        public static Point PuntoMedio(Point p1, Point p2)
        {
            return new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
        }

        public static double CalcularLargoArco(Point p1, Point p2, TipoSegmento tipo)
        {
            double d = Distancia(p1, p2);
            // Geometría EXACTA del arco realmente dibujado (ArcSegment de PolygonManager):
            //  • MediaLuna → semicírculo de radio d/2   ⇒ arco = π·(d/2) = π·d/2
            //  • Ovalado   → arco de radio d cuya cuerda es d ⇒ ángulo central 60° (π/3) ⇒ arco = d·π/3
            if (tipo == TipoSegmento.MediaLuna) return (Math.PI * d) / 2.0;
            if (tipo == TipoSegmento.Ovalado) return d * (Math.PI / 3.0);
            return d;
        }

        /// <summary>
        /// Muestrea el arco realmente dibujado entre <paramref name="p1"/> y <paramref name="p2"/> y devuelve
        /// los puntos INTERMEDIOS (sin incluir p1 ni p2). Reproduce la semántica SVG del ArcSegment de
        /// PolygonManager (SweepDirection.Clockwise, IsLargeArc=false):
        ///   • MediaLuna → radio d/2 (semicírculo)   • Ovalado → radio d (arco de 60°).
        /// Se usa para teselar la curva y que el 3D / motor de cálculo sigan el contorno curvo real.
        /// </summary>
        public static List<Point> PuntosArcoIntermedios(Point p1, Point p2, TipoSegmento tipo, int segmentos)
        {
            var res = new List<Point>();
            if (tipo == TipoSegmento.Recto || segmentos < 2) return res;

            double d = Distancia(p1, p2);
            if (d < 0.001) return res;

            double r = tipo == TipoSegmento.MediaLuna ? d / 2.0 : d;
            List<Point> completo = MuestrearArcoSvg(p1, p2, r, largeArc: false, sweepClockwise: true, segmentos);
            for (int i = 1; i < completo.Count - 1; i++) res.Add(completo[i]);
            return res;
        }

        /// <summary>
        /// Conversión endpoint→centro del arco (spec SVG) para un círculo de radio <paramref name="r"/> sin
        /// rotación. Devuelve segmentos+1 puntos de p1 a p2 (ambos incluidos) a lo largo del arco.
        /// </summary>
        private static List<Point> MuestrearArcoSvg(Point p1, Point p2, double r, bool largeArc, bool sweepClockwise, int segmentos)
        {
            var pts = new List<Point>();

            double x1 = p1.X, y1 = p1.Y, x2 = p2.X, y2 = p2.Y;
            double dx2 = (x1 - x2) / 2.0, dy2 = (y1 - y2) / 2.0; // (x1', y1') sin rotación

            double rx = r, ry = r;
            double lambda = (dx2 * dx2) / (rx * rx) + (dy2 * dy2) / (ry * ry);
            if (lambda > 1.0)
            {
                double s = Math.Sqrt(lambda);
                rx *= s;
                ry *= s;
            }

            double rx2 = rx * rx, ry2 = ry * ry;
            double num = (rx2 * ry2) - (rx2 * dy2 * dy2) - (ry2 * dx2 * dx2);
            if (num < 0) num = 0;
            double den = (rx2 * dy2 * dy2) + (ry2 * dx2 * dx2);
            double coef = den == 0 ? 0 : Math.Sqrt(num / den);
            if (largeArc == sweepClockwise) coef = -coef;

            double cxp = coef * (rx * dy2 / ry);
            double cyp = coef * (-ry * dx2 / rx);
            double cx = cxp + (x1 + x2) / 2.0;
            double cy = cyp + (y1 + y2) / 2.0;

            double ux = (dx2 - cxp) / rx, uy = (dy2 - cyp) / ry;
            double vx = (-dx2 - cxp) / rx, vy = (-dy2 - cyp) / ry;

            double theta1 = AnguloVector(1, 0, ux, uy);
            double dtheta = AnguloVector(ux, uy, vx, vy);
            if (!sweepClockwise && dtheta > 0) dtheta -= 2 * Math.PI;
            else if (sweepClockwise && dtheta < 0) dtheta += 2 * Math.PI;

            for (int i = 0; i <= segmentos; i++)
            {
                double t = theta1 + (dtheta * ((double)i / segmentos));
                pts.Add(new Point(cx + (rx * Math.Cos(t)), cy + (ry * Math.Sin(t))));
            }
            return pts;
        }

        private static double AnguloVector(double ux, double uy, double vx, double vy)
        {
            double dot = (ux * vx) + (uy * vy);
            double len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
            double a = len == 0 ? 0 : Math.Acos(Math.Clamp(dot / len, -1.0, 1.0));
            if ((ux * vy) - (uy * vx) < 0) a = -a;
            return a;
        }

        public static bool SonColineales(Point p1, Point p2, Point p3)
        {
            double dist12 = Distancia(p1, p2);
            double dist23 = Distancia(p2, p3);
            if (dist12 < 1 || dist23 < 1) return true; // Segmentos nulos o ínfimos se fusionan siempre

            // Enfoque 1: Producto cruzado (Área del triángulo) para detectar rectitud por distancia
            double area2 = Math.Abs((p2.X - p1.X) * (p3.Y - p1.Y) - (p2.Y - p1.Y) * (p3.X - p1.X));
            double baseLen = Distancia(p1, p3);
            if (baseLen > 0)
            {
                double height = area2 / baseLen;
                if (height < 3.0) return true; // Si el punto medio está a menos de 3px de la línea recta, fusionar
            }

            // Enfoque 2: Ángulo por si el segmento es muy largo
            double angle1 = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
            double angle2 = Math.Atan2(p3.Y - p2.Y, p3.X - p2.X);
            
            double diff = Math.Abs(angle1 - angle2);
            diff = Math.Min(diff, Math.PI * 2 - diff); 
            
            return diff < (4.5 * Math.PI / 180.0) || diff > (175.5 * Math.PI / 180.0);
        }

        public static Point SnapPunto(Point mouseHit, Point puntoActual, Point? puntoInicial, int cantidadTramos)
        {
            if (puntoInicial == null && cantidadTramos == 0) return mouseHit;

            double dx = mouseHit.X - puntoActual.X;
            double dy = mouseHit.Y - puntoActual.Y;

            if (Math.Abs(dx) > Math.Abs(dy) * 2) 
                return new Point(mouseHit.X, puntoActual.Y); 
            
            if (Math.Abs(dy) > Math.Abs(dx) * 2) 
                return new Point(puntoActual.X, mouseHit.Y); 
            
            if (Math.Abs(Math.Abs(dx) - Math.Abs(dy)) < 30)
            {
                double diag = Math.Abs(dx);
                return new Point(
                    puntoActual.X + (dx > 0 ? diag : -diag),
                    puntoActual.Y + (dy > 0 ? diag : -diag)
                );
            }

            return mouseHit;
        }

        // ── Enderezado automático (regularización) ────────────────────────────────────────────────────
        // Margen MODERADO: cada lado a ≤ 5° de un eje y ninguna esquina se mueve más de 30 cm.
        private const double RegAnguloMaxRad = 5.0 * Math.PI / 180.0;
        private const double RegDespMaxMetros = 0.30;

        /// <summary>
        /// Si el polígono es CASI recto (rectilíneo) lo endereza: cada lado casi-horizontal comparte la Y de
        /// sus dos esquinas y cada lado casi-vertical comparte la X, de modo que los cuatro (o más) lados
        /// quedan perfectamente rectos. NO toca polígonos con curvas, ni los que un lado se sale del margen
        /// angular, ni los que enderezar movería una esquina más de 30 cm (losas asimétricas a propósito).
        /// Trabaja en píxeles; el tope de 30 cm se convierte con <paramref name="pixelesPorMetro"/>.
        /// </summary>
        public static void RegularizarPoligono(Point? puntoInicial, TipoSegmento modoTrazoActual, List<TramoLosa> tramos, double pixelesPorMetro, out Point? nuevoPuntoInicial, out TipoSegmento nuevoModoTrazoActual)
        {
            nuevoPuntoInicial = puntoInicial;
            nuevoModoTrazoActual = modoTrazoActual;

            if (!puntoInicial.HasValue || tramos.Count < 3) return; // < 4 vértices: no hay forma recta que enderezar

            // Vértices y tipos (el Tipo de un vértice = tipo del segmento que llega a él).
            var pts = new List<Point> { puntoInicial.Value };
            var tipos = new List<TipoSegmento> { modoTrazoActual };
            foreach (TramoLosa t in tramos) { pts.Add(t.PuntoFinal); tipos.Add(t.Tipo); }
            int n = pts.Count;
            if (n < 4) return;

            // Solo polígonos TOTALMENTE rectos (si hay una media luna u óvalo, no se toca).
            foreach (TipoSegmento tp in tipos) if (tp != TipoSegmento.Recto) return;

            // 1) Filtro angular: clasificar cada lado como H o V; si alguno se sale del margen → no tocar.
            var ejes = new char[n];
            for (int i = 0; i < n; i++)
            {
                Point a = pts[i], b = pts[(i + 1) % n];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                if (dx == 0 && dy == 0) return;
                double dev;
                if (Math.Abs(dx) >= Math.Abs(dy)) { ejes[i] = 'H'; dev = Math.Atan2(Math.Abs(dy), Math.Abs(dx)); }
                else { ejes[i] = 'V'; dev = Math.Atan2(Math.Abs(dx), Math.Abs(dy)); }
                if (dev > RegAnguloMaxRad) return;
            }

            // 2) Enderezar: lado H → ambas esquinas comparten Y (promedio); lado V → comparten X (promedio).
            var nx = new double[n];
            var ny = new double[n];
            for (int i = 0; i < n; i++) { nx[i] = pts[i].X; ny[i] = pts[i].Y; }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                if (ejes[i] == 'H') { double my = (pts[i].Y + pts[j].Y) / 2.0; ny[i] = my; ny[j] = my; }
                else { double mx = (pts[i].X + pts[j].X) / 2.0; nx[i] = mx; nx[j] = mx; }
            }

            // 3) Filtro de desplazamiento: si mueve alguna esquina más de 30 cm → no tocar.
            double maxDespPx = RegDespMaxMetros * pixelesPorMetro;
            for (int i = 0; i < n; i++)
            {
                double movido = Math.Sqrt(((nx[i] - pts[i].X) * (nx[i] - pts[i].X)) + ((ny[i] - pts[i].Y) * (ny[i] - pts[i].Y)));
                if (movido > maxDespPx) return;
            }

            // Aplicar: primer vértice al out; el resto directo sobre los tramos (mismo conteo, solo coordenadas).
            nuevoPuntoInicial = new Point(nx[0], ny[0]);
            for (int i = 1; i < n; i++) tramos[i - 1].PuntoFinal = new Point(nx[i], ny[i]);
        }

        public static void SimplificarPoligono(Point? puntoInicial, TipoSegmento modoTrazoActual, List<TramoLosa> tramos, out Point? nuevoPuntoInicial, out TipoSegmento nuevoModoTrazoActual)
        {
            nuevoPuntoInicial = puntoInicial;
            nuevoModoTrazoActual = modoTrazoActual;

            if (tramos.Count < 2) return;

            var vertices = new List<TramoLosa>();
            if (puntoInicial.HasValue)
            {
                vertices.Add(new TramoLosa { PuntoFinal = puntoInicial.Value, Tipo = modoTrazoActual });
            }
            vertices.AddRange(tramos);

            bool huboCambios = true;
            while (huboCambios && vertices.Count > 3)
            {
                huboCambios = false;
                for (int i = 0; i < vertices.Count; i++)
                {
                    int prevIdx = (i - 1 + vertices.Count) % vertices.Count;
                    int nextIdx = (i + 1) % vertices.Count;

                    Point prev = vertices[prevIdx].PuntoFinal;
                    Point curr = vertices[i].PuntoFinal;
                    Point next = vertices[nextIdx].PuntoFinal;

                    TipoSegmento tipoLlegada = vertices[i].Tipo; 
                    TipoSegmento tipoSalida = vertices[nextIdx].Tipo;

                    if (tipoLlegada == TipoSegmento.Recto && tipoSalida == TipoSegmento.Recto && SonColineales(prev, curr, next))
                    {
                        vertices.RemoveAt(i);
                        huboCambios = true;
                        break; 
                    }
                }
            }

            if (vertices.Count > 2)
            {
                nuevoPuntoInicial = vertices[0].PuntoFinal;
                nuevoModoTrazoActual = vertices[0].Tipo; 
                
                tramos.Clear();
                for (int i = 1; i < vertices.Count; i++)
                {
                    tramos.Add(vertices[i]);
                }
            }
        }
    }
}
