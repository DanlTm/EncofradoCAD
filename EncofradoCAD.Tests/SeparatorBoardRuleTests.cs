using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;
using EncofradoCAD.Core.Layout;
using Xunit;

namespace EncofradoCAD.Tests
{
    /// <summary>
    /// Regla del usuario para separadores y su tablero:
    ///   1) El reparto sin columnas usa SOLO bandas de 1,20 y 0,60 (nada de 0,80/0,90/1,00/1,10).
    ///   2) POR ELLO, TODO tablero es 0,60×1,20 (normal en la banda de 1,20; girado en la de 0,60).
    ///   3) El tablero GIRADO (horizontal) es EXCLUSIVO de la banda de 0,60.
    /// Estas pruebas fijan esa regla para que no vuelva a cambiar. Ver <see cref="SeparatorPolicy"/>.
    /// </summary>
    public class SeparatorBoardRuleTests
    {
        private const double Run = 19.0;

        private static int SepCm(BandaTablero b) => (int)Math.Round(b.SeparadorM * 100);

        // Anchos variados, incluyendo los que ANTES producían 0,80/0,90 (7,05 · 7,50 · 4,03 · 8,39).
        [Theory]
        [InlineData(4.03)]
        [InlineData(6.05)]
        [InlineData(7.05)]
        [InlineData(7.50)]
        [InlineData(8.39)]
        [InlineData(8.60)]
        [InlineData(9.00)]
        [InlineData(9.60)]
        [InlineData(10.30)]
        [InlineData(12.15)]
        [InlineData(15.77)]
        public void SoloUsaBandasDe120Y060(double ancho)
        {
            PlanAncho plan = BandSolver.SolveWidth(ancho, Run);
            Assert.All(plan.Bandas, b =>
                Assert.True(SepCm(b) == 120 || SepCm(b) == 60,
                    $"banda de {b.SeparadorM:0.00} m (ancho {ancho}); solo se permiten 1,20 y 0,60"));
        }

        [Theory]
        [InlineData(4.03)]
        [InlineData(7.05)]
        [InlineData(7.50)]
        [InlineData(8.39)]
        [InlineData(8.60)]
        [InlineData(9.60)]
        [InlineData(12.15)]
        public void TodoTableroEs060x120(double ancho)
        {
            PlanAncho plan = BandSolver.SolveWidth(ancho, Run);
            Assert.All(plan.Bandas, b =>
            {
                Assert.Equal(0.60, b.AnchoTableroM, 3);
                Assert.Equal(1.20, b.LargoTableroM, 3);
            });
        }

        [Theory]
        [InlineData(4.03)]
        [InlineData(7.05)]
        [InlineData(8.60)]
        [InlineData(9.60)]
        [InlineData(12.15)]
        public void GiradoEsExclusivoDeLaBandaDe060(double ancho)
        {
            PlanAncho plan = BandSolver.SolveWidth(ancho, Run);
            Assert.All(plan.Bandas, b =>
            {
                bool esGirado = b.Orientacion == BoardOrientation.Girado;
                bool es060 = SepCm(b) == 60;
                Assert.True(esGirado == es060,
                    $"banda {b.SeparadorM:0.00} m: girado={esGirado} pero 0,60={es060} (el girado es solo del 0,60)");
            });
        }

        [Fact]
        public void SeparatorPolicy_ReglaDelTablero_EsCorrecta()
        {
            // Banda de 1,20 → tablero 0,60×1,20 NORMAL.
            BandaTablero b120 = SeparatorPolicy.BoardForBand(120);
            Assert.Equal(BoardOrientation.Normal, b120.Orientacion);
            Assert.Equal(1.20, b120.LargoTableroM, 3);
            Assert.Equal(0.60, b120.AnchoTableroM, 3);

            // Banda de 0,60 → tablero 0,60×1,20 GIRADO.
            BandaTablero b60 = SeparatorPolicy.BoardForBand(60);
            Assert.Equal(BoardOrientation.Girado, b60.Orientacion);
            Assert.Equal(1.20, b60.LargoTableroM, 3);
            Assert.Equal(0.60, b60.AnchoTableroM, 3);

            // El conjunto de llenado es exactamente {1,20 ; 0,60}.
            Assert.Equal(new[] { 120, 60 }, SeparatorPolicy.BandsCmForFilling);

            // 0,80/0,90/1,00/1,10 son "no estándar"; 1,20 y 0,60 no lo son.
            Assert.True(SeparatorPolicy.EsBandaNoEstandar(80));
            Assert.True(SeparatorPolicy.EsBandaNoEstandar(90));
            Assert.True(SeparatorPolicy.EsBandaNoEstandar(100));
            Assert.True(SeparatorPolicy.EsBandaNoEstandar(110));
            Assert.False(SeparatorPolicy.EsBandaNoEstandar(120));
            Assert.False(SeparatorPolicy.EsBandaNoEstandar(60));
        }

        [Fact]
        public void CasoReportado_860_Da7x120Mas1x060()
        {
            PlanAncho plan = BandSolver.SolveWidth(8.60, Run);
            Assert.Equal(7, plan.Bandas.Count(b => SepCm(b) == 120));
            Assert.Equal(1, plan.Bandas.Count(b => SepCm(b) == 60));
            Assert.Equal(8, plan.Bandas.Count);
        }

        // ── CON COLUMNAS (el caso de la imagen): ningún tablero 0,60×0,85 / 0,90 ──────────────────────
        private static List<Point> Rect(double L, double W) => new()
        {
            new Point(0, 0), new Point(L, 0), new Point(L, W), new Point(0, W),
        };

        [Theory]
        // Losas con columnas regulares donde {1,20; 0,60} SÍ puede librar las columnas (todo tablero 0,60×1,20).
        [InlineData(8.61, 7.99, new[] { 0.80, 4.305, 7.81 }, new[] { 0.80, 3.995, 7.19 }, 0.30)]
        [InlineData(8.61, 7.99, new[] { 0.80, 4.305, 7.81 }, new[] { 0.80, 3.995, 7.19 }, 0.40)]
        [InlineData(9.00, 7.05, new[] { 0.90, 3.00, 5.10, 7.20 }, new[] { 1.00, 3.00, 5.00 }, 0.30)]
        public void ConColumnas_NingunTablero_060x085_ni_090(double L, double W, double[] cx, double[] cy, double size)
        {
            var slab = Rect(L, W);
            var cols = new List<ColumnSnapshot>();
            foreach (double x in cx)
            {
                foreach (double y in cy)
                {
                    cols.Add(new ColumnSnapshot(x, y, size, size));
                }
            }

            PreviewLayout layout = LayoutEngine.BuildLayout(slab, cols, 0, L, 0, W);

            // Ningún tablero puede tener largo 0,85 ni 0,90 m (las medidas "feas" que se veían en la imagen):
            // con {1,20; 0,60} librando columnas, la banda nunca es 0,80/0,90.
            double[] feos = { 0.85, 0.90 };
            Assert.All(layout.Boards, b =>
                Assert.False(feos.Any(f => Math.Abs(b.Length - f) < 0.02),
                    $"tablero de largo {b.Length:0.00} m (no debe haber 0,60×0,85 ni 0,60×0,90)"));
        }
    }
}
