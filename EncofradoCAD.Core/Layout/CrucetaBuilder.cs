using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// Localiza la torre central (base principal) en la retícula RECTANGULAR: recibe el espaciado a lo
    /// ancho (X) y a lo largo (Y) según el sentido, y devuelve el cuadro 2×2 más cercano al centro.
    /// El arriostre (crucetas) de esa torre lo dibuja Preview3DBuilder; no hay crucetas sueltas por puntal.
    /// </summary>
    public static class CrucetaBuilder
    {
        /// <summary>
        /// Cuadro 2×2 de puntales (xSpacing × ySpacing) más cercano al centro: base de la torre central.
        /// Orden: [inf-izq, inf-der, sup-izq, sup-der].
        /// </summary>
        public static Point[]? TryFindCentralTowerQuad(List<Point> puntales, Point anchor, double xSpacing, double ySpacing)
        {
            Point? bestBl = null;
            double bestScore = double.MaxValue;

            foreach (Point candidate in puntales)
            {
                double x1 = candidate.X + xSpacing;
                double y1 = candidate.Y + ySpacing;
                if (!HasPuntalNear(puntales, x1, candidate.Y)
                    || !HasPuntalNear(puntales, candidate.X, y1)
                    || !HasPuntalNear(puntales, x1, y1))
                {
                    continue;
                }

                double mx = candidate.X + (xSpacing / 2);
                double my = candidate.Y + (ySpacing / 2);
                double score = GeometryUtil.Distance(new Point(mx, my), anchor);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestBl = candidate;
                }
            }

            if (bestBl == null)
            {
                return null;
            }

            Point bl = bestBl.Value;
            return new[]
            {
                bl,
                new Point(bl.X + xSpacing, bl.Y),
                new Point(bl.X, bl.Y + ySpacing),
                new Point(bl.X + xSpacing, bl.Y + ySpacing)
            };
        }

        /// <summary>
        /// Línea continua de crucetas que arriostra el centro de la losa de BORDE A BORDE. Parte de la torre
        /// central y, a lo largo del eje de recorrido, arriostra cada par de puntales adyacentes que exista
        /// hasta agotar la losa. Sin estas líneas continuas la torre central quedaría aislada y la estructura
        /// no tendría arriostre transversal.
        ///
        /// El VANO de cada cruceta (la separación entre los dos puntales que une) va a lo largo de X cuando
        /// <c>large == primaryHorizontal</c>:
        ///   • pequeña (1,20 m): el vano es el del separador  →  fila que recorre el eje perpendicular.
        ///   • grande  (3,0 m):  el vano es el del riel        →  columna que recorre el eje perpendicular.
        /// Así la cruceta conserva "el sentido en que está puesta" y la línea se va repitiendo en el otro eje.
        /// </summary>
        /// <param name="tower">Cuadro central [inf-izq, inf-der, sup-izq, sup-der].</param>
        /// <param name="primaryHorizontal">true: rieles en X (vano largo en X). false: rieles en Y.</param>
        /// <param name="large">true: cruceta grande (vano de riel, 3,0 m). false: pequeña (vano de separador, 1,20 m).</param>
        public static List<CrucetaPiece> BuildCrucetaLine(List<Point> puntales, Point[] tower, bool primaryHorizontal, bool large)
        {
            var result = new List<CrucetaPiece>();
            if (tower.Length != 4)
            {
                return result;
            }

            // El vano corre a lo largo de X exactamente cuando large y primaryHorizontal coinciden.
            bool spanAlongX = large == primaryHorizontal;

            // Extremos del vano (los dos apoyos de la cruceta) tomados del cuadro central.
            double bandLow = spanAlongX ? tower[0].X : tower[0].Y;
            double bandHigh = spanAlongX ? tower[1].X : tower[2].Y;

            // Coordenada a lo largo de la cual se repite la línea (el eje perpendicular al vano).
            IEnumerable<double> runValues = (spanAlongX
                    ? puntales.Where(p => GeometryUtil.NearlyEqual(p.X, bandLow)).Select(p => p.Y)
                    : puntales.Where(p => GeometryUtil.NearlyEqual(p.Y, bandLow)).Select(p => p.X))
                .Distinct()
                .OrderBy(v => v);

            foreach (double run in runValues)
            {
                Point a = spanAlongX ? new Point(bandLow, run) : new Point(run, bandLow);
                Point b = spanAlongX ? new Point(bandHigh, run) : new Point(run, bandHigh);

                // Solo si existe el par completo de puntales que sostiene la cruceta.
                if (HasPuntalNear(puntales, a.X, a.Y) && HasPuntalNear(puntales, b.X, b.Y))
                {
                    result.Add(new CrucetaPiece(a, b));
                }
            }

            return result;
        }

        private static bool HasPuntalNear(List<Point> puntales, double x, double y)
        {
            return puntales.Any(p => GeometryUtil.NearlyEqual(p.X, x) && GeometryUtil.NearlyEqual(p.Y, y));
        }
    }
}
