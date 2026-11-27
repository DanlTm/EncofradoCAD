using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// Coloca los tableros de contacto (1,20 m perpendiculares a los rieles) y las zonas de remate.
    /// El campo de tableros llega hasta la línea central de los rieles exteriores (20 cm por fuera del
    /// borde en los 4 lados). Elige los anchos con programación dinámica para minimizar huecos.
    /// </summary>
    public static partial class BoardBuilder
    {
        private const double Eps = LayoutCatalog.GeometryEpsilon;
        private static readonly double[] Widths = LayoutCatalog.BoardWidthsDescending;

        /// <summary>Un borde de la losa se considera "recto" (vertical u horizontal) si su desviación es menor a esto.</summary>
        private const double AxisCutTolerance = 0.01;

        public static void Generate(
            List<Point> slab,
            List<ColumnSnapshot> columns,
            double minX,
            double maxX,
            double minY,
            double maxY,
            bool board120AlongY,
            List<double> xLines,
            List<double> yLines,
            out List<BoardPiece> boards,
            out List<Rect> remates)
        {
            boards = new List<BoardPiece>();
            remates = new List<Rect>();

            // Líneas de rieles en el sentido del LARGO del tablero: las bandas de tableros se anclan a ellas
            // para que cada tablero vaya de centro de un riel al centro del siguiente.
            List<double> primLines = (board120AlongY ? yLines : xLines).OrderBy(v => v).ToList();

            // Las líneas de corte de la rejilla salen de los bordes de columnas Y de los QUIEBRES RECTOS de la
            // losa. Esto es lo que hace que las CASAS ESPECIALES (formas L / U / T / con escalón) se cubran: sin
            // estos cortes, una celda quedaba a caballo entre la parte que es losa y el hueco cóncavo, fallaba la
            // prueba de "celda dentro" y se descartaba ENTERA, dejando esa franja sin tableros aunque tuviera
            // puntales y rieles.
            //
            // IMPORTANTE — solo se corta en vértices de BORDE RECTO: un borde vertical corta en X y uno
            // horizontal corta en Y. Cortar en TODOS los vértices rompía las formas CURVAS (medialuna, elipse):
            // sus decenas de vértices inclinados partían la rejilla en tiras finísimas y todo se volvía remate
            // (0 tableros). Con los bordes inclinados/curvos no se generan cortes: esas celdas se resuelven por
            // el recorte al borde real (SlabSecExtentInBand + IsSupported), como siempre.
            //
            // Y SOLO en quiebres INTERIORES (no en el borde exterior de la losa): un corte justo en el borde
            // aislaría el VOLADIZO de 20 cm en una celda propia demasiado angosta para un tablero, que se iría
            // a remate — dejando el tablero de orilla "al ras" del borde en vez de llegar al filo del riel.
            // Al no cortar en el borde, el tablero de orilla se extiende hasta el filo del riel (borde + 20 cm),
            // que es donde se apoyan los soportes de los costados. El ancho lo elige la programación dinámica
            // de ChooseBoardWidths para llegar lo más al ras posible del filo.
            double slabMinX = slab.Min(p => p.X);
            double slabMaxX = slab.Max(p => p.X);
            double slabMinY = slab.Min(p => p.Y);
            double slabMaxY = slab.Max(p => p.Y);

            var slabXCuts = new List<double>();
            var slabYCuts = new List<double>();
            for (int s = 0; s < slab.Count; s++)
            {
                Point a = slab[s];
                Point b = slab[(s + 1) % slab.Count];
                if (Math.Abs(a.X - b.X) < AxisCutTolerance
                    && a.X > slabMinX + AxisCutTolerance && a.X < slabMaxX - AxisCutTolerance)
                {
                    slabXCuts.Add(a.X); // borde vertical INTERIOR → corte en X
                }

                if (Math.Abs(a.Y - b.Y) < AxisCutTolerance
                    && a.Y > slabMinY + AxisCutTolerance && a.Y < slabMaxY - AxisCutTolerance)
                {
                    slabYCuts.Add(a.Y); // borde horizontal INTERIOR → corte en Y
                }
            }

            // Cortes de COLUMNA: solo en el eje del ANCHO del tablero (sec), NUNCA en el eje del LARGO
            // (prim, de riel a riel). Antes las columnas cortaban también el eje largo, y como cada línea de
            // corte cruza TODA la losa, partían las bandas de riel a riel en tiras cortas (< 1,20) que se
            // iban a REMATE: son las franjas rosado-rojizas de ancho completo a la altura de cada fila de
            // columnas. Al cortar solo a lo ancho, el tablero va COMPLETO de centro de un riel al centro del
            // otro (0,60×1,20) y la columna solo interrumpe el tablero a lo ancho para rodearla.
            IEnumerable<double> colSecCutsX = board120AlongY
                ? columns.SelectMany(column => new[] { column.MinX, column.MaxX })
                : System.Linq.Enumerable.Empty<double>();
            IEnumerable<double> colSecCutsY = board120AlongY
                ? System.Linq.Enumerable.Empty<double>()
                : columns.SelectMany(column => new[] { column.MinY, column.MaxY });

            List<double> xCuts = BuildCuts(minX, maxX, colSecCutsX.Concat(slabXCuts));
            List<double> yCuts = BuildCuts(minY, maxY, colSecCutsY.Concat(slabYCuts));

            for (int xIndex = 0; xIndex < xCuts.Count - 1; xIndex++)
            {
                double x0 = xCuts[xIndex];
                double x1 = xCuts[xIndex + 1];
                if ((x1 - x0) <= Eps)
                {
                    continue;
                }

                for (int yIndex = 0; yIndex < yCuts.Count - 1; yIndex++)
                {
                    double y0 = yCuts[yIndex];
                    double y1 = yCuts[yIndex + 1];
                    if ((y1 - y0) <= Eps)
                    {
                        continue;
                    }

                    Rect cell = new Rect(x0, y0, x1 - x0, y1 - y0);
                    if (!IsCellInBoardRegion(cell, slab))
                    {
                        continue;
                    }

                    if (CellCenterInsideAnyColumn(cell, columns))
                    {
                        continue;
                    }

                    bool nearColumn = columns.Any(column => DistanceRectToColumnFootprint(cell, column) < 1.22);
                    FillCellWithBoards(cell, slab, primLines, boards, remates, nearColumn, board120AlongY);
                }
            }
        }

        /// <summary>
        /// ¿La celda pertenece al campo de tableros? Vale si CUALQUIER parte de la celda (su centro o alguna
        /// de sus 4 esquinas) cae dentro de la losa o dentro del voladizo de 20 cm.
        ///
        /// CASOS ESPECIALES (L / U / T / escalón / borde inclinado): antes se exigía que el rectángulo
        /// ENTERO estuviera dentro de la losa; una celda que cruzaba el borde cóncavo fallaba y se descartaba
        /// completa, incluida la parte que sí era losa (por eso quedaban franjas con puntales pero sin
        /// tableros). Ahora basta con que la celda TOQUE la losa para procesarla: el recorte fino lo hacen
        /// aguas abajo <see cref="SlabSecExtentInBand"/> (limita el ancho al borde real) y
        /// <see cref="IsSupported"/> (descarta cada tablero que quede fuera del borde + voladizo), así que
        /// abrir esta compuerta nunca dibuja tableros "en el aire", solo deja que se llene lo que sí tiene apoyo.
        /// </summary>
        private static bool IsCellInBoardRegion(Rect cell, List<Point> slab)
        {
            double margin = LayoutCatalog.EdgeOverhang + 0.05;
            Point[] samples =
            {
                new Point(cell.X + (cell.Width / 2), cell.Y + (cell.Height / 2)),
                new Point(cell.X, cell.Y),
                new Point(cell.Right, cell.Y),
                new Point(cell.X, cell.Bottom),
                new Point(cell.Right, cell.Bottom)
            };

            return samples.Any(p => GeometryUtil.DistanceToPolygon(p, slab) <= margin);
        }

        private static bool CellCenterInsideAnyColumn(Rect cell, List<ColumnSnapshot> columns)
        {
            var center = new Point(cell.X + (cell.Width / 2), cell.Y + (cell.Height / 2));
            return columns.Any(column => column.Contains(center, 0.02));
        }

        private static double DistanceRectToColumnFootprint(Rect cell, ColumnSnapshot column)
        {
            double dx = 0;
            if (cell.Right < column.MinX)
            {
                dx = column.MinX - cell.Right;
            }
            else if (cell.Left > column.MaxX)
            {
                dx = cell.Left - column.MaxX;
            }

            double dy = 0;
            if (cell.Bottom < column.MinY)
            {
                dy = column.MinY - cell.Bottom;
            }
            else if (cell.Top > column.MaxY)
            {
                dy = cell.Top - column.MaxY;
            }

            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        /// <summary>
        /// ¿El tablero/remate tiene apoyo? Solo si su centro cae dentro de la losa o dentro del voladizo de
        /// 20 cm real. Evita graficar tableros de más en zonas sin estructura (p. ej. la esquina del bounding
        /// box que la curva ya dejó afuera).
        /// </summary>
        private static bool IsSupported(Rect rect, List<Point> slab)
        {
            // Se conserva si CUALQUIER parte del tablero (centro o esquinas) cae dentro de la losa o del
            // voladizo de 20 cm. Así la banda de voladizo (que sobresale con el centro afuera) sí se dibuja,
            // pero los tableros ENTERAMENTE fuera del borde real (más allá de la curva) se descartan.
            double margin = LayoutCatalog.EdgeOverhang + 0.05;
            Point[] samples =
            {
                new Point(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2)),
                new Point(rect.X, rect.Y),
                new Point(rect.Right, rect.Y),
                new Point(rect.X, rect.Bottom),
                new Point(rect.Right, rect.Bottom)
            };

            foreach (Point p in samples)
            {
                if (GeometryUtil.DistanceToPolygon(p, slab) <= margin)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Extremo de la losa + 20 cm de voladizo en la dirección del ANCHO, dentro de la banda
        /// [<paramref name="primLo"/>, <paramref name="primHi"/>]. Muestrea el borde en varios puntos de la
        /// banda y toma el borde más ADENTRO (máx. izquierda, mín. derecha) para que el tablero no sobresalga
        /// del filo del riel en NINGÚN punto de la banda. Devuelve null si la banda no cruza la losa.
        /// </summary>
        private static (double lo, double hi)? SlabSecExtentInBand(List<Point> slab, bool board120AlongY, double primLo, double primHi)
        {
            double maxLeft = double.MinValue;
            double minRight = double.MaxValue;
            bool any = false;

            foreach (double t in new[] { 0.02, 0.5, 0.98 })
            {
                double p = primLo + ((primHi - primLo) * t);
                List<double> crossings = board120AlongY
                    ? GeometryUtil.HorizontalCrossings(slab, p)
                    : GeometryUtil.VerticalCrossings(slab, p);
                if (crossings.Count >= 2)
                {
                    maxLeft = Math.Max(maxLeft, crossings[0]);
                    minRight = Math.Min(minRight, crossings[^1]);
                    any = true;
                }
            }

            if (!any)
            {
                return null;
            }

            return (maxLeft - LayoutCatalog.EdgeOverhang, minRight + LayoutCatalog.EdgeOverhang);
        }

        private static List<double> BuildCuts(double min, double max, IEnumerable<double> values)
        {
            return values
                .Where(value => value > min + Eps && value < max - Eps)
                .Append(min)
                .Append(max)
                .Distinct()
                .OrderBy(value => value)
                .ToList();
        }
    }
}
