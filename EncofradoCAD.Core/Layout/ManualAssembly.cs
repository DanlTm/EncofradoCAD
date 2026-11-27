using System;
using System.Collections.Generic;
using System.Linq;
using EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.Core.Layout
{
    /// <summary>
    /// ARMADO MANUAL del encofrado: el usuario lo monta como en obra, empezando por la BASE PRINCIPAL y
    /// haciendo crecer la estructura desde el CABEZAL de un puntal, vano por vano.
    ///
    ///  • BASE PRINCIPAL: 4 puntales en un cuadro riel × separador (3,00 × 1,20 m), 2 rieles, 2 separadores,
    ///    2 crucetas grandes (a lo largo del riel) y 2 pequeñas (a lo ancho).
    ///  • CRECER EN EL EJE DEL RIEL (los 3 m): añade 2 puntales, 2 rieles, 1 separador y la CRUCETA PEQUEÑA
    ///    que los arriostra — la "base secundaria" del sistema real (2 puntales + 1 cruceta pequeña).
    ///  • CRECER EN EL EJE DEL SEPARADOR (los 1,20 m): añade la fila siguiente — 2 puntales, 2 separadores,
    ///    el riel que los une y la CRUCETA GRANDE de ese riel.
    ///  • CRUZ ARRIOSTRADA: solo llevan cruceta la base principal y sus dos brazos EN LÍNEA RECTA — la fila
    ///    (crucetas pequeñas) y la columna (crucetas grandes) —, y solo hasta cubrir la losa con su voladizo
    ///    de 5 a 20 cm. El resto del campo, que el usuario rellena después, va sin crucetas.
    ///  • La MEDIDA de cada extensión la decide el contexto: la que predomina en lo ya armado para esa clase
    ///    de pieza, recortada al catálogo cuando el puntal nuevo se pasaría del voladizo máximo.
    ///  • ELIMINAR UNA RED: señalando una pieza con el puntero se retira esa pieza y, en cascada, todo lo que
    ///    dependía de ella (puntales sin amarre y sus piezas y crucetas).
    ///
    /// El modelo es una RETÍCULA DE NUDOS con las piezas colocadas explícitamente (no se deduce nada): así
    /// el 3D y el BOM cuentan exactamente lo que el usuario puso. Las coordenadas internas se guardan en
    /// MILÍMETROS enteros para que la retícula sea exacta y los nudos se puedan comparar sin tolerancias.
    ///
    /// Ejes internos: <c>u</c> = eje de los RIELES, <c>v</c> = eje de los SEPARADORES (perpendicular).
    /// El mapeo a X/Y depende de <see cref="RielesHorizontales"/>.
    ///
    /// El resultado se entrega como un <see cref="PreviewLayout"/> idéntico en forma al del motor
    /// automático, de modo que el preview 3D y la lista de materiales (BOM) lo consumen sin cambios.
    /// </summary>
    public sealed class ManualAssembly
    {
        /// <summary>Vano (m) a partir del cual una cruceta se considera GRANDE (3,00 m) y no pequeña.</summary>
        public const double CrucetaGrandeMin = 2.00;

        /// <summary>Hacia dónde crece el armado desde el cabezal de un puntal.</summary>
        public enum Direccion
        {
            /// <summary>Eje de los rieles, sentido positivo.</summary>
            RielMas,
            /// <summary>Eje de los rieles, sentido negativo.</summary>
            RielMenos,
            /// <summary>Eje de los separadores, sentido positivo.</summary>
            SeparadorMas,
            /// <summary>Eje de los separadores, sentido negativo.</summary>
            SeparadorMenos
        }

        /// <summary>Nudo de la retícula (puntal) en coordenadas internas de milímetros.</summary>
        public readonly record struct Nudo(int U, int V);

        /// <summary>Vano entre dos nudos: es un RIEL si comparten V, y un SEPARADOR si comparten U.</summary>
        public readonly record struct Vano(Nudo A, Nudo B)
        {
            public bool EsRiel => A.V == B.V;
            public int LargoMm => EsRiel ? Math.Abs(B.U - A.U) : Math.Abs(B.V - A.V);
        }

        /// <summary>Vista previa de lo que colocaría una extensión, para dibujar el fantasma antes del clic.</summary>
        public sealed record Extension(
            Point NuevoA,
            Point NuevoB,
            Point AnclaA,
            Point AnclaB,
            string Nombre,
            string Descripcion,
            bool ConCruceta,
            bool CrucetaGrande);

        private readonly HashSet<Nudo> _nudos = new();
        private readonly HashSet<Vano> _vanos = new();
        private readonly HashSet<Vano> _crucetas = new();

        /// <summary>
        /// Los 4 puntales de la BASE PRINCIPAL. Marcan la fila y la columna de la CRUZ ARRIOSTRADA (lo único
        /// que lleva crucetas) y son el ancla del armado: no se caen al eliminar una red y sus piezas no se
        /// borran con la tecla Suprimir — para rehacer la base está su propio botón.
        /// </summary>
        private readonly HashSet<Nudo> _nudosBase = new();

        // ── Configuración ────────────────────────────────────────────────────
        /// <summary>true = los rieles corren en X (y los separadores en Y). false = al revés.</summary>
        public bool RielesHorizontales { get; set; } = true;

        /// <summary>Origen del armado en el plano (m). Lo mueve el arrastre de estructura.</summary>
        public double OrigenX { get; set; }

        /// <summary>Origen del armado en el plano (m). Lo mueve el arrastre de estructura.</summary>
        public double OrigenY { get; set; }

        // ── PLANO DE REFERENCIA ──────────────────────────────────────────────
        /// <summary>Caja de la LOSA sobre la que se arma (m).</summary>
        public readonly record struct Losa(double MinX, double MaxX, double MinY, double MaxY);

        /// <summary>
        /// Losa de referencia. Con ella el armado sabe hasta dónde tiene que llegar, y de eso dependen las
        /// dos cosas que se calculan solas: hasta dónde llega la CRUZ ARRIOSTRADA y qué medida conviene
        /// colocar al crecer. Mientras sea null, el armado se comporta como si la losa fuese infinita.
        /// </summary>
        public Losa? Plano { get; set; }

        /// <summary>Voladizo mínimo del encofrado respecto al filo de la losa (m).</summary>
        public const double VoladizoMin = 0.05;

        /// <summary>Voladizo máximo del encofrado respecto al filo de la losa (m).</summary>
        public const double VoladizoMax = 0.20;

        /// <summary>
        /// true = LOSA LISTA: el armado quedó bloqueado y ya no admite más piezas. Es el paso previo a
        /// colocar los tableros encima.
        /// </summary>
        public bool Congelado { get; private set; }

        /// <summary>Tableros de contacto colocados sobre la estructura (solo con el armado congelado).</summary>
        private readonly List<BoardPiece> _tableros = new();

        /// <summary>
        /// REMATES CON TABLA: los huecos que quedan tras repartir los tableros —los recortes pegados a cada
        /// columna y las franjas demasiado estrechas para un tablero de catálogo—. Salen en el 3D con su
        /// propio color y llegan al módulo de cálculo como superficie de tabla.
        /// </summary>
        private readonly List<Rect> _tablas = new();

        /// <summary>Lado mínimo (m) para que un hueco cuente como remate; por debajo es una junta.</summary>
        private const double RemateMinimo = 0.03;

        /// <summary>Bloquea el armado: ninguna pieza más. Devuelve false si no hay nada que bloquear.</summary>
        public bool CerrarLosa()
        {
            if (Congelado || _nudos.Count < 4)
            {
                return false;
            }
            Congelado = true;
            return true;
        }

        /// <summary>Vuelve a permitir la edición del armado y retira los tableros colocados.</summary>
        public void ReabrirLosa()
        {
            Congelado = false;
            _tableros.Clear();
            _tablas.Clear();
        }

        /// <summary>Tableros ya colocados (vacío mientras la losa no esté lista).</summary>
        public int TotalTableros => _tableros.Count;

        // ── Construcción ─────────────────────────────────────────────────────
        /// <summary>
        /// Base principal: 4 puntales (2×2), 2 rieles del largo indicado, 2 separadores, 2 crucetas grandes
        /// y 2 pequeñas. Es el punto de partida del armado, tal como se monta en obra.
        /// </summary>
        public static ManualAssembly CrearBasePrincipal(
            double largoRiel = 3.00,
            double largoSeparador = 1.20,
            bool rielesHorizontales = true,
            double origenX = 0,
            double origenY = 0)
        {
            var a = new ManualAssembly
            {
                RielesHorizontales = rielesHorizontales,
                OrigenX = origenX,
                OrigenY = origenY
            };

            int u1 = Mm(largoRiel);
            int v1 = Mm(largoSeparador);

            var n00 = new Nudo(0, 0);
            var n10 = new Nudo(u1, 0);
            var n01 = new Nudo(0, v1);
            var n11 = new Nudo(u1, v1);

            a._nudos.Add(n00);
            a._nudos.Add(n10);
            a._nudos.Add(n01);
            a._nudos.Add(n11);
            a._nudosBase.UnionWith(new[] { n00, n10, n01, n11 });

            // Rieles (a lo largo de u) y separadores (a lo largo de v).
            a.AgregarVano(n00, n10);
            a.AgregarVano(n01, n11);
            a.AgregarVano(n00, n01);
            a.AgregarVano(n10, n11);

            // Crucetas: 2 grandes en los vanos de riel, 2 pequeñas en los de separador.
            a.AgregarCruceta(n00, n10);
            a.AgregarCruceta(n01, n11);
            a.AgregarCruceta(n00, n01);
            a.AgregarCruceta(n10, n11);

            return a;
        }

        // ── Crecimiento desde el cabezal de un puntal ────────────────────────
        /// <summary>
        /// ¿Se puede crecer desde este nudo en esta dirección? Hace falta un puntal COMPAÑERO en el eje
        /// perpendicular (unido por una pieza), porque el sistema no crece de puntal en puntal sino de dos
        /// en dos: los dos puntales viajan siempre juntos con su pieza de amarre.
        /// </summary>
        public bool PuedeExtender(Nudo nudo, Direccion dir, double medida)
            => TryPrepararExtension(nudo, dir, medida, out _, out _, out _, out _);

        /// <summary>
        /// Vista previa de la extensión (sin modificar nada): devuelve dónde caerían los dos puntales nuevos,
        /// desde qué puntales salen y el texto con las piezas exactas que se van a colocar.
        /// </summary>
        public Extension? VistaPrevia(Nudo nudo, Direccion dir, double medida)
        {
            if (!TryPrepararExtension(nudo, dir, medida, out Nudo nuevoA, out Nudo nuevoB, out Nudo ancla, out Nudo companero))
            {
                return null;
            }

            bool ejeRiel = dir is Direccion.RielMas or Direccion.RielMenos;
            int signo = dir is Direccion.RielMas or Direccion.SeparadorMas ? 1 : -1;
            double amarre = ejeRiel
                ? Math.Abs(companero.V - ancla.V) / 1000.0
                : Math.Abs(companero.U - ancla.U) / 1000.0;

            // Solo se arriostra la CRUZ: la base principal y sus dos brazos en línea recta, hasta cubrir la
            // losa. El tipo lo decide la luz del amarre — PEQUEÑA en la fila (eje del riel) y GRANDE en la
            // columna (eje del separador).
            bool conCruceta = EnLaCruzArriostrada(ancla, companero, ejeRiel, signo);
            bool crucetaGrande = amarre >= CrucetaGrandeMin;

            string nombre = ejeRiel
                ? $"Riel de {medida:0.00} m"
                : $"Separador de {medida:0.00} m";

            // El AMARRE no es una medida de catálogo elegida por el usuario: es la distancia REAL que ya hay
            // entre el puntal ancla y su compañero, así que puede no coincidir con la medida de la extensión.
            // Se nombra como "de amarre" para que quede claro que describe esa luz y no lo que se eligió.
            string piezas = ejeRiel
                ? $"2 puntales  +  2 rieles de {medida:0.00} m  +  1 separador de amarre de {amarre:0.00} m"
                : $"2 puntales  +  2 separadores de {medida:0.00} m  +  1 riel de amarre de {amarre:0.00} m";

            string desc = conCruceta
                ? $"{piezas}  +  cruceta {(crucetaGrande ? "grande" : "pequeña")}"
                : piezas;

            return new Extension(
                AMundo(nuevoA), AMundo(nuevoB), AMundo(ancla), AMundo(companero),
                nombre, desc, conCruceta, crucetaGrande);
        }

        /// <summary>
        /// Coloca la extensión: los dos puntales nuevos, sus piezas de unión y su cruceta (pequeña en el eje
        /// del riel, grande en el del separador). Devuelve false si el nudo no puede crecer ahí.
        /// </summary>
        public bool TryExtender(Nudo nudo, Direccion dir, double medida, out string descripcion)
        {
            descripcion = string.Empty;
            if (!TryPrepararExtension(nudo, dir, medida, out Nudo nuevoA, out Nudo nuevoB, out Nudo ancla, out Nudo companero))
            {
                return false;
            }

            Extension? preview = VistaPrevia(nudo, dir, medida);
            descripcion = preview == null ? string.Empty : $"{preview.Nombre} — {preview.Descripcion}";

            _nudos.Add(nuevoA);
            _nudos.Add(nuevoB);

            // Piezas que unen los puntales nuevos con los de apoyo (una por cada uno).
            AgregarVano(ancla, nuevoA);
            AgregarVano(companero, nuevoB);

            // Pieza de amarre entre los dos puntales nuevos.
            AgregarVano(nuevoA, nuevoB);

            // Cruceta solo dentro de la CRUZ ARRIOSTRADA (ver EnLaCruzArriostrada):
            //  • en el eje del RIEL el amarre es un separador → cruceta PEQUEÑA (base secundaria);
            //  • en el eje del SEPARADOR el amarre es un riel de 3 m → cruceta GRANDE.
            if (preview?.ConCruceta == true)
            {
                AgregarCruceta(nuevoA, nuevoB);
            }

            return true;
        }

        /// <summary>
        /// Resuelve la extensión sin tocar el estado: valida el nudo, localiza el puntal compañero
        /// (el que viaja con él) y calcula dónde caen los dos puntales nuevos.
        /// </summary>
        private bool TryPrepararExtension(
            Nudo nudo, Direccion dir, double medida,
            out Nudo nuevoA, out Nudo nuevoB, out Nudo ancla, out Nudo companero)
        {
            nuevoA = default;
            nuevoB = default;
            ancla = nudo;
            companero = default;

            if (Congelado || !_nudos.Contains(nudo) || medida <= 0.01)
            {
                return false;
            }

            int delta = Mm(medida);
            bool ejeRiel = dir is Direccion.RielMas or Direccion.RielMenos;
            int signo = dir is Direccion.RielMas or Direccion.SeparadorMas ? 1 : -1;

            // El compañero es el puntal unido a éste por una pieza en el eje PERPENDICULAR al crecimiento.
            Nudo? posible = BuscarCompanero(nudo, perpendicularAlRiel: ejeRiel);
            if (posible == null)
            {
                return false;
            }
            companero = posible.Value;

            if (ejeRiel)
            {
                int u = nudo.U + (signo * delta);
                nuevoA = new Nudo(u, nudo.V);
                nuevoB = new Nudo(u, companero.V);
            }
            else
            {
                int v = nudo.V + (signo * delta);
                nuevoA = new Nudo(nudo.U, v);
                nuevoB = new Nudo(companero.U, v);
            }

            // Si el nudo nuevo ya existe y además ya está unido, no hay nada que colocar.
            return !(_nudos.Contains(nuevoA) && _nudos.Contains(nuevoB)
                     && _vanos.Contains(Normalizar(ancla, nuevoA))
                     && _vanos.Contains(Normalizar(companero, nuevoB)));
        }

        /// <summary>
        /// Puntal unido a <paramref name="nudo"/> por una pieza. Si <paramref name="perpendicularAlRiel"/>
        /// es true busca por el eje de los separadores (para crecer a lo largo del riel) y viceversa.
        /// Ante varios candidatos elige el más cercano.
        /// </summary>
        private Nudo? BuscarCompanero(Nudo nudo, bool perpendicularAlRiel)
        {
            Nudo? mejor = null;
            int mejorDist = int.MaxValue;

            foreach (Vano vano in _vanos)
            {
                Nudo otro;
                if (vano.A.Equals(nudo))
                {
                    otro = vano.B;
                }
                else if (vano.B.Equals(nudo))
                {
                    otro = vano.A;
                }
                else
                {
                    continue;
                }

                // Para crecer en el eje del RIEL hace falta el compañero del eje del SEPARADOR (mismo U).
                bool sirve = perpendicularAlRiel ? otro.U == nudo.U : otro.V == nudo.V;
                if (!sirve)
                {
                    continue;
                }

                int dist = perpendicularAlRiel ? Math.Abs(otro.V - nudo.V) : Math.Abs(otro.U - nudo.U);
                if (dist > 0 && dist < mejorDist)
                {
                    mejorDist = dist;
                    mejor = otro;
                }
            }

            return mejor;
        }

        // ── CRUZ ARRIOSTRADA Y MEDIDA SUGERIDA ───────────────────────────────
        // Las dos cosas que el armado calcula solo, y las dos miran el mismo dato: cuánto falta para llegar
        // al filo de la losa por el lado hacia el que se está creciendo.

        /// <summary>true si crecer en ese eje avanza por el eje X del plano.</summary>
        private bool AvanzaEnX(bool ejeRiel) => RielesHorizontales ? ejeRiel : !ejeRiel;

        /// <summary>Coordenada del nudo en el eje del plano por el que avanza el crecimiento (m).</summary>
        private double CoordAvance(Nudo n, bool ejeRiel)
        {
            Point p = AMundo(n);
            return AvanzaEnX(ejeRiel) ? p.X : p.Y;
        }

        /// <summary>
        /// Filo de la losa hacia el que se avanza, ya con el signo aplicado: así, avanzando en cualquiera de
        /// los dos sentidos, "haber llegado" siempre es <c>coordenada ≥ filo</c>. Null si no hay plano.
        /// </summary>
        private double? FiloLosa(bool ejeRiel, int signo)
        {
            if (Plano == null)
            {
                return null;
            }

            Losa l = Plano.Value;
            bool enX = AvanzaEnX(ejeRiel);
            double filo = signo > 0
                ? (enX ? l.MaxX : l.MaxY)
                : (enX ? l.MinX : l.MinY);
            return signo * filo;
        }

        /// <summary>
        /// ¿Esta extensión forma parte de la CRUZ ARRIOSTRADA? La cruz es la base principal más sus dos
        /// brazos EN LÍNEA RECTA: la fila (eje del riel, crucetas pequeñas) y la columna (eje del separador,
        /// crucetas grandes), prolongadas hasta cubrir la losa con su voladizo.
        ///
        /// Lo que se mira es de dónde SALE el brazo, no hasta dónde llega: mientras el puntal de apoyo no
        /// haya entrado todavía en la banda de voladizo, la extensión es parte de la cruz y va arriostrada,
        /// AUNQUE el puntal nuevo se pase de los 20 cm — la cruceta de la fila y la de la columna son
        /// obligatorias y no dependen de cuánto sobresalga. En cuanto ese lado ya quedó cubierto, el usuario
        /// pasa a rellenar el resto del campo y ahí ya no van crucetas.
        /// </summary>
        private bool EnLaCruzArriostrada(Nudo ancla, Nudo companero, bool ejeRiel, int signo)
        {
            if (_nudosBase.Count == 0)
            {
                return false;
            }

            // 1) El brazo tiene que salir de la fila o de la columna de la base, en línea recta.
            bool enLinea = ejeRiel
                ? _nudosBase.Any(b => b.V == ancla.V) && _nudosBase.Any(b => b.V == companero.V)
                : _nudosBase.Any(b => b.U == ancla.U) && _nudosBase.Any(b => b.U == companero.U);
            if (!enLinea)
            {
                return false;
            }

            // 2) Sin plano no hay filo contra el que medir: se arriostra el brazo entero.
            double? filo = FiloLosa(ejeRiel, signo);
            if (filo == null)
            {
                return true;
            }

            const double tol = 0.005;
            double desde = signo * CoordAvance(ancla, ejeRiel);
            return desde < filo.Value + VoladizoMin - tol;
        }

        /// <summary>
        /// Medida que conviene colocar al crecer desde ese nudo.
        ///
        /// El objetivo NO es llegar al filo de la losa sino CUBRIRLA CON SU VOLADIZO: el puntal nuevo tiene
        /// que caer al menos 20 cm más allá del filo, que es la línea entrecortada del plano. Por eso se
        /// elige la medida de catálogo MÁS AJUSTADA que alcance ese objetivo — y si la más corta disponible
        /// se pasa de largo, no importa: el encofrado debe sobresalir, nunca quedarse corto.
        ///
        /// Lejos del borde ninguna medida del catálogo llega tan lejos, y entonces manda el CONTEXTO: la
        /// medida que predomina en lo ya armado para esa clase de pieza. Lo mismo cuando ese lado ya quedó
        /// cubierto y el usuario está rellenando el resto del campo.
        /// </summary>
        public double MedidaSugerida(Nudo nudo, Direccion dir, double porDefecto)
        {
            bool ejeRiel = dir is Direccion.RielMas or Direccion.RielMenos;
            int signo = dir is Direccion.RielMas or Direccion.SeparadorMas ? 1 : -1;
            double contexto = MedidaPredominante(ejeRiel, porDefecto);

            double? filo = FiloLosa(ejeRiel, signo);
            if (filo == null)
            {
                return contexto;
            }

            const double tol = 0.005;
            double restante = filo.Value - (signo * CoordAvance(nudo, ejeRiel));

            // Ese lado ya sobresale lo suficiente: se está rellenando otra zona, manda el contexto.
            if (restante <= -VoladizoMin)
            {
                return contexto;
            }

            // Hay que llegar AL MENOS al filo más el voladizo completo.
            double objetivo = restante + VoladizoMax;

            double[] catalogo = ejeRiel ? LayoutCatalog.RailLengths : LayoutCatalog.SeparatorLengths;
            foreach (double m in catalogo.OrderBy(x => x))
            {
                if (m >= objetivo - tol)
                {
                    return m;   // la más corta que cubre el voladizo, aunque se pase
                }
            }

            // El borde queda más lejos que la pieza más larga del catálogo: se sigue con el contexto.
            return contexto;
        }

        /// <summary>Medida más repetida entre las piezas ya colocadas de esa clase (a igualdad, la mayor).</summary>
        private double MedidaPredominante(bool rieles, double porDefecto)
        {
            var grupo = _vanos
                .Where(v => v.EsRiel == rieles && v.LargoMm > 0)
                .GroupBy(v => v.LargoMm)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Key)
                .FirstOrDefault();

            return grupo == null ? porDefecto : grupo.Key / 1000.0;
        }

        // ── ELIMINAR UNA RED ─────────────────────────────────────────────────
        // "Red" = la unidad de armado a la que pertenece una pieza. Al señalar un riel (o un separador) con el
        // puntero y pulsar SUPRIMIR se retira esa pieza Y todo lo que dependía de ella: los puntales que se
        // quedan sin amarre en alguno de los dos ejes, sus piezas y sus crucetas. Es exactamente lo contrario
        // de una extensión: si se borra el riel que unió dos puntales nuevos, se van esos dos puntales con sus
        // dos separadores, porque en obra ya no se sostienen solos.

        /// <summary>Recuento de lo que se retiró al eliminar una red.</summary>
        public readonly record struct Borrado(int Puntales, int Rieles, int Separadores, int Crucetas)
        {
            public int Total => Puntales + Rieles + Separadores + Crucetas;

            public override string ToString()
                => $"{Puntales} puntal(es) · {Rieles} riel(es) · {Separadores} separador(es) · {Crucetas} cruceta(s)";
        }

        /// <summary>Pieza señalada por el puntero, ya validada y lista para eliminarse con su red.</summary>
        public sealed record RedSenalada(Vano Pieza, string Nombre);

        /// <summary>
        /// Localiza la pieza que hay entre esos dos extremos del plano (los que devuelve el resalte del 3D) y
        /// comprueba que se pueda eliminar. Devuelve null y el motivo si no procede. NO modifica nada: así el
        /// módulo puede guardar el paso en el registro de deshacer solo cuando el borrado va a ocurrir.
        /// </summary>
        public RedSenalada? LocalizarRed(Point extremoA, Point extremoB, out string motivo, double tolerancia = 0.10)
        {
            motivo = string.Empty;

            if (Congelado)
            {
                motivo = "La losa está lista: reábrala para poder eliminar piezas.";
                return null;
            }

            Vano? encontrado = null;
            double mejor = tolerancia;

            foreach (Vano v in _vanos)
            {
                Point pa = AMundo(v.A);
                Point pb = AMundo(v.B);
                double directo = Math.Max(Separacion(pa, extremoA), Separacion(pb, extremoB));
                double invertido = Math.Max(Separacion(pa, extremoB), Separacion(pb, extremoA));
                double d = Math.Min(directo, invertido);
                if (d < mejor)
                {
                    mejor = d;
                    encontrado = v;
                }
            }

            if (encontrado == null)
            {
                motivo = "Lleve el puntero sobre un riel o un separador y pulse Suprimir.";
                return null;
            }

            Vano pieza = encontrado.Value;

            // La base principal no se desarma con Suprimir: tiene su propio botón "Rehacer base principal".
            if (EsDeLaBase(pieza.A) && EsDeLaBase(pieza.B))
            {
                motivo = "Esa pieza es de la base principal: use \"Rehacer base principal\" para cambiarla.";
                return null;
            }

            string nombre = pieza.EsRiel
                ? $"Riel de {pieza.LargoMm / 1000.0:0.00} m"
                : $"Separador de {pieza.LargoMm / 1000.0:0.00} m";
            return new RedSenalada(pieza, nombre);
        }

        /// <summary>
        /// Elimina la red: retira la pieza señalada y, en cascada, todo puntal que se quede sin amarre en
        /// alguno de los dos ejes (sin riel o sin separador), con sus piezas y sus crucetas. Los 4 puntales de
        /// la base principal nunca se retiran. Devuelve el recuento de lo eliminado.
        /// </summary>
        public Borrado EliminarRed(RedSenalada red)
        {
            int rieles = 0;
            int separadores = 0;
            int puntales = 0;
            int crucetas = 0;

            void Quitar(Vano v)
            {
                if (!_vanos.Remove(v))
                {
                    return;
                }
                if (v.EsRiel)
                {
                    rieles++;
                }
                else
                {
                    separadores++;
                }
            }

            Quitar(red.Pieza);

            bool hubocambio = true;
            while (hubocambio)
            {
                hubocambio = false;
                HashSet<Nudo> conectados = NudosUnidosALaBase();

                foreach (Nudo n in _nudos.ToList())
                {
                    if (EsDeLaBase(n))
                    {
                        continue;   // la base principal es el ancla del armado: nunca se cae
                    }

                    bool conRiel = _vanos.Any(x => x.EsRiel && (x.A.Equals(n) || x.B.Equals(n)));
                    bool conSeparador = _vanos.Any(x => !x.EsRiel && (x.A.Equals(n) || x.B.Equals(n)));

                    // Se cae el puntal que pierde el amarre en alguno de los dos ejes y también el que queda
                    // suelto, sin camino de piezas hasta la base principal.
                    if (conRiel && conSeparador && conectados.Contains(n))
                    {
                        continue;
                    }

                    foreach (Vano incidente in _vanos.Where(x => x.A.Equals(n) || x.B.Equals(n)).ToList())
                    {
                        Quitar(incidente);
                    }
                    crucetas += _crucetas.RemoveWhere(c => c.A.Equals(n) || c.B.Equals(n));
                    _nudos.Remove(n);
                    puntales++;
                    hubocambio = true;
                }
            }

            // Crucetas que se quedaron sin la pieza que arriostraban.
            crucetas += _crucetas.RemoveWhere(c => !_vanos.Contains(Normalizar(c.A, c.B)));

            return new Borrado(puntales, rieles, separadores, crucetas);
        }

        /// <summary>Puntales que todavía tienen un camino de piezas hasta la base principal.</summary>
        private HashSet<Nudo> NudosUnidosALaBase()
        {
            var vistos = new HashSet<Nudo>(_nudosBase.Where(n => _nudos.Contains(n)));
            var pendientes = new Queue<Nudo>(vistos);

            while (pendientes.Count > 0)
            {
                Nudo actual = pendientes.Dequeue();
                foreach (Vano v in _vanos)
                {
                    Nudo otro;
                    if (v.A.Equals(actual))
                    {
                        otro = v.B;
                    }
                    else if (v.B.Equals(actual))
                    {
                        otro = v.A;
                    }
                    else
                    {
                        continue;
                    }

                    if (vistos.Add(otro))
                    {
                        pendientes.Enqueue(otro);
                    }
                }
            }

            return vistos;
        }

        private static double Separacion(Point a, Point b)
            => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

        // ── Consulta ─────────────────────────────────────────────────────────
        /// <summary>Nudos (puntales) del armado con su posición en el plano.</summary>
        public IEnumerable<(Nudo Nudo, Point Punto)> Nudos()
            => _nudos.Select(n => (n, AMundo(n)));

        /// <summary>Total de puntales del armado (los mismos que dibuja el 3D y cuenta el BOM).</summary>
        public int TotalPuntales => _nudos.Count;

        /// <summary>Total de piezas lineales colocadas (rieles + separadores).</summary>
        public int TotalVanos => _vanos.Count;

        /// <summary>
        /// INVENTARIO VIVO del armado: exactamente las piezas que hay puestas AHORA, agrupadas por su medida
        /// REAL (la distancia entre los nudos que unen), no por lo que se colocó en el último clic. Es lo que
        /// debe leer el registro del módulo 3D: así el texto siempre describe lo que se está viendo en
        /// pantalla y nunca queda un aviso viejo contando una pieza que ya no está.
        /// </summary>
        public string ResumenVivo()
        {
            if (_nudos.Count == 0)
            {
                return "Armado vacío: no hay ninguna pieza colocada.";
            }

            Rect caja = CajaEnvolvente;
            var partes = new List<string>
            {
                $"{caja.Width:0.00} × {caja.Height:0.00} m",
                $"{_nudos.Count} puntal(es)"
            };

            string? textoRieles = InventarioPorMedida(rieles: true);
            if (textoRieles != null)
            {
                partes.Add(textoRieles);
            }

            string? textoSeparadores = InventarioPorMedida(rieles: false);
            if (textoSeparadores != null)
            {
                partes.Add(textoSeparadores);
            }

            int grandes = _crucetas.Count(c => c.LargoMm / 1000.0 >= CrucetaGrandeMin);
            int pequenas = _crucetas.Count - grandes;
            if (grandes > 0)
            {
                partes.Add($"{grandes} cruceta(s) grande(s)");
            }
            if (pequenas > 0)
            {
                partes.Add($"{pequenas} cruceta(s) pequeña(s)");
            }

            if (Congelado)
            {
                partes.Add(_tableros.Count > 0 ? $"losa lista y tablerada ({_tableros.Count})" : "losa lista");
            }

            return string.Join("  ·  ", partes);
        }

        /// <summary>
        /// Piezas de una clase agrupadas por su medida real, de la más larga a la más corta:
        /// "Separadores: 2 × 1,20 m + 1 × 1,00 m". Null si todavía no hay ninguna de esa clase.
        /// </summary>
        private string? InventarioPorMedida(bool rieles)
        {
            List<string> grupos = _vanos
                .Where(v => v.EsRiel == rieles && v.LargoMm > 0)
                .GroupBy(v => v.LargoMm)
                .OrderByDescending(g => g.Key)
                .Select(g => $"{g.Count()} × {g.Key / 1000.0:0.00} m")
                .ToList();

            return grupos.Count == 0
                ? null
                : (rieles ? "Rieles: " : "Separadores: ") + string.Join(" + ", grupos);
        }

        /// <summary>Caja envolvente del armado en el plano (m), sin el desplazamiento de arrastre.</summary>
        public Rect CajaEnvolvente
        {
            get
            {
                if (_nudos.Count == 0)
                {
                    return new Rect(OrigenX, OrigenY, 0, 0);
                }

                int u0 = _nudos.Min(n => n.U), u1 = _nudos.Max(n => n.U);
                int v0 = _nudos.Min(n => n.V), v1 = _nudos.Max(n => n.V);

                Point a = AMundo(new Nudo(u0, v0));
                Point b = AMundo(new Nudo(u1, v1));
                return new Rect(a, b);
            }
        }

        /// <summary>Diagonal de esquina a esquina del armado (m) — la cota para centrarlo en el plano.</summary>
        public double Diagonal
        {
            get
            {
                Rect r = CajaEnvolvente;
                return Math.Sqrt((r.Width * r.Width) + (r.Height * r.Height));
            }
        }

        /// <summary>Copia profunda (para la pila de deshacer).</summary>
        public ManualAssembly Clonar()
        {
            var copia = new ManualAssembly
            {
                RielesHorizontales = RielesHorizontales,
                OrigenX = OrigenX,
                OrigenY = OrigenY
            };
            copia._nudos.UnionWith(_nudos);
            copia._vanos.UnionWith(_vanos);
            copia._crucetas.UnionWith(_crucetas);
            copia._nudosBase.UnionWith(_nudosBase);
            copia._tableros.AddRange(_tableros);
            copia._tablas.AddRange(_tablas);
            copia.Congelado = Congelado;
            copia.Plano = Plano;
            return copia;
        }

        // ── Salida: el MISMO PreviewLayout que consume el 3D y el BOM ────────
        /// <summary>
        /// Convierte el armado en un <see cref="PreviewLayout"/>. Sin tableros, remates ni tablas: el modo
        /// manual muestra solo la estructura portante (puntales, rieles, separadores y crucetas).
        /// </summary>
        public PreviewLayout Build()
        {
            var puntales = new List<Point>();
            var rielesH = new List<LinearPiece>();
            var rielesV = new List<LinearPiece>();
            var sepsH = new List<LinearPiece>();
            var sepsV = new List<LinearPiece>();
            var crucetasPeq = new List<CrucetaPiece>();
            var crucetasGrandes = new List<CrucetaPiece>();

            foreach (Nudo n in _nudos.OrderBy(n => n.V).ThenBy(n => n.U))
            {
                puntales.Add(AMundo(n));
            }

            foreach (Vano vano in _vanos.OrderBy(x => x.A.V).ThenBy(x => x.A.U))
            {
                double nominal = vano.LargoMm / 1000.0;
                if (nominal <= 0.001)
                {
                    continue;
                }

                if (vano.EsRiel)
                {
                    LinearPiece pieza = PiezaEnEjeRiel(vano.A.U, vano.B.U, vano.A.V, nominal);
                    if (RielesHorizontales)
                    {
                        rielesH.Add(pieza);
                    }
                    else
                    {
                        rielesV.Add(pieza);
                    }
                }
                else
                {
                    LinearPiece pieza = PiezaEnEjeSeparador(vano.A.V, vano.B.V, vano.A.U, nominal);
                    if (RielesHorizontales)
                    {
                        sepsV.Add(pieza);
                    }
                    else
                    {
                        sepsH.Add(pieza);
                    }
                }
            }

            foreach (Vano cruceta in _crucetas)
            {
                var pieza = new CrucetaPiece(AMundo(cruceta.A), AMundo(cruceta.B));
                if (cruceta.LargoMm / 1000.0 >= CrucetaGrandeMin)
                {
                    crucetasGrandes.Add(pieza);
                }
                else
                {
                    crucetasPeq.Add(pieza);
                }
            }

            return new PreviewLayout(
                puntales, rielesH, rielesV, sepsH, sepsV,
                crucetasPeq, crucetasGrandes,
                new List<BoardPiece>(_tableros),
                new List<Rect>(), new List<Rect>(_tablas), new List<Rect>(),
                EsquinasBase());
        }

        // ── Auxiliares ───────────────────────────────────────────────────────
        private Point[]? EsquinasBase()
        {
            if (_nudos.Count < 4)
            {
                return null;
            }

            int u0 = _nudos.Min(n => n.U), v0 = _nudos.Min(n => n.V);
            int u1 = _nudos.Where(n => n.U > u0).Select(n => n.U).DefaultIfEmpty(u0).Min();
            int v1 = _nudos.Where(n => n.V > v0).Select(n => n.V).DefaultIfEmpty(v0).Min();

            return new[]
            {
                AMundo(new Nudo(u0, v0)),
                AMundo(new Nudo(u1, v0)),
                AMundo(new Nudo(u0, v1)),
                AMundo(new Nudo(u1, v1))
            };
        }

        // ── CONTORNO INTERNO Y TABLEROS ──────────────────────────────────────
        /// <summary>Celda del armado: el recuadro cerrado entre dos rieles y dos separadores (mm).</summary>
        public readonly record struct Celda(int U0, int U1, int V0, int V1);

        /// <summary>
        /// Recuadros cerrados del armado. Cada extensión deja uno: es la unidad sobre la que después se
        /// reparten los tableros, y sirve para saber qué piezas quedan en el borde del armado.
        /// </summary>
        public IReadOnlyList<Celda> Celdas()
        {
            var celdas = new HashSet<Celda>();

            foreach (Vano r in _vanos.Where(v => v.EsRiel))
            {
                int u0 = Math.Min(r.A.U, r.B.U);
                int u1 = Math.Max(r.A.U, r.B.U);
                int v = r.A.V;

                foreach (Vano otro in _vanos.Where(x => x.EsRiel && x.A.V != v))
                {
                    if (Math.Min(otro.A.U, otro.B.U) != u0 || Math.Max(otro.A.U, otro.B.U) != u1)
                    {
                        continue;
                    }

                    int v2 = otro.A.V;
                    if (_vanos.Contains(Normalizar(new Nudo(u0, v), new Nudo(u0, v2)))
                        && _vanos.Contains(Normalizar(new Nudo(u1, v), new Nudo(u1, v2))))
                    {
                        celdas.Add(new Celda(u0, u1, Math.Min(v, v2), Math.Max(v, v2)));
                    }
                }
            }

            return celdas.ToList();
        }

        /// <summary>Segmento del contorno interno, ya en coordenadas del plano.</summary>
        public readonly record struct SegmentoContorno(Point A, Point B, bool EsRiel);

        /// <summary>
        /// CONTORNO INTERNO del armado: recorre el borde real de la estructura (también por los entrantes,
        /// así que en cruz se ve la cruz) y lo dibuja por dentro de las piezas:
        ///  • en los lados de RIEL, por su FILO INTERNO (media anchura hacia adentro), que es lo que puede
        ///    chocar con la cara de una columna;
        ///  • en los lados de SEPARADOR, por su CENTRO.
        /// Una pieza es de borde cuando pertenece a una sola celda; la celda indica hacia dónde está el
        /// interior y, por tanto, hacia dónde desplazar la línea del riel.
        /// </summary>
        public IReadOnlyList<SegmentoContorno> ContornoInterno(double anchoRiel)
        {
            var salida = new List<SegmentoContorno>();
            IReadOnlyList<Celda> celdas = Celdas();
            if (celdas.Count == 0)
            {
                return salida;
            }

            int medio = Mm(anchoRiel / 2);

            foreach (Vano vano in _vanos)
            {
                if (vano.EsRiel)
                {
                    int u0 = Math.Min(vano.A.U, vano.B.U);
                    int u1 = Math.Max(vano.A.U, vano.B.U);
                    int v = vano.A.V;

                    List<Celda> tocan = celdas.Where(c => c.U0 == u0 && c.U1 == u1 && (c.V0 == v || c.V1 == v)).ToList();
                    if (tocan.Count != 1)
                    {
                        continue;   // pieza interior: no forma borde
                    }

                    // El interior de la celda marca hacia dónde corre el filo interno del riel.
                    int haciaDentro = tocan[0].V0 == v ? medio : -medio;
                    salida.Add(new SegmentoContorno(
                        AMundo(new Nudo(u0, v + haciaDentro)),
                        AMundo(new Nudo(u1, v + haciaDentro)),
                        true));
                }
                else
                {
                    int v0 = Math.Min(vano.A.V, vano.B.V);
                    int v1 = Math.Max(vano.A.V, vano.B.V);
                    int u = vano.A.U;

                    List<Celda> tocan = celdas.Where(c => c.V0 == v0 && c.V1 == v1 && (c.U0 == u || c.U1 == u)).ToList();
                    if (tocan.Count != 1)
                    {
                        continue;
                    }

                    // El separador se cotea por su CENTRO: sin desplazamiento.
                    salida.Add(new SegmentoContorno(
                        AMundo(new Nudo(u, v0)),
                        AMundo(new Nudo(u, v1)),
                        false));
                }
            }

            return salida;
        }

        /// <summary>Ancho estándar de tablero (m): el que se usa para el grueso de la losa.</summary>
        private const double AnchoEstandar = 0.60;

        /// <summary>Anchos PEQUEÑOS del catálogo (0,55 … 0,30): los de cerrar contra una columna o un borde.</summary>
        private static readonly double[] AnchosPequenos =
            LayoutCatalog.BoardWidthsDescending.Where(w => w < AnchoEstandar - 0.001).ToArray();

        /// <summary>Hasta cuántas columnas encadenadas puede partir una misma celda (freno de la recursión).</summary>
        private const int NivelesGuillotina = 8;

        /// <summary>
        /// Reparte los TABLEROS de contacto sobre la estructura ya congelada. Celda por celda:
        ///  • donde hay una COLUMNA se parte la celda en guillotina alrededor de ella (izquierda, derecha,
        ///    debajo y encima), de modo que el tablero llega hasta el FILO de la columna y solo queda libre
        ///    su huella, no el recuadro entero;
        ///  • cada tramo libre se llena con tableros estándar de 0,60 y se cierra con hasta tres anchos
        ///    PEQUEÑOS del catálogo, pudiendo retirar un estándar y poner dos pequeños en su lugar si con eso
        ///    se cubre más superficie;
        ///  • entre los cierres que cubren lo mismo se elige el que usa los anchos pequeños MENOS empleados
        ///    hasta ese momento, para que las medidas chicas se repartan por toda la losa en vez de repetir
        ///    siempre la misma junto a cada columna.
        /// Devuelve cuántos tableros colocó; requiere <see cref="Congelado"/>.
        /// </summary>
        public int ColocarTableros(IEnumerable<ColumnSnapshot>? columnas = null, double? voladizo = null)
        {
            _tableros.Clear();
            _tablas.Clear();
            if (!Congelado)
            {
                return 0;
            }

            List<(double U0, double U1, double V0, double V1)> cols =
                (columnas ?? Enumerable.Empty<ColumnSnapshot>()).Select(ColumnaEnUV).ToList();

            var uso = new Dictionary<double, int>();
            foreach (double ancho in AnchosPequenos)
            {
                uso[ancho] = 0;
            }

            double eps = LayoutCatalog.GeometryEpsilon;
            (double U0, double U1, double V0, double V1)? linea = LineaVoladizoEnUV(voladizo);

            foreach (var banda in BandasContinuas())
            {
                double uA = banda.U0, uB = banda.U1, vA = banda.V0, vB = banda.V1;
                bool cortadoU1 = false, cortadoV0 = false, cortadoV1 = false;

                if (linea.HasValue)
                {
                    // LÍNEA DE VOLADIZO: los tableros solo hacen falta hasta ella. Lo que el armado se
                    // extienda más allá —esas filas de rieles que sobran por fuera— se queda VACÍO: ni
                    // tablero ni remate con tabla.
                    var l = linea.Value;
                    cortadoV0 = l.V0 > vA + eps;
                    cortadoV1 = l.V1 < vB - eps;
                    cortadoU1 = l.U1 < uB - eps;

                    uA = Math.Max(uA, l.U0);
                    uB = Math.Min(uB, l.U1);
                    vA = Math.Max(vA, l.V0);
                    vB = Math.Min(vB, l.V1);

                    if (uB - uA <= eps || vB - vA <= eps)
                    {
                        continue;   // la banda entera cae fuera de la línea: no se toca
                    }
                }

                // Cuánto puede pasarse el tablero por cada lado. La línea manda sobre el filo del riel:
                // donde la corta la línea, el tablero se pasa de ella lo justo para cubrirla.
                double vueloA = cortadoV0 ? VueloSobreLinea : (banda.FiloV0 ? VueloEnFilo : 0);
                double vueloB = cortadoV1 ? VueloSobreLinea : (banda.FiloV1 ? VueloEnFilo : 0);
                double vueloU = cortadoU1 ? VueloSobreLinea : 0;

                RellenarZona(uA, uB, vA, vB, cols, uso, 0, vueloA, vueloB, vueloU);
            }

            return _tableros.Count;
        }

        /// <summary>
        /// LÍNEA DE VOLADIZO en los ejes internos del armado (u = riel, v = separador): el filo de la losa
        /// ensanchado el volado que el usuario eligió en el ribbon (5, 10, 15 o 20 cm). Es hasta donde hacen
        /// falta tableros. Devuelve null si no hay plano de losa o no se pidió volado, y entonces el reparto
        /// corre sobre todo el armado, como antes.
        /// </summary>
        private (double U0, double U1, double V0, double V1)? LineaVoladizoEnUV(double? voladizo)
        {
            if (Plano == null || voladizo == null)
            {
                return null;
            }

            Losa l = Plano.Value;
            double d = Math.Max(0, voladizo.Value);
            double x0 = l.MinX - d - OrigenX, x1 = l.MaxX + d - OrigenX;
            double y0 = l.MinY - d - OrigenY, y1 = l.MaxY + d - OrigenY;

            return RielesHorizontales ? (x0, x1, y0, y1) : (y0, y1, x0, x1);
        }

        /// <summary>
        /// BANDAS CONTINUAS: las celdas pegadas de una misma fila se unen en un solo tramo. El tablero no
        /// tiene por qué cortarse en cada riel —se apoya encima y sigue—, así que el reparto corre de un
        /// extremo al otro de la banda y deja UN solo hueco al final del recorrido, en vez de uno por celda.
        /// Es lo que menos piezas gasta. Las columnas siguen partiendo la banda, eso lo resuelve la
        /// guillotina de <see cref="RellenarZona"/>.
        /// </summary>
        private IEnumerable<(double U0, double U1, double V0, double V1, bool FiloV0, bool FiloV1)> BandasContinuas()
        {
            IReadOnlyList<Celda> todas = Celdas();

            // ¿Sigue habiendo armado al OTRO lado de esta fila de rieles, en el tramo de la banda? Si no lo
            // hay, esa línea es un FILO: no viene ninguna fila de rieles paralela con la que compartir el
            // riel, y el tablero puede volar sobre él.
            bool HayVecina(int uDesde, int uHasta, int v, bool porEncima)
            {
                foreach (Celda c in todas)
                {
                    if (c.U1 <= uDesde || c.U0 >= uHasta)
                    {
                        continue;   // no se solapa con la banda
                    }
                    if (porEncima ? c.V0 == v : c.V1 == v)
                    {
                        return true;
                    }
                }
                return false;
            }

            foreach (var fila in todas.GroupBy(c => (c.V0, c.V1)).OrderBy(g => g.Key.V0))
            {
                List<Celda> celdas = fila.OrderBy(c => c.U0).ToList();
                int u0 = celdas[0].U0;
                int u1 = celdas[0].U1;

                for (int i = 1; i < celdas.Count; i++)
                {
                    if (celdas[i].U0 == u1)
                    {
                        u1 = celdas[i].U1;   // pegada a la anterior: sigue siendo la misma banda
                        continue;
                    }

                    yield return (u0 / 1000.0, u1 / 1000.0, fila.Key.V0 / 1000.0, fila.Key.V1 / 1000.0,
                        !HayVecina(u0, u1, fila.Key.V0, false), !HayVecina(u0, u1, fila.Key.V1, true));
                    u0 = celdas[i].U0;
                    u1 = celdas[i].U1;
                }

                yield return (u0 / 1000.0, u1 / 1000.0, fila.Key.V0 / 1000.0, fila.Key.V1 / 1000.0,
                    !HayVecina(u0, u1, fila.Key.V0, false), !HayVecina(u0, u1, fila.Key.V1, true));
            }
        }

        /// <summary>Huella de la columna en los ejes internos del armado (u = riel, v = separador).</summary>
        private (double U0, double U1, double V0, double V1) ColumnaEnUV(ColumnSnapshot c)
            => RielesHorizontales
                ? (c.MinX - OrigenX, c.MaxX - OrigenX, c.MinY - OrigenY, c.MaxY - OrigenY)
                : (c.MinY - OrigenY, c.MaxY - OrigenY, c.MinX - OrigenX, c.MaxX - OrigenX);

        /// <summary>
        /// Llena de tableros un rectángulo del armado. Si una columna lo corta, lo parte en guillotina
        /// alrededor de ella y repite en cada trozo; si está libre, coloca filas de tableros.
        /// </summary>
        private void RellenarZona(
            double uA, double uB, double vA, double vB,
            List<(double U0, double U1, double V0, double V1)> cols,
            Dictionary<double, int> uso, int nivel,
            double vueloA = 0, double vueloB = 0, double vueloU = 0, double toleranciaU = 0)
        {
            double eps = LayoutCatalog.GeometryEpsilon;
            if (uB - uA <= eps || vB - vA <= eps || nivel > NivelesGuillotina)
            {
                return;
            }

            foreach (var c in cols)
            {
                if (c.U1 <= uA + eps || c.U0 >= uB - eps || c.V1 <= vA + eps || c.V0 >= vB - eps)
                {
                    continue;   // esta columna no toca la zona
                }

                double cu0 = Math.Max(uA, c.U0), cu1 = Math.Min(uB, c.U1);
                double cv0 = Math.Max(vA, c.V0), cv1 = Math.Min(vB, c.V1);

                // Contra la cara de una columna el tablero NO puede VOLAR (no hay dónde), pero sí se le admite
                // la TOLERANCIA de 1 cm: el trozo que topa con la columna puede pasarse ese centímetro para no
                // perder un tablero entero por un error de trazado. Los lados que siguen siendo de la banda
                // conservan su vuelo tal cual.
                RellenarZona(uA, cu0, vA, vB, cols, uso, nivel + 1,                            // a un lado
                    vueloA, vueloB, 0, ToleranciaColumna);
                RellenarZona(cu1, uB, vA, vB, cols, uso, nivel + 1,                            // al otro lado
                    vueloA, vueloB, vueloU, toleranciaU);
                RellenarZona(cu0, cu1, vA, cv0, cols, uso, nivel + 1,                          // por debajo
                    vueloA, ToleranciaColumna, 0, ToleranciaColumna);
                RellenarZona(cu0, cu1, cv1, vB, cols, uso, nivel + 1,                          // por encima
                    ToleranciaColumna, vueloB, 0, ToleranciaColumna);
                return;
            }

            // Zona libre: filas de tableros en el sentido del separador.
            //
            // REGLA DEL RIEL: el tablero termina SIEMPRE en la mitad del riel, porque al otro lado viene la
            // fila de rieles paralela y los dos tableros comparten el mismo riel. Si el tablero se pasara de
            // esa mitad, chocaría con el de la banda vecina — que es justo lo que hacía antes.
            //
            // REGLA DEL FILO: en el borde del armado NO hay ninguna fila de rieles que siga en paralelo, así
            // que ahí el tablero SÍ puede rebasar la mitad del riel y volar hacia afuera hasta 15 cm. Es el
            // caso de una banda de 0,80 (separador de 0,80) rematada con un tablero de 0,85: los 5 cm de más
            // vuelan sobre el riel del filo y se ahorra el remate con tabla.
            //
            // REGLA DE LA LÍNEA: donde la banda la corta la línea de voladizo, el tablero que llega a ella
            // puede pasársela; se elige la pieza más pequeña que la cubra.
            if (vueloB >= vueloA)
            {
                // Se llena desde vA hacia vB, para que el sobrante caiga en el lado que admite vuelo.
                double v = vA;
                while (vB - v > eps)
                {
                    (double largo, bool girado) = ElegirLargoTablero(vB - v, vueloB);
                    if (largo <= 0)
                    {
                        AnotarRemate(uA, uB, v, vB);   // franja demasiado estrecha: se cierra con tabla
                        return;
                    }

                    ColocarFila(uA, uB, v, largo, girado, uso, vueloU, toleranciaU);
                    v += largo;
                }
            }
            else
            {
                // El filo está en vA: se recorre al revés para que el vuelo salga por ese lado.
                double v = vB;
                while (v - vA > eps)
                {
                    (double largo, bool girado) = ElegirLargoTablero(v - vA, vueloA);
                    if (largo <= 0)
                    {
                        AnotarRemate(uA, uB, vA, v);
                        return;
                    }

                    ColocarFila(uA, uB, v - largo, largo, girado, uso, vueloU, toleranciaU);
                    v -= largo;
                }
            }
        }

        /// <summary>
        /// Coloca una fila de tableros a lo ancho del tramo, con el reparto de anchos elegido. Si el tramo lo
        /// recortó la LÍNEA DE VOLADIZO (<paramref name="vueloU"/> &gt; 0), el hueco que quede al final se
        /// cierra con la pieza más pequeña del catálogo que llegue a la línea, aunque se pase de ella.
        /// </summary>
        private void ColocarFila(
            double uA, double uB, double v0, double largo, bool girado, Dictionary<double, int> uso,
            double vueloU = 0, double toleranciaU = 0)
        {
            List<double> anchos = girado
                ? RepartoGirado(uB - uA)
                : RepartoAnchos(uB - uA, uso, toleranciaU);

            double u = uA;
            foreach (double ancho in anchos)
            {
                _tableros.Add(new BoardPiece(RectDeCelda(u, u + ancho, v0, v0 + largo), ancho, largo));
                if (!girado && ancho < AnchoEstandar - 0.001)
                {
                    uso[ancho] = uso.TryGetValue(ancho, out int n) ? n + 1 : 1;
                }
                u += ancho;
            }

            // Cierre contra la LÍNEA DE VOLADIZO: el hueco que queda hasta la línea se tapa con el ancho de
            // catálogo más pequeño que llegue a ella, aunque se pase. En las filas GIRADAS no se hace: ahí la
            // banda trabaja con una sola medida por norma del módulo y el sobrante va a remate con tabla.
            double resto = uB - u;
            if (vueloU > 0 && !girado && resto > RemateMinimo)
            {
                double cierre = MedidaQueCubre(resto, vueloU, LayoutCatalog.BoardWidthsDescending);
                if (cierre > 0)
                {
                    _tableros.Add(new BoardPiece(RectDeCelda(u, u + cierre, v0, v0 + largo), cierre, largo));
                    if (cierre < AnchoEstandar - 0.001)
                    {
                        uso[cierre] = uso.TryGetValue(cierre, out int n) ? n + 1 : 1;
                    }
                    u += cierre;
                }
            }

            AnotarRemate(u, uB, v0, v0 + largo);
        }

        /// <summary>
        /// Medida de catálogo MÁS PEQUEÑA que cubre el hueco sin pasarse más de <paramref name="vuelo"/>.
        /// Devuelve 0 si ninguna llega.
        /// </summary>
        private static double MedidaQueCubre(double hueco, double vuelo, double[] catalogoDescendente)
        {
            double eps = LayoutCatalog.GeometryEpsilon;

            for (int i = catalogoDescendente.Length - 1; i >= 0; i--)
            {
                double m = catalogoDescendente[i];
                if (m >= hueco - eps && m <= hueco + vuelo + eps)
                {
                    return m;
                }
            }

            return 0;
        }

        /// <summary>
        /// Guarda como REMATE CON TABLA el hueco que quedó sin tablero. Es lo que se ve pegado a cada
        /// columna y en las franjas de borde: en el 3D sale con su color propio y el módulo de cálculo lo
        /// suma como superficie de tabla.
        /// </summary>
        private void AnotarRemate(double uA, double uB, double vA, double vB)
        {
            if (uB - uA <= RemateMinimo || vB - vA <= RemateMinimo)
            {
                return;
            }
            _tablas.Add(RectDeCelda(uA, uB, vA, vB));
        }

        /// <summary>
        /// Anchos de catálogo con los que se cubre un tramo libre, en el sentido del riel.
        ///
        /// Se prueban todas las combinaciones de N tableros estándar de 0,60 + hasta 3 anchos PEQUEÑOS y gana,
        /// por este orden:
        ///  1. la que cubre MÁS tramo —contando como mucho el tramo, lo que se pase por tolerancia no puntúa—;
        ///  2. la que conserva más tableros estándar;
        ///  3. la de menos piezas;
        ///  4. la de MENOR COSTE DE REPARTO: se prefieren los anchos pequeños menos gastados hasta ahora en
        ///     toda la losa y se penaliza repetir el mismo ancho dentro de un mismo cierre. Así las medidas
        ///     chicas rotan por la obra en vez de salir siempre la misma junto a cada columna.
        ///
        /// <paramref name="tolerancia"/> es la holgura que el reparto puede comerse contra el filo de una
        /// columna (1 cm). Con ella un tramo de 2,99 acepta los 5 tableros de 0,60 en vez de bajar a
        /// 4 × 0,60 + 0,55; vale 0 en cualquier tramo que no tope con una columna.
        /// </summary>
        private static List<double> RepartoAnchos(double tramo, Dictionary<double, int> uso, double tolerancia = 0)
        {
            double eps = LayoutCatalog.GeometryEpsilon;
            double[] pequenos = AnchosPequenos;
            double techo = tramo + tolerancia;
            int maxEstandar = (int)Math.Floor((techo + eps) / AnchoEstandar);

            List<double>? mejor = null;
            double mejorCobertura = -1;
            int mejorEstandar = -1;
            int mejorPiezas = int.MaxValue;
            double mejorCoste = double.MaxValue;

            for (int k = maxEstandar; k >= 0; k--)
            {
                double resto = techo - (k * AnchoEstandar);
                foreach (List<double> cierre in CierresPosibles(pequenos, resto))
                {
                    double total = (k * AnchoEstandar) + cierre.Sum();
                    if (total > techo + eps)
                    {
                        continue;
                    }

                    // Lo que se pasa del tramo por tolerancia NO cuenta como cobertura: solo sirve para no
                    // perder un tablero entero, no para puntuar mejor que un reparto que cubre de verdad.
                    double cobertura = Math.Min(total, tramo);
                    int piezas = k + cierre.Count;
                    double coste = CosteDeReparto(cierre, uso);

                    bool mejora = cobertura > mejorCobertura + eps
                        || (cobertura > mejorCobertura - eps
                            && (k > mejorEstandar
                                || (k == mejorEstandar
                                    && (piezas < mejorPiezas
                                        || (piezas == mejorPiezas && coste < mejorCoste - 1e-9)))));
                    if (!mejora)
                    {
                        continue;
                    }

                    mejorCobertura = cobertura;
                    mejorEstandar = k;
                    mejorPiezas = piezas;
                    mejorCoste = coste;
                    mejor = Enumerable.Repeat(AnchoEstandar, k).Concat(cierre).ToList();
                }
            }

            return mejor ?? new List<double>();
        }

        /// <summary>
        /// Coste de reparto de un cierre: cuánto se han gastado ya esos anchos pequeños en la losa, más una
        /// penalización por repetir el mismo ancho dentro del cierre. Cuanto más bajo, más equitativo.
        /// </summary>
        private static double CosteDeReparto(List<double> cierre, Dictionary<double, int> uso)
        {
            double coste = 0;
            var vistos = new Dictionary<double, int>();

            foreach (double ancho in cierre)
            {
                int yaEnLaLosa = uso.TryGetValue(ancho, out int n) ? n : 0;
                int yaEnEsteCierre = vistos.TryGetValue(ancho, out int m) ? m : 0;

                // Cada repetición pesa como si esa medida ya estuviera más gastada: entre dos cierres que
                // cubren lo mismo, gana el que mezcla medidas en vez de repetir una sola.
                coste += yaEnLaLosa + (yaEnEsteCierre * 1.5);
                vistos[ancho] = yaEnEsteCierre + 1;
            }

            return coste;
        }

        /// <summary>
        /// Multiconjuntos de 0 a 3 anchos pequeños que caben en lo que queda del tramo. El <paramref name="resto"/>
        /// llega ya con la tolerancia de columna sumada, así que aquí no hay que volver a tenerla en cuenta.
        /// </summary>
        private static IEnumerable<List<double>> CierresPosibles(double[] pequenos, double resto)
        {
            yield return new List<double>();

            double eps = LayoutCatalog.GeometryEpsilon;
            if (resto <= eps)
            {
                yield break;
            }

            // pequenos viene en orden descendente: con j ≥ i y m ≥ j no se repiten permutaciones.
            for (int i = 0; i < pequenos.Length; i++)
            {
                if (pequenos[i] > resto + eps)
                {
                    continue;
                }
                yield return new List<double> { pequenos[i] };

                for (int j = i; j < pequenos.Length; j++)
                {
                    if (pequenos[i] + pequenos[j] > resto + eps)
                    {
                        continue;
                    }
                    yield return new List<double> { pequenos[i], pequenos[j] };

                    for (int m = j; m < pequenos.Length; m++)
                    {
                        if (pequenos[i] + pequenos[j] + pequenos[m] > resto + eps)
                        {
                            continue;
                        }
                        yield return new List<double> { pequenos[i], pequenos[j], pequenos[m] };
                    }
                }
            }
        }

        /// <summary>
        /// Cuánto se admite que un tablero rebase la mitad del riel EN UN FILO del armado (m). Solo se aplica
        /// en el borde, donde no hay otra fila de rieles paralela con la que compartir el riel: ahí el tablero
        /// vuela hacia afuera de 0 a 15 cm. Dentro del armado el vuelo es CERO — el tablero para en la mitad
        /// del riel, que es la mitad que le toca; la otra mitad es de la banda vecina.
        /// </summary>
        private const double VueloEnFilo = 0.15;

        /// <summary>
        /// Holgura que el reparto puede comerse contra el FILO DE UNA COLUMNA (m): 1 cm. No es espacio
        /// inventado, es tolerancia de trazado. Un tramo entre columnas de 2,99 m —el típico eje a eje de
        /// 3,29 con columnas de 0,30— admite así los 5 tableros de 0,60 en vez de bajar a 4 × 0,60 + 0,55 y
        /// dejar un remate de 4 cm que en obra no existe. Solo se aplica contra columnas, nunca contra un
        /// riel compartido ni contra el aire.
        /// </summary>
        private const double ToleranciaColumna = 0.01;

        /// <summary>
        /// Cuánto puede pasarse un tablero de la LÍNEA DE VOLADIZO (m). El tablero que llega a la línea tiene
        /// que cubrirla, y como las medidas son de catálogo casi nunca cae justo: se admite que se pase, y se
        /// elige siempre la pieza MÁS PEQUEÑA que llegue. Con el tablero más grande del catálogo como tope,
        /// cualquier hueco menor que un tablero se puede cerrar.
        /// </summary>
        private static readonly double VueloSobreLinea = LayoutCatalog.BoardLengthsDescending[0];

        /// <summary>
        /// Medida de catálogo que cubre la banda en el sentido del separador:
        ///  1. el LARGO de catálogo más grande que entre en la banda;
        ///  2. si ninguno entra pero el lado que se está cerrando es un FILO del armado (no hay fila de rieles
        ///     paralela al otro lado), se coloca el largo que la cubra volando lo menos posible sobre el riel,
        ///     hasta 15 cm —el caso de una banda de 0,80 rematada con un tablero de 0,85—; dentro del armado
        ///     esto NO se permite, porque el tablero invadiría la mitad de riel de la banda vecina;
        ///  3. solo si la banda es MUCHO más estrecha que el largo más corto se GIRA el tablero, y entonces
        ///     quien cubre la banda es un ANCHO de catálogo (0,60 y siguientes) y son los largos los que
        ///     corren a lo ancho del tramo. Ése es el caso de los separadores de 0,60.
        /// Devuelve 0 si ni girado cabe: eso queda para remate con tabla.
        /// </summary>
        private static (double Largo, bool Girado) ElegirLargoTablero(double vano, double vuelo)
        {
            double eps = LayoutCatalog.GeometryEpsilon;

            // 1) Lo que CUBRE la banda entera pasándose lo menos posible, dentro del vuelo admitido. Con
            //    vuelo 0 solo entra aquí la medida que cae justa. A igualdad de sobrante gana el tablero
            //    derecho, que es como se monta normalmente.
            double mejor = 0;
            bool mejorGirado = false;
            double mejorSobra = double.MaxValue;

            foreach (double largo in LayoutCatalog.BoardLengthsDescending)
            {
                double sobra = largo - vano;
                if (sobra >= -eps && sobra <= vuelo + eps && sobra < mejorSobra - eps)
                {
                    mejor = largo;
                    mejorGirado = false;
                    mejorSobra = sobra;
                }
            }

            foreach (double ancho in LayoutCatalog.BoardWidthsDescending)
            {
                double sobra = ancho - vano;
                if (sobra >= -eps && sobra <= vuelo + eps && sobra < mejorSobra - eps)
                {
                    mejor = ancho;
                    mejorGirado = true;
                    mejorSobra = sobra;
                }
            }

            if (mejor > 0)
            {
                return (mejor, mejorGirado);
            }

            // 2) Nada la cubre: se tiende la medida MÁS GRANDE que entre dentro de la banda y lo que quede
            //    se resuelve en la vuelta siguiente (otro tablero, o remate con tabla si ya no cabe nada).
            foreach (double largo in LayoutCatalog.BoardLengthsDescending)
            {
                if (largo <= vano + eps)
                {
                    return (largo, false);
                }
            }

            foreach (double ancho in LayoutCatalog.BoardWidthsDescending)
            {
                if (ancho <= vano + eps)
                {
                    return (ancho, true);
                }
            }

            return (0, false);
        }

        /// <summary>
        /// Tablero GIRADO — el caso de la banda de 0,60. Se tiende ÚNICAMENTE el estándar de 1,20 × 0,60, de
        /// izquierda a derecha, tantos como quepan. No se mezclan otros largos (nada de 0,90 × 0,60 ni
        /// 0,85 × 0,60): en esta banda el sistema trabaja con una sola medida, que es lo que menos piezas
        /// gasta y lo que se monta en obra. El hueco que quede al final del recorrido no se rellena con otro
        /// tablero: se cierra con TABLA, como remate.
        /// </summary>
        private static List<double> RepartoGirado(double tramo)
        {
            double eps = LayoutCatalog.GeometryEpsilon;
            double estandar = LayoutCatalog.BoardLengthsDescending[0];   // 1,20

            var salida = new List<double>();
            double restante = tramo;
            while (restante >= estandar - eps)
            {
                salida.Add(estandar);
                restante -= estandar;
            }

            return salida;
        }

        /// <summary>Rectángulo del tablero en el plano, convirtiendo los ejes u/v según la orientación.</summary>
        private Rect RectDeCelda(double uA, double uB, double vA, double vB)
        {
            Point a = RielesHorizontales
                ? new Point(OrigenX + uA, OrigenY + vA)
                : new Point(OrigenX + vA, OrigenY + uA);
            Point b = RielesHorizontales
                ? new Point(OrigenX + uB, OrigenY + vB)
                : new Point(OrigenX + vB, OrigenY + uB);
            return new Rect(a, b);
        }

        /// <summary>¿El puntal pertenece a la base principal (los 4 iniciales)?</summary>
        private bool EsDeLaBase(Nudo n) => _nudosBase.Contains(n);

        private void AgregarVano(Nudo a, Nudo b)
        {
            if (a.Equals(b))
            {
                return;
            }
            _vanos.Add(Normalizar(a, b));
        }

        private void AgregarCruceta(Nudo a, Nudo b)
        {
            if (a.Equals(b))
            {
                return;
            }
            _crucetas.Add(Normalizar(a, b));
        }

        /// <summary>Orden canónico de los extremos, para que un vano no se guarde dos veces.</summary>
        private static Vano Normalizar(Nudo a, Nudo b)
        {
            bool primeroA = a.V < b.V || (a.V == b.V && a.U <= b.U);
            return primeroA ? new Vano(a, b) : new Vano(b, a);
        }

        private static int Mm(double metros) => (int)Math.Round(metros * 1000);

        /// <summary>Convierte coordenadas internas (u = eje riel, v = eje separador) al plano.</summary>
        private Point AMundo(Nudo n)
        {
            double u = n.U / 1000.0;
            double v = n.V / 1000.0;
            return RielesHorizontales
                ? new Point(OrigenX + u, OrigenY + v)
                : new Point(OrigenX + v, OrigenY + u);
        }

        private LinearPiece PiezaEnEjeRiel(int u0Mm, int u1Mm, int vMm, double nominal)
        {
            double u0 = u0Mm / 1000.0, u1 = u1Mm / 1000.0, v = vMm / 1000.0;
            return RielesHorizontales
                ? new LinearPiece(OrigenX + u0, OrigenX + u1, OrigenY + v, true, nominal)
                : new LinearPiece(OrigenY + u0, OrigenY + u1, OrigenX + v, false, nominal);
        }

        private LinearPiece PiezaEnEjeSeparador(int v0Mm, int v1Mm, int uMm, double nominal)
        {
            double v0 = v0Mm / 1000.0, v1 = v1Mm / 1000.0, u = uMm / 1000.0;
            return RielesHorizontales
                ? new LinearPiece(OrigenY + v0, OrigenY + v1, OrigenX + u, false, nominal)
                : new LinearPiece(OrigenX + v0, OrigenX + v1, OrigenY + u, true, nominal);
        }
    }
}
