using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;
using EncofradoCAD.Core.Layout;
using Xunit;

namespace EncofradoCAD.Tests
{
    /// <summary>
    /// Pruebas del tiler por filas: en losas rectangulares las filas se cubren con tableros estándar de
    /// 0,60 y el tablero de menor anchura solo aparece AL FILO (borde de la losa o filo de columna), nunca
    /// como una tira suelta en medio de una fila libre.
    /// </summary>
    public class RectFieldTilerTests
    {
        private static List<Point> Poly(params (double x, double y)[] pts)
            => pts.Select(p => new Point(p.x, p.y)).ToList();

        private static PreviewLayout Build(List<Point> slab, List<ColumnSnapshot> columns)
        {
            double minX = slab.Min(p => p.X), maxX = slab.Max(p => p.X);
            double minY = slab.Min(p => p.Y), maxY = slab.Max(p => p.Y);
            return LayoutEngine.BuildLayout(slab, columns, minX, maxX, minY, maxY);
        }

        [Fact]
        public void RectangularSlab_IsMostly060Boards()
        {
            var slab = Poly((0, 0), (12.6, 0), (12.6, 9.9), (0, 9.9));
            var columns = new List<ColumnSnapshot>();
            foreach (double x in new[] { 1.5, 4.5, 7.5, 10.5 })
            {
                foreach (double y in new[] { 1.5, 4.0, 6.5, 9.0 })
                {
                    columns.Add(new ColumnSnapshot(x, y, 0.40, 0.40));
                }
            }

            PreviewLayout layout = Build(slab, columns);

            int total = layout.Boards.Count;
            int wide = layout.Boards.Count(b => b.Width >= 0.595);
            Assert.True(total > 0, "sin tableros");
            Assert.True(wide >= total * 0.6, $"pocos 0,60: {wide}/{total}");
        }

        [Fact]
        public void SmallBoards_OnlyAtEdgeOrColumn()
        {
            var slab = Poly((0, 0), (12.6, 0), (12.6, 9.9), (0, 9.9));
            double minX = 0, maxX = 12.6;
            var columns = new List<ColumnSnapshot>();
            foreach (double x in new[] { 1.5, 4.5, 7.5, 10.5 })
            {
                foreach (double y in new[] { 1.5, 4.0, 6.5, 9.0 })
                {
                    columns.Add(new ColumnSnapshot(x, y, 0.40, 0.40));
                }
            }

            PreviewLayout layout = Build(slab, columns);

            // Todo tablero de menor anchura (< 0,60) debe estar pegado a un filo: al borde de la losa (± 0,7 m
            // contando el voladizo) o a un filo de columna (± 0,7 m). Nunca suelto en medio de una fila libre.
            foreach (BoardPiece b in layout.Boards.Where(p => p.Width < 0.595))
            {
                double left = b.Rect.X;
                double right = b.Rect.Right;
                bool nearSlabEdge = left <= minX - 0.20 + 0.70 || right >= maxX + 0.20 - 0.70;
                bool nearColumn = columns.Any(c =>
                    System.Math.Min(System.Math.Abs(left - c.MaxX), System.Math.Abs(right - c.MinX)) < 0.70
                    || System.Math.Min(System.Math.Abs(right - c.MaxX), System.Math.Abs(left - c.MinX)) < 0.70);
                Assert.True(nearSlabEdge || nearColumn,
                    $"tablero pequeño suelto en x=[{left:0.00},{right:0.00}] ancho {b.Width:0.00}");
            }
        }
    }
}
