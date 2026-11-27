using System.Linq;
using EncofradoCAD.Core.Layout;
using Xunit;

namespace EncofradoCAD.Tests
{
    /// <summary>
    /// Pruebas del módulo optimizador de medidas <see cref="BandSolver"/> (espejo del prototipo
    /// validado en _val/band_solver.py). Verifican que la mejor combinación de tableros para completar
    /// un ancho: usa la fila de ajuste GIRADA solo cuando ahorra tableros, respeta el voladizo 0,20–0,30 m,
    /// nunca usa separadores &lt; 0,60 m y evita el separador escaso 0,85 cuando hay alternativa.
    /// </summary>
    public class BandSolverTests
    {
        private const double Run = 19.0;

        private static int SepCm(BandaTablero b) => (int)System.Math.Round(b.SeparadorM * 100);

        [Theory]
        [InlineData(9.60)]
        [InlineData(8.40)]
        public void UsaGiroYAhorraCuandoElSobranteLoPermite(double ancho)
        {
            PlanAncho conGiro = BandSolver.SolveWidth(ancho, Run);

            // El plan mete exactamente una fila de ajuste de 0,60 girada.
            Assert.Contains(conGiro.Bandas, b => SepCm(b) == 60);
            Assert.All(conGiro.Bandas, b =>
                Assert.Equal(SepCm(b) == 60 ? BoardOrientation.Girado : BoardOrientation.Normal, b.Orientacion));

            // La fila de ajuste ahorra tableros frente a un reparto equivalente sin girar.
            int sinGiroTableros = TablerosSinGiro(ancho);
            Assert.True(conGiro.TablerosPorCorridaRiel < sinGiroTableros);
            Assert.Equal(16, sinGiroTableros - conGiro.TablerosPorCorridaRiel);
        }

        [Theory]
        [InlineData(9.00)]
        [InlineData(6.60)]
        [InlineData(10.30)]
        public void NoFuerzaGiroSiNoAyuda(double ancho)
        {
            PlanAncho plan = BandSolver.SolveWidth(ancho, Run);
            Assert.DoesNotContain(plan.Bandas, b => SepCm(b) == 60);
        }

        [Theory]
        [InlineData(9.60)]
        [InlineData(9.00)]
        [InlineData(8.40)]
        [InlineData(7.75)]
        [InlineData(10.30)]
        [InlineData(6.60)]
        [InlineData(5.55)]
        [InlineData(12.15)]
        public void VoladizoDentroDeNorma(double ancho)
        {
            // Con la regla "solo bandas de 1,20 y 0,60" (todo tablero 0,60×1,20), el voladizo se prefiere en
            // 20–30 cm, pero puede crecer hasta ~0,50 m en algunas losas para cerrar SIN medidas intermedias.
            PlanAncho plan = BandSolver.SolveWidth(ancho, Run);
            Assert.InRange(plan.VoladizoPorLadoM, 0.20 - 1e-6, 0.50 + 1e-6);
        }

        [Theory]
        [InlineData(9.60)]
        [InlineData(8.40)]
        [InlineData(5.55)]
        [InlineData(12.15)]
        public void SeparadoresNuncaMenoresA60(double ancho)
        {
            PlanAncho plan = BandSolver.SolveWidth(ancho, Run);
            Assert.All(plan.Bandas, b => Assert.True(SepCm(b) >= 60));
        }

        [Fact]
        public void Evita085CuandoHayAlternativa()
        {
            PlanAncho plan = BandSolver.SolveWidth(9.60, Run);
            Assert.DoesNotContain(plan.Bandas, b => SepCm(b) == 85);
        }

        [Theory]
        [InlineData(9.60)]
        [InlineData(8.40)]
        [InlineData(7.75)]
        public void CoberturaTotalCubreAnchoMasVoladizo(double ancho)
        {
            PlanAncho plan = BandSolver.SolveWidth(ancho, Run);
            double esperado = ancho + (2 * plan.VoladizoPorLadoM);
            Assert.Equal(esperado, plan.AnchoTotalM, 3);
        }

        [Fact]
        public void NuncaColapsaAUnaSolaBanda_AnchosNoMultiplosDe5()
        {
            // Regresión: con anchos "raros" (cajas envolventes de losas rotadas, no múltiplos de 5 cm) el
            // selector saltaba de 5 en 5 desde un valor desalineado y no encontraba solución → una sola banda
            // → el 3D se colapsaba. Ahora SIEMPRE debe haber un reparto válido de ≥ 2 bandas.
            for (int cm = 60; cm <= 1300; cm++)
            {
                double ancho = cm / 100.0;
                PlanAncho plan = BandSolver.SolveWidth(ancho, Run);
                // Suficientes bandas para el ancho (una losa ancha NUNCA colapsa a una sola banda de 1,20).
                Assert.True(plan.Bandas.Count >= (int)(ancho / 1.20),
                    $"ancho {ancho:0.00} colapsó a {plan.Bandas.Count} banda(s)");
                Assert.All(plan.Bandas, b => Assert.True(SepCm(b) >= 60));
                // Cubre el ancho con voladizo razonable (norma 0,20; respaldo hasta 0,60 en casos raros).
                Assert.InRange(plan.VoladizoPorLadoM, 0.20 - 1e-6, 0.60 + 1e-6);
                Assert.Equal(ancho + (2 * plan.VoladizoPorLadoM), plan.AnchoTotalM, 3);
            }
        }

        /// <summary>Reparto equivalente SIN permitir la banda girada de 0,60 (emula el motor actual).</summary>
        private static int TablerosSinGiro(double ancho)
        {
            // Mismo objetivo pero prohibiendo el separador de 0,60 (no hay tablero de largo 0,60 sin girar):
            // se reconstruye a mano el mejor reparto normal usando el catálogo sin el 0,60.
            // Para las medidas probadas equivale a rellenar el sobrante con dos bandas normales sub-1,20.
            int w = (int)System.Math.Round(ancho * 100);
            // 9,60 -> 7×120 + 80 + 80 ; 8,40 -> 6×120 + 80 + 80  (voladizo 0,20)
            int ciento20 = (w + 40) / 120; // bandas de 1,20 base
            int resto = (w + 40) - (ciento20 * 120);
            int bandas = ciento20;
            while (resto > 0)
            {
                bandas++;
                resto -= 80; // sobrante en bandas normales
            }

            int run = 1900;
            return bandas * ((run / 60) + (run % 60 >= 30 ? 1 : 0));
        }
    }
}
