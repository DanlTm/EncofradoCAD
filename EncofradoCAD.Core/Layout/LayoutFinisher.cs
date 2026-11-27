using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// PASE DE ACABADO. Después de que <see cref="BoardBuilder"/> coloca los tableros y de que
    /// <see cref="ColumnTablaBuilder"/> cierra el hueco inmediato de cada columna, todavía pueden quedar
    /// zonas VACÍAS (negras) que el reparto por celdas no alcanzó a cubrir: las tiras verticales alineadas
    /// con una columna angosta (más finas que el tablero más chico) y la franja fina del borde. Este pase
    /// barre TODO lo que quedó sin cubrir y lo resuelve para que no quede ningún hueco negro:
    ///
    ///   • Hueco ANCHO (≥ 0,30 m, cabe un tablero de catálogo)  → se rellena con TABLEROS estándar, eligiendo
    ///     la mejor combinación de anchos (0,60 → 0,30) y el largo de catálogo por banda de riel.
    ///   • Hueco ANGOSTO (&lt; 0,30 m, no cabe ningún tablero)    → se cierra con TABLA (naranja), en una sola
    ///     pieza continua que llena todo el alto (no en pedacitos).
    ///   • Franja de BORDE (fina, contra el filo de la losa)     → TABLA de borde (roja), fusionada en una
    ///     sola tira continua por lado (ahí solo cabe tabla, nada más).
    /// </summary>
    public static class LayoutFinisher
    {
        private const double Eps = LayoutCatalog.GeometryEpsilon;
        private const double Step = 0.05;

        /// <summary>Ancho mínimo (m) que admite un tablero de catálogo; por debajo, la tira va a tabla.</summary>
        private const double MinBoardWidth = 0.30;

        /// <summary>Alto (eje del largo del tablero) por debajo del cual, pegado al borde, la zona es franja de borde.</summary>
        private const double BorderBandMax = 0.50;

        /// <summary>Distancia del centro al filo de la losa para considerar la zona "de borde" (m).</summary>
        private const double BorderEdgeReach = 0.35;

        public sealed class Result
        {
            public List<BoardPiece> ExtraBoards { get; } = new();
            public List<Rect> ColumnTablas { get; } = new();  // naranja
            public List<Rect> BorderTablas { get; } = new();  // roja
        }

        /// <summary>
        /// Resuelve las zonas vacías. <paramref name="boards"/> y <paramref name="claimedTablas"/> (las tablas
        /// de columna ya calculadas) se tratan como YA cubiertas; solo se procesa lo que queda negro.
        /// Los tableros SIEMPRE llevan el 1,20 en Y (board120AlongY), así que el ANCHO se reparte en X y el
        /// LARGO en Y, apoyándose en <paramref name="yLines"/> (líneas de riel/banda).
        /// </summary>
        public static Result Finish(
            List<BoardPiece> boards,
            List<Rect> claimedTablas,
            List<ColumnSnapshot> columns,
            List<Point> slab,
            List<double> yLines,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            var result = new Result();

            double overhang = LayoutCatalog.EdgeOverhang;
            double lo = minX - overhang, hi = maxX + overhang;
            double bot = minY - overhang, top = maxY + overhang;
            double slabMargin = overhang + 0.05;

            int nX = (int)Math.Ceiling((hi - lo) / Step);
            int nY = (int)Math.Ceiling((top - bot) / Step);
            if (nX <= 0 || nY <= 0)
            {
                return result;
            }

            // Ocupación de lo que queda VACÍO (negro): dentro de la losa+voladizo, fuera de columnas, y sin
            // tablero ni tabla ya reclamada encima.
            var empty = new bool[nX, nY];
            for (int i = 0; i < nX; i++)
            {
                double x = lo + ((i + 0.5) * Step);
                if (x > hi + Eps)
                {
                    continue;
                }

                for (int j = 0; j < nY; j++)
                {
                    double y = bot + ((j + 0.5) * Step);
                    if (y > top + Eps)
                    {
                        continue;
                    }

                    var pt = new Point(x, y);
                    if (GeometryUtil.DistanceToPolygon(pt, slab) > slabMargin)
                    {
                        continue;
                    }

                    if (columns.Any(c => c.Contains(pt, 0.0)))
                    {
                        continue;
                    }

                    if (boards.Any(b => b.Rect.Contains(pt)))
                    {
                        continue;
                    }

                    if (claimedTablas != null && claimedTablas.Any(t => t.Contains(pt)))
                    {
                        continue;
                    }

                    empty[i, j] = true;
                }
            }

            List<Rect> rects = ExtractRects(empty, nX, nY, lo, bot);

            var borderPieces = new List<Rect>();
            foreach (Rect r in rects)
            {
                var center = new Point(r.X + (r.Width / 2), r.Y + (r.Height / 2));
                bool insideSlab = GeometryUtil.IsPointInsidePolygon(center, slab);
                // Distancia REAL al filo de la losa (no la de DistanceToPolygon, que da 0 dentro): así una
                // tira fina PEGADA al borde se detecta, pero una tira fina en el interior no.
                double edgeDist = EdgeDistance(center, slab);

                bool isBorder = !insideSlab || (r.Height < BorderBandMax && edgeDist < BorderEdgeReach);

                if (isBorder)
                {
                    borderPieces.Add(r);
                }
                else if (r.Width >= MinBoardWidth - Eps)
                {
                    FillWithBoards(r, yLines, slab, result.ExtraBoards);
                }
                else if (r.Width >= 0.08 && r.Height >= 0.08)
                {
                    // Tira angosta alineada con columna: se cierra con tabla (naranja), pieza completa.
                    // (Se ignoran astillas < 8 cm que son solo ruido del muestreo.)
                    result.ColumnTablas.Add(r);
                }
            }

            result.BorderTablas.AddRange(MergeBorderStrips(borderPieces, minX, maxX, minY, maxY));
            return result;
        }

        /// <summary>Rellena un hueco ancho con tableros: 1,20 en Y (por banda de riel) y combinación de anchos en X.</summary>
        private static void FillWithBoards(Rect rect, List<double> yLines, List<Point> slab, List<BoardPiece> outBoards)
        {
            // Cortes en Y por las líneas de riel dentro del rect (para que ningún tablero pase de 1,20).
            var yCuts = new List<double> { rect.Y, rect.Bottom };
            foreach (double yl in yLines)
            {
                if (yl > rect.Y + Eps && yl < rect.Bottom - Eps)
                {
                    yCuts.Add(yl);
                }
            }

            yCuts.Sort();

            List<double> widths = BoardBuilder.ComboWidthsForSpan(rect.Width);
            if (widths.Count == 0)
            {
                return;
            }

            for (int b = 0; b + 1 < yCuts.Count; b++)
            {
                double y0 = yCuts[b];
                double y1 = yCuts[b + 1];
                double bandH = y1 - y0;
                if (bandH < 0.10)
                {
                    continue;
                }

                double drawn = Math.Min(bandH, LayoutCatalog.BoardLength);
                double nominal = BoardBuilder.PickBoardLength(drawn);

                double cursorX = rect.X;
                foreach (double w in widths)
                {
                    var r = new Rect(cursorX, y0, w, drawn);
                    var c = new Point(r.X + (r.Width / 2), r.Y + (r.Height / 2));
                    if (GeometryUtil.DistanceToPolygon(c, slab) <= LayoutCatalog.EdgeOverhang + 0.05)
                    {
                        outBoards.Add(new BoardPiece(r, w, nominal));
                    }

                    cursorX += w;
                }
            }
        }

        /// <summary>Fusiona las tiras de borde en UNA tabla continua por lado (arriba/abajo/izq/der).</summary>
        private static List<Rect> MergeBorderStrips(List<Rect> pieces, double minX, double maxX, double minY, double maxY)
        {
            var merged = new List<Rect>();
            if (pieces.Count == 0)
            {
                return merged;
            }

            double midX = (minX + maxX) / 2;
            double midY = (minY + maxY) / 2;

            // Clasifica cada tira por el lado más cercano y une el bounding box de cada grupo.
            var groups = new Dictionary<string, List<Rect>>
            {
                ["top"] = new(), ["bottom"] = new(), ["left"] = new(), ["right"] = new(),
            };

            foreach (Rect r in pieces)
            {
                double cx = r.X + (r.Width / 2);
                double cy = r.Y + (r.Height / 2);
                double distTop = Math.Abs(cy - maxY);
                double distBot = Math.Abs(cy - minY);
                double distLeft = Math.Abs(cx - minX);
                double distRight = Math.Abs(cx - maxX);
                double m = Math.Min(Math.Min(distTop, distBot), Math.Min(distLeft, distRight));

                string side = m == distTop ? "top" : m == distBot ? "bottom" : m == distLeft ? "left" : "right";
                groups[side].Add(r);
            }

            foreach (List<Rect> grp in groups.Values)
            {
                if (grp.Count == 0)
                {
                    continue;
                }

                double x0 = grp.Min(r => r.X);
                double y0 = grp.Min(r => r.Y);
                double x1 = grp.Max(r => r.Right);
                double y1 = grp.Max(r => r.Bottom);
                merged.Add(new Rect(x0, y0, x1 - x0, y1 - y0));
            }

            return merged;
        }

        /// <summary>Distancia mínima de un punto al filo (aristas) de la losa, dentro o fuera del polígono.</summary>
        private static double EdgeDistance(Point p, List<Point> slab)
        {
            double best = double.MaxValue;
            for (int i = 0; i < slab.Count; i++)
            {
                best = Math.Min(best, GeometryUtil.DistancePointToSegment(p, slab[i], slab[(i + 1) % slab.Count]));
            }

            return best;
        }

        /// <summary>Fusión voraz de celdas marcadas en rectángulos (misma técnica que ColumnTablaBuilder).</summary>
        private static List<Rect> ExtractRects(bool[,] occ, int nX, int nY, double x0, double y0)
        {
            var rects = new List<Rect>();
            var used = new bool[nX, nY];
            for (int i = 0; i < nX; i++)
            {
                for (int j = 0; j < nY; j++)
                {
                    if (!occ[i, j] || used[i, j])
                    {
                        continue;
                    }

                    int w = 1;
                    while (i + w < nX && occ[i + w, j] && !used[i + w, j])
                    {
                        w++;
                    }

                    int h = 1;
                    bool grow = true;
                    while (grow && j + h < nY)
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

                    rects.Add(new Rect(x0 + (i * Step), y0 + (j * Step), w * Step, h * Step));
                }
            }

            return rects;
        }
    }
}
