using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;
using EncofradoCAD.Core.Layout;
using Xunit;

namespace EncofradoCAD.Tests
{
    /// <summary>
    /// Pruebas del MÓDULO DE TABLA: donde hay una columna el motor deja un hueco (columna atrapada entre sus
    /// dos rieles paralelos) que se cierra con TABLA, no con tablero de catálogo. Estas pruebas verifican que
    /// la tabla se genera junto a cada columna, que NO se dibuja encima de la columna ni encima de un tablero,
    /// y que reemplaza al remate en esas zonas (no se dibuja doble).
    /// </summary>
    public class ColumnTablaTests
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
        public void SlabWithColumn_GeneratesTabla()
        {
            var slab = Poly((0, 0), (8, 0), (8, 6), (0, 6));
            var columns = new List<ColumnSnapshot> { new(4.0, 3.0, 0.40, 0.40) };

            PreviewLayout layout = Build(slab, columns);

            Assert.NotEmpty(layout.Tablas);
        }

        [Fact]
        public void SlabWithoutColumns_HasNoTabla()
        {
            var slab = Poly((0, 0), (8, 0), (8, 6), (0, 6));

            PreviewLayout layout = Build(slab, new List<ColumnSnapshot>());

            Assert.Empty(layout.Tablas);
        }

        [Fact]
        public void Tabla_NeverOverlapsColumnFootprint()
        {
            var slab = Poly((0, 0), (8, 0), (8, 6), (0, 6));
            var columns = new List<ColumnSnapshot> { new(4.0, 3.0, 0.40, 0.40) };

            PreviewLayout layout = Build(slab, columns);

            foreach (Rect t in layout.Tablas)
            {
                var center = new Point(t.X + (t.Width / 2), t.Y + (t.Height / 2));
                Assert.DoesNotContain(columns, c => c.Contains(center, 0.0));
            }
        }

        [Fact]
        public void Tabla_NeverOverlapsExistingBoard()
        {
            var slab = Poly((0, 0), (8, 0), (8, 6), (0, 6));
            var columns = new List<ColumnSnapshot> { new(4.0, 3.0, 0.40, 0.40) };

            PreviewLayout layout = Build(slab, columns);

            foreach (Rect t in layout.Tablas)
            {
                var center = new Point(t.X + (t.Width / 2), t.Y + (t.Height / 2));
                Assert.DoesNotContain(layout.Boards, b => b.Rect.Contains(center));
            }
        }

        [Fact]
        public void Tabla_SitsAtItsColumn()
        {
            var slab = Poly((0, 0), (8, 0), (8, 6), (0, 6));
            var col = new ColumnSnapshot(4.0, 3.0, 0.40, 0.40);
            var columns = new List<ColumnSnapshot> { col };

            PreviewLayout layout = Build(slab, columns);

            // Debe existir al menos una tabla en la banda inmediata de la columna (hueco superior/inferior).
            Assert.Contains(layout.Tablas, t =>
            {
                var center = new Point(t.X + (t.Width / 2), t.Y + (t.Height / 2));
                return center.X >= col.MinX - 0.6 && center.X <= col.MaxX + 0.6
                    && center.Y >= col.MinY - 1.2 && center.Y <= col.MaxY + 1.2;
            });
        }

        [Fact]
        public void Remate_AndTabla_DoNotOverlap()
        {
            var slab = Poly((0, 0), (8, 0), (8, 6), (0, 6));
            var columns = new List<ColumnSnapshot> { new(4.0, 3.0, 0.40, 0.40) };

            PreviewLayout layout = Build(slab, columns);

            // Ningún remate debe tener su centro dentro de una tabla (se limpiaron en el motor).
            foreach (Rect r in layout.Remates)
            {
                var center = new Point(r.X + (r.Width / 2), r.Y + (r.Height / 2));
                Assert.DoesNotContain(layout.Tablas, t => t.Contains(center));
                Assert.DoesNotContain(layout.BorderTablas, t => t.Contains(center));
            }
        }

        [Fact]
        public void SlabWithColumn_LeavesNoUncoveredHoles()
        {
            var slab = Poly((0, 0), (8, 0), (8, 6), (0, 6));
            var columns = new List<ColumnSnapshot> { new(4.0, 3.0, 0.25, 0.25) };

            PreviewLayout layout = Build(slab, columns);

            // Muestrea el interior de la losa (fuera de columnas): todo punto debe quedar cubierto por
            // tablero, tabla, tabla de borde o remate; ningún hueco negro tras el pase de acabado.
            int total = 0, uncovered = 0;
            for (double y = 0.15; y < 6; y += 0.20)
            {
                for (double x = 0.15; x < 8; x += 0.20)
                {
                    var pt = new Point(x, y);
                    if (!GeometryUtil.IsPointInsidePolygon(pt, slab)) continue;
                    if (columns.Any(c => c.Contains(pt, 0.0))) continue;
                    total++;
                    bool cov = layout.Boards.Any(b => b.Rect.Contains(pt))
                        || layout.Tablas.Any(t => t.Contains(pt))
                        || layout.BorderTablas.Any(t => t.Contains(pt))
                        || layout.Remates.Any(r => r.Contains(pt));
                    if (!cov) uncovered++;
                }
            }

            Assert.True(uncovered <= total * 0.02, $"huecos sin cubrir: {uncovered}/{total}");
        }
    }
}
