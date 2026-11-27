using System;
using System.Collections.Generic;
using System.Linq;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// Orientación del tablero de contacto dentro de una banda.
    /// </summary>
    public enum BoardOrientation
    {
        /// <summary>El largo del tablero cruza la banda (0,60 corre a lo largo de la riel).</summary>
        Normal,

        /// <summary>Fila de ajuste: tablero 0,60×1,20 GIRADO, el 1,20 corre a lo largo de la riel.</summary>
        Girado,
    }

    /// <summary>Una banda de separador con el tablero de contacto que la cubre.</summary>
    public sealed record BandaTablero(
        double SeparadorM,
        BoardOrientation Orientacion,
        double LargoTableroM,
        double AnchoTableroM);

    /// <summary>Reparto completo del ANCHO de la losa en bandas de separador.</summary>
    public sealed record PlanAncho(
        IReadOnlyList<BandaTablero> Bandas,
        double AnchoTotalM,
        double VoladizoPorLadoM,
        int TablerosPorCorridaRiel);

    /// <summary>
    /// Módulo optimizador de medidas ("mejor combinación de tableros para completar una medida").
    ///
    /// Dado el ancho de la losa, reparte el encofrado en BANDAS de separador usando SOLO medidas del
    /// catálogo real, buscando el MENOR número de tableros. Reglas:
    ///  • Separadores válidos ≥ 0,60 m (catálogo de <see cref="LayoutCatalog.SeparatorLengths"/>).
    ///  • Acoplamiento de tableros: GRANDE = ancho 0,60, largo ∈ {0,80…1,20}; CHICO = largo 1,20,
    ///    ancho ∈ {0,30…0,55}. Por eso una banda de 0,60 no tiene tablero de largo 0,60 y se cubre
    ///    GIRANDO el tablero estándar 0,60×1,20 (la "fila de ajuste").
    ///  • Voladizo: 0,20 m por lado (norma); se puede estirar hasta 0,30 m SOLO si hace falta para
    ///    cerrar con material disponible.
    ///  • Criterio: menos tableros → menos bandas → voladizo cercano a 0,20 → evitar el separador escaso.
    ///
    /// El costo de tableros de una banda NORMAL es el doble que el de una banda GIRADA (0,60 avanza
    /// 0,60 por pieza a lo largo de la riel; el girado avanza 1,20). Por eso el óptimo mete una fila de
    /// ajuste girada cuando el sobrante del borde lo permite, ahorrando tableros sin usar medidas raras.
    ///
    /// Cálculo interno en centímetros enteros (todas las medidas son múltiplos de 5).
    /// </summary>
    public static class BandSolver
    {
        private const int EdgeOverhangCm = 20;   // norma
        private const int OverhangMaxCm = 30;     // excepción máxima
        private const int MinPieceCm = 30;        // por debajo de esto: remate en obra, no cuenta como tablero
        private const double StdBoardLenM = LayoutCatalog.BoardLength; // 1,20
        private const double StdBoardWidthM = 0.60;

        /// <summary>
        /// Bandas que usa el reparto SIN columnas: SOLO 1,20 y 0,60 (regla del negocio, ver
        /// <see cref="SeparatorPolicy.BandsCmForFilling"/>). Así TODO tablero es 0,60×1,20 (normal o girado).
        /// </summary>
        private static readonly int[] SeparatorsCm = SeparatorPolicy.BandsCmForFilling;

        /// <summary>Separador estándar (m→cm) que se prioriza para llenar, igual que el riel de 3,0 m.</summary>
        private const int StdSeparatorCm = SeparatorPolicy.StdSeparatorCm;

        /// <summary>
        /// Resuelve el reparto óptimo del ancho de la losa.
        /// </summary>
        /// <param name="slabWidthM">Ancho de la losa a cubrir (m).</param>
        /// <param name="railRunM">Largo de la corrida de riel (m), para contar tableros.</param>
        /// <param name="columnCoordsRel">
        /// Posiciones de las columnas en ESTE eje, relativas al inicio de la losa (m). Si hay columnas:
        ///  (1) se EVITA el separador de 60 y se prioriza 1,20 (una columna atrapada entre dos rieles a 60 cm
        ///      no se puede librar); se permite estirar el voladizo hasta 40 cm para lograrlo.
        ///  (2) si un 60 fuera inevitable, se coloca lo más LEJOS posible de las columnas (nunca al filo).
        /// Vacío/nulo (eje de rieles o losa sin columnas) → comportamiento óptimo de siempre.
        /// </param>
        public static PlanAncho SolveWidth(double slabWidthM, double railRunM, IReadOnlyList<double>? columnCoordsRel = null)
        {
            int w = (int)Math.Round(slabWidthM * 100);

            // Reparto con SOLO bandas de 1,20 y 0,60 (regla del negocio): así TODO tablero es 0,60×1,20.
            // Se LLENA con 1,20 y se cierra con una banda de 0,60 (girada) cuando hace falta, MINIMIZANDO el
            // número de filas (= puntales). El voladizo se prefiere en 20–30 cm; como con solo dos medidas no
            // siempre cae ahí, se admite crecer hasta 40 y, como último recurso, hasta 50 cm para poder cerrar
            // SIN usar medidas intermedias (0,80/0,90/1,00/1,10). NUNCA se usa una medida fuera de {1,20; 0,60}.
            Dictionary<int, List<int>> best = BuildDp(w, w + (2 * 50));

            List<int>? winner =
                PickBest(best, w + (2 * EdgeOverhangCm), w + (2 * OverhangMaxCm))  // 20–30 cm (preferente)
                ?? PickBest(best, w + (2 * EdgeOverhangCm), w + (2 * 40))          // hasta 40 cm
                ?? PickBest(best, w + (2 * EdgeOverhangCm), w + (2 * 50));         // hasta 50 cm (último recurso)

            if (winner == null)
            {
                winner = new List<int> { StdSeparatorCm }; // salvaguarda (no debería ocurrir con losas reales)
            }

            double sideM = (winner.Sum() - w) / 2.0 / 100.0;
            List<int> ordered = ArrangeBands(winner, columnCoordsRel, sideM);
            var bandas = ordered.Select(ToBanda).ToList();
            int tableros = ordered.Sum(s => BandPieces(s, railRunM));
            return new PlanAncho(bandas, winner.Sum() / 100.0, sideM, tableros);
        }

        /// <summary>Programación dinámica sobre el ancho total (pasos de 5 cm) hasta <paramref name="tHi"/>.</summary>
        private static Dictionary<int, List<int>> BuildDp(int w, int tHi)
        {
            var best = new Dictionary<int, List<int>> { [0] = new List<int>() };
            for (int t = 5; t <= tHi; t += 5)
            {
                List<int>? cand = null;
                foreach (int s in SeparatorsCm)
                {
                    if (!best.TryGetValue(t - s, out List<int>? prev))
                    {
                        continue;
                    }

                    var plan = new List<int>(prev) { s };
                    if (cand == null || Less(Key(plan), Key(cand)))
                    {
                        cand = plan;
                    }
                }

                if (cand != null)
                {
                    best[t] = cand;
                }
            }

            return best;
        }

        /// <summary>Mejor combinación alcanzable cuyo ancho total cae en [<paramref name="lo"/>, <paramref name="hi"/>].</summary>
        private static List<int>? PickBest(Dictionary<int, List<int>> best, int lo, int hi)
        {
            List<int>? chosen = null;
            foreach (KeyValuePair<int, List<int>> kv in best)
            {
                if (kv.Key < lo || kv.Key > hi)
                {
                    continue;
                }

                if (chosen == null || Less(Key(kv.Value), Key(chosen)))
                {
                    chosen = kv.Value;
                }
            }

            return chosen;
        }

        /// <summary>
        /// Ordena las bandas de borde a borde cumpliendo la regla de apoyo del tablero (de centro de un riel
        /// al centro del siguiente):
        ///  • Una banda cuyo tablero es MÁS GRANDE que la banda (p. ej. 80 → tablero 85, porque no existe el
        ///    largo 80) solo puede ir AL FILO: ahí el sobrante del tablero vuela hacia el borde sin tener que
        ///    centrarse en un riel. Interna sería ilógica (el tablero no aterrizaría centrado).
        ///  • La banda de 60 (fila de ajuste GIRADA) debe quedar INTERNA. Su tablero girado 0,60×1,20 sí calza
        ///    de centro a centro, pero no queremos un separador de 60 en la orilla.
        ///  • El resto (largos exactos del catálogo) rellena el interior.
        /// </summary>
        private static List<int> ArrangeBands(List<int> seps, IReadOnlyList<double>? columnCoordsRel, double sideM)
        {
            var edgeOnly = seps.Where(NeedsOversizeBoard).OrderByDescending(v => v).ToList();
            var giradas = seps.Where(s => s == 60).ToList();
            var normales = seps.Where(s => s != 60 && !NeedsOversizeBoard(s)).OrderByDescending(v => v).ToList();

            // Bandas de filo repartidas a los dos extremos (izquierda/derecha).
            var left = new List<int>();
            var right = new List<int>();
            for (int i = 0; i < edgeOnly.Count; i++)
            {
                if (i % 2 == 0)
                {
                    left.Add(edgeOnly[i]);
                }
                else
                {
                    right.Insert(0, edgeOnly[i]);
                }
            }

            // Base: filos en los extremos + normales en el interior (aún SIN las bandas giradas de 60).
            var baseOrder = new List<int>(left);
            baseOrder.AddRange(normales);
            baseOrder.AddRange(right);

            if (giradas.Count == 0)
            {
                return baseOrder;
            }

            // Sin columnas: las giradas (60) al centro del interior, como siempre (nunca al filo).
            if (columnCoordsRel == null || columnCoordsRel.Count == 0)
            {
                int mid = left.Count + (normales.Count / 2);
                var res = new List<int>(baseOrder);
                res.InsertRange(mid, giradas);
                return res;
            }

            // Con columnas: cada banda de 60 se inserta en la posición INTERNA (nunca al filo) cuyo CENTRO
            // quede lo MÁS LEJOS posible de cualquier columna.
            double sideCm = sideM * 100.0;
            var colsCm = columnCoordsRel.Select(c => c * 100.0).ToList();
            var result = new List<int>(baseOrder);
            foreach (int gir in giradas)
            {
                int loIdx = Math.Max(1, left.Count);                       // no en el filo izquierdo
                int hiIdx = Math.Min(result.Count - 1, result.Count - Math.Max(right.Count, 0)); // no en el derecho
                int bestIdx = loIdx;
                double bestDist = -1.0;
                for (int k = loIdx; k <= hiIdx; k++)
                {
                    double startCm = -sideCm;
                    for (int j = 0; j < k; j++)
                    {
                        startCm += result[j];
                    }

                    double centerCm = startCm + 30.0; // centro de la banda de 60
                    double dist = colsCm.Min(c => Math.Abs(c - centerCm));
                    if (dist > bestDist)
                    {
                        bestDist = dist;
                        bestIdx = k;
                    }
                }

                result.Insert(bestIdx, gir);
            }

            return result;
        }

        /// <summary>
        /// ¿La banda necesita un tablero MÁS GRANDE que ella? (no existe un largo de catálogo EXACTO igual a la
        /// banda). Esas bandas solo pueden ir al filo. La de 60 no cuenta: se cubre girando el estándar, que calza.
        /// </summary>
        private static bool NeedsOversizeBoard(int sepCm)
        {
            if (sepCm == 60)
            {
                return false;
            }

            double sepM = sepCm / 100.0;
            return !LayoutCatalog.BoardLengthsDescending.Any(len => Math.Abs(len - sepM) < 0.01);
        }

        // La regla "qué tablero cubre cada banda" (0,60 → 0,60×1,20 girado; resto → normal) vive centralizada
        // en SeparatorPolicy para que exista UN solo lugar donde cambiarla.
        private static BandaTablero ToBanda(int sepCm) => SeparatorPolicy.BoardForBand(sepCm);

        /// <summary>Tableros que consume una banda a lo largo de la corrida de riel.</summary>
        private static int BandPieces(int sepCm, double railRunM)
        {
            int run = (int)Math.Round(railRunM * 100);
            int step = sepCm == 60 ? 120 : 60; // girado avanza 1,20; normal avanza 0,60
            int n = run / step;
            int rem = run - (n * step);
            return n + (rem >= MinPieceCm ? 1 : 0);
        }

        /// <summary>
        /// Clave lexicográfica del plan (solo hay bandas de 1,20 y 0,60):
        /// 1) MENOS bandas = menos filas = MENOS PUNTALES (prioridad máxima del usuario).
        /// 2) MENOR ancho total = voladizo más ajustado a 20 cm (con solo 1,20/0,60, (bandas, total) ya
        ///    determina cuántas de cada una hay).
        /// 3) MÁS bandas de 1,20 (menos filas giradas de 0,60) — desempate final.
        /// </summary>
        private static (int bands, int total, int negStd) Key(List<int> seps)
        {
            int total = seps.Sum();
            int nStd = seps.Count(s => s == StdSeparatorCm);
            return (seps.Count, total, -nStd);
        }

        private static bool Less(
            (int bands, int total, int negStd) a,
            (int bands, int total, int negStd) b)
        {
            if (a.bands != b.bands)
            {
                return a.bands < b.bands;
            }

            if (a.total != b.total)
            {
                return a.total < b.total;
            }

            return a.negStd < b.negStd;
        }
    }
}
