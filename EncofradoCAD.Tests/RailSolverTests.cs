using System.Linq;
using EncofradoCAD.Core.Layout;
using Xunit;

namespace EncofradoCAD.Tests
{
    /// <summary>
    /// Pruebas del optimizador de rieles <see cref="RailSolver"/> (espejo del prototipo _val/rail_solver.py).
    /// Verifican que llena con 3,0 m tantos como quepan, cierra el sobrante con catálogo real, mantiene el
    /// voladizo en 20–30 cm y nunca colapsa (ni con largos que no son múltiplos de 5 cm).
    /// </summary>
    public class RailSolverTests
    {
        [Fact]
        public void Caso1830_LlenaCon3YCierraConCatalogoReal()
        {
            PlanRiel plan = RailSolver.Solve(18.30);
            // Catálogo real de rieles = {1,0; 1,5; 2,0; 2,5; 3,0}. Para 18,30 m (necesita ≥ 18,70 con
            // voladizo 20 cm) el reparto de MENOS piezas prioriza 3,0 m: 3,0 ×6 + 1,0 → total 19,00, vol. 35 cm.
            Assert.Equal(6, plan.SegmentosM.Count(s => System.Math.Abs(s - 3.0) < 0.001));
            Assert.Contains(plan.SegmentosM, s => System.Math.Abs(s - 1.00) < 0.001);
            Assert.All(plan.SegmentosM, s => Assert.Contains(
                new[] { 1.00, 1.50, 2.00, 2.50, 3.00 }, c => System.Math.Abs(c - s) < 0.001));
            Assert.Equal(0.35, plan.VoladizoPorLadoM, 3);
            Assert.Equal(19.00, plan.LargoTotalM, 3);
        }

        [Theory]
        [InlineData(1.00)]  // 1,0 y 1,3 no existen como largo de tablero, pero SÍ como riel
        [InlineData(1.30)]
        [InlineData(1.50)]
        [InlineData(2.00)]
        [InlineData(2.20)]
        [InlineData(2.50)]
        [InlineData(3.00)]
        public void SoloUsaMedidasDelCatalogoDeRieles(double _)
        {
            double[] catalogo = { 1.00, 1.30, 1.50, 2.00, 2.20, 2.50, 3.00 };
            foreach (double largo in new[] { 9.60, 12.15, 18.30, 21.40, 7.75 })
            {
                PlanRiel plan = RailSolver.Solve(largo);
                Assert.All(plan.SegmentosM, s => Assert.Contains(catalogo, c => System.Math.Abs(c - s) < 0.001));
            }
        }

        [Fact]
        public void PrefiereLlenarConElEstandarDe3()
        {
            // 15,60 → 3,0 ×5 + 1,0 (máximo de estándar), no repartos con muchos 2,5.
            PlanRiel plan = RailSolver.Solve(15.60);
            Assert.Equal(5, plan.SegmentosM.Count(s => System.Math.Abs(s - 3.0) < 0.001));
        }

        [Fact]
        public void NuncaColapsa_LargosNoMultiplosDe5()
        {
            for (int cm = 100; cm <= 3000; cm++)
            {
                double largo = cm / 100.0;
                PlanRiel plan = RailSolver.Solve(largo);
                Assert.NotEmpty(plan.SegmentosM);
                Assert.True(plan.SegmentosM.Count >= (int)(largo / 3.0),
                    $"largo {largo:0.00} colapsó a {plan.SegmentosM.Count} pieza(s)");
                Assert.InRange(plan.VoladizoPorLadoM, 0.20 - 1e-6, 0.60 + 1e-6);
                Assert.Equal(largo + (2 * plan.VoladizoPorLadoM), plan.LargoTotalM, 3);
            }
        }
    }
}
