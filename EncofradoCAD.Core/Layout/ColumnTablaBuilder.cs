using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// MÓDULO DE TABLA (cierre de columnas).
    ///
    /// Regla del negocio: donde hay una columna, el motor deja un HUECO vacío (la columna queda "atrapada"
    /// entre las DOS filas de rieles paralelas que la flanquean, y los tableros de catálogo se cortan al filo
    /// de la columna). Ese hueco —el espacio vacío que queda arriba y abajo de la columna dentro de la banda
    /// de rieles— NO se cubre con un tablero de catálogo, sino que se cierra en obra con TABLA (tablilla de
    /// madera cortada a medida). Este módulo detecta esas zonas y las devuelve como rectángulos de tabla, para
    /// que el 3D y el BOM las muestren con un color distintivo (no se confunden con un tablero).
    ///
    /// Cómo lo calcula, por cada columna:
    ///   1) Encuentra las dos líneas de RIEL paralelas que atrapan la columna (la de justo por debajo y la de
    ///      justo por encima, en el eje perpendicular a los rieles).
    ///   2) Recorta una banda alrededor de la columna (entre esos dos rieles, y a lo largo del riel el ancho
    ///      de la columna más un pequeño margen).
    ///   3) Dentro de esa banda marca como TABLA toda celda que: cae dentro de la losa (+ voladizo), NO está
    ///      dentro de ninguna columna y NO está ya cubierta por un tablero. Así solo se rellena el HUECO real
    ///      (si el tablero especial de columna ya tapó parte, esa parte no se duplica con tabla).
    ///   4) Fusiona las celdas contiguas en rectángulos limpios.
    /// </summary>
    public static class ColumnTablaBuilder
    {
        private const double Eps = LayoutCatalog.GeometryEpsilon;

        /// <summary>Paso de muestreo de la banda (m). 5 cm = misma resolución que el catálogo de tableros.</summary>
        private const double Step = 0.05;

        /// <summary>Cuánto se extiende la banda a lo LARGO del riel, más allá del filo de la columna (m).</summary>
        private const double SideMargin = 0.35;

        /// <summary>Si no hay riel que flanquee la columna (columna al borde), cuánto abrir la banda (m).</summary>
        private const double FallbackBandReach = 0.30;

        /// <summary>Se descartan tiras de tabla más finas que esto (ruido de muestreo, m).</summary>
        private const double MinTablaSize = 0.04;

        /// <summary>
        /// Devuelve los rectángulos de TABLA que cierran los huecos alrededor de cada columna.
        /// <paramref name="xLines"/>/<paramref name="yLines"/> son las líneas de la retícula YA desplazadas
        /// por <see cref="ColumnClearance"/> (las mismas que consumió el generador de tableros), y
        /// <paramref name="boards"/> son los tableros ya colocados (para no rellenar donde ya hay tablero).
        /// <paramref name="primaryHorizontal"/>=true cuando los rieles corren en X (líneas de riel = yLines).
        /// </summary>
        public static List<Rect> Build(
            List<ColumnSnapshot> columns,
            List<BoardPiece> boards,
            List<Point> slab,
            List<double> xLines,
            List<double> yLines,
            bool primaryHorizontal)
        {
            var tablas = new List<Rect>();
            if (columns == null || columns.Count == 0)
            {
                return tablas;
            }

            // Líneas donde se apoya cada RIEL (eje perpendicular a la dirección del riel).
            List<double> railLines = (primaryHorizontal ? yLines : xLines)
                .OrderBy(v => v)
                .ToList();

            double slabMargin = LayoutCatalog.EdgeOverhang + 0.05;

            foreach (ColumnSnapshot column in columns)
            {
                // Extremos de la columna en el eje PERP (a través de los rieles) y en el eje ALONG (a lo largo).
                double perpMin = primaryHorizontal ? column.MinY : column.MinX;
                double perpMax = primaryHorizontal ? column.MaxY : column.MaxX;
                double alongMin = primaryHorizontal ? column.MinX : column.MinY;
                double alongMax = primaryHorizontal ? column.MaxX : column.MaxY;

                // Los dos rieles paralelos que atrapan la columna: el de justo por debajo y el de justo encima.
                double bandLo = railLines.Where(l => l <= perpMin + Eps)
                    .DefaultIfEmpty(perpMin - FallbackBandReach).Max();
                double bandHi = railLines.Where(l => l >= perpMax - Eps)
                    .DefaultIfEmpty(perpMax + FallbackBandReach).Min();

                double aMin = alongMin - SideMargin;
                double aMax = alongMax + SideMargin;

                double aSpan = aMax - aMin;
                double pSpan = bandHi - bandLo;
                if (aSpan <= Step || pSpan <= Step)
                {
                    continue;
                }

                int nA = (int)Math.Ceiling(aSpan / Step);
                int nP = (int)Math.Ceiling(pSpan / Step);

                var occ = new bool[nA, nP];
                for (int i = 0; i < nA; i++)
                {
                    double along = aMin + ((i + 0.5) * Step);
                    // Ceil() puede añadir una fila/columna extra apenas fuera de la banda por redondeo de
                    // coma flotante: se ignora toda celda cuyo centro caiga fuera del rectángulo previsto,
                    // para no dejar tiras de tabla de 5 cm pegadas al riel.
                    if (along > aMax + Eps)
                    {
                        continue;
                    }

                    for (int j = 0; j < nP; j++)
                    {
                        double perp = bandLo + ((j + 0.5) * Step);
                        if (perp > bandHi + Eps)
                        {
                            continue;
                        }

                        double x = primaryHorizontal ? along : perp;
                        double y = primaryHorizontal ? perp : along;
                        var pt = new Point(x, y);

                        // 1) dentro de la losa (+ voladizo).
                        if (GeometryUtil.DistanceToPolygon(pt, slab) > slabMargin)
                        {
                            continue;
                        }

                        // 2) NO dentro de ninguna columna (la columna ocupa su propio espacio).
                        if (columns.Any(c => c.Contains(pt, 0.0)))
                        {
                            continue;
                        }

                        // 3) NO ya cubierto por un tablero (solo se rellena el hueco real).
                        if (boards != null && boards.Any(b => b.Rect.Contains(pt)))
                        {
                            continue;
                        }

                        occ[i, j] = true;
                    }
                }

                ExtractRects(occ, nA, nP, aMin, bandLo, primaryHorizontal, tablas);
            }

            return DeduplicateAndFilter(tablas);
        }

        /// <summary>
        /// Fusiona las celdas marcadas en rectángulos: por cada celda libre expande primero a lo ancho y luego
        /// a lo alto mientras todas las celdas del bloque estén marcadas (barrido voraz). Devuelve pocos
        /// rectángulos limpios que cubren exactamente las celdas de tabla.
        /// </summary>
        private static void ExtractRects(bool[,] occ, int nA, int nP, double aMin, double pMin, bool primaryHorizontal, List<Rect> outRects)
        {
            var used = new bool[nA, nP];
            for (int i = 0; i < nA; i++)
            {
                for (int j = 0; j < nP; j++)
                {
                    if (!occ[i, j] || used[i, j])
                    {
                        continue;
                    }

                    int w = 1;
                    while (i + w < nA && occ[i + w, j] && !used[i + w, j])
                    {
                        w++;
                    }

                    int h = 1;
                    bool grow = true;
                    while (grow && j + h < nP)
                    {
                        for (int k = 0; k < w; k++)
                        {
                            if (!occ[i + k, j + h] || used[i + k, j + h])
                            {
                                grow = false;
                                break;
                            }
                        }

                        if (grow)
                        {
                            h++;
                        }
                    }

                    for (int a = 0; a < w; a++)
                    {
                        for (int b = 0; b < h; b++)
                        {
                            used[i + a, j + b] = true;
                        }
                    }

                    double a0 = aMin + (i * Step);
                    double p0 = pMin + (j * Step);
                    double aLen = w * Step;
                    double pLen = h * Step;

                    // (along, perp) → (x, y). Con rieles en X: along=X, perp=Y; con rieles en Y al revés.
                    Rect rect = primaryHorizontal
                        ? new Rect(a0, p0, aLen, pLen)
                        : new Rect(p0, a0, pLen, aLen);
                    outRects.Add(rect);
                }
            }
        }

        private static List<Rect> DeduplicateAndFilter(List<Rect> rects)
        {
            var result = new List<Rect>();
            foreach (Rect r in rects)
            {
                if (r.Width < MinTablaSize || r.Height < MinTablaSize)
                {
                    continue;
                }

                bool dup = result.Any(e =>
                    Math.Abs(e.X - r.X) < Eps && Math.Abs(e.Y - r.Y) < Eps
                    && Math.Abs(e.Width - r.Width) < Eps && Math.Abs(e.Height - r.Height) < Eps);
                if (!dup)
                {
                    result.Add(r);
                }
            }

            return result;
        }
    }
}
