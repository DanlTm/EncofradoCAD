using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Enums;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// Orquestador del cálculo del encofrado. Es la ÚNICA fuente de verdad: el preview 3D y la lista de
    /// materiales (BOM) consumen el mismo <see cref="PreviewLayout"/>, así 3D y BOM siempre coinciden.
    ///
    /// Núcleo PURO e independiente de plataforma (vive en Core): recibe el polígono de la losa y las
    /// columnas ya extraídas, sin tocar la UI. La extracción desde el lienzo (Avalonia) vive en la capa
    /// App, en LayoutEngineAdapter, que sólo delega en este BuildLayout.
    ///
    /// El trabajo pesado está repartido en módulos independientes: GeometryUtil, DirectionSolver,
    /// GridBuilder, LinearMemberBuilder, CrucetaBuilder, BoardBuilder, MaterialBill. Cada módulo se llama
    /// dentro de un guardado (try/catch) para que si UNO falla, el resto del encofrado se siga dibujando.
    /// </summary>
    public static class LayoutEngine
    {
        private const double Overhang = LayoutCatalog.EdgeOverhang;

        /// <summary>
        /// Ensambla el layout llamando a cada módulo por separado y de forma resiliente: si un módulo
        /// lanza una excepción, esa parte queda vacía pero el resto del encofrado se calcula igual.
        /// </summary>
        public static PreviewLayout BuildLayout(
            List<Point> slab,
            List<ColumnSnapshot> columns,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            Point centroid = SafeRun(
                () => GeometryUtil.ComputePolygonCentroid(slab),
                new Point((minX + maxX) / 2, (minY + maxY) / 2));

            bool primaryHorizontal = SafeRun(
                () => DirectionSolver.DeterminePrimaryHorizontal(slab, minX, maxX, minY, maxY),
                (maxX - minX) >= (maxY - minY));

            // Retícula RECTANGULAR: 3,0 m en el sentido de los rieles, 1,20 m en el de los separadores.
            double xSpacing = primaryHorizontal ? LayoutCatalog.RailSpacing : LayoutCatalog.SeparatorSpacing;
            double ySpacing = primaryHorizontal ? LayoutCatalog.SeparatorSpacing : LayoutCatalog.RailSpacing;

            // Medida mínima del catálogo en cada eje: por debajo de ella el sobrante no puede ser una sola
            // pieza válida, así que el generador lo reparte en dos.
            double xCatalogMin = primaryHorizontal ? LayoutCatalog.RailLengths[0] : LayoutCatalog.SeparatorLengths[0];
            double yCatalogMin = primaryHorizontal ? LayoutCatalog.SeparatorLengths[0] : LayoutCatalog.RailLengths[0];

            // Coordenadas de las columnas EN EL EJE DE SEPARADORES. El generador de bandas las usa para dos
            // reglas: (1) si hay columnas, evita el separador de 60 y prioriza 1,20 (una columna atrapada en
            // una banda de 60 no se puede librar); (2) si un 60 es inevitable, lo coloca lo más lejos posible
            // de las columnas (y nunca al filo). El eje de RIELES no las usa (lista vacía).
            List<double> sepColsX = primaryHorizontal ? new List<double>() : columns.Select(c => c.X).ToList();
            List<double> sepColsY = primaryHorizontal ? columns.Select(c => c.Y).ToList() : new List<double>();

            // Columnas proyectadas al EJE DE RIELES como (centro, semiancho). El planificador de rieles las
            // usa para librar las uniones y, si hace falta desplazar, crecer el voladizo base y NO partir
            // ningún tramo de 3,0 m en piezas fuera de catálogo.
            List<(double center, double half)> railCols = primaryHorizontal
                ? columns.Select(c => (c.X, c.Width / 2.0)).ToList()
                : columns.Select(c => (c.Y, c.Length / 2.0)).ToList();
            double railMin = primaryHorizontal ? minX : minY;
            double railMax = primaryHorizontal ? maxX : maxY;

            // EJE DE RIELES: líneas que YA libran columnas y conservan el voladizo ≥ 20 cm por lado (crece la
            // pieza de cierre si el desplazamiento lo exige). Devuelve las líneas ya desplazadas.
            List<double> railLines = SafeRun(
                () => GridBuilder.GenerateRailLinesClearingColumns(railMin, railMax, railCols),
                new List<double>());

            // EJE DE SEPARADORES: reparto de bandas + desplazamiento rígido para librar columnas (sin cambios).
            double sepSpacing = primaryHorizontal ? ySpacing : xSpacing;
            double sepCatalogMin = primaryHorizontal ? yCatalogMin : xCatalogMin;
            List<double> sepColsForBand = primaryHorizontal ? sepColsY : sepColsX;
            double sepMin = primaryHorizontal ? minY : minX;
            double sepMax = primaryHorizontal ? maxY : maxX;
            List<double> sepLines = SafeRun(
                () => GridBuilder.GenerateStandardLines(sepMin, sepMax, sepSpacing, LayoutCatalog.EdgeOverhang, sepCatalogMin, sepColsForBand),
                new List<double>());

            // 1) MOVIMIENTO RÍGIDO (preferido): desplaza TODA la retícula por un único δ para que las filas de
            //    riel pasen al lado de las columnas. Es lo ideal (retícula uniforme) y funciona siempre que la
            //    separación de columnas lo permita.
            sepLines = SafeRun(() => ColumnClearance.ShiftToClearColumns(sepLines, columns, axisX: !primaryHorizontal), sepLines);

            // 2) Si el movimiento rígido NO logró librar todas las columnas (regla física: el riel no puede
            //    quedar sobre una columna; el tablero va al ras y el riel corre ~10 cm por debajo), reacomoda
            //    las filas usando SOLO separadores de catálogo (1,20…0,60) para que ninguna fila caiga sobre
            //    una columna, con holgura ≥ 5 cm. No inventa medidas: elige el separador que mejor se adapta.
            List<(double center, double half)> sepColsCH = primaryHorizontal
                ? columns.Select(c => (c.Y, c.Length / 2.0)).ToList()
                : columns.Select(c => (c.X, c.Width / 2.0)).ToList();
            if (!SafeRun(() => GridBuilder.BandLinesClearColumns(sepLines, sepColsCH, ColumnClearance.MinClearance), true))
            {
                List<double>? clearedBands = SafeRun(
                    () => GridBuilder.GenerateBandLinesClearingColumns(sepMin, sepMax, sepColsCH),
                    null);
                if (clearedBands != null && clearedBands.Count >= 2)
                {
                    sepLines = clearedBands;
                }
            }

            List<double> xLines = primaryHorizontal ? railLines : sepLines;
            List<double> yLines = primaryHorizontal ? sepLines : railLines;

            List<Point> puntales = SafeRun(
                () => GridBuilder.GeneratePuntales(xLines, yLines, slab, columns, xSpacing, ySpacing, LayoutCatalog.EdgeOverhang, primaryHorizontal),
                new List<Point>());

            // Cobertura interior (casos especiales): garantiza las 4 esquinas de cada celda interior de la
            // losa, para que riel, separador y tablero puedan anclar también en escalones y quiebres de las
            // formas L/U/T. Es aditivo (solo agrega apoyos que falten); en un rectángulo no cambia nada.
            puntales = SafeRun(
                () => GridBuilder.CompleteInteriorCells(puntales, xLines, yLines, slab, columns),
                puntales);

            // Red de seguridad: completa filas que no lleguen al borde real de la losa (casos especiales:
            // bordes inclinados, escalones, protuberancias). Si ya está todo cubierto, no agrega nada.
            puntales = SafeRun(
                () => CoverageCompleter.Complete(puntales, slab, xLines, yLines, primaryHorizontal, primaryHorizontal ? xSpacing : ySpacing),
                puntales);

            // Y quita puntales redundantes (dos muy juntos donde basta uno, típico del borde con escalón).
            puntales = SafeRun(
                () => CoverageCompleter.RemoveRedundant(puntales, primaryHorizontal),
                puntales);

            // Rieles en el sentido primario; separadores en el perpendicular.
            List<LinearPiece> horizontalRails = SafeRun(
                () => primaryHorizontal ? LinearMemberBuilder.BuildRails(puntales, true) : new List<LinearPiece>(),
                new List<LinearPiece>());
            List<LinearPiece> verticalRails = SafeRun(
                () => primaryHorizontal ? new List<LinearPiece>() : LinearMemberBuilder.BuildRails(puntales, false),
                new List<LinearPiece>());
            List<LinearPiece> horizontalSeparators = SafeRun(
                () => primaryHorizontal ? new List<LinearPiece>() : LinearMemberBuilder.BuildSeparators(puntales, true),
                new List<LinearPiece>());
            List<LinearPiece> verticalSeparators = SafeRun(
                () => primaryHorizontal ? LinearMemberBuilder.BuildSeparators(puntales, false) : new List<LinearPiece>(),
                new List<LinearPiece>());

            // Poda de puntales aislados: la retícula recta puede dejar puntales en el halo que ningún riel ni
            // separador toca; se eliminan para no dibujar postes que no sujetan nada.
            puntales = SafeRun(
                () => GridBuilder.PruneIsolated(
                    puntales,
                    horizontalRails.Concat(verticalRails).Concat(horizontalSeparators).Concat(verticalSeparators).ToList()),
                puntales);

            Point[]? centralTower = SafeRun(
                () => CrucetaBuilder.TryFindCentralTowerQuad(puntales, centroid, xSpacing, ySpacing),
                null);

            // Ambas crucetas parten de la base y recorren el centro de la losa de BORDE A BORDE, arriostrando
            // cada par de puntales adyacentes (sin estas líneas continuas la torre central quedaría aislada y
            // la estructura no tendría arriostre transversal):
            //   • pequeñas (1,20 m): fila en el sentido perpendicular a su vano.
            //   • grandes  (3,0 m):  línea perpendicular a su vano (columna), conservando su sentido.
            List<CrucetaPiece> smallCrucetas = SafeRun(
                () => centralTower != null
                    ? CrucetaBuilder.BuildCrucetaLine(puntales, centralTower, primaryHorizontal, large: false)
                    : new List<CrucetaPiece>(),
                new List<CrucetaPiece>());
            List<CrucetaPiece> largeCrucetas = SafeRun(
                () => centralTower != null
                    ? CrucetaBuilder.BuildCrucetaLine(puntales, centralTower, primaryHorizontal, large: true)
                    : new List<CrucetaPiece>(),
                new List<CrucetaPiece>());

            // Tableros hasta la línea central de los rieles exteriores (20 cm por fuera del borde).
            (List<BoardPiece> boards, List<Rect> remates) = SafeRun(
                () =>
                {
                    // Tableros SIEMPRE en vertical: la medida de 1,20 m corre en Y y el ancho (0,60 m
                    // estándar y catálogo) en X, sin importar el sentido de la estructura.
                    //
                    // Losa RECTANGULAR → tiler por filas: cada banda de riel se cubre de corrido con tableros
                    // 0,60 y el pequeño solo al filo; parte en X únicamente donde hay columna (así las filas
                    // libres no quedan fragmentadas con tiras de 0,30/0,40). Formas especiales (L/U/T, curvas)
                    // → BoardBuilder, que ya las resuelve por celdas.
                    if (RectFieldTiler.TryTile(slab, columns, yLines, primaryHorizontal, minX, maxX, minY, maxY,
                            out List<BoardPiece> tb, out List<Rect> tr))
                    {
                        return (tb, tr);
                    }

                    BoardBuilder.Generate(
                        slab, columns,
                        minX - Overhang, maxX + Overhang,
                        minY - Overhang, maxY + Overhang,
                        board120AlongY: true, xLines, yLines, out List<BoardPiece> b, out List<Rect> r);
                    return (b, r);
                },
                (new List<BoardPiece>(), new List<Rect>()));

            // TABLA (cierre de columnas): el hueco inmediato de cada columna —atrapada entre sus dos rieles
            // paralelos— se cierra con tabla (naranja), no con tablero de catálogo.
            List<Rect> columnTablas = SafeRun(
                () => ColumnTablaBuilder.Build(columns, boards, slab, xLines, yLines, primaryHorizontal),
                new List<Rect>());

            // PASE DE ACABADO: barre lo que quedó VACÍO (tiras angostas alineadas con columnas y franja de
            // borde) para que NO quede ningún hueco negro. Huecos anchos → tableros de catálogo; tiras
            // angostas → tabla naranja continua; franja de borde → tabla roja continua por lado.
            LayoutFinisher.Result acabado = SafeRun(
                () => LayoutFinisher.Finish(boards, columnTablas, columns, slab, yLines, minX, maxX, minY, maxY),
                new LayoutFinisher.Result());

            if (acabado.ExtraBoards.Count > 0)
            {
                boards = boards.Concat(acabado.ExtraBoards).ToList();
            }

            List<Rect> tablas = columnTablas.Concat(acabado.ColumnTablas).ToList();
            List<Rect> borderTablas = acabado.BorderTablas;

            // Quita de "remates" lo que ahora cubre una tabla, una tabla de borde o un tablero de acabado,
            // para no dibujar (ni contar) dos piezas en el mismo hueco.
            remates = SafeRun(
                () => RemoveRematesCovered(remates, tablas, borderTablas, acabado.ExtraBoards),
                remates);

            // AJUSTE FINAL AL CATÁLOGO: ningún riel puede dibujarse con una medida que no exista en el
            // catálogo. Los rieles de borde que queden cortos se estiran hacia afuera hasta su pieza de
            // catálogo (el sobrante solo aumenta el voladizo; los tableros no llegan ahí). Se hace al final,
            // tras podar puntales, para no alterar puntales, separadores ni tableros: solo cambia el largo
            // dibujado del riel exterior. Ver RailEdgeExtender.
            horizontalRails = SafeRun(() => RailEdgeExtender.SnapToCatalog(horizontalRails), horizontalRails);
            verticalRails = SafeRun(() => RailEdgeExtender.SnapToCatalog(verticalRails), verticalRails);

            var layout = new PreviewLayout(
                puntales,
                horizontalRails,
                verticalRails,
                horizontalSeparators,
                verticalSeparators,
                smallCrucetas,
                largeCrucetas,
                boards,
                remates,
                tablas,
                borderTablas,
                centralTower);

            // Red de seguridad: verifica invariantes (piezas apoyadas, sin duplicados ni aislados). No
            // interrumpe el dibujo; solo deja constancia en el log de diagnóstico si algo no cuadra.
            SafeRun(() => LayoutValidator.Validate(layout, slab, primaryHorizontal, columns), new List<string>());

            return layout;
        }

        /// <summary>Voladizo (m) del encofrado por cada lado, respecto al borde de la losa. Como ahora el
        /// voladizo es variable (5–20 cm, elegido por el motor según la losa), cada lado puede diferir.</summary>
        public readonly record struct Voladizos(double Izquierda, double Derecha, double Inferior, double Superior);

        /// <summary>
        /// Calcula el voladizo REAL por lado a partir del layout ya resuelto: cuánto sobresale la estructura
        /// (rieles/puntales) del borde de la losa en cada uno de los cuatro lados. Se usa para mostrarlo al
        /// cliente en el módulo 3D. Toma la caja envolvente de los puntales (los nudos exteriores de la
        /// retícula = extremos de riel y filas de borde) contra la caja de la losa.
        /// </summary>
        public static Voladizos ComputeOverhangs(
            PreviewLayout layout, double minX, double maxX, double minY, double maxY)
        {
            if (layout == null || layout.Puntales == null || layout.Puntales.Count == 0)
            {
                return new Voladizos(0, 0, 0, 0);
            }

            double px0 = double.MaxValue, px1 = double.MinValue, py0 = double.MaxValue, py1 = double.MinValue;
            foreach (Point p in layout.Puntales)
            {
                if (p.X < px0) px0 = p.X;
                if (p.X > px1) px1 = p.X;
                if (p.Y < py0) py0 = p.Y;
                if (p.Y > py1) py1 = p.Y;
            }

            static double NonNeg(double v) => v < 0 ? 0 : v;
            return new Voladizos(
                NonNeg(minX - px0),
                NonNeg(px1 - maxX),
                NonNeg(minY - py0),
                NonNeg(py1 - maxY));
        }

        /// <summary>
        /// Calcula el sentido óptimo de la retícula (para el BOM y reportes) a partir del polígono.
        /// </summary>
        public static DireccionBase DetermineOptimalDirection(
            List<Point> slab, double minX, double maxX, double minY, double maxY)
        {
            return DirectionSolver.DeterminePrimaryHorizontal(slab, minX, maxX, minY, maxY)
                ? DireccionBase.Horizontal
                : DireccionBase.Vertical;
        }

        /// <summary>
        /// BOM (lista ordenada de líneas Nombre/Cantidad) a partir de un layout ya calculado — misma lógica
        /// exacta que el preview 3D. Recibe la losa y las columnas para clasificar los tableros pequeños
        /// (cierre de columna vs. volado) y calcular las superficies de encofrado, cierre y volado.
        /// </summary>
        public static List<(string Nombre, string Cantidad)> BuildMaterialBill(
            PreviewLayout layout, List<Point> slab, List<ColumnSnapshot> columns)
        {
            return SafeRun(
                () => MaterialBill.Build(layout, slab, columns),
                new List<(string Nombre, string Cantidad)>());
        }

        /// <summary>
        /// Quita de la lista de remates las zonas que ya cubre otra pieza del acabado (tabla naranja, tabla
        /// de borde roja o tablero de acabado), para no dibujar (ni contar) dos piezas en el mismo hueco. Un
        /// remate se descarta si su centro cae dentro de cualquiera de esas piezas.
        /// </summary>
        private static List<Rect> RemoveRematesCovered(
            List<Rect> remates, List<Rect> tablas, List<Rect> borderTablas, List<BoardPiece> extraBoards)
        {
            if (remates == null || remates.Count == 0)
            {
                return remates ?? new List<Rect>();
            }

            var kept = new List<Rect>();
            foreach (Rect r in remates)
            {
                var center = new Point(r.X + (r.Width / 2), r.Y + (r.Height / 2));
                bool covered =
                    (tablas != null && tablas.Any(t => t.Contains(center)))
                    || (borderTablas != null && borderTablas.Any(t => t.Contains(center)))
                    || (extraBoards != null && extraBoards.Any(b => b.Rect.Contains(center)));
                if (!covered)
                {
                    kept.Add(r);
                }
            }

            return kept;
        }

        /// <summary>Ejecuta un módulo de forma aislada: si falla, devuelve el valor por defecto.</summary>
        private static T SafeRun<T>(Func<T> action, T fallback)
        {
            try
            {
                return action();
            }
            catch
            {
                return fallback;
            }
        }
    }
}
