using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// Lista de materiales (BOM) de la LOSA, derivada del MISMO layout que dibuja el 3D. Cuenta EXACTAMENTE
    /// las mismas piezas que se renderizan en el modelo (mismos filtros de tamaño), de modo que el conteo del
    /// módulo Cálculo coincide pieza por pieza con lo que se ve en el plano 3D.
    ///
    /// Devuelve una lista ORDENADA de líneas <c>(Nombre, Cantidad)</c> donde Cantidad es texto: un entero para
    /// las piezas contables y un valor en m² para las tres tablas de superficie al final.
    /// </summary>
    public static class MaterialBill
    {
        // Debe coincidir con el filtro de remates de Preview3DScene (solo se dibujan/cuentan los > 2 cm).
        private const double RemateMinSize = 0.02;

        // Debe coincidir con GeometryEpsilon de Preview3DScene: AddBeam no dibuja piezas de longitud casi nula.
        private const double MinPieceLength = 0.001;

        // Riel estándar = 3,00 m; cualquier cosa por debajo es "riel pequeño".
        private const double RielEstandar = 3.00;

        // Separador estándar de catálogo.
        private const double SeparadorEstandar = 1.20;

        // Un tablero es "estándar" si mide 0,60 × 1,20 m (con tolerancia de catálogo).
        private const double TableroAnchoStd = 0.60;
        private const double TableroLargoStd = 1.20;

        // Distancia (m) a la que un tablero se considera pegado a una columna (→ cierre de columna).
        private const double CercaColumna = 0.30;

        // Voladizo del encofrado: 20 cm por lado (igual que el resto del motor).
        private const double Voladizo = 0.20;

        /// <summary>
        /// Construye la lista de materiales de la losa. <paramref name="slab"/> y <paramref name="columns"/>
        /// se usan para clasificar los tableros pequeños (cierre de columna vs. volado) y para calcular las
        /// superficies de encofrado, cierre y volado.
        /// </summary>
        public static List<(string Nombre, string Cantidad)> Build(
            PreviewLayout layout, List<Point> slab, List<ColumnSnapshot> columns)
        {
            CultureInfo es = CultureInfo.GetCultureInfo("es-ES");
            var bill = new List<(string Nombre, string Cantidad)>();
            slab ??= new List<Point>();
            columns ??= new List<ColumnSnapshot>();

            void AddPieza(string clave, int cantidad)
            {
                if (cantidad > 0)
                {
                    bill.Add((clave, cantidad.ToString(CultureInfo.InvariantCulture)));
                }
            }

            void AddArea(string clave, double valor, string unidad)
            {
                bill.Add((clave, $"{valor.ToString("0.00", es)} {unidad}"));
            }

            // ── Puntales ──────────────────────────────────────────────────────
            AddPieza("Puntal de 2 m a 3,5 m", layout.Puntales.Count);

            // ── Rieles: pequeño (< 3 m) y estándar (3 m). Solo los que el 3D dibuja (longitud real > 0). ──
            List<LinearPiece> rieles = layout.HorizontalRails
                .Concat(layout.VerticalRails)
                .Where(r => r.ActualLength > MinPieceLength)
                .ToList();
            int rielPequeno = rieles.Count(r => Math.Round(r.NominalLength, 2) < RielEstandar - 0.005);
            int rielEstandar = rieles.Count - rielPequeno;
            AddPieza("Riel pequeño (menor a 3 m)", rielPequeno);
            AddPieza("Riel estándar (3,00 m)", rielEstandar);

            // ── Separadores agrupados por medida de catálogo. El de 1,20 m es el estándar. ──
            foreach (IGrouping<double, LinearPiece> grupo in layout.HorizontalSeparators
                         .Concat(layout.VerticalSeparators)
                         .Where(sep => sep.ActualLength > MinPieceLength)
                         .GroupBy(sep => Math.Round(sep.NominalLength, 2))
                         .OrderByDescending(g => g.Key))
            {
                bool estandar = Math.Abs(grupo.Key - SeparadorEstandar) < 0.005;
                string etiqueta = estandar
                    ? "Separador estándar (1,20 m)"
                    : $"Separador ({grupo.Key.ToString("0.##", es)} m)";
                AddPieza(etiqueta, grupo.Count());
            }

            // ── Tijeras / crucetas de arriostre (piezas reales del 3D). ──
            AddPieza("Tijera / cruceta pequeña (1,20 m)", layout.SmallCrucetas.Count);
            AddPieza("Tijera / cruceta grande (3,00 m)", layout.LargeCrucetas.Count);

            // ── Tableros: estándar 0,60 × 1,20; pequeños divididos en cierre de columna y volado. ──
            AddTableros(bill, layout, slab, columns, es);

            // ── Tablas de superficie (m²). ──
            double areaLosa = slab.Count >= 3 ? GeometryUtil.PolygonArea(slab) : 0.0;
            double areaEncofrado = slab.Count >= 3
                ? GeometryUtil.PolygonArea(GeometryUtil.OffsetPolygon(slab, Voladizo))
                : 0.0;
            double areaCierre = layout.Tablas
                .Where(t => t.Width > RemateMinSize && t.Height > RemateMinSize)
                .Sum(t => t.Width * t.Height);
            double areaVolado = Math.Max(0.0, areaEncofrado - areaLosa);

            AddArea("Tabla para encofrado", areaEncofrado, "m²");
            AddArea("Tabla de cierre de columna", areaCierre, "m²");
            AddArea("Tabla de volado", areaVolado, "m²");

            return bill;
        }

        /// <summary>
        /// Agrupa los tableros de contacto por medida y rol. Estándar = 0,60 × 1,20 m. Los tableros pequeños
        /// (cualquier otra medida) se reparten en dos familias según su ubicación real en el plano:
        /// pegados a una columna → "para cierre de columna"; fuera del borde de la losa (en el voladizo de
        /// 20 cm) → "para volado".
        /// </summary>
        private static void AddTableros(
            List<(string Nombre, string Cantidad)> bill,
            PreviewLayout layout,
            List<Point> slab,
            List<ColumnSnapshot> columns,
            CultureInfo es)
        {
            int estandar = 0;
            var cierre = new Dictionary<(double W, double L), int>();
            var volado = new Dictionary<(double W, double L), int>();

            foreach (BoardPiece board in layout.Boards)
            {
                double w = Math.Round(board.Width, 2);
                double l = Math.Round(board.Length, 2);
                bool esEstandar = w >= TableroAnchoStd - 0.005 && l >= TableroLargoStd - 0.005;
                if (esEstandar)
                {
                    estandar++;
                    continue;
                }

                bool pegadoColumna = columns.Any(c => DistanciaRectColumna(board.Rect, c) < CercaColumna);
                var clave = (w, l);
                if (pegadoColumna || !EnVolado(board.Rect, slab))
                {
                    cierre[clave] = cierre.TryGetValue(clave, out int cc) ? cc + 1 : 1;
                }
                else
                {
                    volado[clave] = volado.TryGetValue(clave, out int cv) ? cv + 1 : 1;
                }
            }

            if (estandar > 0)
            {
                bill.Add(("Tablero estándar (0,60 × 1,20 m)", estandar.ToString(CultureInfo.InvariantCulture)));
            }

            foreach (KeyValuePair<(double W, double L), int> g in cierre
                         .OrderByDescending(p => p.Key.W).ThenByDescending(p => p.Key.L))
            {
                string medida = $"{g.Key.W.ToString("0.##", es)} × {g.Key.L.ToString("0.##", es)} m";
                bill.Add(($"Tablero pequeño para cierre de columna ({medida})",
                    g.Value.ToString(CultureInfo.InvariantCulture)));
            }

            foreach (KeyValuePair<(double W, double L), int> g in volado
                         .OrderByDescending(p => p.Key.W).ThenByDescending(p => p.Key.L))
            {
                string medida = $"{g.Key.W.ToString("0.##", es)} × {g.Key.L.ToString("0.##", es)} m";
                bill.Add(($"Tablero pequeño para volado ({medida})",
                    g.Value.ToString(CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>Distancia mínima (m) entre un rectángulo y la huella de una columna (0 si se solapan).</summary>
        private static double DistanciaRectColumna(Rect r, ColumnSnapshot c)
        {
            double dx = Math.Max(0.0, Math.Max(c.MinX - r.Right, r.Left - c.MaxX));
            double dy = Math.Max(0.0, Math.Max(c.MinY - r.Bottom, r.Top - c.MaxY));
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        /// <summary>¿El tablero cae (aunque sea en parte) fuera del borde de la losa, es decir, en el voladizo?</summary>
        private static bool EnVolado(Rect r, List<Point> slab)
        {
            if (slab.Count < 3)
            {
                return false;
            }

            Point[] esquinas =
            {
                new Point(r.Left, r.Top),
                new Point(r.Right, r.Top),
                new Point(r.Left, r.Bottom),
                new Point(r.Right, r.Bottom)
            };
            return esquinas.Any(p => !GeometryUtil.IsPointInsidePolygon(p, slab));
        }
    }
}
