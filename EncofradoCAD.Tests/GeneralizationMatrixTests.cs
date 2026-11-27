using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;
using EncofradoCAD.Core.Layout;
using Xunit;

namespace EncofradoCAD.Tests
{
    /// <summary>
    /// PRUEBAS DE GENERALIZACIÓN (QA). Recorren una MATRIZ de todo el catálogo de columnas
    /// (0,20 · 0,25 · 0,30 · 0,35 · 0,40 · 0,45 m, cuadradas y rectangulares) contra muchas medidas de losa,
    /// y verifican que el motor —la MISMA fuente del 2D y el 3D— produce un modelo correcto en TODOS los
    /// casos: sin excepciones, sin huecos negros, con tableros y tablas de catálogo, y con la tabla puesta
    /// junto a cada columna. Así se blinda contra errores futuros al cambiar medidas.
    /// </summary>
    public class GeneralizationMatrixTests
    {
        private static readonly double[] ColumnSizes = { 0.20, 0.25, 0.30, 0.35, 0.40, 0.45 };
        private static readonly (double w, double h)[] SlabSizes =
        {
            (8.0, 6.0), (11.74, 4.99), (12.60, 9.93), (15.20, 8.00), (18.0, 10.0), (20.0, 12.0),
        };

        private static readonly double[] CatalogWidths = { 0.30, 0.35, 0.40, 0.45, 0.50, 0.55, 0.60 };
        private static readonly double[] CatalogLengths = { 0.85, 0.90, 1.00, 1.10, 1.20 };

        public static IEnumerable<object[]> SquareMatrix()
        {
            foreach (double cs in ColumnSizes)
            {
                foreach ((double w, double h) in SlabSizes)
                {
                    yield return new object[] { cs, cs, w, h };
                }
            }
        }

        public static IEnumerable<object[]> RectangularMatrix()
        {
            foreach ((double cw, double ch) in new[] { (0.20, 0.45), (0.45, 0.20), (0.25, 0.40), (0.30, 0.45) })
            {
                foreach ((double w, double h) in new[] { (12.60, 9.93), (15.20, 8.00), (18.0, 10.0) })
                {
                    yield return new object[] { cw, ch, w, h };
                }
            }
        }

        private static List<ColumnSnapshot> GridColumns(double w, double h, double cw, double ch)
        {
            var cols = new List<ColumnSnapshot>();
            for (double x = 1.2; x < w - 0.6; x += 3.0)
            {
                for (double y = 1.0; y < h - 0.6; y += 3.0)
                {
                    cols.Add(new ColumnSnapshot(x, y, cw, ch));
                }
            }

            return cols;
        }

        private static PreviewLayout Build(double w, double h, List<ColumnSnapshot> cols)
        {
            var slab = new List<Point>
            {
                new(0, 0), new(w, 0), new(w, h), new(0, h),
            };
            return LayoutEngine.BuildLayout(slab, cols, 0, w, 0, h);
        }

        [Theory]
        [MemberData(nameof(SquareMatrix))]
        [MemberData(nameof(RectangularMatrix))]
        public void EveryCatalogCase_ProducesValidModel(double cw, double ch, double w, double h)
        {
            var cols = GridColumns(w, h, cw, ch);
            PreviewLayout layout = Build(w, h, cols);
            string id = $"col {cw:0.00}x{ch:0.00} losa {w:0.0}x{h:0.0}";

            // 1) Se generan tableros.
            Assert.True(layout.Boards.Count > 0, $"{id}: sin tableros");

            // 2) Todo tablero usa anchos y largos de CATÁLOGO (± 3 cm).
            foreach (BoardPiece b in layout.Boards)
            {
                Assert.True(CatalogWidths.Any(cwid => Math.Abs(b.Width - cwid) < 0.03),
                    $"{id}: ancho fuera de catálogo {b.Width:0.00}");
                Assert.True(CatalogLengths.Any(cl => Math.Abs(b.Length - cl) < 0.03),
                    $"{id}: largo fuera de catálogo {b.Length:0.00}");
            }

            // 3) Ningún tablero cae CENTRADO dentro de una columna (los tableros la rodean).
            foreach (BoardPiece b in layout.Boards)
            {
                var c = new Point(b.Rect.X + (b.Rect.Width / 2), b.Rect.Y + (b.Rect.Height / 2));
                Assert.DoesNotContain(cols, col => col.Contains(c, -0.03));
            }

            // 4) Cada columna tiene TABLA (naranja) en su banda inmediata.
            foreach (ColumnSnapshot col in cols)
            {
                Assert.Contains(layout.Tablas, t =>
                {
                    var tc = new Point(t.X + (t.Width / 2), t.Y + (t.Height / 2));
                    return tc.X >= col.MinX - 0.7 && tc.X <= col.MaxX + 0.7
                        && tc.Y >= col.MinY - 1.3 && tc.Y <= col.MaxY + 1.3;
                });
            }

            // 5) COBERTURA: el interior de la losa (fuera de columnas) queda cubierto por tablero, tabla,
            //    tabla de borde o remate; ningún hueco negro (≥ 98 %).
            int total = 0, uncovered = 0;
            for (double y = 0.15; y < h; y += 0.25)
            {
                for (double x = 0.15; x < w; x += 0.25)
                {
                    var pt = new Point(x, y);
                    if (cols.Any(c => c.Contains(pt, 0.0)))
                    {
                        continue;
                    }

                    total++;
                    bool cov = layout.Boards.Any(b => b.Rect.Contains(pt))
                        || layout.Tablas.Any(t => t.Contains(pt))
                        || layout.BorderTablas.Any(t => t.Contains(pt))
                        || layout.Remates.Any(r => r.Contains(pt));
                    if (!cov)
                    {
                        uncovered++;
                    }
                }
            }

            Assert.True(uncovered <= total * 0.02, $"{id}: huecos negros {uncovered}/{total}");
        }

        [Theory]
        [MemberData(nameof(SquareMatrix))]
        public void SmallBoards_SitOnlyAtEdgeOrColumn(double cw, double ch, double w, double h)
        {
            var cols = GridColumns(w, h, cw, ch);
            PreviewLayout layout = Build(w, h, cols);

            foreach (BoardPiece b in layout.Boards.Where(p => p.Width < 0.595))
            {
                double left = b.Rect.X, right = b.Rect.Right;
                bool nearSlabEdge = left <= -0.20 + 0.75 || right >= w + 0.20 - 0.75;
                bool nearColumn = cols.Any(c =>
                    Math.Abs(left - c.MaxX) < 0.75 || Math.Abs(right - c.MinX) < 0.75
                    || Math.Abs(right - c.MaxX) < 0.75 || Math.Abs(left - c.MinX) < 0.75);
                Assert.True(nearSlabEdge || nearColumn,
                    $"col {cw:0.00} losa {w:0.0}x{h:0.0}: tablero pequeño {b.Width:0.00} suelto en x=[{left:0.00},{right:0.00}]");
            }
        }

        [Fact]
        public void NoColumns_FieldIsFullyTiled_NoTabla()
        {
            PreviewLayout layout = Build(12.60, 9.93, new List<ColumnSnapshot>());
            Assert.True(layout.Boards.Count > 0);
            Assert.Empty(layout.Tablas);

            // Sin columnas, el campo se cubre casi todo con tableros de 0,60 (estándar).
            int wide = layout.Boards.Count(b => b.Width >= 0.595);
            Assert.True(wide >= layout.Boards.Count * 0.6,
                $"pocos 0,60 en losa libre: {wide}/{layout.Boards.Count}");
        }
    }
}
