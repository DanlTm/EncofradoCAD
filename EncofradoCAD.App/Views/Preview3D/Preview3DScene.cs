using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using EncofradoCAD.Core.Geometry;
using EncofradoCAD.Core.Layout;

namespace EncofradoCAD.App.Views.Preview3D
{
    /// <summary>
    /// Construye la ESCENA 3D (caras, aristas y zonas medibles) a partir del <see cref="PreviewLayout"/> del
    /// motor (la MISMA fuente que el BOM). Es el puerto neutral de Preview3DBuilder/BatchedScene de la v1:
    /// en vez de fusionar mallas de HelixToolkit, acumula quads que el control pinta por software.
    ///
    /// Sigue siendo LIGERO: cajas simples por pieza, sin sub-geometría, y el control aplica back-face culling
    /// + orden por profundidad (painter's algorithm), así funciona bien en equipos con gráfica integrada.
    /// </summary>
    public sealed class Preview3DScene
    {
        // ── MEDIDAS (idénticas a la v1) ──────────────────────────────────────────────────────────────
        private const double StandardPuntalHeight = 3.0;

        /// <summary>Altura del CABEZAL del puntal (m). La usa el armado manual para el clic sobre la base
        /// superior del puntal y para dibujar las flechas de crecimiento a esa misma altura.</summary>
        public static double AlturaCabezalPuntal => StandardPuntalHeight;

        /// <summary>Lado de la placa del cabezal (m): el área sensible al clic en el armado manual.</summary>
        public static double LadoCabezalPuntal => PuntalPlateSize;
        private const double BoardThickness = 0.08;
        private const double RailHeight = 0.15;

        /// <summary>
        /// Anchura del RIEL: 15 cm (medida real). El largo NO se fija aquí — cada riel se dibuja de eje a eje de los dos
        /// puntales que une, así que un riel de catálogo de 3,00 m ocupa exactamente 3,00 m entre los ejes de
        /// las dos bases superiores (ver <see cref="AddBeam"/>).
        /// </summary>
        private const double RailWidth = 0.15;

        /// <summary>Placa del puntal (base superior e inferior): 12,5 × 12,5 cm (medida real).</summary>
        private const double PuntalPlateSize = 0.125;

        private const double PuntalPlateThickness = 0.02;
        private const double PuntalPostSize = 0.07;
        private const double RemateThickness = 0.03;
        private const double SeparatorDropBelowHead = 0.20;
        private const double LargeCrucetaZLow = 0.50;
        private const double LargeCrucetaZHigh = 1.50;
        private const double SmallCrucetaZLow = 0.40;
        private const double SmallCrucetaZHigh = 1.70;
        /// <summary>Barra de la cruceta: 3 cm de lado (tijera grande y cruceta pequeña).</summary>
        private const double CrucetaBarThickness = 0.03;

        /// <summary>Separador: tubo REDONDO. Diámetro de dibujo 6 cm para que se distinga en pantalla.</summary>
        private const double SeparatorDiameter = 0.06;

        /// <summary>Anchura del riel: la usa el contorno interno para situar su filo.</summary>
        public static double AnchoRiel => RailWidth;

        /// <summary>Diámetro de dibujo del separador (m). Lo usan las cotas de holgura a las columnas.</summary>
        public static double DiametroSeparador => SeparatorDiameter;

        /// <summary>Altura (m) del eje de los rieles: a esa cota se dibuja su holgura contra la columna.</summary>
        public static double AlturaRieles => StandardPuntalHeight + (RailHeight / 2);

        /// <summary>Altura (m) del eje de los separadores: a esa cota se dibuja su holgura contra la columna.</summary>
        public static double AlturaSeparadores => StandardPuntalHeight - SeparatorDropBelowHead;

        private const double GeometryEpsilon = 0.001;

        // ── PALETA (idéntica a la v1) ────────────────────────────────────────────────────────────────
        private static readonly Color ColorColumna = Color.FromArgb(205, 100, 116, 139); // slate translúcido
        private static readonly Color ColorPuntal = Color.Parse("#FACC15");              // amber-400
        private static readonly Color ColorCrucetaPeq = Color.Parse("#F97316");          // orange-500
        private static readonly Color ColorCrucetaGrande = Color.Parse("#2563EB");       // blue-600
        private static readonly Color ColorSeparador = Color.Parse("#A855F7");           // purple-500
        private static readonly Color ColorRemate = Color.FromArgb(220, 255, 45, 45);    // rojo vivo (remate con tabla)
        private static readonly Color ColorTabla = Color.FromArgb(235, 255, 140, 0);     // naranja fuerte (tabla de cierre de columna)
        private static readonly Color ColorTablaBorde = Color.FromArgb(240, 220, 20, 20); // rojo fuerte (tabla continua de borde)
        private static readonly Color ColorContornoTablero = Color.Parse("#E5E7EB");     // gris-200
        private static readonly Color ColorLosa = Color.FromRgb(0xB6, 0xFF, 0x00);       // verde lima neón
        /// <summary>Voladizo por defecto del contorno entrecortado (m): 20 cm, la norma del motor.</summary>
        public const double VoladizoPorDefecto = 0.20;

        private static readonly Color ColorContornoVoladizo = Color.FromArgb(210, 220, 220, 220); // blanco/gris (voladizo)
        private static readonly Color ColorHuellaArmado = Color.FromArgb(225, 0x22, 0xD3, 0xEE);  // cian (huella del armado manual)
        private static readonly Color ColorContornoRiel = Color.FromArgb(240, 0xFF, 0xB3, 0x00);        // ámbar (filo interno del riel)
        private static readonly Color ColorContornoSeparador = Color.FromArgb(215, 0xFF, 0xE0, 0x80);   // ámbar claro (eje del separador)

        public Modo3D Modo { get; }
        public bool MostrarTableros { get; }

        public readonly List<Face3D> Faces = new();
        public readonly List<Edge3D> Edges = new();
        public readonly List<HoverItem> Hovers = new();

        public double SlabHeight { get; private set; }

        private Preview3DScene(Modo3D modo, bool mostrarTableros)
        {
            Modo = modo;
            MostrarTableros = mostrarTableros;
        }

        /// <summary>Puerto de RenderPreview: llena la escena a partir del layout y del contorno de la losa.</summary>
        public static Preview3DScene Build(
            Modo3D modo,
            bool mostrarTableros,
            PreviewLayout layout,
            IReadOnlyList<Point> slab,
            IReadOnlyList<ColumnSnapshot> columns,
            Rect? huellaArmado = null,
            IReadOnlyList<ManualAssembly.SegmentoContorno>? contornoInterno = null,
            double voladizoMetros = VoladizoPorDefecto,
            bool mostrarVoladizo = true)
        {
            var s = new Preview3DScene(modo, mostrarTableros);

            double puntalHeight = StandardPuntalHeight;
            double slabHeight = puntalHeight + RailHeight + BoardThickness;
            double boardZ = slabHeight - (BoardThickness / 2);
            double railZ = slabHeight - BoardThickness - (RailHeight / 2);
            double separatorZ = puntalHeight - SeparatorDropBelowHead;
            s.SlabHeight = slabHeight;

            s.AddSlabOutline(slab, slabHeight);
            if (mostrarVoladizo)
            {
                s.AddVoladizoOutline(slab, slabHeight, voladizoMetros);
            }

            // Huella del ARMADO MANUAL: rectángulo cian a ras de suelo con sus dos diagonales, para ver de
            // esquina a esquina dónde queda el armado y poder centrarlo respecto al plano.
            if (huellaArmado.HasValue)
            {
                s.AddHuellaArmado(huellaArmado.Value);
            }

            // Contorno INTERNO del armado manual, a la altura de los rieles: centro en los separadores y
            // filo interno en los rieles, que es la línea que puede chocar con la cara de una columna.
            if (contornoInterno != null)
            {
                s.AddContornoInterno(contornoInterno, railZ + (RailHeight / 2) + 0.006);
            }

            // Columnas (con etiqueta de medida para el tooltip al pasar el cursor)
            foreach (ColumnSnapshot column in columns)
            {
                s.AddBox(ColorColumna, new Vec3(column.X, column.Y, slabHeight / 2), column.Width, column.Length, slabHeight, PiezaTipo.Columna, ColumnLabel(column), fixedInWorld: true);
            }

            // Puntales (placa base + poste + placa superior)
            foreach (Point puntal in layout.Puntales)
            {
                s.AddPuntal(puntal, puntalHeight);
            }

            // Rieles (coloreados por longitud) con etiqueta de medida para el hover
            foreach (LinearPiece rail in layout.HorizontalRails)
            {
                s.AddBeam(GetRailEdgeColor(rail.NominalLength), rail, railZ, true, RailWidth, RailHeight, PieceLabel("Riel", rail));
            }
            foreach (LinearPiece rail in layout.VerticalRails)
            {
                s.AddBeam(GetRailEdgeColor(rail.NominalLength), rail, railZ - 0.008, false, RailWidth, RailHeight, PieceLabel("Riel", rail));
            }

            // Separadores (morado): tubo REDONDO, 20 cm por debajo de la base superior del puntal.
            foreach (LinearPiece sep in layout.HorizontalSeparators)
            {
                s.AddTubo(ColorSeparador, sep, separatorZ, true, SeparatorDiameter, PieceLabel("Separador", sep));
            }
            foreach (LinearPiece sep in layout.VerticalSeparators)
            {
                s.AddTubo(ColorSeparador, sep, separatorZ - 0.01, false, SeparatorDiameter, PieceLabel("Separador", sep));
            }

            // Crucetas (aspa en X) — misma altura tope que la v1
            double smallZHigh = Math.Min(SmallCrucetaZHigh, puntalHeight - 0.05);
            foreach (CrucetaPiece c in layout.SmallCrucetas)
            {
                s.AddCrucetaX(ColorCrucetaPeq, c.A, c.B, SmallCrucetaZLow, smallZHigh);
            }
            double largeZHigh = Math.Min(LargeCrucetaZHigh, puntalHeight - 0.05);
            foreach (CrucetaPiece c in layout.LargeCrucetas)
            {
                s.AddCrucetaX(ColorCrucetaGrande, c.A, c.B, LargeCrucetaZLow, largeZHigh);
            }

            // Tableros + remates (ocultables)
            if (mostrarTableros)
            {
                foreach (BoardPiece board in layout.Boards)
                {
                    Rect r = board.Rect;
                    var center = new Vec3(r.X + (r.Width / 2), r.Y + (r.Height / 2), boardZ);
                    Color color = WithAlpha(GetBoardEdgeColor(board.Width), 90);
                    s.AddBox(color, center, r.Width, r.Height, BoardThickness, PiezaTipo.Tablero, BoardLabel(board));
                    s.AddDashedRect(ColorContornoTablero, center, r.Width, r.Height, boardZ + (BoardThickness / 2) + 0.004);
                }

                foreach (Rect remate in layout.Remates.Where(x => x.Width > 0.02 && x.Height > 0.02))
                {
                    s.AddBox(ColorRemate, new Vec3(remate.X + (remate.Width / 2), remate.Y + (remate.Height / 2), slabHeight + 0.01), remate.Width, remate.Height, RemateThickness);
                }

                // TABLA DE BORDE: la franja fina del borde (donde no cabe ningún tablero) se cierra con una
                // sola tabla continua por lado, en ROJO, para que se vea como una pieza completa que llena
                // todo el espacio (no en pedacitos). Se dibuja ANTES que la tabla de columna para que, si una
                // tira de columna toca el borde, el naranja quede por encima (no lo tape el rojo).
                foreach (Rect borde in layout.BorderTablas.Where(x => x.Width > 0.02 && x.Height > 0.02))
                {
                    var center = new Vec3(borde.X + (borde.Width / 2), borde.Y + (borde.Height / 2), boardZ);
                    s.AddBox(ColorTablaBorde, center, borde.Width, borde.Height, BoardThickness, PiezaTipo.Tablero, TablaBordeLabel(borde));
                    s.AddDashedRect(ColorContornoTablero, center, borde.Width, borde.Height, boardZ + (BoardThickness / 2) + 0.004);
                }

                // TABLA (cierre de columnas): los huecos alrededor de cada columna se cierran con tabla, no con
                // tablero. Se dibujan en NARANJA fuerte para que se distingan de un vistazo de los tableros
                // (marrón/verde) y de los remates de ajuste (rojo).
                foreach (Rect tabla in layout.Tablas.Where(x => x.Width > 0.02 && x.Height > 0.02))
                {
                    var center = new Vec3(tabla.X + (tabla.Width / 2), tabla.Y + (tabla.Height / 2), boardZ);
                    s.AddBox(ColorTabla, center, tabla.Width, tabla.Height, BoardThickness, PiezaTipo.Tablero, TablaLabel(tabla));
                    s.AddDashedRect(ColorContornoTablero, center, tabla.Width, tabla.Height, boardZ + (BoardThickness / 2) + 0.004);
                }
            }

            return s;
        }

        // ── ELEMENTOS (puerto de Preview3DBuilder.Elements) ──────────────────────────────────────────
        private void AddPuntal(Point center, double height)
        {
            double postHeight = Math.Max(height - (2 * PuntalPlateThickness), 0.10);
            AddBox(ColorPuntal, new Vec3(center.X, center.Y, PuntalPlateThickness / 2), PuntalPlateSize, PuntalPlateSize, PuntalPlateThickness);
            AddBox(ColorPuntal, new Vec3(center.X, center.Y, height - (PuntalPlateThickness / 2)), PuntalPlateSize, PuntalPlateSize, PuntalPlateThickness);
            AddBox(ColorPuntal, new Vec3(center.X, center.Y, height / 2), PuntalPostSize, PuntalPostSize, postHeight);
        }

        /// <summary>
        /// Viga (riel) entre dos puntales. <c>Start</c> y <c>End</c> son los EJES de los dos puntales, así que
        /// la pieza se dibuja de eje a eje: un riel de 3,00 m mide 3,00 m entre los centros de las dos bases
        /// superiores, no entre sus filos.
        /// </summary>
        private void AddBeam(Color color, LinearPiece piece, double z, bool horizontal, double width, double height, string? label = null)
        {
            double length = piece.ActualLength;
            if (length <= GeometryEpsilon)
            {
                return;
            }

            double midX = horizontal ? (piece.Start + piece.End) / 2 : piece.FixedCoord;
            double midY = horizontal ? piece.FixedCoord : (piece.Start + piece.End) / 2;
            double sizeX = horizontal ? length : width;
            double sizeY = horizontal ? width : length;
            AddBox(color, new Vec3(midX, midY, z), sizeX, sizeY, height, PiezaTipo.Estructura, label);
        }

        /// <summary>
        /// Separador como TUBO REDONDO: prisma de 10 caras que a simple vista se lee como un cilindro, sin
        /// el coste de una malla real (el pintado es por software).
        /// </summary>
        private void AddTubo(Color color, LinearPiece piece, double z, bool horizontal, double diametro, string? label = null)
        {
            double length = piece.ActualLength;
            if (length <= GeometryEpsilon)
            {
                return;
            }

            double mid = (piece.Start + piece.End) / 2;
            Vec3 centro = horizontal
                ? new Vec3(mid, piece.FixedCoord, z)
                : new Vec3(piece.FixedCoord, mid, z);
            Vec3 eje = horizontal ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);
            Vec3 a = centro - (eje * (length / 2));
            Vec3 b = centro + (eje * (length / 2));

            if (label != null)
            {
                // Cara superior aproximada para el hit-test de la medida.
                double r = diametro / 2;
                Vec3 t0 = horizontal ? new Vec3(a.X, a.Y - r, z + r) : new Vec3(a.X - r, a.Y, z + r);
                Vec3 t1 = horizontal ? new Vec3(b.X, b.Y - r, z + r) : new Vec3(b.X - r, b.Y, z + r);
                Vec3 t2 = horizontal ? new Vec3(b.X, b.Y + r, z + r) : new Vec3(b.X + r, b.Y, z + r);
                Vec3 t3 = horizontal ? new Vec3(a.X, a.Y + r, z + r) : new Vec3(a.X + r, a.Y, z + r);
                Hovers.Add(new HoverItem { Label = label, T0 = t0, T1 = t1, T2 = t2, T3 = t3 });
            }

            AddCilindro(color, a, b, diametro, 10);
        }

        /// <summary>Cilindro aproximado por un prisma de <paramref name="lados"/> caras entre dos puntos.</summary>
        private void AddCilindro(Color color, Vec3 a, Vec3 b, double diametro, int lados)
        {
            Vec3 axis = b - a;
            double len = axis.Length;
            if (len < 1e-6)
            {
                return;
            }
            axis = axis * (1.0 / len);

            Vec3 referencia = Math.Abs(axis.Z) < 0.9 ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0);
            Vec3 u = Vec3.Cross(axis, referencia).Normalized();
            Vec3 w = Vec3.Cross(axis, u).Normalized();
            double r = diametro / 2;

            Color faceColor = Modo == Modo3D.Transparente ? WithLitAlpha(color) : color;

            for (int i = 0; i < lados; i++)
            {
                double t0 = 2 * Math.PI * i / lados;
                double t1 = 2 * Math.PI * (i + 1) / lados;

                Vec3 d0 = (u * (Math.Cos(t0) * r)) + (w * (Math.Sin(t0) * r));
                Vec3 d1 = (u * (Math.Cos(t1) * r)) + (w * (Math.Sin(t1) * r));

                AddQuad(faceColor, a + d0, a + d1, b + d1, b + d0);
            }
        }

        private void AddCrucetaX(Color color, Point a, Point b, double zLow, double zHigh)
        {
            var aLow = new Vec3(a.X, a.Y, zLow);
            var aHigh = new Vec3(a.X, a.Y, zHigh);
            var bLow = new Vec3(b.X, b.Y, zLow);
            var bHigh = new Vec3(b.X, b.Y, zHigh);
            AddBar(color, aLow, bHigh, CrucetaBarThickness);
            AddBar(color, aHigh, bLow, CrucetaBarThickness);
        }

        private void AddSlabOutline(IReadOnlyList<Point> slab, double slabHeight)
        {
            if (slab.Count < 2)
            {
                return;
            }
            for (int i = 0; i < slab.Count; i++)
            {
                Point cur = slab[i];
                Point nxt = slab[(i + 1) % slab.Count];
                Edges.Add(new Edge3D(
                    new Vec3(cur.X, cur.Y, slabHeight),
                    new Vec3(nxt.X, nxt.Y, slabHeight),
                    ColorLosa, 1.6, false, fixedInWorld: true)); // plano 2D: SIEMPRE fijo
            }
        }

        /// <summary>
        /// Contorno del VOLADIZO (el volado elegido hacia afuera del borde de la losa) como línea entrecortada blanca/gris,
        /// a la misma altura que la losa. Es el mismo offset que se ve en el plano 2D, para comparar en 3D.
        /// </summary>
        private void AddVoladizoOutline(IReadOnlyList<Point> slab, double slabHeight, double voladizoMetros)
        {
            if (slab.Count < 3)
            {
                return;
            }

            if (voladizoMetros <= 0)
            {
                return;
            }

            List<Point> off = GeometryUtil.OffsetPolygon(new List<Point>(slab), voladizoMetros);
            if (off.Count < 3)
            {
                return;
            }

            for (int i = 0; i < off.Count; i++)
            {
                Point cur = off[i];
                Point nxt = off[(i + 1) % off.Count];
                Edges.Add(new Edge3D(
                    new Vec3(cur.X, cur.Y, slabHeight),
                    new Vec3(nxt.X, nxt.Y, slabHeight),
                    ColorContornoVoladizo, 1.2, true, fixedInWorld: true)); // entrecortada: SIEMPRE fija
            }
        }

        /// <summary>
        /// Contorno INTERNO del armado manual, a la altura de los rieles: por el filo interno en los lados
        /// de riel y por el centro en los de separador. Es la línea con la que se comprueba si el riel
        /// choca con la cara de una columna. Se mueve junto con la estructura.
        /// </summary>
        private void AddContornoInterno(IReadOnlyList<ManualAssembly.SegmentoContorno> contorno, double z)
        {
            foreach (ManualAssembly.SegmentoContorno seg in contorno)
            {
                Edges.Add(new Edge3D(
                    new Vec3(seg.A.X, seg.A.Y, z),
                    new Vec3(seg.B.X, seg.B.Y, z),
                    seg.EsRiel ? ColorContornoRiel : ColorContornoSeparador,
                    seg.EsRiel ? 2.0 : 1.6,
                    true));
            }
        }

        /// <summary>
        /// Huella del armado manual a ras de suelo: rectángulo entrecortado cian + las dos diagonales de
        /// esquina a esquina y una cruz en el centro. Es la referencia visual para centrar el armado dentro
        /// del plano (cuando la cruz coincide con el centro de la losa, está centrado).
        /// NO es fija: se mueve junto con la estructura al arrastrarla.
        /// </summary>
        private void AddHuellaArmado(Rect huella)
        {
            const double z = 0.015;
            Vec3 a = new(huella.Left, huella.Top, z);
            Vec3 b = new(huella.Right, huella.Top, z);
            Vec3 c = new(huella.Right, huella.Bottom, z);
            Vec3 d = new(huella.Left, huella.Bottom, z);

            Edges.Add(new Edge3D(a, b, ColorHuellaArmado, 1.4, true));
            Edges.Add(new Edge3D(b, c, ColorHuellaArmado, 1.4, true));
            Edges.Add(new Edge3D(c, d, ColorHuellaArmado, 1.4, true));
            Edges.Add(new Edge3D(d, a, ColorHuellaArmado, 1.4, true));

            // Diagonales de esquina a esquina.
            Edges.Add(new Edge3D(a, c, ColorHuellaArmado, 1.0, true));
            Edges.Add(new Edge3D(b, d, ColorHuellaArmado, 1.0, true));

            // Cruz en el centro del armado.
            double cx = (huella.Left + huella.Right) / 2;
            double cy = (huella.Top + huella.Bottom) / 2;
            const double brazo = 0.25;
            Edges.Add(new Edge3D(new Vec3(cx - brazo, cy, z), new Vec3(cx + brazo, cy, z), ColorHuellaArmado, 1.6, false));
            Edges.Add(new Edge3D(new Vec3(cx, cy - brazo, z), new Vec3(cx, cy + brazo, z), ColorHuellaArmado, 1.6, false));
        }

        // ── CAJAS / BARRAS (puerto de BatchedScene) ──────────────────────────────────────────────────
        private void AddBox(Color color, Vec3 center, double sizeX, double sizeY, double sizeZ, PiezaTipo tipo = PiezaTipo.Estructura, string? label = null, bool fixedInWorld = false)
        {
            double hx = Math.Max(sizeX, 0.005) / 2;
            double hy = Math.Max(sizeY, 0.005) / 2;
            double hz = Math.Max(sizeZ, 0.005) / 2;

            Vec3 p000 = new(center.X - hx, center.Y - hy, center.Z - hz);
            Vec3 p001 = new(center.X - hx, center.Y - hy, center.Z + hz);
            Vec3 p010 = new(center.X - hx, center.Y + hy, center.Z - hz);
            Vec3 p011 = new(center.X - hx, center.Y + hy, center.Z + hz);
            Vec3 p100 = new(center.X + hx, center.Y - hy, center.Z - hz);
            Vec3 p101 = new(center.X + hx, center.Y - hy, center.Z + hz);
            Vec3 p110 = new(center.X + hx, center.Y + hy, center.Z - hz);
            Vec3 p111 = new(center.X + hx, center.Y + hy, center.Z + hz);

            if (label != null)
            {
                // Cara superior para el hit-test del tooltip de medidas.
                Hovers.Add(new HoverItem { Label = label, T0 = p001, T1 = p101, T2 = p111, T3 = p011, Fixed = fixedInWorld });
            }

            if (Modo == Modo3D.Alambrico)
            {
                if (tipo == PiezaTipo.Columna)
                {
                    AddFaces(SolidColor(color), p000, p001, p010, p011, p100, p101, p110, p111, fixedInWorld);
                    return;
                }
                if (tipo == PiezaTipo.Tablero)
                {
                    AddFaces(Color.FromArgb(90, color.R, color.G, color.B), p000, p001, p010, p011, p100, p101, p110, p111, fixedInWorld);
                    return;
                }

                // Estructura → solo aristas
                AddEdge(color, p000, p100, fixedInWorld); AddEdge(color, p100, p110, fixedInWorld); AddEdge(color, p110, p010, fixedInWorld); AddEdge(color, p010, p000, fixedInWorld);
                AddEdge(color, p001, p101, fixedInWorld); AddEdge(color, p101, p111, fixedInWorld); AddEdge(color, p111, p011, fixedInWorld); AddEdge(color, p011, p001, fixedInWorld);
                AddEdge(color, p000, p001, fixedInWorld); AddEdge(color, p100, p101, fixedInWorld); AddEdge(color, p110, p111, fixedInWorld); AddEdge(color, p010, p011, fixedInWorld);
                return;
            }

            Color faceColor;
            if (tipo == PiezaTipo.Tablero)
            {
                faceColor = color; // conserva su alpha bajo (translúcido)
            }
            else if (Modo == Modo3D.Transparente)
            {
                Color lit = Lighten(color, 0.35);
                faceColor = Color.FromArgb(165, lit.R, lit.G, lit.B);
            }
            else
            {
                faceColor = color;
            }

            AddFaces(faceColor, p000, p001, p010, p011, p100, p101, p110, p111, fixedInWorld);
        }

        private void AddFaces(Color color, Vec3 p000, Vec3 p001, Vec3 p010, Vec3 p011, Vec3 p100, Vec3 p101, Vec3 p110, Vec3 p111, bool fixedInWorld = false)
        {
            Faces.Add(new Face3D(p001, p101, p111, p011, new Vec3(0, 0, 1), color, fixedInWorld));   // arriba (+Z)
            Faces.Add(new Face3D(p100, p000, p010, p110, new Vec3(0, 0, -1), color, fixedInWorld));  // abajo (-Z)
            Faces.Add(new Face3D(p100, p110, p111, p101, new Vec3(1, 0, 0), color, fixedInWorld));   // +X
            Faces.Add(new Face3D(p000, p001, p011, p010, new Vec3(-1, 0, 0), color, fixedInWorld));  // -X
            Faces.Add(new Face3D(p010, p011, p111, p110, new Vec3(0, 1, 0), color, fixedInWorld));   // +Y
            Faces.Add(new Face3D(p000, p100, p101, p001, new Vec3(0, -1, 0), color, fixedInWorld));  // -Y
        }

        /// <summary>Barra rectangular orientada entre dos puntos (para crucetas diagonales).</summary>
        private void AddBar(Color color, Vec3 a, Vec3 b, double thickness)
        {
            Vec3 axis = b - a;
            double len = axis.Length;
            if (len < 1e-6)
            {
                return;
            }
            axis = axis * (1.0 / len);
            Vec3 reference = Math.Abs(axis.Z) < 0.9 ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0);
            Vec3 u = Vec3.Cross(axis, reference).Normalized();
            Vec3 w = Vec3.Cross(axis, u).Normalized();

            double h = thickness / 2;
            u = u * h;
            w = w * h;

            Vec3 a0 = a + u + w, a1 = a + u - w, a2 = a - u - w, a3 = a - u + w;
            Vec3 b0 = b + u + w, b1 = b + u - w, b2 = b - u - w, b3 = b - u + w;

            Color faceColor = Modo == Modo3D.Transparente
                ? WithLitAlpha(color)
                : color;

            AddQuad(faceColor, a0, a1, b1, b0);
            AddQuad(faceColor, a1, a2, b2, b1);
            AddQuad(faceColor, a2, a3, b3, b2);
            AddQuad(faceColor, a3, a0, b0, b3);
            AddQuad(faceColor, a3, a2, a1, a0);
            AddQuad(faceColor, b0, b1, b2, b3);
        }

        private void AddQuad(Color color, Vec3 a, Vec3 b, Vec3 c, Vec3 d)
        {
            Vec3 n = Vec3.Cross(b - a, c - a).Normalized();
            Faces.Add(new Face3D(a, b, c, d, n, color));
        }

        private void AddEdge(Color color, Vec3 a, Vec3 b, bool fixedInWorld = false)
            => Edges.Add(new Edge3D(a, b, SolidColor(color), 1.1, false, fixedInWorld));

        // Contorno PUNTEADO de un tablero (rectángulo horizontal a la altura z).
        private void AddDashedRect(Color color, Vec3 center, double sizeX, double sizeY, double z)
        {
            double hx = sizeX / 2;
            double hy = sizeY / 2;
            Vec3 a = new(center.X - hx, center.Y - hy, z);
            Vec3 b = new(center.X + hx, center.Y - hy, z);
            Vec3 c = new(center.X + hx, center.Y + hy, z);
            Vec3 d = new(center.X - hx, center.Y + hy, z);
            Edges.Add(new Edge3D(a, b, color, 1.0, true));
            Edges.Add(new Edge3D(b, c, color, 1.0, true));
            Edges.Add(new Edge3D(c, d, color, 1.0, true));
            Edges.Add(new Edge3D(d, a, color, 1.0, true));
        }

        // ── COLORES (puerto de Elements/BatchedScene) ────────────────────────────────────────────────
        private static Color WithLitAlpha(Color color)
        {
            Color lit = Lighten(color, 0.35);
            return Color.FromArgb(165, lit.R, lit.G, lit.B);
        }

        private static Color GetBoardEdgeColor(double width)
            => width >= 0.595 ? Color.Parse("#C08457") : Color.Parse("#4ADE80");

        private static Color GetRailEdgeColor(double nominalLength)
        {
            return nominalLength switch
            {
                >= 2.95 => Color.Parse("#A5F3FC"),
                >= 2.45 => Color.Parse("#67E8F9"),
                >= 1.95 => Color.Parse("#22D3EE"),
                >= 1.48 => Color.Parse("#06B6D4"),
                >= 1.28 => Color.Parse("#0891B2"),
                >= 1.12 => Color.Parse("#0E7490"),
                _ => Color.Parse("#155E75")
            };
        }

        private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
        private static Color SolidColor(Color color) => Color.FromArgb(255, color.R, color.G, color.B);

        private static Color Lighten(Color color, double t)
        {
            byte Mix(byte c) => (byte)Math.Round(c + ((255 - c) * t));
            return Color.FromArgb(color.A, Mix(color.R), Mix(color.G), Mix(color.B));
        }

        private static string BoardLabel(BoardPiece board) => $"Tablero: {board.Width:0.00} × {board.Length:0.00} m";
        private static string ColumnLabel(ColumnSnapshot column)
            => $"Columna: {column.Width:0.00} × {column.Length:0.00} m ({column.Width * 100:0} × {column.Length * 100:0} cm)";
        private static string TablaLabel(Rect tabla) => $"Tabla (cierre columna): {tabla.Width:0.00} × {tabla.Height:0.00} m";
        private static string TablaBordeLabel(Rect borde) => $"Tabla de borde: {borde.Width:0.00} × {borde.Height:0.00} m";

        private static string PieceLabel(string kind, LinearPiece piece)
        {
            double nominal = piece.NominalLength;
            double actual = piece.ActualLength;
            return Math.Abs(nominal - actual) < 0.02
                ? $"{kind}: {actual:0.00} m"
                : $"{kind}: {actual:0.00} m (catálogo {nominal:0.00} m)";
        }
    }
}
