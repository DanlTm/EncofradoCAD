using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    public static partial class GridBuilder
    {
        // ==== EJE DE SEPARADORES (ancho de la losa): bandas de catálogo y DP que libra columnas ====
        // Reglas de separadores: priorizar 1,20 y minimizar filas (=puntales); ver SeparatorPolicy/BandSolver.

        /// <summary>
        /// Líneas del eje de SEPARADORES derivadas del reparto óptimo de bandas (<see cref="BandSolver"/>).
        /// El voladizo por lado lo fija el propio optimizador (0,20 m, estirable hasta 0,30 m si hace falta
        /// para cerrar con material disponible).
        /// </summary>
        private static List<double> GenerateBandLines(double min, double max, IReadOnlyList<double>? columnCoords = null)
        {
            // Coordenadas de columnas relativas al inicio de la losa (BandSolver trabaja en 0..ancho).
            List<double> rel = columnCoords == null
                ? new List<double>()
                : columnCoords.Select(c => c - min).ToList();
            return LinesFromPlan(BandSolver.SolveWidth(max - min, railRunM: 1.0, rel), min, max);
        }

        private static List<double> LinesFromPlan(PlanAncho plan, double min, double max)
        {
            double side = plan.VoladizoPorLadoM;

            var lines = new List<double> { min - side };
            double cursor = min - side;
            foreach (BandaTablero banda in plan.Bandas)
            {
                cursor += banda.SeparadorM;
                lines.Add(cursor);
            }

            // Ancla el extremo final exactamente en max + voladizo (evita deriva de coma flotante).
            lines[^1] = max + side;
            return lines;
        }

        // Comparación lexicográfica del DP de separadores que libra columnas. Posiciones (positional tuples):
        //   (nb ; fea ; negSum ; neg120 ; pref)  — aquí "fea" = bandas NO estándar.
        //   1) nb      = nº de filas (= filas de PUNTALES): MENOS es mejor (prioridad del usuario).
        //   2) fea     = nº de bandas NO estándar (todo lo que no sea 1,20 ni 0,60: 0,80/0,90/1,00/1,10):
        //                MENOS es mejor. Regla del negocio: usar SOLO 1,20 y 0,60; una medida intermedia solo
        //                aparece cuando una COLUMNA obliga. Como es desempate DESPUÉS de nb, evitarlas NUNCA
        //                agrega filas/puntales. Ver SeparatorPolicy.EsBandaNoEstandar.
        //   3) negSum  = −(suma de bandas): al minimizar se MAXIMIZA el voladizo hacia los 20 cm de norma.
        //   4) neg120  = −nº de bandas de 1,20 (se prioriza el estándar).
        //   5) pref    = preferencia de tamaño (piezas grandes).
        private static bool Less4(
            (int nb, int fea, int sum, int neg3, int pref) a,
            (int nb, int fea, int sum, int neg3, int pref) b)
        {
            if (a.nb != b.nb)
            {
                return a.nb < b.nb;
            }

            if (a.fea != b.fea)
            {
                return a.fea < b.fea;
            }

            if (a.sum != b.sum)
            {
                return a.sum < b.sum;
            }

            if (a.neg3 != b.neg3)
            {
                return a.neg3 < b.neg3;
            }

            return a.pref < b.pref;
        }

        /// <summary>Catálogo de separadores en cm, de mayor a menor (para el DP que libra columnas en el eje de filas).</summary>
        private static readonly int[] SeparatorBaysCm =
            LayoutCatalog.SeparatorLengths.Select(m => (int)Math.Round(m * 100)).OrderByDescending(v => v).ToArray();

        /// <summary>
        /// ¿TODAS las filas de riel (<paramref name="lines"/>, eje de separadores) libran las columnas con al
        /// menos <paramref name="clearance"/> de holgura FILO de columna → FILO de riel? Cada columna cuyo
        /// centro quede a menos de (semilargo + semirriel + holgura) de una fila tendría el riel ENCIMA (choque
        /// físico: el tablero va al ras y el riel corre ~10 cm por debajo). <paramref name="cols"/> son las
        /// columnas proyectadas al eje de separadores como (centro, semilargo en ese eje).
        /// </summary>
        public static bool BandLinesClearColumns(List<double> lines, List<(double center, double half)> cols, double clearance)
        {
            if (lines == null || lines.Count == 0 || cols == null || cols.Count == 0)
            {
                return true;
            }

            foreach ((double center, double half) in cols)
            {
                double need = half + ColumnClearance.RailHalfWidth + clearance;
                double dmin = lines.Min(l => Math.Abs(l - center));
                if (dmin < need - 1e-4)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Líneas del eje de SEPARADORES (posiciones de las FILAS de riel) colocadas con separadores de
        /// CATÁLOGO (1,20…0,60) de modo que NINGUNA fila caiga sobre una columna. Se usa SOLO cuando el
        /// desplazamiento rígido de toda la retícula (<see cref="ColumnClearance.ShiftToClearColumns"/>) no
        /// logra librar todas las columnas —lo que ocurre cuando la separación de columnas no es múltiplo del
        /// paso de 1,20 m y ningún δ rígido, sin descubrir la losa, deja la holgura en todas las filas—.
        ///
        /// Respeta la decisión del usuario: NO inventa medidas; usa únicamente los separadores del catálogo,
        /// eligiendo cuál va en cada banda (el que mejor se adapte a la distancia faltante) para que una fila
        /// pase AL LADO de las columnas con ≥ 5 cm de holgura. OBJETIVO: MENOS puntales (menos filas). El
        /// voladizo puede moverse en el rango 5–20 cm según convenga a cada losa (se prefiere el mayor, cerca
        /// de 20 cm, siempre que no obligue a más filas). Devuelve null si ni a 5 cm de holgura existe reparto.
        /// </summary>
        public static List<double>? GenerateBandLinesClearingColumns(double min, double max, List<(double center, double half)> cols)
        {
            if (cols == null || cols.Count == 0)
            {
                return null;
            }

            // Holgura MÍNIMA (5 cm): así el DP puede usar el reparto con MENOS filas (prioridad del usuario).
            // Exigir 15 cm de entrada obligaría a más filas de las necesarias.
            // 1) Rango de voladizo preferido 5–20 cm (norma del usuario). 2) Si —por una geometría extrema, p.
            //    ej. columna casi pegada al borde— no hubiera reparto en 5–20 cm, se extiende el voladizo hasta
            //    45 cm como ÚLTIMO recurso: es preferible más voladizo (más material) que dejar un riel sobre
            //    una columna (choque físico). Así el sistema resuelve TODOS los casos, no solo los comunes.
            // REGLA DEL USUARIO: usar SOLO bandas de 1,20 y 0,60 (todo tablero 0,60×1,20), incluso al librar
            // columnas. Se intenta PRIMERO con {1,20; 0,60} (dejando crecer el voladizo hasta 50 cm para
            // acomodar las filas al lado de las columnas). SOLO si con esas dos medidas es IMPOSIBLE librar
            // todas las columnas se recurre, como último recurso, al catálogo completo (una banda intermedia
            // 0,80/0,90/1,00/1,10, que dará un tablero 0,60×0,85/0,90/1,00/1,10) para no dejar un riel sobre
            // una columna (choque físico). Así el tablero 0,60×0,85 ya no aparece salvo que sea inevitable.
            List<(double center, double half)> rel = cols.Select(c => (c.center - min, c.half)).ToList();
            int[] soloEstandar = SeparatorPolicy.BandsCmForFilling; // {120, 60}
            List<int>? joints =
                PlanBandLinesClearingColumns(max - min, rel, ColumnClearance.MinClearance, minOhCm: 5, maxOhCm: 20, bays: soloEstandar)
                ?? PlanBandLinesClearingColumns(max - min, rel, ColumnClearance.MinClearance, minOhCm: 5, maxOhCm: 50, bays: soloEstandar)
                ?? PlanBandLinesClearingColumns(max - min, rel, ColumnClearance.MinClearance, minOhCm: 5, maxOhCm: 20, bays: SeparatorBaysCm)
                ?? PlanBandLinesClearingColumns(max - min, rel, ColumnClearance.MinClearance, minOhCm: 5, maxOhCm: 45, bays: SeparatorBaysCm);

            if (joints == null)
            {
                return null;
            }

            return joints.Select(v => min + (v / 100.0)).ToList();
        }

        /// <summary>
        /// DP (paso 5 cm) que reparte el ancho del eje de separadores en filas de catálogo cuyas líneas NO
        /// pisan ninguna columna. Idéntico en estructura al de rieles (<see cref="PlanRailJointsClearingColumns"/>)
        /// pero con el catálogo de separadores y la holgura pedida. Prioriza (Less4): MENOS filas (menos
        /// puntales) → MAYOR voladizo (hacia los 20 cm de norma, dentro del rango 5–20) → MÁS bandas de 1,20 →
        /// piezas grandes. <paramref name="cols"/> ya está relativo al inicio del eje. Devuelve las posiciones
        /// (cm) de las líneas, o null si no hay reparto.
        /// </summary>
        private static List<int>? PlanBandLinesClearingColumns(double slabWidthM, List<(double center, double half)> cols, double clearance, int minOhCm, int maxOhCm, int[] bays)
        {
            const int Step = 5;
            int MinOhCm = minOhCm;    // voladizo MÍNIMO por lado (normal 5 cm)
            int MaxOhCm = maxOhCm;    // voladizo MÁXIMO por lado (normal 20 cm; hasta 45 como último recurso)
            int w = (int)Math.Round(slabWidthM * 100);

            var colClear = cols
                .Select(c => (center: (int)Math.Round(c.center * 100),
                              gap: (int)Math.Round((c.half + ColumnClearance.RailHalfWidth + clearance) * 100)))
                .ToList();

            bool Clear(int p)
            {
                foreach ((int center, int gap) in colClear)
                {
                    if (Math.Abs(p - center) < gap)
                    {
                        return false;
                    }
                }

                return true;
            }

            int lo = -MaxOhCm;
            int hi = w + MaxOhCm;

            var key = new Dictionary<int, (int nb, int fea, int negSum, int neg120, int pref)>();
            var prev = new Dictionary<int, int>();

            for (int p = -MaxOhCm; p <= -MinOhCm; p += Step)
            {
                if (Clear(p))
                {
                    key[p] = (0, 0, 0, 0, 0);
                    prev[p] = int.MinValue;
                }
            }

            for (int p = lo; p <= hi; p += Step)
            {
                if (!key.TryGetValue(p, out (int nb, int fea, int negSum, int neg120, int pref) kp))
                {
                    continue;
                }

                for (int bi = 0; bi < bays.Length; bi++)
                {
                    int b = bays[bi];
                    int q = p + b;
                    if (q > hi || !Clear(q))
                    {
                        continue;
                    }

                    // fea = bandas NO estándar (ni 1,20 ni 0,60): se minimizan tras el nº de filas, así el eje
                    // usa solo 1,20/0,60 y baja a una medida intermedia SOLO si una columna obliga, sin sumar
                    // puntales. negSum = −(suma de bandas): al minimizar se MAXIMIZA el voladizo hacia 20 cm.
                    var nk = (
                        kp.nb + 1,
                        kp.fea + (SeparatorPolicy.EsBandaNoEstandar(b) ? 1 : 0),
                        kp.negSum - b,
                        kp.neg120 - (b == 120 ? 1 : 0),
                        kp.pref + bi);
                    if (!key.TryGetValue(q, out (int nb, int fea, int negSum, int neg120, int pref) kq) || Less4(nk, kq))
                    {
                        key[q] = nk;
                        prev[q] = p;
                    }
                }
            }

            int bestP = int.MinValue;
            (int nb, int fea, int negSum, int neg120, int pref) bestK = (int.MaxValue, 0, 0, 0, 0);
            foreach (KeyValuePair<int, (int nb, int fea, int negSum, int neg120, int pref)> kv in key)
            {
                int p = kv.Key;
                if (p < w + MinOhCm || p > w + MaxOhCm)
                {
                    continue;
                }

                if (bestP == int.MinValue || Less4(kv.Value, bestK))
                {
                    bestK = kv.Value;
                    bestP = p;
                }
            }

            if (bestP == int.MinValue)
            {
                return null;
            }

            var joints = new List<int> { bestP };
            int cur = bestP;
            while (prev.TryGetValue(cur, out int pr) && pr != int.MinValue)
            {
                cur = pr;
                joints.Add(cur);
            }

            joints.Reverse();
            return joints;
        }
    }
}
