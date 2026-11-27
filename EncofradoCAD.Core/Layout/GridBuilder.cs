using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// Construye la retícula de puntales de forma INTELIGENTE: fija el voladizo de 20 cm en los dos
    /// extremos (borde de losa → centro del riel), rellena con vanos ESTÁNDAR (3,0 m en rieles, 1,20 m en
    /// separadores) y, a partir de la medida total, decide sola si necesita 0, 1 o 2 medidas especiales
    /// para cerrar el sobrante. Luego recorta los puntales a la losa (+ halo de voladizo).
    ///
    /// MAPA DE ARCHIVOS (clase partial repartida por responsabilidad, para saber DÓNDE tocar cada cosa):
    ///   • GridBuilder.cs            → este archivo: entrada común y despacho por eje (GenerateStandardLines).
    ///   • GridBuilder.Rails.cs      → EJE DE RIELES (largo): reparto en piezas de catálogo y DP que libra
    ///                                  columnas. Regla "piezas &lt;3 m a los filos".
    ///   • GridBuilder.Separators.cs → EJE DE SEPARADORES (ancho): bandas de catálogo y DP que libra columnas.
    ///   • GridBuilder.Puntales.cs   → generación/poda de puntales.
    ///   • GridBuilder.Coverage.cs   → cobertura de celdas y limpieza.
    ///   • SeparatorPolicy.cs        → REGLAS del separador/tablero (prioridad 1,20, evitar 0,60×0,85/0,90,
    ///                                  banda de 0,60 = tablero girado). Cambia las reglas AHÍ, en un solo lugar.
    ///   • RailSolver.cs / BandSolver.cs → optimizadores por eje sin columnas.
    ///   • BoardBuilder*.cs          → tableros de contacto reales (rectángulos) sobre la retícula.
    /// </summary>
    public static partial class GridBuilder
    {
        private const double Eps = LayoutCatalog.GeometryEpsilon;

        /// <summary>
        /// Líneas de la retícula en un eje, con las dos líneas exteriores EXACTAMENTE a
        /// <paramref name="overhang"/> por fuera de [min,max] (voladizo obligatorio). Entre ellas coloca
        /// vanos estándar de <paramref name="spacing"/> y resuelve el sobrante:
        ///   • sobrante ≈ 0            → todos los vanos estándar (ninguna medida especial);
        ///   • sobrante ≥ catálogo mín → un único vano especial en el extremo;
        ///   • sobrante muy pequeño    → se funde con el último estándar y se parte en DOS vanos válidos.
        /// Así se usa SIEMPRE la medida estándar salvo que la dimensión de la losa obligue a otra.
        /// </summary>
        public static List<double> GenerateStandardLines(double min, double max, double spacing, double overhang, double catalogMin, IReadOnlyList<double>? sepColumnCoords = null)
        {
            // Eje de SEPARADORES (1,20 m): las líneas salen del optimizador de bandas (BandSolver), que reparte
            // el ancho con las medidas del catálogo real minimizando tableros. Si hay columnas en este eje, se
            // le pasan sus coordenadas para que evite el separador de 60 (prioriza 1,20) y, si un 60 fuera
            // inevitable, lo coloque lejos de las columnas.
            if (Math.Abs(spacing - LayoutCatalog.SeparatorSpacing) < 0.01)
            {
                return GenerateBandLines(min, max, sepColumnCoords);
            }

            // Eje de RIELES (3,0 m): las líneas salen del optimizador de rieles (RailSolver), que llena con el
            // riel estándar de 3,0 m y cierra el sobrante con el catálogo de rieles descendente dentro del
            // voladizo 20–30 cm (todos rieles reales de catálogo, sin cortes).
            if (Math.Abs(spacing - LayoutCatalog.RailSpacing) < 0.01)
            {
                return GenerateRailLines(min, max);
            }

            double lo = min - overhang;
            double hi = max + overhang;
            double total = hi - lo;

            var lines = new List<double> { lo };
            if (total <= spacing + Eps)
            {
                // La losa cabe en un solo vano en este eje.
                if (total > Eps)
                {
                    lines.Add(hi);
                }

                return lines;
            }

            int fullBays = (int)Math.Floor((total / spacing) + Eps); // vanos estándar completos
            double remainder = total - (fullBays * spacing);         // sobrante en [0, spacing)

            var bays = new List<double>();
            if (remainder <= Eps)
            {
                // Encaja exacto en múltiplos de la medida estándar: sin piezas especiales.
                for (int i = 0; i < fullBays; i++)
                {
                    bays.Add(spacing);
                }
            }
            else if (remainder >= catalogMin - Eps)
            {
                // El sobrante da para UNA pieza especial del catálogo: va al extremo.
                for (int i = 0; i < fullBays; i++)
                {
                    bays.Add(spacing);
                }

                bays.Add(remainder);
            }
            else if (fullBays >= 1)
            {
                // Sobrante demasiado chico para una sola pieza válida: se funde con el último vano estándar
                // y se reparte en DOS piezas iguales, ambas dentro del catálogo (caso "dos medidas que suman").
                for (int i = 0; i < fullBays - 1; i++)
                {
                    bays.Add(spacing);
                }

                double combo = spacing + remainder;
                bays.Add(combo / 2);
                bays.Add(combo / 2);
            }
            else
            {
                bays.Add(total);
            }

            double cursor = lo;
            foreach (double bay in bays)
            {
                cursor += bay;
                lines.Add(cursor);
            }

            // Ancla el extremo final exactamente en hi (evita deriva de coma flotante en el voladizo).
            lines[^1] = hi;
            return lines;
        }
    }
}
