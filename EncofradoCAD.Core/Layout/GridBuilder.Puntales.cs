using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    public static partial class GridBuilder
    {
        /// <summary>
        /// Puntales de la retícula RECTA AL MUNDO, recorriendo en el sentido de los rieles. Cada fila de
        /// rieles se extiende 20 cm de voladizo siguiendo el borde real de la losa (lados rectos, inclinados
        /// o curvos), apoyándose en las líneas interiores y rellenando para que ningún vano supere la medida
        /// estándar. Las filas de voladizo superior/inferior (que no cruzan la losa) toman el ancho del borde
        /// más cercano, de modo que SIEMPRE llevan un riel completo (no un muñón) y se cierran las esquinas.
        /// Un único recorrido evita puntales encimados; el deduplicado tolerante remata cualquier coincidencia.
        /// Una columna elimina puntales SOLO dentro de la losa; el voladizo exterior queda intacto.
        /// </summary>
        public static List<Point> GeneratePuntales(
            List<double> xLines,
            List<double> yLines,
            List<Point> slab,
            List<ColumnSnapshot> columns,
            double xSpacing,
            double ySpacing,
            double overhang,
            bool primaryHorizontal)
        {
            if (slab.Count < 3)
            {
                return new List<Point>();
            }

            var raw = new List<Point>();

            // 1) RETÍCULA RECTA (halo de voladizo): da rieles Y separadores limpios en TODOS los lados rectos
            //    (izquierdo, superior, inferior) y el anillo perimetral de 20 cm, igual que en un rectángulo.
            foreach (double x in xLines)
            {
                foreach (double y in yLines)
                {
                    if (GeometryUtil.DistanceToPolygon(new Point(x, y), slab) <= LayoutCatalog.OuterRingHalo)
                    {
                        raw.Add(new Point(x, y));
                    }
                }
            }

            // 2) SEGUIR EL BORDE en el sentido de los rieles: extiende los rieles hacia la parte inclinada o
            //    curva (media luna/óvalo) donde el anillo recto no alcanza. Ahí no hay separadores (imposible),
            //    pero los rieles cubren el borde con su voladizo de 20 cm.
            List<double> fixedLines = primaryHorizontal ? yLines : xLines;
            List<double> alongLines = primaryHorizontal ? xLines : yLines;
            double alongSpacing = primaryHorizontal ? xSpacing : ySpacing;
            Func<double, List<double>> crossAt = primaryHorizontal
                ? (v => GeometryUtil.HorizontalCrossings(slab, v))
                : (v => GeometryUtil.VerticalCrossings(slab, v));

            // Rango de líneas fijas que SÍ cruzan la losa. Las filas de voladizo superior/inferior (que
            // quedan fuera) toman el ancho de la fila interior más cercana, para que SIEMPRE lleven un riel
            // completo y se cubra la esquina (si no, esa parte de la losa quedaría sin apoyo y no se funde).
            double loInside = double.NaN, hiInside = double.NaN;
            foreach (double v in fixedLines)
            {
                if (crossAt(v).Count >= 2)
                {
                    if (double.IsNaN(loInside))
                    {
                        loInside = v;
                    }

                    hiInside = v;
                }
            }

            foreach (double fixedCoord in fixedLines)
            {
                List<double> crossings = crossAt(fixedCoord);
                if (crossings.Count < 2)
                {
                    if (!double.IsNaN(loInside) && fixedCoord < loInside)
                    {
                        crossings = crossAt(loInside);
                    }
                    else if (!double.IsNaN(hiInside) && fixedCoord > hiInside)
                    {
                        crossings = crossAt(hiInside);
                    }
                    else
                    {
                        continue;
                    }
                }

                EmitLine(crossings, alongLines, alongSpacing, overhang, fixedCoord, primaryHorizontal, raw);
            }

            // Deduplicado TOLERANTE (~1 cm) para no encimar puntales, y recorte de columnas dentro de la losa.
            var result = new List<Point>();
            var seen = new HashSet<(long, long)>();
            foreach (Point p in raw)
            {
                var key = ((long)Math.Round(p.X * 100), (long)Math.Round(p.Y * 100));
                if (!seen.Add(key))
                {
                    continue;
                }

                if (GeometryUtil.IsPointInsidePolygon(p, slab)
                    && columns.Any(column => column.Contains(p, LayoutCatalog.ColumnClearance)))
                {
                    continue;
                }

                result.Add(p);
            }

            return result;
        }

        /// <summary>
        /// Emite los puntales de UNA línea de rieles: por cada tramo interior [cruce, cruce] extendido
        /// ±voladizo, se apoya en las líneas de retícula que caen dentro y rellena para que ningún vano
        /// supere <paramref name="spacing"/>. La coordenada fija va perpendicular al riel.
        /// </summary>
        /// <summary>Tolerancia de ajuste del extremo de riel a la línea de retícula más cercana (m).</summary>
        private const double GridSnapTolerance = 0.55;

        /// <summary>
        /// Ajusta <paramref name="value"/> a una línea de retícula cercana, PERO solo en el sentido de
        /// <paramref name="toward"/> (-1 = solo hacia la izquierda, +1 = solo hacia la derecha). Así alinea
        /// el borde a la retícula sin acortar nunca la cobertura (nunca deja la losa sin cubrir).
        /// </summary>
        private static double SnapOutward(double value, List<double> gridLines, int toward)
            => SnapOutward(value, gridLines, toward, GridSnapTolerance);

        private static double SnapOutward(double value, List<double> gridLines, int toward, double tolerance)
        {
            double best = value;
            double bestDist = tolerance;
            foreach (double g in gridLines)
            {
                double delta = g - value;
                bool outward = (toward < 0 && delta <= Eps) || (toward > 0 && delta >= -Eps);
                double d = System.Math.Abs(delta);
                if (outward && d < bestDist)
                {
                    bestDist = d;
                    best = g;
                }
            }

            return best;
        }

        /// <summary>Muñón máximo que se absorbe pegando el extremo del riel a la última línea de unión (m).</summary>
        private const double StubTolerance = 0.10;

        /// <summary>
        /// Pega el extremo de un riel a la línea de unión inmediatamente INTERIOR si quedó un "muñón" (≤
        /// <see cref="StubTolerance"/>) por fuera de ella. Solo mueve hacia adentro y muy poco, así elimina el
        /// puntal duplicado que fragmentaría el riel sin recortar la cobertura real (la última unión ya trae el
        /// voladizo del RailSolver, ≥ 15 cm). <paramref name="isHigh"/>=true para el extremo derecho/superior
        /// (busca una línea &lt; extremo); false para el izquierdo/inferior (línea &gt; extremo).
        /// </summary>
        private static double SnapStubToGrid(double endpoint, List<double> gridLines, bool isHigh)
        {
            double best = endpoint;
            double bestDist = StubTolerance;
            foreach (double g in gridLines)
            {
                double d = Math.Abs(g - endpoint);
                if (d <= Eps || d >= bestDist)
                {
                    continue;
                }

                bool inward = isHigh ? (g < endpoint) : (g > endpoint);
                if (inward)
                {
                    bestDist = d;
                    best = g;
                }
            }

            return best;
        }

        private static void EmitLine(List<double> crossings, List<double> alongLines, double spacing, double overhang, double fixedCoord, bool primaryHorizontal, List<Point> sink)
        {
            for (int i = 0; i + 1 < crossings.Count; i += 2)
            {
                // El extremo del riel (borde ±voladizo) se AJUSTA a la línea de retícula más cercana si cae
                // dentro de la tolerancia: así los bordes casi rectos quedan alineados en columna (misma
                // medida de riel fila por fila) en vez de escalonados. Las curvas reales quedan lejos del
                // grid, así que no se ajustan y se siguen igual.
                // Se ajusta SOLO hacia afuera (lo hacia la izquierda, hi hacia la derecha): alinea los bordes
                // casi rectos a la retícula sin jalar nunca el riel hacia adentro (que dejaría la losa sin cubrir).
                // La ventana de ajuste es un vano completo (spacing): así el extremo del riel se pega SIEMPRE a
                // la línea de retícula exterior aunque el voladizo haya crecido (al librar columnas), y todas
                // las filas —incluidas las de voladizo— quedan alineadas en las mismas uniones de catálogo.
                double lo = SnapOutward(crossings[i] - overhang, alongLines, toward: -1, tolerance: spacing);
                double hi = SnapOutward(crossings[i + 1] + overhang, alongLines, toward: +1, tolerance: spacing);

                // Elimina el "MUÑÓN" de voladizo: cuando el voladizo real del RailSolver es <20 cm, la última
                // línea de unión queda ~1 cm POR DENTRO del extremo borde±0,20 y SnapOutward (que solo pega
                // hacia afuera) no la alcanza. Ese muñón crea un puntal duplicado a ~1 cm de la última unión;
                // luego CoverageCompleter.RespaceLine lo colapsa y, como el tramo restante mide 3,01 m (>3,0),
                // inserta un puntal a la mitad → parte el riel de 3,0 m en 1,50 + 1,50 y añade una fila de
                // puntales que NO se necesita. Al pegar el extremo a esa última unión (muñón ≤ StubTol) el riel
                // queda de 3,0 m completo, sin fila extra. La última unión ES el fin real del riel (RailSolver
                // ya garantiza voladizo ≥ 15 cm), así que pegarse a ella no descubre la losa.
                lo = SnapStubToGrid(lo, alongLines, isHigh: false);
                hi = SnapStubToGrid(hi, alongLines, isHigh: true);

                var seq = new List<double> { lo, hi };
                foreach (double g in alongLines)
                {
                    if (g > lo + Eps && g < hi - Eps)
                    {
                        seq.Add(g);
                    }
                }

                seq.Sort();

                void Put(double along)
                {
                    sink.Add(primaryHorizontal ? new Point(along, fixedCoord) : new Point(fixedCoord, along));
                }

                Put(seq[0]);
                for (int k = 1; k < seq.Count; k++)
                {
                    double a = seq[k - 1];
                    double b = seq[k];
                    int subdivisions = (int)Math.Ceiling(((b - a) / spacing) - 1e-9);
                    for (int m = 1; m < subdivisions; m++)
                    {
                        Put(a + ((b - a) * m / subdivisions));
                    }

                    Put(b);
                }
            }
        }
    }
}
