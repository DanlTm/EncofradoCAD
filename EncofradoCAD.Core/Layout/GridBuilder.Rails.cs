using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    public static partial class GridBuilder
    {
        // ==== EJE DE RIELES (largo de la losa): reparto en piezas de catálogo y DP que libra columnas ====
        // Reglas de rieles: llenar con 3,0 m, cerrar con catálogo, piezas <3 m a los filos, no pisar columnas.

        /// <summary>
        /// Líneas del eje de RIELES derivadas del reparto óptimo del <see cref="RailSolver"/> (llena con 3,0 m
        /// y cierra el sobrante con el catálogo de rieles dentro del voladizo 20–30 cm).
        /// </summary>
        /// <summary>
        /// Líneas del eje de RIELES que ADEMÁS libran las columnas de ese eje, GARANTIZANDO que TODA unión de
        /// rieles caiga en una posición de catálogo y que NINGUNA quede sobre una columna.
        ///
        /// Sin columnas usa el reparto simétrico de <see cref="RailSolver"/> (5×3,0 m + cierre de catálogo).
        /// Con columnas resuelve una BÚSQUEDA (programación dinámica sobre posiciones, paso 5 cm): coloca
        /// uniones de izquierda a derecha, cada vano = una medida REAL del catálogo de rieles
        /// (3,0 / 2,5 / 2,0 / 1,5 / 1,0), y solo permite una unión donde no pise ninguna columna (holgura
        /// filo-de-columna → filo-de-riel). Si un vano de 3,0 m aterrizaría sobre una columna, en vez de
        /// fragmentar en medidas inventadas, la búsqueda cierra ese tramo con piezas de catálogo (p. ej.
        /// 1,5 + 1,5, poniendo un puntal a cada lado de la columna). El criterio (lexicográfico) prioriza:
        /// MENOS uniones (menos puntales) → voladizo total más ajustado → MÁS vanos de 3,0 m → piezas grandes.
        /// El voladizo mínimo es 20 cm por lado (y puede crecer lo necesario para librar columnas). Devuelve
        /// las líneas YA resueltas: el llamador NO debe aplicar <see cref="ColumnClearance.ShiftToClearColumns"/>
        /// sobre este eje.
        /// </summary>
        public static List<double> GenerateRailLinesClearingColumns(double min, double max, List<(double center, double half)> railColumns)
        {
            double w = max - min;

            List<double> Symmetric()
            {
                PlanRiel p = RailSolver.Solve(w);
                double side = p.VoladizoPorLadoM;
                var l = new List<double> { min - side };
                double cur = min - side;
                foreach (double seg in p.SegmentosM)
                {
                    cur += seg;
                    l.Add(cur);
                }

                l[^1] = max + side;
                return l;
            }

            if (railColumns == null || railColumns.Count == 0)
            {
                return Symmetric();
            }

            List<int>? jointsCm = PlanRailJointsClearingColumns(w, railColumns);
            if (jointsCm == null)
            {
                return Symmetric(); // salvaguarda: columnas patológicas sin solución (no debería ocurrir).
            }

            return jointsCm.Select(v => min + (v / 100.0)).ToList();
        }

        /// <summary>Vanos del catálogo de rieles en cm, de mayor a menor.</summary>
        private static readonly int[] RailBaysCm =
            LayoutCatalog.RailLengths.Select(m => (int)Math.Round(m * 100)).OrderByDescending(v => v).ToArray();

        /// <summary>
        /// Programación dinámica que devuelve las posiciones (cm, relativas al inicio de la losa) de las
        /// uniones de rieles: cada vano es una medida del catálogo y ninguna unión pisa una columna. Devuelve
        /// null solo si no existe ningún reparto (columnas imposibles). Ver
        /// <see cref="GenerateRailLinesClearingColumns"/>.
        /// </summary>
        private static List<int>? PlanRailJointsClearingColumns(double slabWidthM, List<(double center, double half)> cols)
        {
            const int Step = 5;
            const int MinOhCm = 15;    // voladizo mínimo por lado (coincide con RailSolver: permite cierres casi exactos de catálogo)
            const int MaxOhCm = 150;   // margen de búsqueda del voladizo por lado
            int w = (int)Math.Round(slabWidthM * 100);

            // Holgura EXIGIDA a cada unión respecto al centro de la columna. Debe ser ≥ la que usa
            // GeneratePuntales para PODAR puntales cercanos a columnas (semiancho de columna +
            // ColumnClearance = 0,20 m), para que ninguna unión aprobada por el DP se pode luego (lo que
            // reintroduciría la fragmentación). Se añade un pequeño margen de 2 cm por seguridad numérica.
            int pruneGap = (int)Math.Round(LayoutCatalog.ColumnClearance * 100); // 20 cm (mismo que GeneratePuntales)
            var colClear = cols
                .Select(c => (center: (int)Math.Round(c.center * 100), gap: (int)Math.Round(c.half * 100) + pruneGap + 2))
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

            // Estado del DP = (posición de la unión ; el último vano colocado fue PEQUEÑO <3,0 m). Guardamos
            // ese "último vano pequeño" para poder REGALAR la pieza pequeña de cierre en la meta (es un filo).
            // Regla del usuario: las piezas <3,0 m van SIEMPRE en los FILOS. Por eso el criterio PRINCIPAL es
            // MINIMIZAR las piezas pequeñas INTERIORES (las que no son ni el primer ni el último vano). El
            // primer vano se regala aquí (nb==0) y el último en la meta; solo las pequeñas del medio "cuestan".
            var key = new Dictionary<(int p, bool lastSmall), (int smalls, int nb, int sum, int neg3, int pref)>();
            var prev = new Dictionary<(int p, bool lastSmall), (int p, bool lastSmall)>();

            // Estados iniciales: la unión más a la izquierda, en el voladizo izquierdo [−MaxOh, −MinOh].
            // Aún no hay ningún vano colocado (lastSmall = false, nb = 0).
            for (int p = -MaxOhCm; p <= -MinOhCm; p += Step)
            {
                if (Clear(p))
                {
                    key[(p, false)] = (0, 0, 0, 0, 0);
                }
            }

            // Relajación en orden ascendente de posición (todas las transiciones van hacia la derecha).
            for (int p = lo; p <= hi; p += Step)
            {
                foreach (bool sPrev in new[] { false, true })
                {
                    if (!key.TryGetValue((p, sPrev), out (int smalls, int nb, int sum, int neg3, int pref) kp))
                    {
                        continue;
                    }

                    for (int bi = 0; bi < RailBaysCm.Length; bi++)
                    {
                        int b = RailBaysCm[bi];
                        int q = p + b;
                        if (q > hi || !Clear(q))
                        {
                            continue;
                        }

                        bool baySmall = b < 300;
                        bool isFirstBay = kp.nb == 0;                 // el primer vano es un filo: pequeño gratis
                        int addSmall = (baySmall && !isFirstBay) ? 1 : 0;

                        var nk = (
                            kp.smalls + addSmall,
                            kp.nb + 1,
                            kp.sum + b,
                            kp.neg3 - (b == 300 ? 1 : 0),
                            kp.pref + bi);
                        (int p, bool lastSmall) st = (q, baySmall);
                        if (!key.TryGetValue(st, out (int smalls, int nb, int sum, int neg3, int pref) kq) || Less5(nk, kq))
                        {
                            key[st] = nk;
                            prev[st] = (p, sPrev);
                        }
                    }
                }
            }

            // Meta: la unión más a la derecha, en el voladizo derecho [w+MinOh, w+MaxOh]. Se recorren las
            // posiciones REALMENTE alcanzadas (todas múltiplos de 5 desde el inicio); NO se puede iterar de 5
            // en 5 desde w+MinOh porque w puede no ser múltiplo de 5 y nunca coincidiría con las uniones.
            // Si el ÚLTIMO vano fue pequeño, se REGALA (es un filo): se descuenta 1 del conteo de pequeñas.
            bool found = false;
            (int p, bool lastSmall) bestState = default;
            (int smalls, int nb, int sum, int neg3, int pref) bestK = (int.MaxValue, 0, 0, 0, 0);
            foreach (KeyValuePair<(int p, bool lastSmall), (int smalls, int nb, int sum, int neg3, int pref)> kv in key)
            {
                int p = kv.Key.p;
                if (p < w + MinOhCm || p > w + MaxOhCm)
                {
                    continue;
                }

                (int smalls, int nb, int sum, int neg3, int pref) t = kv.Value;
                int effSmalls = t.smalls - (kv.Key.lastSmall && t.nb >= 2 ? 1 : 0);
                (int smalls, int nb, int sum, int neg3, int pref) effK = (effSmalls, t.nb, t.sum, t.neg3, t.pref);
                if (!found || Less5(effK, bestK))
                {
                    bestK = effK;
                    bestState = kv.Key;
                    found = true;
                }
            }

            if (!found)
            {
                return null;
            }

            var joints = new List<int> { bestState.p };
            (int p, bool lastSmall) cur2 = bestState;
            while (prev.TryGetValue(cur2, out (int p, bool lastSmall) pr))
            {
                cur2 = pr;
                joints.Add(cur2.p);
            }

            joints.Reverse();
            return joints;
        }

        /// <summary>
        /// Comparación lexicográfica del DP de rieles con la regla "pequeñas en los filos":
        /// (nº de piezas PEQUEÑAS interiores → nº de piezas/uniones → largo total → −nº de 3,0 → tamaño).
        /// La primera componente es la que fuerza a que los rieles &lt;3,0 m queden en los extremos.
        /// </summary>
        private static bool Less5(
            (int smalls, int nb, int sum, int neg3, int pref) a,
            (int smalls, int nb, int sum, int neg3, int pref) b)
        {
            if (a.smalls != b.smalls)
            {
                return a.smalls < b.smalls;
            }

            if (a.nb != b.nb)
            {
                return a.nb < b.nb;
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

        private static List<double> GenerateRailLines(double min, double max)
        {
            PlanRiel plan = RailSolver.Solve(max - min);
            double side = plan.VoladizoPorLadoM;

            var lines = new List<double> { min - side };
            double cursor = min - side;
            foreach (double segmento in plan.SegmentosM)
            {
                cursor += segmento;
                lines.Add(cursor);
            }

            // Ancla el extremo final exactamente en max + voladizo (evita deriva de coma flotante).
            lines[^1] = max + side;
            return lines;
        }
    }
}
