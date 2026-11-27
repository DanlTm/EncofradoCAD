using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;
using EncofradoCAD.Core.Layout;
using Xunit;

namespace EncofradoCAD.Tests
{
    /// <summary>
    /// Pruebas del caso reportado por el usuario: con columnas cerca de las uniones de rieles, el sistema
    /// NO debe partir un tramo de 3,0 m en piezas fuera de catálogo (p. ej. 2,00 + 1,60) ni añadir una fila
    /// de puntales extra en el borde. Todo riel debe medir una medida REAL del catálogo, priorizando 3,0 m y
    /// cerrando con una sola pieza de catálogo, y TODAS las filas deben quedar iguales.
    /// </summary>
    public class RailColumnClearanceTests
    {
        private static readonly double[] Catalog = LayoutCatalog.RailLengths;

        private static bool EnCatalogo(double largo) =>
            Catalog.Any(c => Math.Abs(c - largo) < 0.05);

        private static List<Point> Rect(double L, double W) => new()
        {
            new Point(0, 0), new Point(L, 0), new Point(L, W), new Point(0, W),
        };

        private static List<ColumnSnapshot> Grid(double size, double[] cx, double[] cy)
        {
            var cols = new List<ColumnSnapshot>();
            foreach (double x in cx)
            {
                foreach (double y in cy)
                {
                    cols.Add(new ColumnSnapshot(x, y, size, size));
                }
            }

            return cols;
        }

        private static List<LinearPiece> Rails(PreviewLayout layout) =>
            layout.HorizontalRails.Count > 0 ? layout.HorizontalRails : layout.VerticalRails;

        [Fact]
        public void ColumnasCercaDeUniones_NoParteRielesFueraDeCatalogo()
        {
            // Losa 15,77 × 4,03 con columnas de 0,40 m casi sobre las uniones de 3,0 m (el caso de las imágenes).
            var slab = Rect(15.77, 4.03);
            var cols = Grid(0.40, new[] { 2.6, 5.6, 8.6, 11.6, 14.6 }, new[] { 1.0, 3.0 });

            PreviewLayout layout = LayoutEngine.BuildLayout(slab, cols, 0, 15.77, 0, 4.03);
            List<LinearPiece> rails = Rails(layout);

            Assert.NotEmpty(rails);
            Assert.All(rails, r => Assert.True(EnCatalogo(r.ActualLength),
                $"riel de {r.ActualLength:0.###} m no está en el catálogo"));
        }

        [Fact]
        public void ColumnasCercaDeUniones_TodasLasFilasIguales_YMayoria3m()
        {
            var slab = Rect(15.77, 4.03);
            var cols = Grid(0.40, new[] { 2.6, 5.6, 8.6, 11.6, 14.6 }, new[] { 1.0, 3.0 });

            PreviewLayout layout = LayoutEngine.BuildLayout(slab, cols, 0, 15.77, 0, 4.03);
            List<LinearPiece> rails = Rails(layout);

            // Todas las filas de rieles deben tener el MISMO reparto (sin filas fragmentadas distintas).
            List<string> patrones = rails
                .GroupBy(p => Math.Round(p.FixedCoord, 3))
                .Select(g => string.Join("·", g.OrderBy(p => Math.Min(p.Start, p.End))
                    .Select(p => p.ActualLength.ToString("0.00"))))
                .Distinct()
                .ToList();
            Assert.Single(patrones);

            // Debe priorizar 3,0 m: al menos cinco tramos de 3,0 m por fila.
            int tresMetros = rails
                .Where(r => Math.Abs(r.ActualLength - 3.0) < 0.05)
                .GroupBy(r => Math.Round(r.FixedCoord, 3))
                .First().Count();
            Assert.True(tresMetros >= 5, $"se esperaban ≥5 rieles de 3,0 m por fila, hubo {tresMetros}");
        }

        [Fact]
        public void ColumnasCercaDeUniones_ValidadorSinProblemasDeRiel()
        {
            var slab = Rect(15.77, 4.03);
            var cols = Grid(0.40, new[] { 2.6, 5.6, 8.6, 11.6, 14.6 }, new[] { 1.0, 3.0 });
            bool primaryH = DirectionSolver.DeterminePrimaryHorizontal(slab, 0, 15.77, 0, 4.03);

            PreviewLayout layout = LayoutEngine.BuildLayout(slab, cols, 0, 15.77, 0, 4.03);
            List<string> issues = LayoutValidator.Validate(layout, slab, primaryH, cols);

            Assert.DoesNotContain(issues, i => i.Contains("fuera de catálogo"));
            Assert.DoesNotContain(issues, i => i.Contains("Riel demasiado largo"));
        }

        [Fact]
        public void SinColumnas_SigueDando5x3mMas1x15m()
        {
            // Red de no-regresión: sin columnas, el reparto ideal se mantiene.
            var slab = Rect(15.77, 4.03);
            PreviewLayout layout = LayoutEngine.BuildLayout(slab, new List<ColumnSnapshot>(), 0, 15.77, 0, 4.03);
            List<LinearPiece> rails = Rails(layout);

            List<double> fila = rails
                .Where(r => Math.Abs(r.FixedCoord - rails.Min(x => x.FixedCoord)) < 0.01)
                .OrderBy(r => r.Start)
                .Select(r => r.ActualLength)
                .ToList();

            Assert.Equal(5, fila.Count(l => Math.Abs(l - 3.0) < 0.03));
            Assert.Contains(fila, l => Math.Abs(l - 1.5) < 0.03);
            Assert.All(rails, r => Assert.True(EnCatalogo(r.ActualLength)));
        }

        [Theory]
        [InlineData(15.77, 4.03, 0.40, new[] { 2.6, 5.6, 8.6, 11.6, 14.6 })]   // columnas ~sobre las uniones de 3 m
        [InlineData(15.77, 4.03, 0.40, new[] { 1.4, 5.5, 9.6, 13.7 })]         // separación ~4,1 m (antes fragmentaba)
        [InlineData(11.00, 4.00, 0.30, new[] { 1.4, 5.5, 9.6 })]               // antes daba 1,53 + 1,53
        [InlineData(15.77, 4.03, 0.40, new[] { 2.5, 5.0, 7.5, 10.0, 12.5, 15.0 })] // densas a 2,5 m
        public void ColumnasEnCualquierPosicion_TodosLosRielesSonCatalogo(double L, double W, double colSize, double[] cx)
        {
            var slab = Rect(L, W);
            var cols = Grid(colSize, cx, new[] { W * 0.25, W * 0.75 });

            PreviewLayout layout = LayoutEngine.BuildLayout(slab, cols, 0, L, 0, W);
            List<LinearPiece> rails = Rails(layout);

            Assert.NotEmpty(rails);
            Assert.All(rails, r => Assert.True(EnCatalogo(r.ActualLength),
                $"riel de {r.ActualLength:0.###} m no está en catálogo (L={L}, cols={cx.Length * 2})"));

            // Todas las filas idénticas: ninguna fila fragmentada distinta de las demás.
            int patrones = rails
                .GroupBy(p => Math.Round(p.FixedCoord, 3))
                .Select(g => string.Join("·", g.OrderBy(p => Math.Min(p.Start, p.End)).Select(p => p.ActualLength.ToString("0.00"))))
                .Distinct()
                .Count();
            Assert.Equal(1, patrones);
        }

        [Fact]
        public void NingunaUnionCaeSobreUnaColumna()
        {
            // Losa con columnas cerca de las uniones: ninguna línea de rieles debe quedar dentro de la zona
            // de poda de una columna (semiancho + 20 cm), o el puntal se eliminaría y el riel se fragmentaría.
            double L = 15.77, W = 4.03, size = 0.40;
            double[] cx = { 1.4, 5.5, 9.6, 13.7 };
            var railCols = cx.Select(x => (x, size / 2.0)).ToList();

            List<double> lines = GridBuilder.GenerateRailLinesClearingColumns(0, L, railCols);

            double pruneRadius = (size / 2.0) + LayoutCatalog.ColumnClearance; // = 0,40 m
            foreach (double line in lines)
            {
                foreach (double c in cx)
                {
                    Assert.True(Math.Abs(line - c) >= pruneRadius,
                        $"unión en {line:0.##} demasiado cerca de la columna en {c:0.##}");
                }
            }
        }

        [Fact]
        public void CasoImagenes_861x799_NoFragmentaRielesDe3m()
        {
            // Caso reportado (imágenes): losa 8,61 × 7,99 m. El RailSolver cierra con 3×3,0 m y voladizo de
            // 19,5 cm/lado; un muñón de 1 cm partía el 3er riel en 1,50 + 1,50 y añadía una fila de puntales.
            // Tras el fix, cada fila debe ser exactamente 3,0 · 3,0 · 3,0 (sin rieles de 1,5 m ni fila extra).
            var slab = Rect(8.61, 7.99);
            double[] cx = { 0.80, 4.305, 7.81 };
            double[] cy = { 0.80, 3.995, 7.19 };
            var cols = new List<ColumnSnapshot>();
            foreach (double y in cy)
            {
                foreach (double x in cx)
                {
                    bool central = Math.Abs(x - 4.305) < 0.01 && Math.Abs(y - 3.995) < 0.01;
                    bool superior = Math.Abs(y - 7.19) < 0.01;
                    cols.Add(central || superior
                        ? new ColumnSnapshot(x, y, 0.20, 0.40)
                        : new ColumnSnapshot(x, y, 0.30, 0.30));
                }
            }

            PreviewLayout layout = LayoutEngine.BuildLayout(slab, cols, 0, 8.61, 0, 7.99);
            List<LinearPiece> rails = Rails(layout);

            Assert.NotEmpty(rails);
            // Ningún riel de 1,5 m (la fragmentación evitable).
            Assert.DoesNotContain(rails, r => Math.Abs(r.ActualLength - 1.5) < 0.06);
            // Cada fila = 3 piezas de 3,0 m.
            foreach (IGrouping<double, LinearPiece> fila in rails.GroupBy(r => Math.Round(r.FixedCoord, 3)))
            {
                List<LinearPiece> f = fila.ToList();
                Assert.Equal(3, f.Count);
                Assert.All(f, r => Assert.True(Math.Abs(r.ActualLength - 3.0) < 0.06,
                    $"riel de {r.ActualLength:0.###} m (se esperaba 3,0 m) en y={fila.Key:0.##}"));
            }
        }

        [Fact]
        public void CasoImagenes_861x799_NingunaFilaDeRielEncimaDeColumna()
        {
            // Problema 2: ninguna FILA de riel puede quedar sobre una columna (el tablero va al ras y el riel
            // corre ~10 cm por debajo → chocaría). Debe respetarse ≥ 5 cm de holgura filo-columna → filo-riel
            // en TODAS las filas contra TODAS las columnas, usando solo separadores de catálogo.
            var slab = Rect(8.61, 7.99);
            double[] cx = { 0.80, 4.305, 7.81 };
            double[] cy = { 0.80, 3.995, 7.19 };
            var cols = new List<ColumnSnapshot>();
            foreach (double y in cy)
            {
                foreach (double x in cx)
                {
                    bool central = Math.Abs(x - 4.305) < 0.01 && Math.Abs(y - 3.995) < 0.01;
                    bool superior = Math.Abs(y - 7.19) < 0.01;
                    cols.Add(central || superior
                        ? new ColumnSnapshot(x, y, 0.20, 0.40)
                        : new ColumnSnapshot(x, y, 0.30, 0.30));
                }
            }

            PreviewLayout layout = LayoutEngine.BuildLayout(slab, cols, 0, 8.61, 0, 7.99);
            List<LinearPiece> rails = Rails(layout);
            bool rielHoriz = layout.HorizontalRails.Count > 0;

            const double railHalf = ColumnClearance.RailHalfWidth;
            const double minClear = 0.05;
            List<double> filas = rails.Select(r => r.FixedCoord).Distinct().ToList();

            foreach (ColumnSnapshot c in cols)
            {
                double centro = rielHoriz ? c.Y : c.X;
                double semi = (rielHoriz ? c.Length : c.Width) / 2.0;
                double distMin = filas.Min(f => Math.Abs(f - centro));
                double holgura = distMin - semi - railHalf;
                Assert.True(holgura >= minClear - 1e-6,
                    $"columna ({c.X:0.00},{c.Y:0.00}) con fila de riel a holgura {holgura * 100:0} cm (< 5 cm)");
            }

            // Y las bandas de separador deben seguir siendo TODAS de catálogo.
            double[] sepCat = LayoutCatalog.SeparatorLengths;
            List<LinearPiece> seps = layout.VerticalSeparators.Count > 0 ? layout.VerticalSeparators : layout.HorizontalSeparators;
            Assert.All(seps, s => Assert.True(sepCat.Any(v => Math.Abs(v - s.ActualLength) < 0.06),
                $"separador de {s.ActualLength:0.###} m fuera de catálogo"));
        }

        [Theory]
        // Generalización: distintas losas y rejillas de columnas. En TODAS, ninguna fila de riel puede quedar
        // sobre una columna (holgura filo-filo ≥ 5 cm) y todos los separadores deben ser de catálogo.
        [InlineData(8.61, 7.99, new[] { 0.80, 4.305, 7.81 }, new[] { 0.80, 3.995, 7.19 }, 0.30)] // caso imágenes
        [InlineData(8.61, 7.99, new[] { 0.80, 4.305, 7.81 }, new[] { 0.80, 3.995, 7.19 }, 0.40)] // columnas 40 cm
        [InlineData(6.05, 6.05, new[] { 0.90, 3.00, 5.10 }, new[] { 0.90, 3.00, 5.10 }, 0.30)]   // simétrica 3×3
        [InlineData(7.00, 7.00, new[] { 0.70, 3.50, 6.30 }, new[] { 0.70, 3.50, 6.30 }, 0.30)]   // separación 2,8
        [InlineData(6.00, 6.00, new[] { 0.35, 3.00, 5.65 }, new[] { 0.35, 3.00, 5.65 }, 0.30)]   // pegadas al borde
        [InlineData(5.00, 5.00, new[] { 2.50 }, new[] { 2.50 }, 0.40)]                            // una sola columna
        [InlineData(9.00, 9.00, new[] { 1.0, 3.0, 5.0, 7.0, 8.0 }, new[] { 1.0, 3.0, 5.0 }, 0.30)] // muchas/juntas
        public void Generaliza_NingunaFilaDeRielSobreColumna(double L, double W, double[] cx, double[] cy, double size)
        {
            var slab = Rect(L, W);
            var cols = Grid(size, cx, cy);
            PreviewLayout layout = LayoutEngine.BuildLayout(slab, cols, 0, L, 0, W);

            List<LinearPiece> rails = Rails(layout);
            Assert.NotEmpty(rails);
            bool rielHoriz = layout.HorizontalRails.Count > 0;
            List<double> filas = rails.Select(r => r.FixedCoord).Distinct().ToList();

            const double railHalf = ColumnClearance.RailHalfWidth;
            const double minClear = 0.05;
            foreach (ColumnSnapshot c in cols)
            {
                double centro = rielHoriz ? c.Y : c.X;
                double semi = (rielHoriz ? c.Length : c.Width) / 2.0;
                double holgura = filas.Min(f => Math.Abs(f - centro)) - semi - railHalf;
                Assert.True(holgura >= minClear - 1e-6,
                    $"L={L} W={W} size={size}: columna en ({c.X:0.00},{c.Y:0.00}) con fila de riel a holgura {holgura * 100:0} cm (< 5 cm)");
            }

            // Separadores y rieles: siempre de catálogo.
            double[] sepCat = LayoutCatalog.SeparatorLengths;
            List<LinearPiece> seps = layout.VerticalSeparators.Count > 0 ? layout.VerticalSeparators : layout.HorizontalSeparators;
            Assert.All(seps, s => Assert.True(sepCat.Any(v => Math.Abs(v - s.ActualLength) < 0.06),
                $"separador {s.ActualLength:0.###} m fuera de catálogo (L={L},W={W})"));
            Assert.All(rails, r => Assert.True(EnCatalogo(r.ActualLength),
                $"riel {r.ActualLength:0.###} m fuera de catálogo (L={L},W={W})"));
        }

        [Fact]
        public void CasoImagenes_VoladizoPorLado_EsVariableYSano()
        {
            // El voladizo ahora es variable por lado (5–20 cm). ComputeOverhangs debe reportarlo para que el
            // módulo 3D lo muestre. Para este caso, algún lado (eje de separadores) baja de 20 cm.
            var slab = Rect(8.61, 7.99);
            double[] cx = { 0.80, 4.305, 7.81 };
            double[] cy = { 0.80, 3.995, 7.19 };
            var cols = new List<ColumnSnapshot>();
            foreach (double y in cy)
            {
                foreach (double x in cx)
                {
                    bool central = Math.Abs(x - 4.305) < 0.01 && Math.Abs(y - 3.995) < 0.01;
                    bool superior = Math.Abs(y - 7.19) < 0.01;
                    cols.Add(central || superior
                        ? new ColumnSnapshot(x, y, 0.20, 0.40)
                        : new ColumnSnapshot(x, y, 0.30, 0.30));
                }
            }

            PreviewLayout layout = LayoutEngine.BuildLayout(slab, cols, 0, 8.61, 0, 7.99);
            LayoutEngine.Voladizos v = LayoutEngine.ComputeOverhangs(layout, 0, 8.61, 0, 7.99);

            // Todos los lados: valores sanos (no negativos, no absurdos).
            foreach (double lado in new[] { v.Izquierda, v.Derecha, v.Inferior, v.Superior })
            {
                Assert.InRange(lado, 0.0, 0.30);
            }

            // Al menos un lado quedó por debajo de 20 cm (voladizo variable, no fijo).
            Assert.True(Math.Min(Math.Min(v.Izquierda, v.Derecha), Math.Min(v.Inferior, v.Superior)) < 0.15,
                $"se esperaba al menos un lado con voladizo < 15 cm; fue izq={v.Izquierda:0.00} der={v.Derecha:0.00} inf={v.Inferior:0.00} sup={v.Superior:0.00}");
        }

        /// <summary>
        /// Regla del usuario: en TODA fila de rieles, las piezas PEQUEÑAS (&lt;3,0 m) solo pueden estar en los
        /// FILOS (primer o último tramo); nunca en el interior. Verifica en cada fila (agrupada por su
        /// coordenada fija), ordenando por posición, que ningún riel &lt;3,0 m quede en una posición intermedia.
        /// </summary>
        private static void PequenasSoloEnFilos(List<LinearPiece> rails)
        {
            const double std = 3.0;
            const double tol = 0.06;
            foreach (IGrouping<double, LinearPiece> fila in rails.GroupBy(r => Math.Round(r.FixedCoord, 3)))
            {
                List<LinearPiece> ordenada = fila
                    .OrderBy(r => Math.Min(r.Start, r.End))
                    .ToList();
                for (int i = 1; i < ordenada.Count - 1; i++) // interiores: excluye primero y último
                {
                    double largo = ordenada[i].ActualLength;
                    Assert.True(largo >= std - tol,
                        $"riel pequeño de {largo:0.00} m en el INTERIOR (posición {i} de {ordenada.Count}) " +
                        $"en la fila {fila.Key:0.##}; las piezas <3,0 m deben ir en los filos");
                }
            }
        }

        [Fact]
        public void SinColumnas_PiezaPequenaVaEnUnFilo()
        {
            var slab = Rect(15.77, 4.03);
            PreviewLayout layout = LayoutEngine.BuildLayout(slab, new List<ColumnSnapshot>(), 0, 15.77, 0, 4.03);
            PequenasSoloEnFilos(Rails(layout));
        }

        [Fact]
        public void CasoImagenes_PiezasPequenasEnLosFilos()
        {
            var slab = Rect(15.77, 4.03);
            var cols = Grid(0.40, new[] { 2.6, 5.6, 8.6, 11.6, 14.6 }, new[] { 1.0, 3.0 });
            PreviewLayout layout = LayoutEngine.BuildLayout(slab, cols, 0, 15.77, 0, 4.03);
            PequenasSoloEnFilos(Rails(layout));
        }

        [Theory]
        [InlineData(15.77, 4.03, 0.40, new[] { 2.6, 5.6, 8.6, 11.6, 14.6 })]
        [InlineData(15.77, 4.03, 0.40, new[] { 1.4, 5.5, 9.6, 13.7 })]
        [InlineData(11.00, 4.00, 0.30, new[] { 1.4, 5.5, 9.6 })]
        [InlineData(18.30, 4.03, 0.40, new[] { 2.6, 5.6, 8.6, 11.6, 14.6 })]
        public void ConColumnas_PiezasPequenasEnLosFilos(double L, double W, double colSize, double[] cx)
        {
            var slab = Rect(L, W);
            var cols = Grid(colSize, cx, new[] { W * 0.25, W * 0.75 });
            PreviewLayout layout = LayoutEngine.BuildLayout(slab, cols, 0, L, 0, W);
            PequenasSoloEnFilos(Rails(layout));
        }

        [Fact]
        public void RailSolver_ReparteLasPequenasEnLosFilos()
        {
            // 18,30 → 6×3,0 + 1,0: la pieza de 1,0 m debe quedar en un extremo del reparto, no en el medio.
            PlanRiel plan = RailSolver.Solve(18.30);
            List<double> segs = plan.SegmentosM.ToList();
            for (int i = 1; i < segs.Count - 1; i++)
            {
                Assert.True(segs[i] >= 3.0 - 0.06,
                    $"pieza pequeña de {segs[i]:0.00} m en el interior (posición {i}); debe ir en un filo");
            }
        }

        [Fact]
        public void RailSolver_ConVoladizoMayor_CreceSoloLaPiezaDeCierre()
        {
            // Al pedir voladizo base mayor (para librar columnas), los tramos interiores siguen en 3,0 m y
            // solo crece la pieza de cierre; todo dentro del catálogo.
            PlanRiel plan = RailSolver.Solve(15.77, 0.60);
            Assert.True(plan.VoladizoPorLadoM >= 0.60 - 1e-6, "el voladizo base debe respetar el mínimo pedido");
            Assert.Equal(5, plan.SegmentosM.Count(s => Math.Abs(s - 3.0) < 0.001));
            Assert.All(plan.SegmentosM, s => Assert.Contains(Catalog, c => Math.Abs(c - s) < 0.001));
        }
    }
}
