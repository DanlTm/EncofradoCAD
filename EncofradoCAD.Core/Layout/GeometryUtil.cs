using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// Utilidades geométricas PURAS (sin estado): distancias, punto-en-polígono, centroide.
    /// Aisladas para poder probarse y reutilizarse sin arrastrar el resto del motor.
    /// </summary>
    public static class GeometryUtil
    {
        private const double Eps = LayoutCatalog.GeometryEpsilon;

        public static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        public static bool NearlyEqual(double a, double b)
        {
            return Math.Abs(a - b) <= 0.02;
        }

        public static Point ComputePolygonCentroid(List<Point> polygon)
        {
            if (polygon.Count == 0)
            {
                return new Point(0, 0);
            }

            double twiceArea = 0;
            double cx = 0;
            double cy = 0;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                double t = (polygon[i].X * polygon[j].Y) - (polygon[j].X * polygon[i].Y);
                twiceArea += t;
                cx += (polygon[i].X + polygon[j].X) * t;
                cy += (polygon[i].Y + polygon[j].Y) * t;
            }

            if (Math.Abs(twiceArea) < Eps)
            {
                double sx = polygon.Sum(p => p.X) / polygon.Count;
                double sy = polygon.Sum(p => p.Y) / polygon.Count;
                return new Point(sx, sy);
            }

            return new Point(cx / (3 * twiceArea), cy / (3 * twiceArea));
        }

        /// <summary>Área con signo (shoelace): positiva en sentido antihorario, negativa en horario.</summary>
        public static double PolygonSignedArea(List<Point> polygon)
        {
            double s = 0;
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                Point a = polygon[i];
                Point b = polygon[(i + 1) % n];
                s += (a.X * b.Y) - (b.X * a.Y);
            }

            return s / 2.0;
        }

        /// <summary>Área (m²) encerrada por el polígono.</summary>
        public static double PolygonArea(List<Point> polygon)
        {
            return polygon.Count < 3 ? 0.0 : Math.Abs(PolygonSignedArea(polygon));
        }

        /// <summary>
        /// Desplaza el contorno del polígono <paramref name="distance"/> m hacia AFUERA, con las esquinas en
        /// inglete (cada lado se corre hacia afuera y los lados contiguos se prolongan hasta cortarse). Es el
        /// contorno del VOLADIZO: p. ej. 0,20 m rebasa el borde de la losa en los 4 lados. Devuelve un polígono
        /// con el mismo número de vértices (funciona con losas rectangulares y en L/U; los lados curvos ya
        /// vienen teselados en varios vértices).
        /// </summary>
        public static List<Point> OffsetPolygon(List<Point> polygon, double distance)
        {
            int n = polygon.Count;
            if (n < 3)
            {
                return new List<Point>(polygon);
            }

            int sign = PolygonSignedArea(polygon) > 0 ? 1 : -1;

            var lineA = new Point[n];
            var lineB = new Point[n];
            for (int i = 0; i < n; i++)
            {
                Point p1 = polygon[i];
                Point p2 = polygon[(i + 1) % n];
                double ex = p2.X - p1.X;
                double ey = p2.Y - p1.Y;
                double len = Math.Sqrt((ex * ex) + (ey * ey));
                if (len < Eps)
                {
                    len = 1.0;
                }

                double nx = sign * ey / len;   // normal exterior del lado
                double ny = -sign * ex / len;
                lineA[i] = new Point(p1.X + (nx * distance), p1.Y + (ny * distance));
                lineB[i] = new Point(p2.X + (nx * distance), p2.Y + (ny * distance));
            }

            var result = new List<Point>(n);
            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                result.Add(LineIntersection(lineA[prev], lineB[prev], lineA[i], lineB[i], lineB[prev]));
            }

            return result;
        }

        /// <summary>Intersección de las rectas (a1,a2) y (b1,b2); si son casi paralelas, devuelve <paramref name="fallback"/>.</summary>
        private static Point LineIntersection(Point a1, Point a2, Point b1, Point b2, Point fallback)
        {
            double d = ((a1.X - a2.X) * (b1.Y - b2.Y)) - ((a1.Y - a2.Y) * (b1.X - b2.X));
            if (Math.Abs(d) < 1e-9)
            {
                return fallback;
            }

            double pre = (a1.X * a2.Y) - (a1.Y * a2.X);
            double post = (b1.X * b2.Y) - (b1.Y * b2.X);
            double px = ((pre * (b1.X - b2.X)) - ((a1.X - a2.X) * post)) / d;
            double py = ((pre * (b1.Y - b2.Y)) - ((a1.Y - a2.Y) * post)) / d;
            return new Point(px, py);
        }

        public static bool IsPointInsidePolygon(Point point, List<Point> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                bool intersects = ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y))
                    && (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / ((polygon[j].Y - polygon[i].Y) + 0.000001) + polygon[i].X);

                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>Distancia de un punto al polígono de la losa (0 si está dentro).</summary>
        public static double DistanceToPolygon(Point point, List<Point> polygon)
        {
            if (IsPointInsidePolygon(point, polygon))
            {
                return 0.0;
            }

            double best = double.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                best = Math.Min(best, DistancePointToSegment(point, polygon[i], polygon[(i + 1) % polygon.Count]));
            }

            return best;
        }

        public static double DistancePointToSegment(Point p, Point a, Point b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double lengthSquared = (dx * dx) + (dy * dy);
            if (lengthSquared < 1e-12)
            {
                return Distance(p, a);
            }

            double t = (((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / lengthSquared;
            t = Math.Clamp(t, 0.0, 1.0);
            var projection = new Point(a.X + (t * dx), a.Y + (t * dy));
            return Distance(p, projection);
        }

        /// <summary>
        /// Coordenadas X donde la recta horizontal y = <paramref name="y"/> cruza el contorno del polígono,
        /// ordenadas. En pares [x0,x1],[x2,x3]… delimitan los tramos que quedan DENTRO de la losa (maneja
        /// formas cóncavas). Semiabierto en el vértice inferior para no contar dos veces.
        /// </summary>
        public static List<double> HorizontalCrossings(List<Point> polygon, double y)
        {
            var xs = new List<double>();
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                Point a = polygon[i];
                Point b = polygon[(i + 1) % n];
                double y1 = a.Y, y2 = b.Y;
                if ((y1 <= y && y < y2) || (y2 <= y && y < y1))
                {
                    xs.Add(a.X + ((y - y1) / (y2 - y1) * (b.X - a.X)));
                }
            }

            xs.Sort();
            return xs;
        }

        /// <summary>
        /// Coordenadas Y donde la recta vertical x = <paramref name="x"/> cruza el contorno del polígono,
        /// ordenadas. Análoga a <see cref="HorizontalCrossings"/> para el sentido perpendicular.
        /// </summary>
        public static List<double> VerticalCrossings(List<Point> polygon, double x)
        {
            var ys = new List<double>();
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                Point a = polygon[i];
                Point b = polygon[(i + 1) % n];
                double x1 = a.X, x2 = b.X;
                if ((x1 <= x && x < x2) || (x2 <= x && x < x1))
                {
                    ys.Add(a.Y + ((x - x1) / (x2 - x1) * (b.Y - a.Y)));
                }
            }

            ys.Sort();
            return ys;
        }
    }
}
