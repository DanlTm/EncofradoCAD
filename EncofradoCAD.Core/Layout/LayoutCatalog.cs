namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// Reglas del negocio y catálogos de materiales en un solo lugar.
    /// Cambiar una medida aquí afecta a TODO el motor de forma coherente.
    ///
    /// Modelo de retícula (según el sistema real de encofrado):
    ///  • A lo LARGO de los rieles los puntales se separan 3,0 m (medida estándar del riel).
    ///  • A lo ANCHO (perpendicular) los puntales se separan 1,20 m (medida del separador superior).
    ///  • Todo el encofrado rebasa 20 cm el borde de la losa en los 4 lados (voladizo para costados).
    /// </summary>
    public static class LayoutCatalog
    {
        public const double GeometryEpsilon = 0.001;

        /// <summary>
        /// Holgura entre el filo de una columna y un puntal (m). Se baja de 0,20 a 0,15: con 0,20 el motor
        /// descartaba empalmes de riel que en realidad NO chocan con la columna (p. ej. un empalme a 0,34 m del
        /// centro de una columna de 0,30 = 0,19 m de su filo), y para "librarla" fragmentaba el riel de 3,0 m en
        /// piezas pequeñas. Con 0,15 el reparto de catálogo limpio (7×3,0 + 1×1,0) sobrevive y solo se desvía un
        /// empalme cuando de verdad quedaría sobre la columna. El planificador de rieles (DP) usa esta misma
        /// holgura, así que ambos quedan coherentes automáticamente.
        /// </summary>
        public const double ColumnClearance = 0.15;

        /// <summary>Voladizo: el puntal exterior queda 20 cm por FUERA del borde de la losa (costados).</summary>
        public const double EdgeOverhang = 0.20;

        /// <summary>Distancia máxima de un puntal al borde para conservarlo (anillo exterior + esquinas).</summary>
        public const double OuterRingHalo = 0.45;

        // ── Espaciados de la retícula ────────────────────────────────────────
        /// <summary>Separación de puntales a lo largo de los rieles (m).</summary>
        public const double RailSpacing = 3.00;

        /// <summary>Separación de puntales a lo ancho, entre filas (m) — medida del separador estándar.</summary>
        public const double SeparatorSpacing = 1.20;

        // ── Umbral para cortar un tramo continuo (rompe en huecos de columna) ──
        // Debe ser MAYOR que el espaciado normal y MENOR que un hueco de columna (2× el espaciado).
        public const double RailClusterThreshold = 3.30;       // > 3,0 (normal) y < 6,0 (columna)
        public const double SeparatorClusterThreshold = 1.70;  // > 1,2 (normal) y < 2,4 (columna)

        // ── Tableros de contacto ─────────────────────────────────────────────
        public const double BoardLength = 1.20;

        /// <summary>Anchos de tablero (estándar 0,60 m y especiales de relleno).</summary>
        public static readonly double[] BoardWidthsDescending = { 0.60, 0.55, 0.50, 0.45, 0.40, 0.35, 0.30 };

        /// <summary>
        /// Catálogo REAL de LARGOS de tablero grande (m), ancho fijo 0,60. Descendente. El sistema elige el
        /// largo más chico que cubra el tramo. (Se quitaron 1,15 / 0,75 / 0,70 / 0,60 que no existen en bodega:
        /// no hay tablero de largo 0,60 — esa banda se cubre GIRANDO el estándar 0,60×1,20, ver BandSolver.)
        /// </summary>
        public static readonly double[] BoardLengthsDescending = { 1.20, 1.10, 1.00, 0.90, 0.85 };

        /// <summary>Largo mínimo de tablero utilizable (m); por debajo, el sobrante va a remate.</summary>
        public const double BoardLengthMinUsable = 0.50;

        /// <summary>Catálogo de rieles de la empresa (m): estándar 3,00 + medidas para completar.</summary>
        public static readonly double[] RailLengths = { 1.00, 1.50, 2.00, 2.50, 3.00 };

        /// <summary>Catálogo de separadores de la empresa (m): estándar 1,20 + medidas para completar.</summary>
        public static readonly double[] SeparatorLengths = { 0.60, 0.80, 0.90, 1.00, 1.10, 1.20 };
    }
}
