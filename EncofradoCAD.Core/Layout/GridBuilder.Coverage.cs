using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    public static partial class GridBuilder
    {
        /// <summary>
        /// COBERTURA INTERIOR (casas especiales). Garantiza que TODA celda de la retícula cuyo centro cae
        /// dentro de la losa tenga sus 4 esquinas como puntales. Es un pase ADITIVO: nunca quita nada, solo
        /// agrega los puntales que falten.
        ///
        /// Por qué hace falta: la retícula base (paso 1 de <see cref="GeneratePuntales"/>) solo conserva los
        /// cruces de retícula que estén a ≤45 cm de la losa. En una esquina CÓNCAVA o un ESCALÓN, la esquina
        /// de una celda que sí es interior puede caer un poco más afuera de esos 45 cm y quedar descartada;
        /// entonces esa celda pierde un apoyo y ni el riel ni el separador pueden anclar ahí → se ve como una
        /// franja con la losa dibujada pero SIN riel, sin separador y sin tablero. Al forzar las 4 esquinas de
        /// cada celda interior, riel y separador vuelven a tener de dónde agarrarse y el tablero se llena.
        ///
        /// En una losa rectangular no cambia nada (esas esquinas ya existían); solo añade apoyos en los
        /// quiebres de las formas irregulares. Los puntales que quedaran de más los depuran aguas abajo
        /// <see cref="CoverageCompleter.RemoveRedundant"/> y <see cref="PruneIsolated"/>.
        /// </summary>
        public static List<Point> CompleteInteriorCells(
            List<Point> puntales,
            List<double> xLines,
            List<double> yLines,
            List<Point> slab,
            List<ColumnSnapshot> columns)
        {
            if (slab.Count < 3 || xLines.Count < 2 || yLines.Count < 2)
            {
                return puntales;
            }

            var result = new List<Point>(puntales);
            var present = new HashSet<(long, long)>();
            foreach (Point p in result)
            {
                present.Add(((long)Math.Round(p.X * 100), (long)Math.Round(p.Y * 100)));
            }

            void Ensure(double x, double y)
            {
                var point = new Point(x, y);

                // No colocar un puntal encima de una columna (misma regla que GeneratePuntales).
                if (GeometryUtil.IsPointInsidePolygon(point, slab)
                    && columns.Any(column => column.Contains(point, LayoutCatalog.ColumnClearance)))
                {
                    return;
                }

                var key = ((long)Math.Round(x * 100), (long)Math.Round(y * 100));
                if (present.Add(key))
                {
                    result.Add(point);
                }
            }

            for (int xi = 0; xi + 1 < xLines.Count; xi++)
            {
                for (int yj = 0; yj + 1 < yLines.Count; yj++)
                {
                    double x0 = xLines[xi];
                    double x1 = xLines[xi + 1];
                    double y0 = yLines[yj];
                    double y1 = yLines[yj + 1];

                    // Solo celdas cuyo CENTRO es losa real: así se cubre el interior (incluidos escalones y
                    // brazos de una L/U/T) sin invadir el hueco cóncavo ni el exterior.
                    var center = new Point((x0 + x1) / 2, (y0 + y1) / 2);
                    if (!GeometryUtil.IsPointInsidePolygon(center, slab))
                    {
                        continue;
                    }

                    Ensure(x0, y0);
                    Ensure(x1, y0);
                    Ensure(x0, y1);
                    Ensure(x1, y1);
                }
            }

            return result;
        }

        /// <summary>
        /// Elimina puntales AISLADOS: los que ningún riel ni separador toca (postes que no sujetan nada).
        /// Se llama tras construir rieles y separadores; quitar aislados no afecta a esas piezas.
        /// </summary>
        public static List<Point> PruneIsolated(List<Point> puntales, IEnumerable<LinearPiece> members)
        {
            var used = new bool[puntales.Count];
            foreach (LinearPiece m in members)
            {
                Point a = m.Horizontal ? new Point(m.Start, m.FixedCoord) : new Point(m.FixedCoord, m.Start);
                Point b = m.Horizontal ? new Point(m.End, m.FixedCoord) : new Point(m.FixedCoord, m.End);
                for (int i = 0; i < puntales.Count; i++)
                {
                    if (used[i])
                    {
                        continue;
                    }

                    Point p = puntales[i];
                    if ((GeometryUtil.NearlyEqual(p.X, a.X) && GeometryUtil.NearlyEqual(p.Y, a.Y))
                        || (GeometryUtil.NearlyEqual(p.X, b.X) && GeometryUtil.NearlyEqual(p.Y, b.Y)))
                    {
                        used[i] = true;
                    }
                }
            }

            var result = new List<Point>();
            for (int i = 0; i < puntales.Count; i++)
            {
                if (used[i])
                {
                    result.Add(puntales[i]);
                }
            }

            return result;
        }
    }
}
