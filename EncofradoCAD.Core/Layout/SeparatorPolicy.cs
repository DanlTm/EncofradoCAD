using System;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// REGLAS DEL NEGOCIO para los SEPARADORES y el TABLERO que cubre cada banda — TODAS EN UN SOLO LUGAR.
    ///
    /// Si quieres cambiar CÓMO el sistema elige separadores o QUÉ tablero cubre una banda, este es el archivo
    /// que debes tocar. Las reglas son (pedidas por el usuario; no cambiarlas sin avisar):
    ///
    ///  1) SOLO BANDAS DE 1,20 Y 0,60 → TODO TABLERO ES 0,60×1,20. El sistema reparte el ancho USANDO
    ///     ÚNICAMENTE bandas de 1,20 m (tablero 0,60×1,20 normal) y de 0,60 m (tablero 0,60×1,20 GIRADO). NO
    ///     usa bandas de 0,80 / 0,90 / 1,00 / 1,10 (darían tableros 0,60×0,85 / 0,90 / 1,00 / 1,10, que NO son
    ///     óptimos). El precio es que, en algunas losas, el voladizo crece hasta ~0,50 m para cerrar con esas
    ///     dos medidas. Ver <see cref="BandsCmForFilling"/>. ÚNICA EXCEPCIÓN: el DP que libra columnas puede
    ///     usar una medida intermedia SOLO si una columna obliga (para que ninguna fila caiga sobre la columna).
    ///
    ///  2) MENOS PUNTALES manda. Dentro de la regla anterior, se MINIMIZA el número de filas de separador
    ///     (cada fila = una fila de puntales). Cambiar a solo 1,20/0,60 NO agrega puntales (mismo nº de filas
    ///     que el reparto con catálogo completo); solo puede agrandar el voladizo.
    ///
    ///  3) BANDA DE 0,60 = TABLERO 0,60×1,20 GIRADO (horizontal), SIEMPRE COMPLETO. Una banda de 0,60 m se
    ///     cubre SIEMPRE con el tablero estándar 0,60×1,20 puesto de lado (el 1,20 corre a lo largo del riel).
    ///     El girado NUNCA es de 0,85/0,90: el sobrante del recorrido va a remate. Ver <see cref="BoardForBand"/>
    ///     y BoardBuilder.Fill.cs (rama de la fila de ajuste).
    ///
    /// Estas reglas las consumen: <see cref="BandSolver"/> (eje sin columnas) y el DP de separadores que libra
    /// columnas en GridBuilder.Separators.cs (eje con columnas). Los dos usan el criterio de aquí.
    /// </summary>
    public static class SeparatorPolicy
    {
        /// <summary>Separador estándar (cm) que se prioriza para llenar, como el riel de 3,0 m.</summary>
        public const int StdSeparatorCm = 120;

        /// <summary>Banda que se cubre GIRANDO el tablero estándar 0,60×1,20 (la "fila de ajuste").</summary>
        public const int GiradoBandCm = 60;

        /// <summary>
        /// ÚNICAS medidas de banda (cm) que el sistema usa para repartir el ancho (regla 1): 1,20 y 0,60.
        /// Así TODO tablero es 0,60×1,20 (normal en la banda de 1,20; girado en la de 0,60). El eje sin
        /// columnas (<see cref="BandSolver"/>) se limita a estas dos. El DP que libra columnas parte de estas
        /// y solo baja al resto del catálogo si una columna lo obliga.
        /// </summary>
        public static readonly int[] BandsCmForFilling = { 120, 60 };

        /// <summary>Largo del tablero estándar (m).</summary>
        public const double StdBoardLenM = LayoutCatalog.BoardLength; // 1,20

        /// <summary>Ancho del tablero estándar (m).</summary>
        public const double StdBoardWidthM = 0.60;

        /// <summary>
        /// ¿Es una banda NO estándar? Cualquiera que NO sea 1,20 ni 0,60 (es decir 0,80 / 0,90 / 1,00 / 1,10).
        /// Su tablero saldría 0,60×0,85 / 0,90 / 1,00 / 1,10 (no óptimo). El eje sin columnas nunca las usa;
        /// el DP que libra columnas las penaliza y solo las emplea si una columna obliga.
        /// </summary>
        public static bool EsBandaNoEstandar(int sepCm) => sepCm != StdSeparatorCm && sepCm != GiradoBandCm;

        /// <summary>
        /// Tablero que cubre una banda de separador según la regla del negocio:
        ///  • banda de 0,60 → tablero estándar 0,60×1,20 GIRADO (horizontal);
        ///  • cualquier otra → tablero normal de largo = ancho de la banda y ancho 0,60.
        /// </summary>
        public static BandaTablero BoardForBand(int sepCm)
        {
            double sepM = sepCm / 100.0;
            if (sepCm == GiradoBandCm)
            {
                return new BandaTablero(sepM, BoardOrientation.Girado, StdBoardLenM, StdBoardWidthM);
            }

            return new BandaTablero(sepM, BoardOrientation.Normal, sepM, StdBoardWidthM);
        }
    }
}
