using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Layout;
using Xunit;

namespace EncofradoCAD.Tests
{
    /// <summary>
    /// Pruebas del reparto de SEPARADORES (BandSolver) endurecido: priorizar 1,20 m y cerrar el sobrante con
    /// una pieza de catálogo escalonando hacia abajo, usando SOLO medidas del catálogo, para cualquier ancho.
    /// </summary>
    public class BandSolverCatalogTests
    {
        private static readonly double[] Catalog = LayoutCatalog.SeparatorLengths;

        private static bool EnCatalogo(double m) => Catalog.Any(c => Math.Abs(c - m) < 0.02);

        private static List<double> Seps(double w) =>
            BandSolver.SolveWidth(w, 1.0).Bandas.Select(b => b.SeparadorM).ToList();

        [Fact]
        public void Ancho860_Da7x120Mas1x060()
        {
            // El caso reportado: 8,60 m de ancho → 7 separadores de 1,20 + 1 de 0,60 (no 0,80 + 6×1,20 + 1,00).
            List<double> seps = Seps(8.60);
            Assert.Equal(7, seps.Count(s => Math.Abs(s - 1.20) < 0.02));
            Assert.Equal(1, seps.Count(s => Math.Abs(s - 0.60) < 0.02));
            Assert.Equal(8, seps.Count);
            Assert.Equal(9.00, seps.Sum(), 2); // 8,60 + 0,20 + 0,20
        }

        [Fact]
        public void ElSeparador060_QuedaEnElInterior_NoAlFilo()
        {
            List<double> seps = Seps(8.60);
            int idx = seps.FindIndex(s => Math.Abs(s - 0.60) < 0.02);
            Assert.True(idx > 0 && idx < seps.Count - 1, "el separador de 0,60 no debe ir en el filo");
        }

        [Theory]
        [InlineData(4.00)]
        [InlineData(5.40)]
        [InlineData(6.00)]
        [InlineData(7.05)]
        [InlineData(8.60)]
        [InlineData(9.00)]
        [InlineData(10.30)]
        [InlineData(12.15)]
        [InlineData(15.77)]
        public void CualquierAncho_SoloUsaMedidasDelCatalogo(double w)
        {
            List<double> seps = Seps(w);
            Assert.NotEmpty(seps);
            Assert.All(seps, s => Assert.True(EnCatalogo(s), $"separador {s:0.###} fuera de catálogo (ancho {w})"));
        }

        [Fact]
        public void NuncaColapsa_YSiempreEsCatalogo_BarridoFino()
        {
            for (int cm = 240; cm <= 2000; cm += 5)
            {
                double w = cm / 100.0;
                List<double> seps = Seps(w);
                Assert.NotEmpty(seps);
                Assert.All(seps, s => Assert.True(EnCatalogo(s), $"ancho {w:0.00} produjo {s:0.###} fuera de catálogo"));
            }
        }

        [Fact]
        public void PrioridadDe120_LlenaConElEstandar()
        {
            // 9,00 m entra exacto en 8 separadores de 1,20 (voladizo 0,30): todos estándar, sin piezas de cierre.
            List<double> seps = Seps(9.00);
            Assert.Equal(8, seps.Count(s => Math.Abs(s - 1.20) < 0.02));
            Assert.Equal(8, seps.Count);
        }
    }
}
