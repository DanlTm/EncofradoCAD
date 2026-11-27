using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// MÓDULO APARTE de red de seguridad para la cobertura. Después de armar los puntales, recorre cada fila
    /// de la retícula (en el sentido de los rieles), calcula dónde cruza el borde real de la losa y, si los
    /// puntales de esa fila NO llegan hasta el borde + 20 cm de voladizo, AGREGA los puntales que faltan
    /// (apoyándose en la retícula y sin dejar vanos mayores a la medida estándar).
    ///
    /// Reconoce y completa casos especiales que la lógica principal pudiera no cubrir del todo (bordes
    /// inclinados, escalones, protuberancias), para que ninguna parte de la losa quede sin apoyo y toda la
    /// losa se pueda fundir. Si la estructura ya está completa, no agrega nada.
    /// </summary>
    public static class CoverageCompleter
    {
        private const double Eps = LayoutCatalog.GeometryEpsilon;
        private const double Overhang = LayoutCatalog.EdgeOverhang;

        /// <summary>Solo se completa si la fila queda corta por más de esto (hueco real, no ruido de ajuste).</summary>
        private const double Trigger = 0.30;

        /// <summary>Dos puntales de una misma fila más cerca que esto son un "par redundante" (stub de riel).</summary>
        private const double CloseSpacing = 0.70;

        /// <summary>Holgura sobre el vano máximo para NO reinsertar un puntal cuando el tramo solo excede 3,0 m
        /// por redondeo del voladizo (evita partir un riel de 3,0 m en 1,50 + 1,50).</summary>
        private const double SplitSlack = 0.03;

        public static List<Point> Complete(
            List<Point> puntales,
            List<Point> slab,
            List<double> xLines,
            List<double> yLines,
            bool primaryHorizontal,
            double alongSpacing)
        {
            if (slab.Count < 3 || puntales.Count == 0)
            {
                return puntales;
            }

            List<double> fixedLines = primaryHorizontal ? yLines : xLines;
            List<double> alongLines = primaryHorizontal ? xLines : yLines;
            Func<double, List<double>> crossAt = primaryHorizontal
                ? (v => GeometryUtil.HorizontalCrossings(slab, v))
                : (v => GeometryUtil.VerticalCrossings(slab, v));

            // Rango de líneas fijas que cruzan la losa: las filas de voladizo (fuera) proyectan la más cercana.
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

            if (double.IsNaN(loInside))
            {
                return puntales;
            }

            var result = new List<Point>(puntales);

            void Emit(double along, double fixedCoord)
            {
                double x = primaryHorizontal ? along : fixedCoord;
                double y = primaryHorizontal ? fixedCoord : along;
                // Deduplicado TOLERANTE: no agrega si ya hay un puntal casi en el mismo punto (evita encimados).
                if (!result.Any(q => GeometryUtil.NearlyEqual(q.X, x) && GeometryUtil.NearlyEqual(q.Y, y)))
                {
                    result.Add(new Point(x, y));
                }
            }

            // Emite puntales de a0 a a1 apoyándose en la retícula y rellenando para que ningún vano exceda el
            // espaciado estándar (así el riel resultante queda dividido en piezas de catálogo válidas).
            void EmitSpan(double a0, double a1, double fixedCoord)
            {
                var seq = new List<double> { a0, a1 };
                foreach (double g in alongLines)
                {
                    if (g > a0 + Eps && g < a1 - Eps)
                    {
                        seq.Add(g);
                    }
                }

                seq.Sort();
                Emit(seq[0], fixedCoord);
                for (int k = 1; k < seq.Count; k++)
                {
                    int subdivisions = (int)Math.Ceiling(((seq[k] - seq[k - 1]) / alongSpacing) - 1e-9);
                    for (int m = 1; m < subdivisions; m++)
                    {
                        Emit(seq[k - 1] + ((seq[k] - seq[k - 1]) * m / subdivisions), fixedCoord);
                    }

                    Emit(seq[k], fixedCoord);
                }
            }

            foreach (double fixedCoord in fixedLines)
            {
                List<double> crossings = crossAt(fixedCoord);
                if (crossings.Count < 2)
                {
                    if (fixedCoord < loInside)
                    {
                        crossings = crossAt(loInside);
                    }
                    else if (fixedCoord > hiInside)
                    {
                        crossings = crossAt(hiInside);
                    }
                    else
                    {
                        continue;
                    }
                }

                // Coordenadas (en el sentido del riel) de los puntales que YA existen en esta fila.
                List<double> row = puntales
                    .Where(p => GeometryUtil.NearlyEqual(primaryHorizontal ? p.Y : p.X, fixedCoord))
                    .Select(p => primaryHorizontal ? p.X : p.Y)
                    .OrderBy(a => a)
                    .ToList();

                for (int i = 0; i + 1 < crossings.Count; i += 2)
                {
                    double needLo = crossings[i] - Overhang;
                    double needHi = crossings[i + 1] + Overhang;
                    List<double> inside = row.Where(a => a >= needLo - Eps && a <= needHi + Eps).ToList();

                    if (inside.Count == 0)
                    {
                        // La fila no cubre este tramo de losa: se completa entero.
                        EmitSpan(needLo, needHi, fixedCoord);
                    }
                    else
                    {
                        double coveredMin = inside.Min();
                        double coveredMax = inside.Max();
                        if (coveredMin > needLo + Trigger)
                        {
                            EmitSpan(needLo, coveredMin, fixedCoord);
                        }

                        if (coveredMax < needHi - Trigger)
                        {
                            EmitSpan(coveredMax, needHi, fixedCoord);
                        }

                        // Vanos INTERNOS mayores al espaciado del riel: hueco donde el riel no puede cruzar
                        // (parte de la losa "en el aire"). Se rellena para que no quede nada sin apoyo.
                        for (int k = 0; k + 1 < inside.Count; k++)
                        {
                            if (inside[k + 1] - inside[k] > alongSpacing + Trigger)
                            {
                                EmitSpan(inside[k], inside[k + 1], fixedCoord);
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Quita PUNTALES REDUNDANTES: cuando en una misma fila (en el sentido del riel) quedan dos puntales
        /// muy juntos —típico del borde: la última línea de retícula + el puntal de voladizo, a ~0,40 m—,
        /// re‑espacia ese tramo para dejar UN solo puntal, sin exceder los 3,0 m de riel ni perder el borde.
        /// Así no se grafican puntales que no se necesitan (dos donde basta uno).
        /// </summary>
        public static List<Point> RemoveRedundant(List<Point> puntales, bool primaryHorizontal)
        {
            double maxSpan = LayoutCatalog.RailSpacing;
            var result = new List<Point>();

            foreach (IGrouping<double, Point> group in puntales.GroupBy(p => Math.Round(primaryHorizontal ? p.Y : p.X, 2)))
            {
                double fixedCoord = group.Key;
                List<double> along = group
                    .Select(p => primaryHorizontal ? p.X : p.Y)
                    .OrderBy(a => a)
                    .ToList();

                RespaceLine(along, maxSpan);

                foreach (double a in along)
                {
                    result.Add(primaryHorizontal ? new Point(a, fixedCoord) : new Point(fixedCoord, a));
                }
            }

            return result;
        }

        /// <summary>
        /// Re‑espacia una fila de coordenadas: elimina pares más cercanos que <see cref="CloseSpacing"/>,
        /// reinsertando el punto medio solo si al quitar uno el vano superaría <paramref name="maxSpan"/>.
        /// </summary>
        private static void RespaceLine(List<double> xs, double maxSpan)
        {
            bool changed = true;
            int guard = 0;
            while (changed && guard++ < 500)
            {
                changed = false;
                for (int i = 0; i + 1 < xs.Count; i++)
                {
                    if (xs[i + 1] - xs[i] >= CloseSpacing)
                    {
                        continue;
                    }

                    double a = xs[i];
                    double b = xs[i + 1];
                    bool hasPrev = i > 0;
                    bool hasNext = i + 2 < xs.Count;

                    if (!hasNext && hasPrev)
                    {
                        // Par en el borde derecho/superior: se conserva el exterior (b) y se re‑reparte prev→b.
                        double prev = xs[i - 1];
                        xs.RemoveAt(i);
                        // SplitSlack: no reinsertar un puntal si el tramo solo excede 3,0 m por redondeo del
                        // voladizo (p. ej. 3,01 m). Sin esta holgura, un muñón de ~1 cm partía el riel de 3,0
                        // en 1,50 + 1,50 y añadía una fila de puntales innecesaria.
                        if (b - prev > maxSpan + SplitSlack)
                        {
                            xs.Insert(i, (prev + b) / 2);
                        }

                        changed = true;
                        break;
                    }

                    if (!hasPrev && hasNext)
                    {
                        // Par en el borde izquierdo/inferior: se conserva el exterior (a) y se re‑reparte a→next.
                        double next = xs[i + 2];
                        xs.RemoveAt(i + 1);
                        if (next - a > maxSpan + SplitSlack)
                        {
                            xs.Insert(i + 1, (a + next) / 2);
                        }

                        changed = true;
                        break;
                    }

                    if (hasPrev && hasNext && b - xs[i - 1] <= maxSpan + 1e-6)
                    {
                        // Par interior: se quita el interior si el riel puede cruzar sin él.
                        xs.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }
        }
    }
}
