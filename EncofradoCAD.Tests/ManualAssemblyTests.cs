using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;
using EncofradoCAD.Core.Layout;
using Xunit;

namespace EncofradoCAD.Tests
{
    /// <summary>
    /// Pruebas del ARMADO MANUAL (<see cref="ManualAssembly"/>): la base principal debe salir con las piezas
    /// exactas del sistema real, y el crecimiento desde el cabezal de un puntal debe colocar SIEMPRE los dos
    /// puntales con sus piezas de unión (y la cruceta pequeña de la base secundaria en el eje del riel).
    /// </summary>
    public class ManualAssemblyTests
    {
        private static int Rieles(PreviewLayout l) => l.HorizontalRails.Count + l.VerticalRails.Count;
        private static int Separadores(PreviewLayout l) => l.HorizontalSeparators.Count + l.VerticalSeparators.Count;

        /// <summary>Nudo de la esquina de origen de la base principal (0,0).</summary>
        private static ManualAssembly.Nudo Origen => new(0, 0);

        [Fact]
        public void BasePrincipal_TienePiezasExactasDelSistemaReal()
        {
            PreviewLayout l = ManualAssembly.CrearBasePrincipal().Build();

            Assert.Equal(4, l.Puntales.Count);
            Assert.Equal(2, Rieles(l));
            Assert.Equal(2, Separadores(l));
            Assert.Equal(2, l.LargeCrucetas.Count);   // los dos vanos de 3,00 m
            Assert.Equal(2, l.SmallCrucetas.Count);   // los dos vanos de 1,20 m
            Assert.Empty(l.Boards);                    // el modo manual no coloca tableros
        }

        [Fact]
        public void BasePrincipal_MideRielPorSeparador()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal(largoRiel: 3.00, largoSeparador: 1.20);

            Assert.Equal(3.00, a.CajaEnvolvente.Width, 3);
            Assert.Equal(1.20, a.CajaEnvolvente.Height, 3);
            Assert.Equal(Math.Sqrt((3.00 * 3.00) + (1.20 * 1.20)), a.Diagonal, 3);

            PreviewLayout l = a.Build();
            Assert.All(l.HorizontalRails, r => Assert.Equal(3.00, r.ActualLength, 3));
            Assert.All(l.VerticalSeparators, s => Assert.Equal(1.20, s.ActualLength, 3));
        }

        [Fact]
        public void EjeVertical_ColocaLosRielesEnY()
        {
            PreviewLayout l = ManualAssembly.CrearBasePrincipal(rielesHorizontales: false).Build();

            Assert.Empty(l.HorizontalRails);
            Assert.Equal(2, l.VerticalRails.Count);
            Assert.Equal(2, l.HorizontalSeparators.Count);
            Assert.All(l.VerticalRails, r => Assert.Equal(3.00, r.ActualLength, 3));
        }

        [Fact]
        public void CrecerEnElEjeDelRiel_DejaBaseSecundaria()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal();
            Assert.True(a.TryExtender(Origen, ManualAssembly.Direccion.RielMenos, 3.00, out string desc));

            PreviewLayout l = a.Build();

            // 2 puntales nuevos, 2 rieles nuevos, 1 separador nuevo y 1 cruceta pequeña (base secundaria).
            Assert.Equal(6, l.Puntales.Count);
            Assert.Equal(4, Rieles(l));
            Assert.Equal(3, Separadores(l));
            Assert.Equal(3, l.SmallCrucetas.Count);   // las 2 de la base + la de la base secundaria
            Assert.Equal(2, l.LargeCrucetas.Count);   // no se añaden crucetas grandes al crecer a lo largo
            Assert.Contains("Riel de", desc);
            Assert.Contains("cruceta pequeña", desc);
        }

        [Fact]
        public void CrecerEnElEjeDelSeparador_AgregaLaFilaSiguienteConCrucetaGrande()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal();
            Assert.True(a.TryExtender(Origen, ManualAssembly.Direccion.SeparadorMenos, 1.20, out string desc));

            PreviewLayout l = a.Build();

            // 2 puntales nuevos, 2 separadores nuevos, el riel de 3 m que los une y su cruceta grande.
            Assert.Equal(6, l.Puntales.Count);
            Assert.Equal(3, Rieles(l));
            Assert.Equal(4, Separadores(l));
            Assert.Equal(2, l.SmallCrucetas.Count);
            Assert.Equal(3, l.LargeCrucetas.Count);
            Assert.Contains("Separador de", desc);
            Assert.Contains("cruceta grande", desc);
        }

        [Fact]
        public void FueraDeLaCruzDeLaBase_YaNoSeColocanCrucetas()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal();

            // Primer brazo: sale de la base principal → lleva su cruceta pequeña.
            Assert.True(a.TryExtender(Origen, ManualAssembly.Direccion.RielMenos, 3.00, out _));
            Assert.Equal(3, a.Build().SmallCrucetas.Count);

            // Segundo tramo: ya está fuera de la base principal → solo puntal, riel y separador.
            var afuera = new ManualAssembly.Nudo(-3000, 0);
            ManualAssembly.Extension? ext = a.VistaPrevia(afuera, ManualAssembly.Direccion.RielMenos, 3.00);
            Assert.NotNull(ext);
            Assert.False(ext!.ConCruceta);
            Assert.DoesNotContain("cruceta", ext.Descripcion);

            Assert.True(a.TryExtender(afuera, ManualAssembly.Direccion.RielMenos, 3.00, out _));
            PreviewLayout l = a.Build();
            Assert.Equal(3, l.SmallCrucetas.Count);   // no aumentó
            Assert.Equal(2, l.LargeCrucetas.Count);
            Assert.Equal(8, l.Puntales.Count);        // pero los puntales sí
        }

        [Fact]
        public void CreciendoEnLosDosEjes_LaBasePrincipalQuedaEnCruz()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal();
            var opuesta = new ManualAssembly.Nudo(3000, 1200);

            // Un brazo a cada lado en los dos ejes: la base principal queda en el centro de una cruz.
            Assert.True(a.TryExtender(Origen, ManualAssembly.Direccion.RielMenos, 3.00, out _));
            Assert.True(a.TryExtender(opuesta, ManualAssembly.Direccion.RielMas, 3.00, out _));
            Assert.True(a.TryExtender(Origen, ManualAssembly.Direccion.SeparadorMenos, 1.20, out _));
            Assert.True(a.TryExtender(opuesta, ManualAssembly.Direccion.SeparadorMas, 1.20, out _));

            Assert.Equal(4 + (4 * 2), a.TotalPuntales);
            Assert.Equal(9.00, a.CajaEnvolvente.Width, 3);    // 3,00 + 3,00 + 3,00
            Assert.Equal(3.60, a.CajaEnvolvente.Height, 3);   // 1,20 + 1,20 + 1,20
        }

        [Fact]
        public void CrecerRespetaLaMedidaElegidaDelCatalogo()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal();
            Assert.True(a.TryExtender(Origen, ManualAssembly.Direccion.RielMenos, 2.50, out _));

            PreviewLayout l = a.Build();
            Assert.Equal(2, l.HorizontalRails.Count(r => Math.Abs(r.NominalLength - 2.50) < 0.001));
            Assert.Equal(5.50, a.CajaEnvolvente.Width, 3);   // 3,00 de la base + 2,50 hacia atrás
        }

        [Fact]
        public void SeCreceEnCadena_ElArmadoAvanzaVanoAVano()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal();

            // Tres vanos seguidos en el eje del riel, cada uno desde el puntal recién colocado.
            var nudo = new ManualAssembly.Nudo(3000, 0);
            for (int paso = 0; paso < 3; paso++)
            {
                Assert.True(a.TryExtender(nudo, ManualAssembly.Direccion.RielMas, 3.00, out _));
                nudo = new ManualAssembly.Nudo(nudo.U + 3000, 0);
            }

            // Solo el primer tramo salía de la base principal, así que solo ése lleva cruceta.
            Assert.Equal(3, a.Build().SmallCrucetas.Count);
            Assert.Equal(4 + (3 * 2), a.TotalPuntales);
            Assert.Equal(12.00, a.CajaEnvolvente.Width, 3);
            Assert.Equal(1.20, a.CajaEnvolvente.Height, 3);
        }

        [Fact]
        public void NoSePuedeCrecerDesdeUnNudoInexistente()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal();
            var fantasma = new ManualAssembly.Nudo(9999, 9999);

            Assert.False(a.PuedeExtender(fantasma, ManualAssembly.Direccion.RielMas, 3.00));
            Assert.False(a.TryExtender(fantasma, ManualAssembly.Direccion.RielMas, 3.00, out _));
            Assert.Null(a.VistaPrevia(fantasma, ManualAssembly.Direccion.RielMas, 3.00));
        }

        [Fact]
        public void SoloSeOfreceCrecerHaciaAfuera_NoHaciaDentroDeLaBase()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal();

            // Desde la esquina (0,0) el interior de la base ya está armado: solo se crece hacia afuera.
            Assert.True(a.PuedeExtender(Origen, ManualAssembly.Direccion.RielMenos, 3.00));
            Assert.True(a.PuedeExtender(Origen, ManualAssembly.Direccion.SeparadorMenos, 1.20));
            Assert.False(a.PuedeExtender(Origen, ManualAssembly.Direccion.RielMas, 3.00));
            Assert.False(a.PuedeExtender(Origen, ManualAssembly.Direccion.SeparadorMas, 1.20));

            // Desde la esquina opuesta, al revés.
            var opuesta = new ManualAssembly.Nudo(3000, 1200);
            Assert.True(a.PuedeExtender(opuesta, ManualAssembly.Direccion.RielMas, 3.00));
            Assert.True(a.PuedeExtender(opuesta, ManualAssembly.Direccion.SeparadorMas, 1.20));
            Assert.False(a.PuedeExtender(opuesta, ManualAssembly.Direccion.RielMenos, 3.00));
        }

        [Fact]
        public void VistaPrevia_NoModificaElArmado()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal();
            int puntalesAntes = a.TotalPuntales;
            int vanosAntes = a.TotalVanos;

            ManualAssembly.Extension? ext = a.VistaPrevia(Origen, ManualAssembly.Direccion.RielMenos, 3.00);

            Assert.NotNull(ext);
            Assert.True(ext!.ConCruceta);        // sale de la base principal
            Assert.False(ext.CrucetaGrande);     // el amarre es un separador de 1,20 → cruceta pequeña
            Assert.Contains("Riel de", ext.Nombre);
            Assert.Equal(puntalesAntes, a.TotalPuntales);
            Assert.Equal(vanosAntes, a.TotalVanos);
        }

        [Fact]
        public void Clonar_EsCopiaIndependiente_ParaElRegistroDeDeshacer()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal();
            ManualAssembly copia = a.Clonar();

            a.TryExtender(Origen, ManualAssembly.Direccion.RielMas, 3.00, out _);

            Assert.Equal(4, copia.TotalPuntales);   // la copia no cambió
            Assert.Equal(6, a.TotalPuntales);
        }

        [Fact]
        public void Origen_DesplazaTodaLaEstructura()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal(origenX: 5, origenY: 2);
            PreviewLayout l = a.Build();

            Assert.Equal(5, l.Puntales.Min(p => p.X), 3);
            Assert.Equal(2, l.Puntales.Min(p => p.Y), 3);
        }

        [Fact]
        public void LaBasePrincipalEsUnaSolaCelda()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal();
            Assert.Single(a.Celdas());

            a.TryExtender(Origen, ManualAssembly.Direccion.RielMenos, 3.00, out _);
            Assert.Equal(2, a.Celdas().Count);
        }

        [Fact]
        public void ContornoInterno_CentraLosSeparadoresYMeteElFiloDelRiel()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal();
            IReadOnlyList<ManualAssembly.SegmentoContorno> c = a.ContornoInterno(0.15);

            Assert.Equal(4, c.Count);                       // 2 rieles + 2 separadores
            Assert.Equal(2, c.Count(s => s.EsRiel));

            // Los rieles se meten media anchura hacia dentro: 0,075 y 1,125 en vez de 0,00 y 1,20.
            List<double> ys = c.Where(s => s.EsRiel).Select(s => Math.Round(s.A.Y, 3)).OrderBy(v => v).ToList();
            Assert.Equal(new[] { 0.075, 1.125 }, ys);

            // Los separadores van por su centro: sin desplazamiento respecto a los puntales.
            List<double> xs = c.Where(s => !s.EsRiel).Select(s => Math.Round(s.A.X, 3)).OrderBy(v => v).ToList();
            Assert.Equal(new[] { 0.00, 3.00 }, xs);
        }

        [Fact]
        public void LosaLista_BloqueaElArmado()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal();

            Assert.False(a.Congelado);
            Assert.True(a.CerrarLosa());
            Assert.True(a.Congelado);

            // Con la losa lista ya no entra ninguna pieza más.
            Assert.False(a.PuedeExtender(Origen, ManualAssembly.Direccion.RielMenos, 3.00));
            Assert.False(a.TryExtender(Origen, ManualAssembly.Direccion.RielMenos, 3.00, out _));
            Assert.Equal(4, a.TotalPuntales);

            a.ReabrirLosa();
            Assert.False(a.Congelado);
            Assert.True(a.TryExtender(Origen, ManualAssembly.Direccion.RielMenos, 3.00, out _));
        }

        [Fact]
        public void ColocarTableros_CubreLaCeldaConMedidasDeCatalogo()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal();

            Assert.Equal(0, a.ColocarTableros());   // sin cerrar la losa no coloca nada

            a.CerrarLosa();
            int n = a.ColocarTableros();

            // Celda de 3,00 × 1,20 → 5 tableros estándar de 0,60 × 1,20.
            Assert.Equal(5, n);
            PreviewLayout l = a.Build();
            Assert.Equal(5, l.Boards.Count);
            Assert.All(l.Boards, b =>
            {
                Assert.Equal(0.60, b.Width, 3);
                Assert.Equal(1.20, b.Length, 3);
            });
        }

        [Fact]
        public void ColocarTableros_DejaLibreElEspacioDeLasColumnas()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal();
            a.CerrarLosa();

            // Una columna en mitad de la celda: el tablero que la pisa no se coloca.
            var columna = new ColumnSnapshot(1.50, 0.60, 0.30, 0.30);
            int n = a.ColocarTableros(new[] { columna });

            Assert.Equal(4, n);
            Assert.DoesNotContain(a.Build().Boards, b =>
                b.Rect.Left < columna.MaxX && b.Rect.Right > columna.MinX);
        }

        [Fact]
        public void ElBomCuentaLasPiezasDelArmadoManual()
        {
            ManualAssembly a = ManualAssembly.CrearBasePrincipal();
            a.TryExtender(Origen, ManualAssembly.Direccion.RielMenos, 3.00, out _);

            var losa = new List<Point>
            {
                new Point(0, 0), new Point(10, 0), new Point(10, 8), new Point(0, 8)
            };

            List<(string Nombre, string Cantidad)> bom =
                LayoutEngine.BuildMaterialBill(a.Build(), losa, new List<ColumnSnapshot>());

            Assert.Contains(bom, x => x.Nombre.StartsWith("Puntal") && x.Cantidad == "6");
            Assert.Contains(bom, x => x.Nombre.Contains("Riel estándar") && x.Cantidad == "4");
            Assert.Contains(bom, x => x.Nombre.Contains("Separador estándar") && x.Cantidad == "3");
            Assert.Contains(bom, x => x.Nombre.Contains("cruceta pequeña") && x.Cantidad == "3");
            Assert.Contains(bom, x => x.Nombre.Contains("cruceta grande") && x.Cantidad == "2");
            Assert.DoesNotContain(bom, x => x.Nombre.StartsWith("Tablero"));
        }
    }
}
