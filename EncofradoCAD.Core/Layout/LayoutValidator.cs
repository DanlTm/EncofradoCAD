using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// Verifica invariantes geométricas del layout para que el motor no dibuje piezas "que faltan o que no
    /// necesita". No lanza excepciones: recopila advertencias (para log/diagnóstico) y deja que el
    /// encofrado se dibuje igual. Sirve de red de seguridad ante cambios futuros.
    ///
    /// Invariantes comprobadas:
    ///  1. Todo extremo de riel/separador coincide con un puntal (ninguna pieza "colgando").
    ///  2. Ninguna pieza supera la medida máxima de su catálogo.
    ///  3. No hay puntales duplicados.
    ///  4. Toda cruceta une dos puntales existentes.
    ///  5. Ningún puntal queda totalmente aislado (sin riel ni separador que lo toque).
    ///  6. COBERTURA DE TABLEROS — ninguna parte de la losa (fuera de columnas) queda sin tablero ni remate.
    ///     Es la que "recuerda" las CASAS ESPECIALES: si una forma L/U/T o con escalón vuelve a dejar una
    ///     franja sin tableros, el verificador lo detecta y lo registra para que no pase inadvertido.
    ///  7. EFICIENCIA DE MATERIAL — ninguna línea de rieles/separadores tiene un puntal intermedio
    ///     redundante (dos piezas contiguas que caben en una sola). Verifica, después de generar, que "de
    ///     verdad se necesita esa cantidad de material" y avisa del ahorro si alguna vez sobrara una pieza.
    /// </summary>
    public static class LayoutValidator
    {
        private const double MaxRail = 3.00;
        private const double MaxSeparator = 1.20;
        private const double LengthTolerance = 0.05;
        private const double Overhang = 0.20;
        private const double CoverageTolerance = 0.12;

        /// <summary>Paso de muestreo (m) para verificar que la losa esté cubierta de tableros.</summary>
        private const double BoardSampleStep = 0.30;

        /// <summary>Fracción de losa sin tablero por encima de la cual se registra un caso especial sin cubrir.</summary>
        private const double BoardGapRatioTolerance = 0.02;

        /// <summary>Holgura (m) para considerar dos piezas contiguas y evaluables como fusionables.</summary>
        private const double MergeTolerance = 0.03;

        public static List<string> Validate(PreviewLayout layout, List<Point> slab, bool primaryHorizontal, List<ColumnSnapshot> columns)
        {
            var issues = new List<string>();
            List<Point> puntales = layout.Puntales;

            // 0: COBERTURA — cada fila de rieles debe llegar al borde real de la losa ± voladizo. Si no, esa
            // parte de la losa quedaría sin apoyo (no se podría fundir). Es una red de seguridad: si algún
            // caso futuro deja una fila corta, queda registrado en el log.
            CheckRailCoverage(primaryHorizontal ? layout.HorizontalRails : layout.VerticalRails, slab, primaryHorizontal, issues);

            // 0b: COBERTURA DE TABLEROS — la red de seguridad para las CASAS ESPECIALES. Recorre la losa y
            // comprueba que cada zona (fuera de columnas) tenga tablero o remate encima; si una forma
            // irregular deja una franja descubierta, lo registra con su ubicación aproximada.
            CheckBoardCoverage(layout, slab, columns, issues);

            // 1 y 2: rieles y separadores apoyados y dentro de catálogo. Los rieles admiten VOLADIZO: su
            // extremo exterior puede volar sobre el borde sin puntal justo en la punta (ver RailEdgeExtender),
            // por eso se validan con allowCantilever = true (basta con que la pieza se apoye en un puntal).
            CheckMembers(layout.HorizontalRails, puntales, MaxRail, "Riel", issues, allowCantilever: true);
            CheckMembers(layout.VerticalRails, puntales, MaxRail, "Riel", issues, allowCantilever: true);
            CheckMembers(layout.HorizontalSeparators, puntales, MaxSeparator, "Separador", issues, allowCantilever: false);
            CheckMembers(layout.VerticalSeparators, puntales, MaxSeparator, "Separador", issues, allowCantilever: false);

            // 2c: CATÁLOGO DE RIELES — el largo REAL de cada riel dibujado debe ser una medida que EXISTE en
            // el catálogo (regla del usuario: nunca un riel "de 1,59 m"). Es la red de seguridad del ajuste
            // de RailEdgeExtender: si alguna vez un riel quedara con una medida fuera de catálogo, se registra.
            CheckRailCatalog(layout.HorizontalRails, issues);
            CheckRailCatalog(layout.VerticalRails, issues);

            // 2b: EFICIENCIA DE MATERIAL — verificación posterior al modelo de que no se gasta material de
            // más. Detecta puntales intermedios REDUNDANTES: dos piezas contiguas (riel o separador) que,
            // sumadas, caben en UNA sola pieza de catálogo → el puntal que las separa sobra. Si aparece,
            // se registra con el ahorro posible. Hoy no debería dispararse (el reparto ya es mínimo), pero
            // es la red de seguridad que garantiza que "de verdad se necesita esa cantidad de material".
            CheckMaterialEfficiency(layout, issues);

            // 3: puntales duplicados.
            for (int i = 0; i < puntales.Count; i++)
            {
                for (int j = i + 1; j < puntales.Count; j++)
                {
                    if (GeometryUtil.NearlyEqual(puntales[i].X, puntales[j].X)
                        && GeometryUtil.NearlyEqual(puntales[i].Y, puntales[j].Y))
                    {
                        issues.Add($"Puntal duplicado en ({puntales[i].X:0.##}, {puntales[i].Y:0.##}).");
                    }
                }
            }

            // 4: crucetas entre puntales existentes.
            CheckCrucetas(layout.SmallCrucetas, puntales, "Cruceta pequeña", issues);
            CheckCrucetas(layout.LargeCrucetas, puntales, "Cruceta grande", issues);

            // 5: puntales aislados.
            var touched = new HashSet<int>();
            MarkTouched(layout.HorizontalRails, puntales, touched);
            MarkTouched(layout.VerticalRails, puntales, touched);
            MarkTouched(layout.HorizontalSeparators, puntales, touched);
            MarkTouched(layout.VerticalSeparators, puntales, touched);
            for (int i = 0; i < puntales.Count; i++)
            {
                if (!touched.Contains(i))
                {
                    issues.Add($"Puntal aislado (sin riel ni separador) en ({puntales[i].X:0.##}, {puntales[i].Y:0.##}).");
                }
            }

            foreach (string issue in issues)
            {
                Debug.WriteLine($"[LayoutValidator] {issue}");
            }

            return issues;
        }

        /// <summary>
        /// Cada fila de rieles (agrupada por su coordenada perpendicular) debe cubrir el ancho de la losa en
        /// esa fila + 20 cm de voladizo a cada lado. Si el riel se queda corto respecto al borde real, se
        /// registra: esa zona de la losa quedaría sin apoyo.
        /// </summary>
        private static void CheckRailCoverage(List<LinearPiece> rails, List<Point> slab, bool primaryHorizontal, List<string> issues)
        {
            foreach (IGrouping<double, LinearPiece> group in rails.GroupBy(r => System.Math.Round(r.FixedCoord, 3)))
            {
                double coord = group.Key;
                List<double> crossings = primaryHorizontal
                    ? GeometryUtil.HorizontalCrossings(slab, coord)
                    : GeometryUtil.VerticalCrossings(slab, coord);
                if (crossings.Count < 2)
                {
                    continue; // fila de voladizo (fuera de la losa): la cubre la proyección, no aplica aquí.
                }

                double minStart = group.Min(r => System.Math.Min(r.Start, r.End));
                double maxEnd = group.Max(r => System.Math.Max(r.Start, r.End));
                double needLo = crossings[0] - Overhang;
                double needHi = crossings[^1] + Overhang;

                if (minStart > needLo + CoverageTolerance || maxEnd < needHi - CoverageTolerance)
                {
                    issues.Add($"Fila de riel en {coord:0.##}: no llega al borde de la losa. Necesita [{needLo:0.##}, {needHi:0.##}] y va [{minStart:0.##}, {maxEnd:0.##}].");
                }
            }
        }

        /// <summary>
        /// COBERTURA DE TABLEROS (memoria de casos especiales). Muestrea el interior de la losa en una malla
        /// fina y comprueba que cada punto —que no caiga dentro de una columna— esté cubierto por un tablero
        /// o por una zona de remate. Los puntos de losa que quedan sin cubrir delatan una CASA ESPECIAL
        /// (forma L/U/T, escalón o protuberancia) que el generador de tableros no llenó. No corrige: registra
        /// el hueco (cantidad, porcentaje y ubicación del primero) para que quede constancia en el log y el
        /// caso no pase inadvertido la próxima vez que se presente.
        /// </summary>
        private static void CheckBoardCoverage(PreviewLayout layout, List<Point> slab, List<ColumnSnapshot> columns, List<string> issues)
        {
            if (slab.Count < 3)
            {
                return;
            }

            double minX = slab.Min(p => p.X);
            double maxX = slab.Max(p => p.X);
            double minY = slab.Min(p => p.Y);
            double maxY = slab.Max(p => p.Y);

            int sampled = 0;
            int uncovered = 0;
            double firstGapX = double.NaN;
            double firstGapY = double.NaN;

            for (double x = minX + (BoardSampleStep / 2); x < maxX; x += BoardSampleStep)
            {
                for (double y = minY + (BoardSampleStep / 2); y < maxY; y += BoardSampleStep)
                {
                    var point = new Point(x, y);

                    // Solo interesa la losa REAL (no el voladizo exterior).
                    if (!GeometryUtil.IsPointInsidePolygon(point, slab))
                    {
                        continue;
                    }

                    // Bajo una columna no va tablero: no es un hueco, se ignora.
                    if (columns.Any(column => column.Contains(point, 0.0)))
                    {
                        continue;
                    }

                    sampled++;

                    bool covered = layout.Boards.Any(board => board.Rect.Contains(point))
                        || layout.Remates.Any(remate => remate.Contains(point))
                        || layout.Tablas.Any(tabla => tabla.Contains(point))
                        || layout.BorderTablas.Any(tabla => tabla.Contains(point));
                    if (!covered)
                    {
                        uncovered++;
                        if (double.IsNaN(firstGapX))
                        {
                            firstGapX = x;
                            firstGapY = y;
                        }
                    }
                }
            }

            if (sampled == 0)
            {
                return;
            }

            double ratio = (double)uncovered / sampled;
            if (uncovered > 0 && ratio > BoardGapRatioTolerance)
            {
                issues.Add($"Cobertura de tableros: {uncovered}/{sampled} puntos de la losa sin tablero (~{ratio:P0}). "
                    + $"Primer hueco cerca de ({firstGapX:0.##}, {firstGapY:0.##}). "
                    + "Posible CASA ESPECIAL (forma L/U/T o escalón) sin cubrir — revisar tableros en esa zona.");
            }
        }

        private static void CheckMembers(List<LinearPiece> pieces, List<Point> puntales, double maxLength, string tipo, List<string> issues, bool allowCantilever)
        {
            foreach (LinearPiece piece in pieces)
            {
                Point a = piece.Horizontal ? new Point(piece.Start, piece.FixedCoord) : new Point(piece.FixedCoord, piece.Start);
                Point b = piece.Horizontal ? new Point(piece.End, piece.FixedCoord) : new Point(piece.FixedCoord, piece.End);

                bool aOk = HasPuntal(puntales, a);
                bool bOk = HasPuntal(puntales, b);

                // Un riel puede VOLAR sobre el borde: la punta exterior no lleva puntal, pero la pieza sigue
                // apoyada si algún puntal cae SOBRE ella (entre sus extremos). En ese caso el extremo "suelto"
                // es un voladizo válido, no una pieza colgando. Los separadores no vuelan: exigen puntal en
                // ambos extremos.
                if (allowCantilever && (!aOk || !bOk) && HasPuntalOnSegment(puntales, piece))
                {
                    aOk = true;
                    bOk = true;
                }

                if (!aOk)
                {
                    issues.Add($"{tipo} con extremo sin puntal en ({a.X:0.##}, {a.Y:0.##}).");
                }

                if (!bOk)
                {
                    issues.Add($"{tipo} con extremo sin puntal en ({b.X:0.##}, {b.Y:0.##}).");
                }

                if (piece.ActualLength > maxLength + LengthTolerance)
                {
                    issues.Add($"{tipo} demasiado largo: {piece.ActualLength:0.##} m (máx {maxLength:0.##}).");
                }
            }
        }

        /// <summary>
        /// CATÁLOGO DE RIELES. Comprueba que el largo REAL (dibujado) de cada riel coincida con una medida que
        /// EXISTE en <see cref="LayoutCatalog.RailLengths"/>. Es la garantía de que el sistema nunca muestra un
        /// riel de una medida inventada (p. ej. 1,59 m): tras <see cref="RailEdgeExtender"/> todo riel debe
        /// medir 1,00 / 1,50 / 2,00 / 2,50 / 3,00 m. Cualquier desviación se registra para diagnóstico.
        /// </summary>
        private static void CheckRailCatalog(List<LinearPiece> rails, List<string> issues)
        {
            foreach (LinearPiece rail in rails)
            {
                bool enCatalogo = LayoutCatalog.RailLengths
                    .Any(c => System.Math.Abs(c - rail.ActualLength) <= LengthTolerance);
                if (!enCatalogo)
                {
                    issues.Add($"Riel fuera de catálogo: {rail.ActualLength:0.##} m en "
                        + $"({(rail.Horizontal ? System.Math.Min(rail.Start, rail.End) : rail.FixedCoord):0.##}, "
                        + $"{(rail.Horizontal ? rail.FixedCoord : System.Math.Min(rail.Start, rail.End)):0.##}) — "
                        + "debe ser una medida real del catálogo de rieles.");
                }
            }
        }

        /// <summary>
        /// EFICIENCIA DE MATERIAL. Busca puntales intermedios REDUNDANTES: en una misma línea (fila de
        /// rieles o de separadores), dos piezas contiguas cuya suma cabe en UNA sola pieza de catálogo. Si
        /// existen, el puntal que las separa sobra y podría quitarse (ahorro de un puntal + fusión de dos
        /// piezas en una). No corrige: registra el hallazgo con el ahorro para revisión.
        /// </summary>
        private static void CheckMaterialEfficiency(PreviewLayout layout, List<string> issues)
        {
            CheckDirectionRedundancy(layout.HorizontalRails, MaxRail, "Riel", issues);
            CheckDirectionRedundancy(layout.VerticalRails, MaxRail, "Riel", issues);
            CheckDirectionRedundancy(layout.HorizontalSeparators, MaxSeparator, "Separador", issues);
            CheckDirectionRedundancy(layout.VerticalSeparators, MaxSeparator, "Separador", issues);
        }

        private static void CheckDirectionRedundancy(List<LinearPiece> pieces, double maxLength, string tipo, List<string> issues)
        {
            foreach (IGrouping<double, LinearPiece> group in pieces.GroupBy(p => System.Math.Round(p.FixedCoord, 2)))
            {
                List<LinearPiece> ordered = group
                    .OrderBy(p => System.Math.Min(p.Start, p.End))
                    .ToList();

                for (int i = 0; i + 1 < ordered.Count; i++)
                {
                    LinearPiece a = ordered[i];
                    LinearPiece b = ordered[i + 1];
                    double aEnd = System.Math.Max(a.Start, a.End);
                    double bStart = System.Math.Min(b.Start, b.End);

                    // Contiguas (comparten un puntal) y juntas caben CÓMODAMENTE en una sola pieza de
                    // catálogo (suma por debajo del máximo, no apenas encima). La tolerancia va RESTANDO en
                    // la suma: así una subdivisión legítima de borde curvo/inclinado (p. ej. 1,51+1,51 = 3,02
                    // que NO cabe en un riel de 3,00) no se marca como redundante por error.
                    if (System.Math.Abs(aEnd - bStart) <= MergeTolerance
                        && (a.ActualLength + b.ActualLength) <= maxLength - MergeTolerance)
                    {
                        issues.Add($"{tipo}: dos piezas contiguas ({a.ActualLength:0.##} m + {b.ActualLength:0.##} m) "
                            + $"en {group.Key:0.##} caben en UNA de {(a.ActualLength + b.ActualLength):0.##} m — "
                            + "puntal intermedio redundante (se puede ahorrar un puntal).");
                    }
                }
            }
        }

        private static void CheckCrucetas(List<CrucetaPiece> crucetas, List<Point> puntales, string tipo, List<string> issues)
        {
            foreach (CrucetaPiece cruceta in crucetas)
            {
                if (!HasPuntal(puntales, cruceta.A) || !HasPuntal(puntales, cruceta.B))
                {
                    issues.Add($"{tipo} sin puntal en un extremo: ({cruceta.A.X:0.##}, {cruceta.A.Y:0.##})-({cruceta.B.X:0.##}, {cruceta.B.Y:0.##}).");
                }
            }
        }

        private static void MarkTouched(List<LinearPiece> pieces, List<Point> puntales, HashSet<int> touched)
        {
            foreach (LinearPiece piece in pieces)
            {
                // Marca TODO puntal que caiga sobre la pieza, no solo los extremos: un riel de voladizo apoya
                // en un puntal que queda por DENTRO de su punta exterior, y ese puntal no debe contarse como
                // aislado. Cubre también las uniones normales (los extremos caen sobre puntales).
                double lo = System.Math.Min(piece.Start, piece.End);
                double hi = System.Math.Max(piece.Start, piece.End);
                for (int i = 0; i < puntales.Count; i++)
                {
                    Point p = puntales[i];
                    double along = piece.Horizontal ? p.X : p.Y;
                    double cross = piece.Horizontal ? p.Y : p.X;
                    if (GeometryUtil.NearlyEqual(cross, piece.FixedCoord)
                        && along >= lo - LayoutCatalog.GeometryEpsilon
                        && along <= hi + LayoutCatalog.GeometryEpsilon)
                    {
                        touched.Add(i);
                    }
                }
            }
        }

        private static bool HasPuntal(List<Point> puntales, Point target)
        {
            return puntales.Any(p => GeometryUtil.NearlyEqual(p.X, target.X) && GeometryUtil.NearlyEqual(p.Y, target.Y));
        }

        /// <summary>¿Algún puntal cae SOBRE la pieza (en su eje, entre sus extremos)? Un riel de voladizo se
        /// apoya en un puntal interior aunque su punta exterior quede al aire.</summary>
        private static bool HasPuntalOnSegment(List<Point> puntales, LinearPiece piece)
        {
            double lo = System.Math.Min(piece.Start, piece.End);
            double hi = System.Math.Max(piece.Start, piece.End);
            foreach (Point p in puntales)
            {
                double along = piece.Horizontal ? p.X : p.Y;
                double cross = piece.Horizontal ? p.Y : p.X;
                if (GeometryUtil.NearlyEqual(cross, piece.FixedCoord)
                    && along >= lo - LayoutCatalog.GeometryEpsilon
                    && along <= hi + LayoutCatalog.GeometryEpsilon)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
