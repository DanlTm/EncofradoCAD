using System;
using System.Collections.Generic;
using System.Linq;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// Ajuste FINAL de rieles al catálogo. Regla del negocio (insistida por el usuario): el LARGO REAL de
    /// TODO riel dibujado debe ser SIEMPRE una medida que EXISTE en el catálogo real
    /// (<see cref="LayoutCatalog.RailLengths"/>). El sistema NUNCA debe mostrar un riel de una medida que no
    /// está en catálogo (p. ej. 1,59 m).
    ///
    /// El reparto interior ya cae en medidas de catálogo (uniones sobre puntal). El único punto donde un riel
    /// puede quedar con una medida "rara" es el BORDE: el generador de puntales fija el voladizo exterior en
    /// 20 cm, y la distancia del puntal exterior al primer puntal interior puede no coincidir con ninguna
    /// pieza de catálogo (queda corta, p. ej. 1,59 m &lt; 2,00 m).
    ///
    /// Solución: si un riel de borde queda MÁS CORTO que la pieza de catálogo que le corresponde, se ESTIRA su
    /// extremo EXTERIOR hacia afuera hasta completar la medida de catálogo (se usa "un riel de mayor medida").
    /// El puntal de apoyo NO se mueve: el riel simplemente vuela un poco más sobre el voladizo. Como los
    /// tableros solo cubren 20 cm más allá del borde, ese sobrante del riel queda sin tablero y no estorba
    /// ("aunque sobresalga un poco, no hay problema"). Así tableros, separadores y puntales quedan intactos y
    /// solo cambia el largo dibujado del riel exterior, que pasa a ser una medida REAL de catálogo.
    ///
    /// Solo se estiran las piezas MÁS EXTERIORES de cada línea de rieles (la primera y la última), que dan al
    /// vacío del voladizo. Las uniones interiores (entre dos rieles sobre un puntal) NUNCA se tocan: estirar
    /// ahí chocaría con el riel vecino.
    /// </summary>
    public static class RailEdgeExtender
    {
        /// <summary>Si el largo real está a ≤ esto de una medida de catálogo, ya se considera de catálogo
        /// (ruido de coma flotante); no se estira. Solo se estira cuando el riel queda claramente corto.</summary>
        private const double CatalogTolerance = 0.04;

        /// <summary>Catálogo de rieles ordenado ascendente (m).</summary>
        private static readonly double[] Catalog =
            LayoutCatalog.RailLengths.OrderBy(c => c).ToArray();

        /// <summary>
        /// Medida de catálogo a la que debe llevarse un riel de borde de largo <paramref name="actual"/>, o
        /// null si NO hay que tocarlo: (a) ya coincide con una medida de catálogo dentro de la tolerancia, o
        /// (b) es más largo que la pieza mayor del catálogo (no se encoge).
        /// </summary>
        private static double? TargetLength(double actual)
        {
            double nearest = Catalog.OrderBy(c => Math.Abs(c - actual)).First();
            if (Math.Abs(nearest - actual) <= CatalogTolerance)
            {
                return null; // ya es de catálogo (o ruido de coma flotante): no estirar.
            }

            foreach (double c in Catalog)
            {
                if (actual <= c + CatalogTolerance)
                {
                    return c; // menor medida de catálogo que cubre el vano: se estira hasta ella.
                }
            }

            return null; // más largo que el máximo del catálogo: no se toca.
        }

        /// <summary>
        /// Devuelve la lista de rieles con las piezas exteriores estiradas a su medida de catálogo. No muta la
        /// lista de entrada. Si la lista está vacía (eje sin rieles) se devuelve tal cual.
        /// </summary>
        public static List<LinearPiece> SnapToCatalog(List<LinearPiece> rails)
        {
            if (rails == null || rails.Count == 0)
            {
                return rails ?? new List<LinearPiece>();
            }

            var result = new List<LinearPiece>(rails);

            // Cada "línea de rieles" es una fila (misma coordenada perpendicular). Dentro de la línea puede
            // haber varias piezas y hasta varios tramos (partidos por columnas): la MÁS exterior de todas por
            // la izquierda/abajo y la MÁS exterior por la derecha/arriba son las que dan al voladizo libre.
            IEnumerable<IGrouping<double, int>> lines = Enumerable
                .Range(0, result.Count)
                .GroupBy(i => Math.Round(result[i].FixedCoord, 3));

            foreach (IGrouping<double, int> line in lines)
            {
                List<int> ordered = line
                    .OrderBy(i => Math.Min(result[i].Start, result[i].End))
                    .ToList();

                int firstIdx = ordered[0];                 // pieza exterior de MENOR coordenada
                result[firstIdx] = ExtendLowerEndOutward(result[firstIdx]);

                int lastIdx = ordered[^1];                 // pieza exterior de MAYOR coordenada
                if (lastIdx != firstIdx)
                {
                    result[lastIdx] = ExtendUpperEndOutward(result[lastIdx]);
                }
            }

            return result;
        }

        /// <summary>Estira el extremo de MENOR coordenada (borde exterior) dejando fijo el extremo interior.</summary>
        private static LinearPiece ExtendLowerEndOutward(LinearPiece piece)
        {
            double lo = Math.Min(piece.Start, piece.End);
            double hi = Math.Max(piece.Start, piece.End);
            double? target = TargetLength(hi - lo);
            if (target == null)
            {
                return piece;
            }

            double newLo = hi - target.Value; // interior fijo (hi); exterior baja hasta completar catálogo.
            return piece with { Start = newLo, End = hi };
        }

        /// <summary>Estira el extremo de MAYOR coordenada (borde exterior) dejando fijo el extremo interior.</summary>
        private static LinearPiece ExtendUpperEndOutward(LinearPiece piece)
        {
            double lo = Math.Min(piece.Start, piece.End);
            double hi = Math.Max(piece.Start, piece.End);
            double? target = TargetLength(hi - lo);
            if (target == null)
            {
                return piece;
            }

            double newHi = lo + target.Value; // interior fijo (lo); exterior sube hasta completar catálogo.
            return piece with { Start = lo, End = newHi };
        }
    }
}
