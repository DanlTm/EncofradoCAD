using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;
using EncofradoCAD.Core.Layout;
using Xunit;

namespace EncofradoCAD.Tests
{
    /// <summary>
    /// Pruebas de COBERTURA del motor de layout sobre muchas formas de losa (rectas, cóncavas L/U/T,
    /// escalón, cruz, protuberancia, triángulo, paralelogramo inclinado, medialuna, elipse).
    ///
    /// Idea: para cada forma se calcula el layout y se muestrea el interior de la losa; cada punto de losa
    /// (fuera de columnas) debe quedar cubierto por un tablero (o, en bordes irregulares, por un remate).
    /// Estas pruebas cierran los casos de "casas especiales" que dejaban franjas sin tablero/riel/separador.
    ///
    /// Los umbrales se calibraron con una simulación fiel del mismo algoritmo (ver CASOS_ESPECIALES_TABLEROS.md).
    /// Guardan además contra la regresión de las CURVAS (que una versión previa dejaba en 0 tableros).
    /// </summary>
    public class LayoutCoverageTests
    {
        private static List<Point> Poly(params (double x, double y)[] pts)
            => pts.Select(p => new Point(p.x, p.y)).ToList();

        private static PreviewLayout Build(List<Point> slab, List<ColumnSnapshot>? columns = null)
        {
            columns ??= new List<ColumnSnapshot>();
            double minX = slab.Min(p => p.X), maxX = slab.Max(p => p.X);
            double minY = slab.Min(p => p.Y), maxY = slab.Max(p => p.Y);
            return LayoutEngine.BuildLayout(slab, columns, minX, maxX, minY, maxY);
        }

        private static (int total, int board, int any) Coverage(
            List<Point> slab, List<ColumnSnapshot> columns, PreviewLayout layout, double step = 0.12)
        {
            double minX = slab.Min(p => p.X), maxX = slab.Max(p => p.X);
            double minY = slab.Min(p => p.Y), maxY = slab.Max(p => p.Y);
            int total = 0, board = 0, any = 0;
            for (double y = minY + step / 2; y < maxY; y += step)
            {
                for (double x = minX + step / 2; x < maxX; x += step)
                {
                    var pt = new Point(x, y);
                    if (!GeometryUtil.IsPointInsidePolygon(pt, slab))
                    {
                        continue;
                    }

                    if (columns.Any(c => c.Contains(pt, 0.0)))
                    {
                        continue;
                    }

                    total++;
                    bool onBoard = layout.Boards.Any(b => b.Rect.Contains(pt));
                    bool onRemate = layout.Remates.Any(r => r.Contains(pt));
                    bool onTabla = layout.Tablas.Any(t => t.Contains(pt))
                        || layout.BorderTablas.Any(t => t.Contains(pt));
                    if (onBoard)
                    {
                        board++;
                    }

                    if (onBoard || onRemate || onTabla)
                    {
                        any++;
                    }
                }
            }

            return (total, board, any);
        }

        private static double BoardPct(List<Point> slab, PreviewLayout l, List<ColumnSnapshot>? cols = null)
        {
            cols ??= new List<ColumnSnapshot>();
            var (total, board, _) = Coverage(slab, cols, l);
            return total == 0 ? 100.0 : 100.0 * board / total;
        }

        private static double AnyPct(List<Point> slab, PreviewLayout l, List<ColumnSnapshot>? cols = null)
        {
            cols ??= new List<ColumnSnapshot>();
            var (total, _, any) = Coverage(slab, cols, l);
            return total == 0 ? 100.0 : 100.0 * any / total;
        }

        // ── Formas ────────────────────────────────────────────────────────────
        private static List<Point> Rectangle() => Poly((0, 0), (8, 0), (8, 6), (0, 6));
        private static List<Point> LShape() => Poly((0, 0), (8, 0), (8, 4), (4, 4), (4, 8), (0, 8));
        private static List<Point> UShape() => Poly((0, 0), (10, 0), (10, 8), (7, 8), (7, 3), (3, 3), (3, 8), (0, 8));
        private static List<Point> TShape() => Poly((0, 0), (12, 0), (12, 3), (8, 3), (8, 9), (4, 9), (4, 3), (0, 3));
        private static List<Point> Staircase() => Poly((0, 0), (12, 0), (12, 2), (9, 2), (9, 4), (6, 4), (6, 6), (3, 6), (3, 8), (0, 8));
        private static List<Point> Cross() => Poly((4, 0), (8, 0), (8, 4), (12, 4), (12, 8), (8, 8), (8, 12), (4, 12), (4, 8), (0, 8), (0, 4), (4, 4));
        private static List<Point> Triangle() => Poly((0, 0), (10, 0), (5, 8));
        private static List<Point> Parallelogram() => Poly((0, 0), (8, 0), (11, 6), (3, 6));
        private static List<Point> Notch() => Poly((0, 0), (10, 0), (10, 8), (0, 8), (0, 5), (4, 5), (4, 3), (0, 3));

        private static List<Point> Semicircle(double cx = 6, double cy = 0, double r = 6, int n = 48)
        {
            var pts = new List<Point>();
            for (int i = 0; i <= n; i++)
            {
                double a = Math.PI * i / n;
                pts.Add(new Point(cx + (r * Math.Cos(a)), cy + (r * Math.Sin(a))));
            }

            return pts;
        }

        private static List<Point> Ellipse(double cx = 0, double cy = 0, double rx = 7, double ry = 4, int n = 64)
        {
            var pts = new List<Point>();
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n;
                pts.Add(new Point(cx + (rx * Math.Cos(a)), cy + (ry * Math.Sin(a))));
            }

            return pts;
        }

        // ── Rectilíneas: deben quedar casi 100% cubiertas por tablero ──────────
        public static IEnumerable<object[]> RectilinearShapes()
        {
            yield return new object[] { "Rectangulo", Rectangle(), 99.0 };
            yield return new object[] { "L", LShape(), 98.0 };
            yield return new object[] { "U", UShape(), 92.0 };
            yield return new object[] { "T", TShape(), 93.0 };
            yield return new object[] { "Escalera", Staircase(), 92.0 };
            yield return new object[] { "Cruz", Cross(), 95.0 };
            yield return new object[] { "Notch", Notch(), 90.0 };
        }

        [Theory]
        [MemberData(nameof(RectilinearShapes))]
        public void Rectilinear_Shapes_AreCoveredByBoards(string name, List<Point> slab, double minBoardPct)
        {
            var layout = Build(slab);
            double pct = BoardPct(slab, layout);
            Assert.True(pct >= minBoardPct, $"{name}: cobertura de tablero {pct:0.0}% < {minBoardPct}%");
            Assert.True(layout.Boards.Count > 0, $"{name}: no se generó ningún tablero");
        }

        // ── Inclinadas: se preservan (baseline razonable) ──────────────────────
        [Theory]
        [InlineData("Triangulo")]
        [InlineData("Paralelogramo")]
        public void Inclined_Shapes_ArePreserved(string name)
        {
            List<Point> slab = name == "Triangulo" ? Triangle() : Parallelogram();
            var layout = Build(slab);
            double pct = BoardPct(slab, layout);
            Assert.True(pct >= 88.0, $"{name}: cobertura de tablero {pct:0.0}% < 88%");
        }

        // ── CURVAS: guarda contra la regresión que las dejaba en 0 tableros ────
        [Theory]
        [InlineData("Medialuna")]
        [InlineData("Elipse")]
        public void Curved_Shapes_AreNotShattered(string name)
        {
            List<Point> slab = name == "Medialuna" ? Semicircle() : Ellipse();
            var layout = Build(slab);
            double pct = BoardPct(slab, layout);
            Assert.True(layout.Boards.Count > 30, $"{name}: solo {layout.Boards.Count} tableros (¿rejilla fragmentada?)");
            Assert.True(pct >= 88.0, $"{name}: cobertura de tablero {pct:0.0}% < 88%");
        }

        // ── Ninguna forma debe dejar 'huecos reales' (ni tablero ni remate) grandes ──
        [Fact]
        public void NoShape_LeavesLargeUncoveredArea()
        {
            var shapes = new (string, List<Point>)[]
            {
                ("Rectangulo", Rectangle()), ("L", LShape()), ("U", UShape()), ("T", TShape()),
                ("Escalera", Staircase()), ("Cruz", Cross()), ("Notch", Notch()),
                ("Triangulo", Triangle()), ("Paralelogramo", Parallelogram()),
                ("Medialuna", Semicircle()), ("Elipse", Ellipse()),
            };

            foreach (var (name, slab) in shapes)
            {
                var layout = Build(slab);
                double any = AnyPct(slab, layout);
                Assert.True(any >= 92.0, $"{name}: {100 - any:0.0}% de la losa sin tablero NI remate");
            }
        }

        // ── Cóncava con columna: el caso que estaba muy roto (57% -> ~94%) ─────
        [Fact]
        public void LShape_WithColumn_IsCovered()
        {
            var slab = LShape();
            var cols = new List<ColumnSnapshot> { new ColumnSnapshot(2, 2, 0.4, 0.4) };
            var layout = Build(slab, cols);
            double pct = BoardPct(slab, layout, cols);
            Assert.True(pct >= 88.0, $"L+columna: cobertura de tablero {pct:0.0}% < 88%");
        }

        /// <summary>
        /// % del ANILLO de voladizo (fuera de la losa, hasta 19 cm del borde) cubierto por TABLERO. Mide que
        /// el tablero de orilla se extienda hacia afuera hasta el filo del riel, no que se corte al ras del borde.
        /// </summary>
        private static double OverhangBoardPct(List<Point> slab, PreviewLayout layout, double step = 0.06, double band = 0.19)
        {
            double minX = slab.Min(p => p.X), maxX = slab.Max(p => p.X);
            double minY = slab.Min(p => p.Y), maxY = slab.Max(p => p.Y);
            int total = 0, covered = 0;
            for (double y = minY - band - 0.05; y < maxY + band + 0.05; y += step)
            {
                for (double x = minX - band - 0.05; x < maxX + band + 0.05; x += step)
                {
                    var pt = new Point(x, y);
                    if (GeometryUtil.IsPointInsidePolygon(pt, slab))
                    {
                        continue;
                    }

                    double d = GeometryUtil.DistanceToPolygon(pt, slab);
                    if (d <= 0 || d > band)
                    {
                        continue;
                    }

                    total++;
                    if (layout.Boards.Any(b => b.Rect.Contains(pt)))
                    {
                        covered++;
                    }
                }
            }

            return total == 0 ? 100.0 : 100.0 * covered / total;
        }

        // ── El tablero de orilla debe llegar al filo del riel (voladizo de 20 cm), no cortarse al ras ──
        [Fact]
        public void EdgeBoards_ReachRailFilo_OnStraightBorders()
        {
            var slab = Rectangle();
            var layout = Build(slab);
            double over = OverhangBoardPct(slab, layout);
            Assert.True(over >= 95.0, $"Voladizo del rectángulo solo {over:0.0}% cubierto por tablero (debería llegar al filo del riel).");
        }

        // ── EFICIENCIA DE MATERIAL: el rectángulo 15,20×8,00 usa 7 separadores de 1,20 (sin 0,80), y el
        //    verificador no encuentra puntales redundantes ──
        [Fact]
        public void Rectangle_UsesMinimalMaterial_NoRedundantSupports()
        {
            var slab = Poly((0, 0), (15.20, 0), (15.20, 8.00), (0, 8.00));
            var layout = Build(slab);

            bool primaryH = DirectionSolver.DeterminePrimaryHorizontal(slab, 0, 15.20, 0, 8.00);
            var issues = LayoutValidator.Validate(layout, slab, primaryH, new List<ColumnSnapshot>());
            Assert.DoesNotContain(issues, i => i.Contains("redundante"));

            // 8,00 + 0,20 × 2 = 8,40 = 7 × 1,20 exacto → todos los separadores deben ser de 1,20 m.
            var seps = layout.VerticalSeparators.Concat(layout.HorizontalSeparators).ToList();
            Assert.NotEmpty(seps);
            Assert.All(seps, s => Assert.True(Math.Abs(s.NominalLength - 1.20) < 0.03,
                $"Separador de {s.NominalLength:0.00} m (se esperaba 1,20)"));
        }

        // ── El verificador debe DETECTAR una losa a la que (a propósito) le faltan tableros ──
        [Fact]
        public void Validator_ReportsBoardCoverageGaps_WhenBoardsMissing()
        {
            var slab = Rectangle();
            var layout = Build(slab);
            // Layout vacío de tableros -> el verificador debe quejarse de la cobertura.
            var empty = layout with { Boards = new List<BoardPiece>(), Remates = new List<Rect>() };
            var issues = LayoutValidator.Validate(empty, slab, true, new List<ColumnSnapshot>());
            Assert.Contains(issues, i => i.Contains("Cobertura de tableros"));
        }
    }
}
