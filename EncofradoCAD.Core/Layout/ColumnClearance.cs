using System;
using System.Collections.Generic;
using System.Linq;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// Desplaza RÍGIDAMENTE toda la rejilla (todas las líneas de un eje por el mismo δ) para que los rieles
    /// —y con ellos toda la estructura— no choquen con las columnas: cada línea queda como mínimo
    /// <see cref="MinClearance"/> (filo del riel → filo de la columna) separada de cualquier columna.
    ///
    /// Como las columnas suelen alinearse a la rejilla, un único desplazamiento uniforme por eje las libra a
    /// todas a la vez (cada columna, que estaba sobre una línea, cae en el hueco entre líneas). Se elige el
    /// |δ| más pequeño posible; si no hace falta mover (ya hay holgura) se devuelve la rejilla igual.
    /// </summary>
    public static class ColumnClearance
    {
        /// <summary>Holgura PREFERIDA entre el filo del riel y el filo de la columna (metros).</summary>
        public const double TargetClearance = 0.15;

        /// <summary>Holgura MÍNIMA aceptable (relajación): si a 15 cm no se libra todo, se admite hasta 5 cm.
        /// Se garantiza SIEMPRE una separación &gt; 0, de modo que la fila de rieles nunca se sobrepone a la
        /// fila de columnas; solo se permite acercarse más que antes (10 cm) cuando la geometría lo exige.</summary>
        public const double MinClearance = 0.05;

        /// <summary>Semiancho del riel (debe coincidir con RailWidth/2 del dibujo 3D: 0,15/2).</summary>
        public const double RailHalfWidth = 0.075;

        /// <summary>Tope del desplazamiento de la retícula (m). Como el movimiento es rígido (todas las líneas
        /// se mueven igual y nunca se reordenan), el tope solo limita cuánto puede derivar el bloque; basta con
        /// que cubra el mayor "alcance" de columna (col 45 → 0,225 + 0,075 + 0,15 = 0,45 m).</summary>
        private const double MaxShift = 0.60;

        private const double Eps = 1e-4;

        /// <summary>
        /// Devuelve las líneas del eje desplazadas por el δ mínimo que garantiza la holgura contra todas las
        /// columnas. <paramref name="axisX"/>=true trata las líneas como posiciones en X (usa Width de la
        /// columna); false, como posiciones en Y (usa Length de la columna).
        /// </summary>
        public static List<double> ShiftToClearColumns(List<double> lines, List<ColumnSnapshot> columns, bool axisX)
        {
            if (lines == null || lines.Count == 0 || columns == null || columns.Count == 0)
            {
                return lines ?? new List<double>();
            }

            double shift = ComputeShift(lines, columns, axisX);
            if (Math.Abs(shift) < Eps)
            {
                return lines;
            }

            // Desplazamiento RÍGIDO: toda la retícula (líneas exteriores incluidas) se mueve el mismo δ. Así
            // las bandas de separador conservan SIEMPRE su medida de catálogo (0,60/0,80/0,90/1,00/1,10/1,20);
            // ninguna banda de orilla se redimensiona a una medida inventada. Contrapartida: al mover el bloque
            // completo el voladizo puede quedar algo disparejo entre lados opuestos, lo cual es preferible a
            // usar separadores fuera de catálogo (decisión del usuario: prioridad al catálogo real).
            return lines.Select(v => v + shift).ToList();
        }

        /// <summary>
        /// δ (puede ser 0) que desplaza TODA la retícula para librar las columnas. Busca por barrido fino el
        /// menor |δ| con el que NINGUNA línea quede a menos de la holgura (filo de riel a filo de columna) de
        /// NINGUNA columna. Trabaja en dos niveles: primero intenta la holgura PREFERIDA (15 cm); si por la
        /// geometría no logra librar todo a 15 cm, RELAJA a la mínima (10 cm). Si aún así no existe un δ que
        /// las libre todas (columnas muy juntas/irregulares), devuelve el que deja MENOS choques (mejor
        /// esfuerzo). La holgura de cada columna usa su dimensión en ESE eje, así vale para cualquier medida
        /// del catálogo, incluidas las rectangulares.
        /// </summary>
        public static double ComputeShift(List<double> lines, List<ColumnSnapshot> columns, bool axisX)
        {
            // Centro y medio ancho (en este eje) de cada columna.
            var cols = new List<(double center, double half)>();
            foreach (ColumnSnapshot col in columns)
            {
                double center = axisX ? col.X : col.Y;
                double half = (axisX ? col.Width : col.Length) / 2.0;
                cols.Add((center, half));
            }

            return ComputeShift(lines, cols);
        }

        /// <summary>
        /// δ (puede ser 0) que desplaza TODA la retícula para librar las columnas ya proyectadas al eje como
        /// pares (centro, semiancho). Misma lógica que la sobrecarga con <see cref="ColumnSnapshot"/>; se
        /// expone para el planificador de rieles, que necesita conocer el δ ANTES de fijar el voladizo.
        /// </summary>
        public static double ComputeShift(List<double> lines, List<(double center, double half)> cols)
        {
            if (lines == null || lines.Count == 0 || cols == null || cols.Count == 0)
            {
                return 0.0;
            }

            int Choques(double delta, double clearance)
            {
                int v = 0;
                foreach ((double center, double half) in cols)
                {
                    foreach (double line in lines)
                    {
                        if (Math.Abs(center - (line + delta)) < half + RailHalfWidth + clearance - Eps)
                        {
                            v++;
                        }
                    }
                }

                return v;
            }

            (double delta, int choques) BestShift(double clearance)
            {
                int baseChoques = Choques(0.0, clearance);
                if (baseChoques == 0)
                {
                    return (0.0, 0);
                }

                double mejorDelta = 0.0;
                int mejorChoques = baseChoques;
                for (double d = 0.01; d <= MaxShift + Eps; d += 0.01)
                {
                    foreach (double delta in new[] { d, -d }) // ambos sentidos, |δ| creciente
                    {
                        int v = Choques(delta, clearance);
                        if (v < mejorChoques || (v == mejorChoques && Math.Abs(delta) < Math.Abs(mejorDelta)))
                        {
                            mejorChoques = v;
                            mejorDelta = delta;
                        }

                        if (mejorChoques == 0)
                        {
                            return (mejorDelta, 0); // primer δ (el más pequeño) que libra TODAS a esta holgura
                        }
                    }
                }

                return (mejorDelta, mejorChoques);
            }

            // 1) Intenta librar todo a la holgura preferida (15 cm).
            (double d15, int c15) = BestShift(TargetClearance);
            if (c15 == 0)
            {
                return d15;
            }

            // 2) Relaja a la holgura mínima (10 cm) si a 15 cm no se pudo librar todo.
            (double d10, int c10) = BestShift(MinClearance);
            if (c10 == 0)
            {
                return d10;
            }

            // 3) Ni a 10 cm se libra todo: el desplazamiento con menos choques (mejor esfuerzo).
            return c10 <= c15 ? d10 : d15;
        }
    }
}
