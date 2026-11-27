using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// Genera piezas lineales apoyadas de puntal a puntal (rieles y separadores comparten lógica).
    /// Regla clave: TODA unión entre piezas cae en el CENTRO de un puntal (ahí está el acople superior).
    /// Cada pieza abarca lo más posible sin pasar del elemento más largo del catálogo; el sobrante de
    /// borde se completa con la medida de catálogo que cubra el vano real.
    /// </summary>
    public static class LinearMemberBuilder
    {
        private const double Eps = LayoutCatalog.GeometryEpsilon;

        /// <summary>Rieles: sentido de <paramref name="horizontal"/>, catálogo de rieles (máx. 3,0 m).</summary>
        public static List<LinearPiece> BuildRails(List<Point> puntales, bool horizontal)
        {
            return BuildMembers(puntales, horizontal, LayoutCatalog.RailLengths, LayoutCatalog.RailClusterThreshold);
        }

        /// <summary>Separadores: sentido perpendicular a los rieles, catálogo de separadores (máx. 1,20 m).</summary>
        public static List<LinearPiece> BuildSeparators(List<Point> puntales, bool horizontal)
        {
            return BuildMembers(puntales, horizontal, LayoutCatalog.SeparatorLengths, LayoutCatalog.SeparatorClusterThreshold);
        }

        private static List<LinearPiece> BuildMembers(List<Point> puntales, bool horizontal, double[] catalog, double clusterThreshold)
        {
            // Agrupa por la coordenada fija (fila si horizontal, columna si vertical).
            IEnumerable<IGrouping<double, Point>> groups = horizontal
                ? puntales.GroupBy(point => System.Math.Round(point.Y, 3)).OrderBy(group => group.Key)
                : puntales.GroupBy(point => System.Math.Round(point.X, 3)).OrderBy(group => group.Key);

            var pieces = new List<LinearPiece>();
            foreach (IGrouping<double, Point> group in groups)
            {
                List<double> values = horizontal
                    ? group.Select(point => point.X).OrderBy(value => value).ToList()
                    : group.Select(point => point.Y).OrderBy(value => value).ToList();

                foreach (List<double> cluster in ClusterOrderedValues(values, clusterThreshold))
                {
                    if (cluster.Count < 2)
                    {
                        continue;
                    }

                    pieces.AddRange(SplitAtSupports(cluster, group.Key, horizontal, catalog));
                }
            }

            return pieces;
        }

        /// <summary>
        /// Agrupa posiciones ordenadas en tramos continuos. Une mientras el hueco sea de retícula normal
        /// o de borde; corta cuando aparece un hueco grande (columna/agujero), según el umbral.
        /// </summary>
        private static List<List<double>> ClusterOrderedValues(List<double> values, double threshold)
        {
            var clusters = new List<List<double>>();
            if (values.Count == 0)
            {
                return clusters;
            }

            var current = new List<double> { values[0] };
            for (int index = 1; index < values.Count; index++)
            {
                double gap = values[index] - values[index - 1];
                if (gap <= threshold)
                {
                    current.Add(values[index]);
                    continue;
                }

                clusters.Add(current);
                current = new List<double> { values[index] };
            }

            clusters.Add(current);
            return clusters;
        }

        /// <summary>
        /// Divide un tramo apoyándose EXCLUSIVAMENTE en los puntales (posiciones ya ordenadas). Cada pieza
        /// abarca lo más posible sin pasar del elemento más largo del catálogo, y toda unión queda en el
        /// centro de un puntal. La longitud nominal se redondea a la medida de catálogo que cubre el vano.
        /// </summary>
        private static IEnumerable<LinearPiece> SplitAtSupports(List<double> supports, double fixedCoord, bool horizontal, double[] catalog)
        {
            double maxPiece = catalog[^1];
            int i = 0;
            int n = supports.Count;

            while (i < n - 1)
            {
                int j = i + 1;
                while (j + 1 < n && (supports[j + 1] - supports[i]) <= maxPiece + Eps)
                {
                    j++;
                }

                double start = supports[i];
                double end = supports[j];
                yield return new LinearPiece(start, end, fixedCoord, horizontal, SelectFromCatalog(end - start, catalog));
                i = j;
            }
        }

        /// <summary>Medida de catálogo más pequeña que cubre el vano real (si ninguna, la más grande).</summary>
        private static double SelectFromCatalog(double actualLength, double[] catalog)
        {
            foreach (double candidate in catalog)
            {
                if (actualLength <= candidate + Eps)
                {
                    return candidate;
                }
            }

            return catalog[^1];
        }
    }
}
