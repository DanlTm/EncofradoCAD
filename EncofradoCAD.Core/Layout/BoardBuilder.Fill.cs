using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    public static partial class BoardBuilder
    {
        private static void FillCellWithBoards(Rect cell, List<Point> slab, List<double> primLines, List<BoardPiece> boards, List<Rect> remates, bool nearColumn, bool board120AlongY)
        {
            // "sec"  = dirección del ANCHO (tableros lado a lado, 0,30–0,60 m).
            // "prim" = dirección del LARGO, perpendicular a los rieles.
            double primStart = board120AlongY ? cell.Y : cell.X;
            double primEnd = board120AlongY ? cell.Bottom : cell.Right;
            double secCellStart = board120AlongY ? cell.X : cell.Y;
            double secCellEnd = board120AlongY ? cell.Right : cell.Bottom;

            // ORILLAS SUP/INF — APOYO EN EL RIEL EXTERIOR. El tablero de orilla DEBE llegar al riel exterior
            // (o sobrepasarlo) para quedar apoyado; si termina antes, cuelga en el aire y se cae. Cuando el
            // voladizo se estira (>20 cm) o el desplazamiento por columnas corre la retícula, el riel exterior
            // puede quedar por FUERA de la celda (que llega hasta borde+20 cm). Se extiende el rango hasta ese
            // riel exterior (si está a ≤40 cm del borde de la celda y esta toca el borde real de la losa), para
            // que la banda de orilla llegue de riel a riel. El otro caso (riel exterior por DENTRO del voladizo)
            // lo resuelve el filtro de abajo, que hace que el tablero cantiléver hasta la línea de voladizo.
            double slabPrimMin = board120AlongY ? slab.Min(p => p.Y) : slab.Min(p => p.X);
            double slabPrimMax = board120AlongY ? slab.Max(p => p.Y) : slab.Max(p => p.X);
            // Umbral generoso (0,70 m): tras el desplazamiento por columnas el riel exterior puede quedar
            // hasta ~voladizo(0,30) + parte del desplazamiento por fuera de la celda; se extiende hasta él para
            // que la banda de orilla llegue de riel a riel. Si aun así la banda pasara de 1,20, la salvaguarda
            // de abajo la topa y remata el resto (nunca se estira un tablero).
            const double OuterRielReach = 0.70;
            if (primStart <= slabPrimMin + Eps)
            {
                double origStart = primStart;
                foreach (double pl in primLines)
                {
                    if (pl < origStart && origStart - pl <= OuterRielReach && pl < primStart)
                    {
                        primStart = pl;
                    }
                }
            }

            if (primEnd >= slabPrimMax - Eps)
            {
                double origEnd = primEnd;
                foreach (double pl in primLines)
                {
                    if (pl > origEnd && pl - origEnd <= OuterRielReach && pl > primEnd)
                    {
                        primEnd = pl;
                    }
                }
            }

            if (primEnd - primStart < LayoutCatalog.BoardLengthMinUsable - Eps)
            {
                remates.Add(cell);
                return;
            }

            // Límites de banda = las líneas de RIELES dentro de la celda (+ extremos). Cada banda va de un
            // riel al siguiente, así el tablero se apoya de centro de un riel al centro del otro.
            //
            // ORILLAS SUP/INF (voladizo): los extremos de la celda (primStart / primEnd) son la LÍNEA DEL
            // VOLADIZO (borde de losa + 20 cm). Cuando el desplazamiento por columnas deja el riel exterior
            // ADENTRO de esa línea, ese riel formaría una banda de orilla más corta que el tablero de catálogo
            // que alcanza el voladizo (p. ej. pondría 1,10 en vez de 1,20 y no llegaría a la línea entrecortada).
            // Para evitarlo, NO se usa como límite de banda ningún riel que quede a menos de un tablero mínimo
            // (0,50 m) del borde de la celda: así la banda de orilla llega COMPLETA hasta el voladizo y
            // SelectBoardLength elige el largo de catálogo que alcanza (escalona 0,90 → 1,00 → 1,10 → 1,20).
            var bounds = new List<double> { primStart, primEnd };
            foreach (double pl in primLines)
            {
                if (pl > primStart + Eps && pl < primEnd - Eps
                    && pl - primStart >= LayoutCatalog.BoardLengthMinUsable
                    && primEnd - pl >= LayoutCatalog.BoardLengthMinUsable)
                {
                    bounds.Add(pl);
                }
            }

            bounds.Sort();

            for (int b = 0; b + 1 < bounds.Count; b++)
            {
                double p0 = bounds[b];
                double p1 = bounds[b + 1];
                double gap = p1 - p0; // distancia real riel a riel (centro a centro)
                if (gap < LayoutCatalog.BoardLengthMinUsable - Eps)
                {
                    continue; // tramo muy corto entre rieles: no lleva tablero.
                }

                double nominalLength = SelectBoardLength(gap); // medida de catálogo que encaja (o sobrepasa).

                // Extremo REAL de la losa+voladizo en el ancho, dentro de esta banda (para no sobresalir).
                (double lo, double hi)? ext = SlabSecExtentInBand(slab, board120AlongY, p0, p1);
                if (!ext.HasValue)
                {
                    continue;
                }

                double secStart = Math.Max(secCellStart, ext.Value.lo);
                double secEnd = Math.Min(secCellEnd, ext.Value.hi);
                double secExtent = secEnd - secStart;
                if (secExtent < Widths[^1] - Eps)
                {
                    continue;
                }

                // ── FILA DE AJUSTE (banda de 0,60 m) ──────────────────────────────────────────────────
                // No hay tablero de largo 0,60; se cubre GIRANDO el estándar 0,60×1,20: el 1,20 corre a lo
                // largo de la riel (dirección "sec"), el 0,60 llena la banda. Usa la mitad de tableros que una
                // banda normal y solo piezas del catálogo. SIEMPRE que la banda mida 0,60 (regla del negocio:
                // un separador de 60 siempre lleva tablero 0,60×1,20 girado), incluso pegada a una columna: la
                // celda ya viene recortada al borde de la columna, así que el girado nunca la invade.
                if (Math.Abs(gap - 0.60) < 0.03)
                {
                    // Regla del usuario: el tablero GIRADO (horizontal) SOLO existe para el separador de 0,60 y
                    // SIEMPRE es el estándar 0,60×1,20 COMPLETO. NUNCA un girado de 0,85/0,90 (no es óptimo).
                    // El sobrante final del recorrido (< 1,20) NO se convierte en un tablero de medida rara: va
                    // a REMATE (ajuste en obra).
                    double cursorRot = secStart;
                    while (secEnd - cursorRot > 0.02)
                    {
                        double seg = Math.Min(LayoutCatalog.BoardLength, secEnd - cursorRot);
                        Rect rot = board120AlongY
                            ? new Rect(cursorRot, p0, seg, gap)
                            : new Rect(p0, cursorRot, gap, seg);
                        if (IsSupported(rot, slab))
                        {
                            if (seg >= LayoutCatalog.BoardLength - 0.02)
                            {
                                // Pieza COMPLETA: tablero estándar 0,60×1,20 girado.
                                boards.Add(new BoardPiece(rot, 0.60, LayoutCatalog.BoardLength));
                            }
                            else
                            {
                                remates.Add(rot); // sobrante < 1,20 → remate, nunca un girado 0,85/0,90
                            }
                        }

                        cursorRot += seg;
                    }

                    continue;
                }

                // ── SALVAGUARDA: el tablero NUNCA se estira más de 1,20 m ─────────────────────────────
                // Si la banda (riel a riel + cantiléver de orilla) mide MÁS de 1,20 m — el tablero más largo
                // del catálogo —, el tablero se dibuja a 1,20 (su tamaño REAL, apoyado en el riel interior) y
                // el sobrante se marca como REMATE. Así el modelo nunca dibuja un "1,20" estirado que en la
                // realidad no alcanza; muestra el tablero honesto + la franja que queda por cubrir.
                double drawnPrim = Math.Min(gap, LayoutCatalog.BoardLength);
                double boardPrimStart = p0;
                bool hasExcess = gap > LayoutCatalog.BoardLength + Eps;
                double excessLo = 0.0;
                double excessHi = 0.0;
                if (hasExcess)
                {
                    bool topEdge = p0 <= slabPrimMin + Eps; // orilla superior: el riel interior es p1
                    if (topEdge)
                    {
                        boardPrimStart = p1 - drawnPrim; // apoya en el riel interior (p1); remate hacia afuera
                        excessLo = p0;
                        excessHi = p1 - drawnPrim;
                    }
                    else
                    {
                        boardPrimStart = p0;             // apoya en el riel interior (p0); remate hacia afuera
                        excessLo = p0 + drawnPrim;
                        excessHi = p1;
                    }
                }

                nominalLength = SelectBoardLength(drawnPrim); // etiqueta = largo REAL dibujado (tope 1,20)

                // ── REPARTO DE ANCHO POR COMBINACIÓN ──────────────────────────────────────────────────
                // Mide el espacio real (de filo de columna a filo de columna, o de filo de columna al
                // borde+voladizo de la losa) y lo TAPA con la MEJOR COMBINACIÓN de anchos del catálogo:
                // prioriza tableros de 0,60 y cierra el hueco con las medidas que hagan falta (0,55…0,30),
                // admitiendo ~1–4 cm de sobre-cobertura hacia el filo con tal de NO dejar remate.
                //   Ej.: 0,70 (0,50 + 0,20 voladizo) → 0,40 + 0,30   (no un 0,60 que dejaría remate)
                //        1,80 (filo a filo)          → 0,60 + 0,60 + 0,60
                //        1,94 (filo a filo)          → 0,60 + 0,60 + 0,45 + 0,30 ≈ 1,95 (sobra 1 cm)
                void PlaceSec(double at, double w, double nominal)
                {
                    Rect r = board120AlongY
                        ? new Rect(at, boardPrimStart, w, drawnPrim)
                        : new Rect(boardPrimStart, at, drawnPrim, w);
                    if (IsSupported(r, slab))
                    {
                        boards.Add(new BoardPiece(r, w, nominal));
                    }
                }

                List<double> widths = ChooseBoardWidthsCombo(secExtent);
                double cursorSec = secStart;
                foreach (double width in widths)
                {
                    // El tablero abarca de riel a riel (alto = drawnPrim, tope 1,20); nominal = medida real.
                    PlaceSec(cursorSec, width, nominalLength);
                    cursorSec += width;
                }

                double leftover = secEnd - cursorSec;
                if (leftover > 0.02)
                {
                    // Espacio menor que el tablero más chico del catálogo: va a remate (ajuste en obra).
                    Rect rmt = board120AlongY
                        ? new Rect(cursorSec, boardPrimStart, leftover, drawnPrim)
                        : new Rect(boardPrimStart, cursorSec, drawnPrim, leftover);
                    if (IsSupported(rmt, slab))
                    {
                        remates.Add(rmt);
                    }
                }

                // Excedente de la banda por encima de 1,20 m: franja de REMATE (no se estira ningún tablero).
                if (hasExcess && excessHi - excessLo > 0.02)
                {
                    Rect rmtEx = board120AlongY
                        ? new Rect(secStart, excessLo, secEnd - secStart, excessHi - excessLo)
                        : new Rect(excessLo, secStart, excessHi - excessLo, secEnd - secStart);
                    if (IsSupported(rmtEx, slab))
                    {
                        remates.Add(rmtEx);
                    }
                }
            }
        }

        /// <summary>Acceso interno a la combinación de anchos de catálogo (lo usan los módulos de acabado).</summary>
        internal static List<double> ComboWidthsForSpan(double width) => ChooseBoardWidthsCombo(width);

        /// <summary>Acceso interno al largo de catálogo que cubre un tramo (lo usan los módulos de acabado).</summary>
        internal static double PickBoardLength(double need) => SelectBoardLength(need);

        /// <summary>Largo de catálogo más chico que cubre <paramref name="need"/> (si ninguno, el estándar 1,20).</summary>
        private static double SelectBoardLength(double need)
        {
            double best = LayoutCatalog.BoardLength;
            foreach (double length in LayoutCatalog.BoardLengthsDescending)
            {
                if (length + Eps >= need && length <= best + Eps)
                {
                    best = length;
                }
            }

            return best;
        }

        /// <summary>
        /// MÓDULO DE COMBINACIÓN DE ANCHOS. Mide un espacio y devuelve la MEJOR combinación de anchos del
        /// catálogo (0,60 … 0,30) que lo TAPA por completo, minimizando la sobre-cobertura (para no dejar
        /// remate) y priorizando los tableros de 0,60. Criterio, en orden:
        ///   1) menor sobre-cobertura (el total cubre el espacio con el menor sobrante posible, ≤ ~4 cm);
        ///   2) más tableros de 0,60;
        ///   3) menos piezas;
        ///   4) piezas más grandes primero (desempate).
        /// Ej.: 0,70 → {0,40; 0,30} · 1,80 → {0,60×3} · 1,94 → {0,60; 0,60; 0,45; 0,30} (≈1,95).
        /// Devuelve lista vacía si el espacio es menor que el tablero más chico (el llamador lo manda a remate).
        /// Cálculo en centímetros enteros; todas las medidas son múltiplos de 5 cm.
        /// </summary>
        private static List<double> ChooseBoardWidthsCombo(double width)
        {
            int target = (int)Math.Round(width * 100);
            int[] cat = { 60, 55, 50, 45, 40, 35, 30 };
            int minPiece = 30;

            if (target < minPiece - 2)
            {
                return new List<double>(); // demasiado chico para cualquier tablero → remate
            }

            // Sumas alcanzables desde el objetivo hasta el objetivo + un tablero (cota de sobre-cobertura).
            int cap = target + 60;
            var best = new Dictionary<int, Combo> { [0] = Combo.Empty };
            for (int s = 5; s <= cap; s += 5)
            {
                Combo? chosen = null;
                foreach (int w in cat)
                {
                    if (s - w >= 0 && best.TryGetValue(s - w, out Combo? prev))
                    {
                        Combo cand = prev.With(w);
                        if (chosen == null || cand.IsBetterThan(chosen))
                        {
                            chosen = cand;
                        }
                    }
                }

                if (chosen != null)
                {
                    best[s] = chosen;
                }
            }

            // Menor sobre-cobertura: la primera suma alcanzable ≥ objetivo.
            for (int s = target; s <= cap; s++)
            {
                if (best.TryGetValue(s, out Combo? combo))
                {
                    return combo.Widths.Select(cm => cm / 100.0).ToList();
                }
            }

            return new List<double>();
        }

        /// <summary>Combinación de anchos (cm) para el módulo <see cref="ChooseBoardWidthsCombo"/>.</summary>
        private sealed class Combo
        {
            private Combo(List<int> widths, int count60, int total)
            {
                Widths = widths;
                Count60 = count60;
                Total = total;
            }

            public static Combo Empty { get; } = new(new List<int>(), 0, 0);

            public List<int> Widths { get; }

            public int Count60 { get; }

            public int Total { get; }

            public Combo With(int w)
            {
                var next = new List<int>(Widths) { w };
                next.Sort((a, b) => b.CompareTo(a)); // descendente, para desempate por piezas grandes
                return new Combo(next, Count60 + (w == 60 ? 1 : 0), Total + w);
            }

            /// <summary>Mejor = más 0,60, luego menos piezas, luego piezas más grandes (lexicográfico desc).</summary>
            public bool IsBetterThan(Combo other)
            {
                if (Count60 != other.Count60)
                {
                    return Count60 > other.Count60;
                }

                if (Widths.Count != other.Widths.Count)
                {
                    return Widths.Count < other.Widths.Count;
                }

                for (int i = 0; i < Widths.Count; i++)
                {
                    if (Widths[i] != other.Widths[i])
                    {
                        return Widths[i] > other.Widths[i];
                    }
                }

                return false;
            }
        }

        // Aquí vivía un SEGUNDO reparto de anchos por programación dinámica (ChooseBoardWidths y su
        // clase Combination), de la etapa anterior del motor. Lo sustituyó ChooseBoardWidthsCombo/Combo
        // y su único invocador era el relleno junto a columnas, que también quedó sin uso: se retiró todo.
    }
}
