using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Layout;
using Xunit;

namespace EncofradoCAD.Tests
{
    /// <summary>
    /// Pruebas de <see cref="RailEdgeExtender"/>: garantiza que TODO riel dibujado mide una medida REAL del
    /// catálogo. Los rieles de borde que quedan cortos se estiran hacia afuera hasta su pieza de catálogo
    /// (el extremo INTERIOR queda fijo; solo crece el voladizo). Las uniones interiores no se tocan.
    /// </summary>
    public class RailEdgeExtenderTests
    {
        private static bool EnCatalogo(double largo) =>
            LayoutCatalog.RailLengths.Any(c => System.Math.Abs(c - largo) < 0.01);

        [Fact]
        public void RielDeBordeCorto_SeEstiraHaciaAfuera_HastaCatalogo()
        {
            // Riel de 1,59 m con catálogo 2,00 m (el caso reportado): debe pasar a medir 2,00 m REALES,
            // dejando fijo el extremo interior (1,39) y bajando el exterior a -0,61.
            var rails = new List<LinearPiece>
            {
                new LinearPiece(-0.20, 1.39, 0.0, true, 2.00),
            };

            List<LinearPiece> result = RailEdgeExtender.SnapToCatalog(rails);

            Assert.Equal(2.00, result[0].ActualLength, 3);
            Assert.Equal(1.39, result[0].End, 3);   // interior fijo
            Assert.Equal(-0.61, result[0].Start, 3); // exterior estirado
        }

        [Fact]
        public void LineaConTresPiezas_SoloEstiraLasDosExteriores()
        {
            var rails = new List<LinearPiece>
            {
                new LinearPiece(-0.20, 1.39, 0.0, true, 2.00), // borde izquierdo (1,59 → 2,00)
                new LinearPiece(1.39, 4.39, 0.0, true, 3.00),  // interior (3,00 exacto)
                new LinearPiece(4.39, 5.98, 0.0, true, 2.00),  // borde derecho (1,59 → 2,00)
            };

            List<LinearPiece> result = RailEdgeExtender.SnapToCatalog(rails);

            Assert.Equal(2.00, result[0].ActualLength, 3); // izquierdo estirado
            Assert.Equal(3.00, result[1].ActualLength, 3); // interior intacto
            Assert.Equal(1.39, result[1].Start, 3);
            Assert.Equal(4.39, result[1].End, 3);
            Assert.Equal(2.00, result[2].ActualLength, 3); // derecho estirado
            Assert.Equal(4.39, result[2].Start, 3);        // interior fijo
            Assert.Equal(6.39, result[2].End, 3);          // exterior estirado
        }

        [Fact]
        public void TodosLosRieles_QuedanEnCatalogo()
        {
            var rails = new List<LinearPiece>
            {
                new LinearPiece(-0.20, 1.39, 0.0, true, 2.00),
                new LinearPiece(1.39, 4.39, 0.0, true, 3.00),
                new LinearPiece(4.39, 5.98, 0.0, true, 2.00),
                new LinearPiece(-0.20, 0.65, 1.20, true, 1.00), // otra fila, borde corto (0,85 → 1,00)
                new LinearPiece(0.65, 3.65, 1.20, true, 3.00),
            };

            List<LinearPiece> result = RailEdgeExtender.SnapToCatalog(rails);

            Assert.All(result, r => Assert.True(EnCatalogo(r.ActualLength),
                $"riel de {r.ActualLength:0.###} m no está en catálogo"));
        }

        [Fact]
        public void RielYaEnCatalogo_NoSeModifica()
        {
            var rails = new List<LinearPiece>
            {
                new LinearPiece(0.0, 3.00, 0.0, true, 3.00),
            };

            List<LinearPiece> result = RailEdgeExtender.SnapToCatalog(rails);

            Assert.Equal(0.0, result[0].Start, 3);
            Assert.Equal(3.00, result[0].End, 3);
        }

        [Fact]
        public void PiezaUnica_SeEstiraHastaCatalogo_PorElExtremoInferior()
        {
            // Una fila con UNA sola pieza: ambos extremos dan al voladizo; el extensor estira el inferior.
            var rails = new List<LinearPiece>
            {
                new LinearPiece(2.10, 3.69, 5.0, true, 2.00), // 1,59 m → 2,00 m
            };

            List<LinearPiece> result = RailEdgeExtender.SnapToCatalog(rails);

            Assert.Equal(2.00, result[0].ActualLength, 3);
            Assert.Equal(3.69, result[0].End, 3);   // extremo superior (interior) fijo
            Assert.Equal(1.69, result[0].Start, 3); // extremo inferior estirado
        }

        [Fact]
        public void RuidoDeComaFlotante_NoSeSobreEstira()
        {
            // 1,51 m está a 1 cm de la medida de catálogo 1,50: se deja como está, NO se estira a 2,00.
            var rails = new List<LinearPiece>
            {
                new LinearPiece(0.0, 1.51, 0.0, true, 2.00),
            };

            List<LinearPiece> result = RailEdgeExtender.SnapToCatalog(rails);

            Assert.Equal(1.51, result[0].ActualLength, 3);
        }

        [Fact]
        public void ListaVacia_NoFalla()
        {
            List<LinearPiece> result = RailEdgeExtender.SnapToCatalog(new List<LinearPiece>());
            Assert.Empty(result);
        }

        [Fact]
        public void NoMutaLaListaDeEntrada()
        {
            var original = new LinearPiece(-0.20, 1.39, 0.0, true, 2.00);
            var rails = new List<LinearPiece> { original };

            RailEdgeExtender.SnapToCatalog(rails);

            Assert.Equal(-0.20, rails[0].Start, 3); // la entrada no cambia
            Assert.Equal(1.39, rails[0].End, 3);
        }
    }
}
