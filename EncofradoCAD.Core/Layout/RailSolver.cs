using System;
using System.Collections.Generic;
using System.Linq;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>Reparto del LARGO de la losa (eje de los rieles) en piezas de riel de catálogo.</summary>
    public sealed record PlanRiel(
        IReadOnlyList<double> SegmentosM,
        double LargoTotalM,
        double VoladizoPorLadoM);

    /// <summary>
    /// Optimizador de RIELES, hermano del <see cref="BandSolver"/> pero para el eje de los rieles.
    ///
    /// Dado el largo de la losa, reparte el encofrado en tramos usando el catálogo real de rieles
    /// (<see cref="LayoutCatalog.RailLengths"/>: 1,0 / 1,3 / 1,5 / 2,0 / 2,2 / 2,5 / 3,0 m). Los rieles NO rotan.
    ///
    /// REGLA DE ORO (lo que pidió el usuario): NO partir un tramo en dos rieles pequeños solo para ajustar el
    /// voladizo, porque cada UNIÓN de rieles obliga a una fila de puntales; partir de más = puntales de más.
    /// Por eso el criterio principal es MINIMIZAR EL NÚMERO DE PIEZAS (= filas de puntales). Se acepta que el
    /// riel exterior sobresalga más de 30 cm del borde: el riel se dibuja completo y los tableros solo cubren
    /// los 20 cm de voladizo (el sobrante del riel queda sin tablero, ver BoardBuilder).
    ///
    /// Criterio (lexicográfico): MENOS piezas (menos uniones/puntales) → MENOR largo total (menos material y
    /// menos voladizo) → MÁS rieles de 3,0 m (retícula uniforme) → piezas lo más grandes posible. Restricción
    /// única: cada lado rebasa el borde ≥ 20 cm (voladizo mínimo obligatorio). Cálculo en centímetros enteros.
    /// </summary>
    public static class RailSolver
    {
        // Voladizo MÍNIMO por lado. Se relaja de 20 a 15 cm porque las medidas de riel del catálogo saltan de
        // 50 en 50 cm: exigir ≥20 cm por lado puede descartar por 1–2 cm la combinación que cierra casi exacta
        // (p. ej. losa 21,62 m → 7×3,0 + 1×1,0 = 22,0 m, 19 cm/lado) y obligar a saltar a la siguiente (22,5 m,
        // 44 cm/lado), desperdiciando material. Con 15 cm de piso, el criterio "menos piezas → menor largo"
        // elige SIEMPRE el cierre de catálogo más ajustado (~15–20 cm/lado), nunca uno inventado.
        private const int MinOverhangCm = 15;    // voladizo MÍNIMO por lado (piso; el óptimo queda ~20 cm)
        private const int StdRailCm = 300;        // riel estándar de 3,0 m

        /// <summary>Catálogo de rieles en cm, de mayor a menor.</summary>
        private static readonly int[] RailsCm =
            LayoutCatalog.RailLengths
                .Select(m => (int)Math.Round(m * 100))
                .OrderByDescending(v => v)
                .ToArray();

        /// <summary>Preferencia por tamaño para el desempate del sobrante (más grande = mejor = menor índice).</summary>
        private static readonly Dictionary<int, int> RailPref =
            RailsCm.Select((cm, i) => (cm, i)).ToDictionary(p => p.cm, p => p.i);

        /// <summary>Resuelve el reparto óptimo del largo de la losa en el eje de los rieles.</summary>
        public static PlanRiel Solve(double slabLengthM) => Solve(slabLengthM, MinOverhangCm / 100.0);

        /// <summary>
        /// Igual que <see cref="Solve(double)"/> pero exigiendo un voladizo MÍNIMO por lado mayor al estándar
        /// de 20 cm. Se usa cuando la retícula debe desplazarse para librar columnas: al desplazar, un lado
        /// pierde voladizo, así que se pide de antemano un voladizo base más holgado para que, tras el
        /// desplazamiento, AMBOS lados sigan rebasando el borde ≥ 20 cm sin romper el reparto en piezas de
        /// catálogo (solo crece la pieza de cierre; los tramos interiores siguen siendo de 3,0 m).
        /// </summary>
        public static PlanRiel Solve(double slabLengthM, double minOverhangPerSideM)
        {
            int w = (int)Math.Round(slabLengthM * 100);
            int minOverhang = Math.Max(MinOverhangCm, (int)Math.Round(minOverhangPerSideM * 100));

            // Cobertura MÍNIMA: el largo total debe rebasar el borde al menos <minOverhang> cm por lado. NO
            // hay tope superior de voladizo (el riel puede sobresalir); solo damos margen de un riel estándar
            // (3,0 m) de holgura para que el reparto tenga de dónde elegir el cierre de una sola pieza.
            int lo = w + (2 * minOverhang);
            int hi = lo + StdRailCm;

            // Programación dinámica: para cada largo total alcanzable con el catálogo, guarda el mejor plan.
            var best = new Dictionary<int, List<int>> { [0] = new List<int>() };
            for (int t = 5; t <= hi; t += 5)
            {
                List<int>? cand = null;
                foreach (int s in RailsCm)
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

            // Gana el plan que cubre la losa (≥ lo) con MENOS piezas (menos filas de puntales) y, a igualdad,
            // el de menor largo total (menos material y voladizo más ajustado).
            List<int>? winner = null;
            foreach (KeyValuePair<int, List<int>> kv in best)
            {
                if (kv.Key < lo || kv.Key > hi)
                {
                    continue;
                }

                if (winner == null || Less(Key(kv.Value), Key(winner)))
                {
                    winner = kv.Value;
                }
            }

            if (winner == null)
            {
                winner = new List<int> { StdRailCm }; // salvaguarda (no debería ocurrir con losas reales)
            }

            List<double> ordered = ArrangeSmallAtEdges(winner).Select(cm => cm / 100.0).ToList();
            int total = winner.Sum();
            double sideM = (total - w) / 2.0 / 100.0;
            return new PlanRiel(ordered, total / 100.0, sideM);
        }

        /// <summary>
        /// Regla del usuario: las piezas PEQUEÑAS (menores a 3,0 m) van SIEMPRE en los FILOS (extremos
        /// izquierdo/derecho), nunca en el interior. El interior queda solo con rieles de 3,0 m (retícula
        /// uniforme). Si hay varias piezas pequeñas, se reparten alternando entre el filo izquierdo y el
        /// derecho (la primera a la izquierda, la segunda a la derecha, etc.). No cambia el conteo ni las
        /// medidas: solo el ORDEN en que se colocan a lo largo del eje.
        /// </summary>
        private static List<int> ArrangeSmallAtEdges(List<int> segments)
        {
            List<int> small = segments.Where(s => s < StdRailCm).OrderByDescending(s => s).ToList();
            List<int> big = segments.Where(s => s >= StdRailCm).OrderByDescending(s => s).ToList();

            var left = new List<int>();
            var right = new List<int>();
            for (int i = 0; i < small.Count; i++)
            {
                if (i % 2 == 0)
                {
                    left.Add(small[i]);      // filo izquierdo
                }
                else
                {
                    right.Add(small[i]);     // filo derecho
                }
            }

            right.Reverse(); // el más grande hacia adentro, el más pequeño al borde exterior derecho
            var result = new List<int>(segments.Count);
            result.AddRange(left);
            result.AddRange(big);
            result.AddRange(right);
            return result;
        }

        /// <summary>
        /// Clave lexicográfica: (nº de piezas ; largo total ; −nº de 3,0 ; preferencia de tamaño).
        /// Menos piezas = menos uniones = menos filas de puntales (objetivo principal). Luego menor largo
        /// total (menos material y voladizo). Luego más rieles de 3,0 m (retícula uniforme) y piezas grandes.
        /// </summary>
        private static (int piezas, int total, int negStd, int pref) Key(List<int> seg)
        {
            int total = seg.Sum();
            int n300 = seg.Count(s => s == StdRailCm);
            int pref = seg.Sum(s => RailPref.TryGetValue(s, out int p) ? p : 9);
            return (seg.Count, total, -n300, pref);
        }

        private static bool Less(
            (int piezas, int total, int negStd, int pref) a,
            (int piezas, int total, int negStd, int pref) b)
        {
            if (a.piezas != b.piezas)
            {
                return a.piezas < b.piezas;
            }

            if (a.total != b.total)
            {
                return a.total < b.total;
            }

            if (a.negStd != b.negStd)
            {
                return a.negStd < b.negStd;
            }

            return a.pref < b.pref;
        }
    }
}
