using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// TILER POR FILAS (losas rectangulares). Reparte los tableros de contacto banda de riel por banda de
    /// riel, tratando cada fila como un tramo LIBRE y continuo:
    ///
    ///   • Donde NO hay columna en la banda, se cubre TODO el ancho de corrido con tableros ESTÁNDAR de
    ///     0,60 × 1,20 y solo al FILO (el borde de la losa) se pone el tablero de menor anchura que haga
    ///     falta (0,55 … 0,30). Nunca aparece una tira de 0,30/0,40 en medio de una fila libre.
    ///   • Donde SÍ hay columna, la fila se PARTE únicamente en esa columna: cada tramo libre entre columnas
    ///     (o entre columna y borde) se cubre igual (0,60 primero, pequeño al filo) y el hueco de la columna
    ///     se deja vacío para que lo cierre la TABLA (naranja).
    ///
    /// Solo actúa en losas rectangulares (el caso real del sistema). Para formas especiales (L/U/T, curvas)
    /// el motor sigue usando <see cref="BoardBuilder"/>, que ya las resuelve. Los tableros van SIEMPRE con el
    /// 1,20 en Y (board120AlongY), así que el ancho se reparte en X y el largo (por banda) en Y.
    /// </summary>
    public static class RectFieldTiler
    {
        private const double Eps = LayoutCatalog.GeometryEpsilon;
        private const double Overhang = LayoutCatalog.EdgeOverhang;
        private const double MinBoardWidth = 0.30;

        /// <summary>
        /// Reparte los tableros si la losa es un rectángulo alineado a los ejes; devuelve false (y no toca
        /// las salidas) si no lo es, para que el llamador use <see cref="BoardBuilder"/>.
        /// </summary>
        public static bool TryTile(
            List<Point> slab,
            List<ColumnSnapshot> columns,
            List<double> yLines,
            bool primaryHorizontal,
            double minX,
            double maxX,
            double minY,
            double maxY,
            out List<BoardPiece> boards,
            out List<Rect> remates)
        {
            boards = new List<BoardPiece>();
            remates = new List<Rect>();

            // Solo se aplica cuando los rieles corren en HORIZONTAL: ahí las líneas de riel (yLines) están a
            // 1,20 m, que es justo el largo del tablero, así cada banda lleva UN tablero de largo. En losas más
            // altas que anchas las yLines van a 3,0 m (eje de rieles) y este tiler no aplica → BoardBuilder.
            if (!primaryHorizontal || !IsAxisAlignedRectangle(slab, minX, maxX, minY, maxY))
            {
                return false;
            }

            double fx0 = minX - Overhang;
            double fx1 = maxX + Overhang;
            List<double> yl = yLines.Where(v => v >= minY - Overhang - 0.5 && v <= maxY + Overhang + 0.5)
                .OrderBy(v => v).ToList();
            if (yl.Count < 2)
            {
                // Red de seguridad: si por algún motivo no llegaron las líneas de riel, se generan bandas
                // regulares de 1,20 m entre el voladizo inferior y el superior.
                yl = new List<double>();
                for (double v = minY - Overhang; v < maxY + Overhang - Eps; v += LayoutCatalog.BoardLength)
                {
                    yl.Add(v);
                }

                yl.Add(maxY + Overhang);
            }

            for (int k = 0; k + 1 < yl.Count; k++)
            {
                double y0 = yl[k];
                double y1 = yl[k + 1];
                double bandH = y1 - y0;
                if (bandH < 0.10)
                {
                    continue;
                }

                double drawn = Math.Min(bandH, LayoutCatalog.BoardLength);
                double nominal = BoardBuilder.PickBoardLength(drawn);

                // Columnas que cruzan ESTA banda (en Y).
                List<ColumnSnapshot> inBand = columns
                    .Where(c => c.MaxY > y0 + Eps && c.MinY < y1 - Eps)
                    .OrderBy(c => c.X)
                    .ToList();

                // Tramos LIBRES en X (entre columnas y bordes).
                var spans = new List<(double a, double b)>();
                double cursor = fx0;
                foreach (ColumnSnapshot c in inBand)
                {
                    double cxMin = Math.Max(fx0, c.MinX);
                    double cxMax = Math.Min(fx1, c.MaxX);
                    if (cxMin > cursor + Eps)
                    {
                        spans.Add((cursor, cxMin));
                    }

                    cursor = Math.Max(cursor, cxMax);
                }

                if (fx1 > cursor + Eps)
                {
                    spans.Add((cursor, fx1));
                }

                foreach ((double a, double b) in spans)
                {
                    double w = b - a;
                    if (w < MinBoardWidth - Eps)
                    {
                        if (w > 0.02)
                        {
                            remates.Add(new Rect(a, y0, w, drawn));
                        }

                        continue;
                    }

                    bool leftFilo = Math.Abs(a - fx0) < Eps;
                    bool rightFilo = Math.Abs(b - fx1) < Eps;
                    TileSpan(a, b, y0, drawn, nominal, leftFilo, rightFilo, boards);
                }

                // Excedente de banda por encima de 1,20 m (raro): franja de remate.
                if (bandH > LayoutCatalog.BoardLength + Eps)
                {
                    remates.Add(new Rect(fx0, y0 + LayoutCatalog.BoardLength, fx1 - fx0, bandH - LayoutCatalog.BoardLength));
                }
            }

            return true;
        }

        /// <summary>
        /// Cubre un tramo libre [a,b] con la mejor combinación de anchos, poniendo los 0,60 primero y el
        /// tablero menor al FILO (el borde de la losa). Si el filo está a la izquierda, el pequeño va a la
        /// izquierda; si no, va a la derecha.
        /// </summary>
        private static void TileSpan(double a, double b, double y0, double drawn, double nominal, bool leftFilo, bool rightFilo, List<BoardPiece> boards)
        {
            List<double> widths = BoardBuilder.ComboWidthsForSpan(b - a);
            if (widths.Count == 0)
            {
                return;
            }

            // ComboWidthsForSpan viene en orden descendente (0,60 primero). Para que el pequeño quede pegado
            // al filo de la losa: si el filo es el borde izquierdo, se invierte (ascendente → pequeño a la
            // izquierda); en cualquier otro caso queda descendente (pequeño a la derecha).
            if (leftFilo && !rightFilo)
            {
                widths = widths.OrderBy(v => v).ToList();
            }
            else
            {
                widths = widths.OrderByDescending(v => v).ToList();
            }

            double x = a;
            foreach (double w in widths)
            {
                boards.Add(new BoardPiece(new Rect(x, y0, w, drawn), w, nominal));
                x += w;
            }
        }

        /// <summary>¿La losa es un rectángulo alineado a los ejes que coincide con su bounding box?</summary>
        private static bool IsAxisAlignedRectangle(List<Point> slab, double minX, double maxX, double minY, double maxY)
        {
            if (slab == null || slab.Count != 4)
            {
                return false;
            }

            const double tol = 0.02;
            foreach (Point p in slab)
            {
                bool onX = Math.Abs(p.X - minX) < tol || Math.Abs(p.X - maxX) < tol;
                bool onY = Math.Abs(p.Y - minY) < tol || Math.Abs(p.Y - maxY) < tol;
                if (!onX || !onY)
                {
                    return false;
                }
            }

            // Deben aparecer las 4 esquinas distintas.
            int corners = slab.Select(p =>
                    ((Math.Abs(p.X - maxX) < tol) ? 1 : 0) | ((Math.Abs(p.Y - maxY) < tol) ? 2 : 0))
                .Distinct().Count();
            return corners == 4;
        }
    }
}
