using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using EncofradoCAD.App.Controls;
using EncofradoCAD.App.Services;
using EncofradoCAD.Core.Layout;
using CoreGeom = EncofradoCAD.Core.Geometry;

namespace EncofradoCAD.App.Views.Preview3D
{
    /// <summary>
    /// Vista 3D del encofrado renderizada POR SOFTWARE sobre un lienzo 2D de Avalonia (sin GPU dedicada ni
    /// OpenGL), pensada para laptops con gráfica integrada. Reemplaza al HelixViewport3D de la v1:
    ///   • Toma la geometría del motor (LayoutEngineAdapter → PreviewLayout), la MISMA que el BOM.
    ///   • Proyecta cada cara con una cámara orbital ortográfica, descarta las caras traseras,
    ///     las ordena por profundidad (painter's algorithm) y las sombrea con una luz fija + ambiente.
    ///   • Órbita con arrastre izquierdo, desplaza con arrastre derecho y hace zoom con la rueda.
    ///   • Al pasar el ratón sobre un riel/separador/tablero muestra su medida.
    /// </summary>
    public sealed class Preview3DControl : Control
    {
        private readonly Camera3D _camera = new();
        private Preview3DScene? _scene;

        // Entradas cacheadas para poder reconstruir la escena al cambiar el estilo o al ocultar tableros
        // sin recalcular el layout ni recolocar la cámara.
        private PreviewLayout? _layout;
        private IReadOnlyList<CoreGeom.Point> _slab = Array.Empty<CoreGeom.Point>();
        private IReadOnlyList<ColumnSnapshot> _columns = Array.Empty<ColumnSnapshot>();

        // Caja envolvente del plano (losa), para centrar el armado manual.
        private double _slabMinX, _slabMaxX, _slabMinY, _slabMaxY;

        // ── ARMADO MANUAL ────────────────────────────────────────────────────────────────────────────
        // Con el modo manual activo, el 3D NO usa el motor automático: dibuja el contorno del plano 2D, el
        // voladizo entrecortado y SOLO las piezas que el usuario fue colocando (base principal + filas).
        private ManualAssembly? _armado;
        private bool _modoManual;

        // Cabezal de puntal señalado por el cursor y flecha de crecimiento activa (armado por clic).
        private ManualAssembly.Nudo? _nudoHover;
        private ManualAssembly.Direccion? _flechaHover;
        private ManualAssembly.Extension? _fantasma;

        // Menú de medidas del catálogo: se abre con clic izquierdo sobre el cabezal del puntal.
        private List<OpcionMenu>? _menu;
        private ManualAssembly.Nudo? _menuNudo;
        private Point _menuOrigen;
        private int _menuIndice = -1;

        /// <summary>Fila del menú de medidas. Las cabeceras no son elegibles.</summary>
        private sealed record OpcionMenu(string Texto, ManualAssembly.Direccion Dir, double Medida, bool EsCabecera);

        /// <summary>Se dispara cuando cambian las medidas del armado (al armar, arrastrar o centrar).</summary>
        public event Action? MedidasCambiadas;

        /// <summary>Se dispara JUSTO ANTES de modificar el armado, para que el módulo guarde el paso previo
        /// en el registro de deshacer.</summary>
        public event Action? AntesDeCambiarArmado;

        /// <summary>Medida de riel (m) que se coloca al crecer en el eje de los rieles.</summary>
        public double MedidaRiel { get; set; } = 3.00;

        // Voladizo del contorno entrecortado y si se dibuja (lo manda el ribbon del armado manual).
        private double _voladizoContorno = Preview3DScene.VoladizoPorDefecto;
        private bool _mostrarVoladizoContorno = true;

        /// <summary>Medida de separador (m) que se coloca al crecer en el eje de los separadores.</summary>
        public double MedidaSeparador { get; set; } = 1.20;

        /// <summary>
        /// Aviso PUNTUAL de la última acción (colocar una pieza, eliminar una red, bloquear la losa…).
        ///
        /// Es de UN SOLO USO: el módulo 3D lo retira con <see cref="ConsumirUltimaAccion"/> y, cuando no hay
        /// ninguno pendiente, el registro pasa a mostrar el inventario VIVO del armado. Antes este texto se
        /// quedaba guardado para siempre y se reimprimía en cada refresco, así que un "Colocado: … separador
        /// 1,00 m" de hace diez pasos seguía en pantalla como si describiera la estructura actual — aunque
        /// esa pieza ya se hubiera deshecho o la base se hubiera rehecho con otras medidas.
        /// </summary>
        private string? _ultimaAccion;

        /// <summary>
        /// Devuelve el aviso pendiente y lo DESCARTA. null cuando no ha ocurrido nada nuevo desde la última
        /// consulta: en ese caso el registro debe leer el estado real del armado, no repetir esto.
        /// </summary>
        public string? ConsumirUltimaAccion()
        {
            string? aviso = _ultimaAccion;
            _ultimaAccion = null;
            return aviso;
        }

        /// <summary>Voladizo real por lado del último layout calculado (m). Lo consume el módulo 3D para
        /// mostrarle al cliente cuánto sobresale el encofrado en cada lado.</summary>
        public LayoutEngine.Voladizos Voladizo { get; private set; }

        /// <summary>true si el último cálculo produjo un layout válido (hay voladizo que mostrar).</summary>
        public bool HayLayout => _layout != null;

        private Modo3D _modo = Modo3D.Transparente;
        private bool _mostrarTableros = true;
        private bool _mostrarCotasColumnas = true;
        private string? _mensaje = "Traza la losa en 2D y pulsa \"Calcular materiales\" para ver el 3D.";

        // Interacción
        private bool _dragOrbit;
        private bool _dragPan;
        private bool _dragStructure;
        private Point _lastPointer;

        // Mover ESTRUCTURA: desplazamiento (offset) en el plano XY del mundo (m) que se aplica SOLO a la
        // geometría movible (encofrado). El plano 2D, el voladizo y las columnas quedan fijos (Fixed=true).
        private bool _moverEstructura;
        private double _offX;
        private double _offY;

        // Hover
        private string? _hoverLabel;
        private HoverItem? _hoverItem;
        private Point _hoverPos;

        // Luz fija (mundo) + ambiente, para dar volumen sin depender de la GPU.
        private static readonly Vec3 LightDir = new Vec3(0.35, 0.25, 0.9).Normalized();
        private const double Ambient = 0.5;
        private const double Diffuse = 0.5;

        private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12));
        private static readonly IBrush TooltipBg = new SolidColorBrush(Color.FromArgb(230, 0x25, 0x25, 0x25));
        private static readonly IBrush TooltipFg = Brushes.White;
        private static readonly Typeface TooltipFont = new Typeface("Segoe UI");

        // Resaltado de la pieza medida: línea BLANCA con HALO oscuro (se dibuja primero un trazo oscuro más
        // grueso y encima el blanco), para que se vea nítido sobre cualquier pieza sin resultar llamativo.
        private static readonly IPen HighlightDashHaloPen =
            new Pen(new SolidColorBrush(Color.FromArgb(210, 0, 0, 0)), 4.5) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
        private static readonly IPen HighlightDashPen =
            new Pen(new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)), 2.0) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
        private static readonly IPen HighlightEndHaloPen =
            new Pen(new SolidColorBrush(Color.FromArgb(210, 0, 0, 0)), 5.0);
        // Verticales de extremo en VERDE FOSFORESCENTE (neón), con el halo oscuro por debajo.
        private static readonly IPen HighlightEndPen =
            new Pen(new SolidColorBrush(Color.FromArgb(255, 0x39, 0xFF, 0x14)), 2.6);

        // ── Pinceles del armado por clic (cabezal, flechas y fantasma) ───────────────────────────────
        private static readonly IBrush CabezalRelleno = new SolidColorBrush(Color.FromArgb(70, 0x22, 0xD3, 0xEE));
        private static readonly IPen CabezalPen = new Pen(new SolidColorBrush(Color.FromArgb(255, 0x22, 0xD3, 0xEE)), 2.0);
        private static readonly IBrush FlechaRelleno = new SolidColorBrush(Color.FromArgb(120, 0x22, 0xD3, 0xEE));
        private static readonly IBrush FlechaRellenoActiva = new SolidColorBrush(Color.FromArgb(255, 0xB6, 0xFF, 0x00));
        private static readonly IPen FlechaEjePen = new Pen(new SolidColorBrush(Color.FromArgb(110, 0x22, 0xD3, 0xEE)), 1.2);
        private static readonly IPen FlechaEjePenActiva = new Pen(new SolidColorBrush(Color.FromArgb(230, 0xB6, 0xFF, 0x00)), 1.8);
        private static readonly IBrush FantasmaRelleno = new SolidColorBrush(Color.FromArgb(60, 0xB6, 0xFF, 0x00));
        private static readonly IPen FantasmaPen =
            new Pen(new SolidColorBrush(Color.FromArgb(220, 0xB6, 0xFF, 0x00)), 1.8) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };

        // ── Cotas de holgura contra las columnas (líneas ROJAS acotadas) ─────────────────────────────
        private static readonly IPen CotaHaloPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), 4.2);
        private static readonly IPen CotaPen = new Pen(new SolidColorBrush(Color.FromArgb(255, 0xFF, 0x3B, 0x30)), 2.0);
        private static readonly IBrush CotaChip = new SolidColorBrush(Color.FromArgb(235, 0x2A, 0x0E, 0x0E));
        private static readonly IBrush CotaTextoJusto = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
        private static readonly IBrush CotaTextoBueno = new SolidColorBrush(Color.FromRgb(0x39, 0xFF, 0x14));
        private static readonly IBrush CotaTextoAmplio = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));

        // Menú de medidas del catálogo.
        private static readonly IBrush MenuFondo = new SolidColorBrush(Color.FromArgb(244, 0x22, 0x27, 0x2E));
        private static readonly IPen MenuBorde = new Pen(new SolidColorBrush(Color.FromArgb(255, 0x3C, 0x44, 0x4E)), 1.0);
        private static readonly IBrush MenuSeleccion = new SolidColorBrush(Color.FromArgb(255, 0x2F, 0x6F, 0x3E));
        private static readonly IBrush MenuTextoCabecera = new SolidColorBrush(Color.FromRgb(0xB6, 0xFF, 0x00));
        private static readonly IBrush MenuTextoActivo = Brushes.White;

        private readonly Dictionary<uint, IBrush> _brushCache = new();
        private readonly Dictionary<uint, IPen> _penCache = new();

        public Preview3DControl()
        {
            ClipToBounds = true;
            Focusable = true;
        }

        public Modo3D Modo
        {
            get => _modo;
            set
            {
                if (_modo == value)
                {
                    return;
                }
                _modo = value;
                RebuildScene();
                InvalidateVisual();
            }
        }

        public bool MostrarTableros
        {
            get => _mostrarTableros;
            set
            {
                if (_mostrarTableros == value)
                {
                    return;
                }
                _mostrarTableros = value;
                RebuildScene();
                InvalidateVisual();
            }
        }

        /// <summary>
        /// Voladizo (m) con el que se dibuja el contorno entrecortado blanco alrededor de la losa. Lo fija
        /// el usuario desde el ribbon del ARMADO MANUAL (5, 10, 15 o 20 cm) para ver con qué volado se está
        /// contando en el modelado 3D. Por defecto, los 20 cm de la norma.
        /// </summary>
        public double VoladizoContorno
        {
            get => _voladizoContorno;
            set
            {
                if (Math.Abs(_voladizoContorno - value) < 1e-9)
                {
                    return;
                }
                _voladizoContorno = value;
                // Si los tableros ya estaban puestos, se rehace el reparto: la línea de voladizo es la que
                // marca hasta dónde llegan, así que al cambiarla el reparto deja de valer.
                if (_modoManual && _armado != null && _armado.TotalTableros > 0)
                {
                    _armado.ColocarTableros(_columns, _voladizoContorno);
                    ReconstruirArmado();
                    return;
                }
                RebuildScene();
                InvalidateVisual();
            }
        }

        /// <summary>Muestra u oculta el contorno entrecortado del voladizo en la vista 3D.</summary>
        public bool MostrarVoladizoContorno
        {
            get => _mostrarVoladizoContorno;
            set
            {
                if (_mostrarVoladizoContorno == value)
                {
                    return;
                }
                _mostrarVoladizoContorno = value;
                RebuildScene();
                InvalidateVisual();
            }
        }

        /// <summary>
        /// Modo "mover estructura": mientras está activo, el clic izquierdo + arrastrar DESPLAZA el encofrado
        /// (rieles, puntales, separadores, crucetas y tableros) en el plano, dejando fijos el plano 2D, el
        /// voladizo y las columnas. Con el modo apagado, el clic izquierdo vuelve a mover la cámara (pan).
        /// </summary>
        public bool MoverEstructura
        {
            get => _moverEstructura;
            set
            {
                _moverEstructura = value;
                if (!value)
                {
                    _dragStructure = false;
                }
            }
        }

        /// <summary>
        /// Cotas rojas de holgura entre las piezas del armado y la cara de las columnas: la distancia del
        /// filo interno del riel y del filo del separador a la columna, para comprobar de un vistazo si hay
        /// sitio para plegar (de 5 a 15 cm) o si hay que reubicar la estructura.
        /// </summary>
        public bool MostrarCotasColumnas
        {
            get => _mostrarCotasColumnas;
            set
            {
                if (_mostrarCotasColumnas == value)
                {
                    return;
                }
                _mostrarCotasColumnas = value;
                InvalidateVisual();
            }
        }

        /// <summary>Regresa la estructura a su posición original (offset 0), sin recalcular el layout.</summary>
        public void RestablecerPosicionEstructura()
        {
            if (_offX == 0 && _offY == 0)
            {
                return;
            }
            _offX = 0;
            _offY = 0;
            _hoverLabel = null;
            _hoverItem = null;
            InvalidateVisual();
            MedidasCambiadas?.Invoke();
        }

        /// <summary>Aplica el offset de estructura a un punto de mundo, salvo que la pieza sea fija.</summary>
        private Vec3 ApplyOffset(Vec3 v, bool fixedInWorld)
            => fixedInWorld ? v : new Vec3(v.X + _offX, v.Y + _offY, v.Z);

        // ── ARMADO MANUAL: API pública que consume el módulo 3D ──────────────────────────────────────

        /// <summary>Armado manual actual (null si nunca se activó el modo manual).</summary>
        public ManualAssembly? Armado => _armado;

        /// <summary>true si el preview está mostrando el armado manual en vez del cálculo automático.</summary>
        public bool ModoManual => _modoManual;

        /// <summary>
        /// Entra en modo manual: toma el plano 2D (contorno + voladizo + columnas) y coloca la BASE PRINCIPAL
        /// centrada en la losa. NO ejecuta el motor automático.
        /// </summary>
        public bool IniciarArmadoManual(DrawingCanvas canvas, ManualAssembly armado, bool centrar = true)
        {
            if (!LayoutEngineAdapter.TryGetPlano(canvas, out List<CoreGeom.Point> slab,
                    out List<ColumnSnapshot> columns,
                    out double minX, out double maxX, out double minY, out double maxY,
                    out string? error))
            {
                _mensaje = error ?? "No hay un plano válido para armar en 3D.";
                _layout = null;
                _scene = null;
                InvalidateVisual();
                return false;
            }

            _slab = slab;
            _columns = columns;
            _slabMinX = minX; _slabMaxX = maxX; _slabMinY = minY; _slabMaxY = maxY;
            _offX = 0;
            _offY = 0;
            _mensaje = null;
            _modoManual = true;
            _armado = armado;

            // Armado NUEVO (entrar en manual, rehacer la base o deshacer un paso): cualquier aviso pendiente
            // habla de la estructura ANTERIOR, así que se descarta ANTES de reconstruir. Si no, el evento de
            // ReconstruirArmado lo pintaría en el registro junto a una estructura que ya no le corresponde.
            _ultimaAccion = null;

            // El armado necesita saber dónde está el filo de la losa: de ahí salen el alcance de la cruz
            // arriostrada y la medida que se sugiere al crecer.
            _armado.Plano = new ManualAssembly.Losa(_slabMinX, _slabMaxX, _slabMinY, _slabMaxY);

            if (centrar)
            {
                CentrarEnLosa(notificar: false);
            }
            ReconstruirArmado(reencuadrar: true);
            return true;
        }

        /// <summary>Sale del modo manual y vuelve al cálculo automático del motor.</summary>
        public void SalirArmadoManual(DrawingCanvas canvas)
        {
            _modoManual = false;
            _armado = null;
            _ultimaAccion = null;   // se sale del armado manual: nada de lo anterior sigue en pantalla
            Actualizar(canvas);
            MedidasCambiadas?.Invoke();
        }

        /// <summary>Rehace la geometría a partir del armado manual (tras agregar/quitar filas o centrar).</summary>
        public void ReconstruirArmado(bool reencuadrar = false)
        {
            if (!_modoManual || _armado == null)
            {
                return;
            }

            _layout = _armado.Build();
            _mensaje = null;
            Voladizo = default;
            RebuildScene();

            if (reencuadrar)
            {
                _camera.Frame(_slabMinX, _slabMaxX, _slabMinY, _slabMaxY, _scene?.SlabHeight ?? 3.23);
            }

            InvalidateVisual();
            MedidasCambiadas?.Invoke();
        }

        /// <summary>
        /// Coloca el armado en el CENTRO de la losa: iguala la separación a los cuatro bordes. Es el punto de
        /// partida recomendado para la base principal, antes de ajustarla a mano para librar columnas.
        /// </summary>
        public void CentrarEnLosa(bool notificar = true)
        {
            if (_armado == null)
            {
                return;
            }

            CoreGeom.Rect caja = _armado.CajaEnvolvente;
            double centroLosaX = (_slabMinX + _slabMaxX) / 2;
            double centroLosaY = (_slabMinY + _slabMaxY) / 2;

            _armado.OrigenX += centroLosaX - (caja.Left + (caja.Width / 2));
            _armado.OrigenY += centroLosaY - (caja.Top + (caja.Height / 2));
            _offX = 0;
            _offY = 0;

            if (notificar)
            {
                ReconstruirArmado();
            }
        }

        /// <summary>true si el armado ya está bloqueado (losa lista) y no admite más piezas.</summary>
        public bool LosaLista => _armado?.Congelado == true;

        /// <summary>true si ya hay tableros colocados sobre la estructura.</summary>
        public bool HayTableros => (_armado?.TotalTableros ?? 0) > 0;

        /// <summary>
        /// LOSA LISTA: bloquea el armado. Se acaban las flechas, el menú y el arrastre; la estructura queda
        /// estática para poder tablerarla.
        /// </summary>
        public bool CerrarLosa()
        {
            if (_armado == null || !_armado.CerrarLosa())
            {
                return false;
            }

            CerrarMenu();
            _nudoHover = null;
            _flechaHover = null;
            _fantasma = null;
            MoverEstructura = false;
            _ultimaAccion = "Losa lista: la estructura quedó bloqueada.";
            ReconstruirArmado();
            return true;
        }

        /// <summary>Reabre el armado para seguir colocando piezas (retira los tableros).</summary>
        public void ReabrirLosa()
        {
            if (_armado == null)
            {
                return;
            }
            _armado.ReabrirLosa();
            _ultimaAccion = "Losa reabierta: puede seguir armando.";
            ReconstruirArmado();
        }

        /// <summary>Reparte los tableros de catálogo sobre la estructura ya bloqueada.</summary>
        public int ColocarTableros()
        {
            if (_armado == null || !_armado.Congelado)
            {
                return 0;
            }

            // El volado que el usuario eligió en el ribbon marca hasta dónde hacen falta tableros: lo que
            // el armado sobresalga de esa línea se queda vacío.
            int n = _armado.ColocarTableros(_columns, _voladizoContorno);
            _ultimaAccion = n > 0
                ? $"Colocados {n} tableros sobre la estructura."
                : "No se pudo colocar ningún tablero.";
            ReconstruirArmado();
            return n;
        }

        /// <summary>Medidas vivas del armado: tamaño, diagonal de esquina a esquina y holgura a cada borde.</summary>
        public MedidasArmado Medidas
        {
            get
            {
                if (_armado == null)
                {
                    return default;
                }

                CoreGeom.Rect c = _armado.CajaEnvolvente;
                double izq = (c.Left + _offX) - _slabMinX;
                double der = _slabMaxX - (c.Right + _offX);
                double inf = (c.Top + _offY) - _slabMinY;
                double sup = _slabMaxY - (c.Bottom + _offY);

                return new MedidasArmado(
                    c.Width, c.Height, _armado.Diagonal,
                    izq, der, inf, sup,
                    _armado.TotalPuntales, _armado.TotalVanos,
                    ColumnasInvadidas());
            }
        }

        // ── ARMADO POR CLIC SOBRE EL CABEZAL DEL PUNTAL ──────────────────────────────────────────────
        // Se apunta el cabezal (la base superior) de cualquier puntal: se resalta y aparecen 4 flechas.
        // Al apuntar una flecha se ve en FANTASMA lo que se va a colocar y su medida; el clic izquierdo
        // lo coloca. Es el mismo gesto en los dos ejes: 3 m en el de los rieles, 1,20 m en el de los
        // separadores.

        // La flecha se coloca a menos de media retícula (1,20/2 = 0,60 m) para que siempre quede dentro del
        // territorio de SU puntal y no se confunda con la del puntal vecino.
        private const double DistanciaFlecha = 0.38;   // separación de la flecha respecto al cabezal (m)
        private const double HisteresisPx = 14;        // el puntal ya señalado se conserva ante empates
        private const double RadioCabezalPx = 16;      // zona del cabezal donde AÚN no se elige dirección (px)
        private const double MargenRosetaPx = 30;      // holgura alrededor de las flechas para no perder el foco (px)
        private const double CoincidenciaMinima = 0.5; // qué tan alineado debe ir el cursor con una flecha

        /// <summary>
        /// Medida (m) que corresponde colocar al crecer desde ese puntal. La decide el ARMADO a partir del
        /// contexto (lo ya montado) y del filo de la losa; las medidas del panel solo son el punto de partida
        /// cuando todavía no hay nada de esa clase colocado.
        /// </summary>
        private double MedidaDe(ManualAssembly.Nudo nudo, ManualAssembly.Direccion dir)
        {
            double porDefecto = dir is ManualAssembly.Direccion.RielMas or ManualAssembly.Direccion.RielMenos
                ? MedidaRiel
                : MedidaSeparador;
            return _armado?.MedidaSugerida(nudo, dir, porDefecto) ?? porDefecto;
        }

        /// <summary>Posición en el mundo de la flecha de una dirección, a la altura del cabezal.</summary>
        private Vec3 PosicionFlecha(CoreGeom.Point cabezal, ManualAssembly.Direccion dir)
        {
            bool ejeRiel = dir is ManualAssembly.Direccion.RielMas or ManualAssembly.Direccion.RielMenos;
            double signo = dir is ManualAssembly.Direccion.RielMas or ManualAssembly.Direccion.SeparadorMas ? 1 : -1;
            bool haciaX = _armado?.RielesHorizontales != false ? ejeRiel : !ejeRiel;

            double z = Preview3DScene.AlturaCabezalPuntal;
            return haciaX
                ? new Vec3(cabezal.X + (signo * DistanciaFlecha), cabezal.Y, z)
                : new Vec3(cabezal.X, cabezal.Y + (signo * DistanciaFlecha), z);
        }

        /// <summary>
        /// Localiza el puntal señalado y la flecha activa. Funciona por CERCANÍA y por DIRECCIÓN, no por
        /// aciertos exactos: basta acercar el cursor a un puntal para que aparezca su roseta de flechas, y
        /// luego moverlo HACIA la flecha deseada para activarla. Entre el cabezal y las flechas no queda
        /// ninguna zona muerta, así que el resalte no se pierde por el camino.
        /// </summary>
        private bool ActualizarHoverArmado(Point pos)
        {
            // Con la losa lista la estructura es estática: ni resalte ni flechas.
            if (!_modoManual || _armado == null || _armado.Congelado)
            {
                if (_nudoHover != null || _flechaHover != null)
                {
                    _nudoHover = null;
                    _flechaHover = null;
                    _fantasma = null;
                    InvalidateVisual();
                }
                return false;
            }

            (Vec3 right, Vec3 up, Vec3 fwd) = _camera.Basis();
            double scale = _camera.Scale(Bounds.Width, Bounds.Height);
            double z = Preview3DScene.AlturaCabezalPuntal;

            ManualAssembly.Nudo? nudoAnterior = _nudoHover;
            ManualAssembly.Direccion? flechaAnterior = _flechaHover;

            // 1) Puntal más cercano al cursor en pantalla.
            ManualAssembly.Nudo? nudo = null;
            CoreGeom.Point punto = default;
            Point centro = default;
            double mejorDist = double.MaxValue;

            CoreGeom.Point puntoPrevio = default;
            Point centroPrevio = default;
            double distPrevio = double.MaxValue;

            foreach ((ManualAssembly.Nudo n, CoreGeom.Point p) in _armado.Nudos())
            {
                (Point s, _) = Proyectar(new Vec3(p.X, p.Y, z), right, up, fwd, scale);
                double d = Distancia(s, pos);
                if (d < mejorDist)
                {
                    mejorDist = d;
                    nudo = n;
                    punto = p;
                    centro = s;
                }
                if (nudoAnterior != null && n.Equals(nudoAnterior.Value))
                {
                    distPrevio = d;
                    puntoPrevio = p;
                    centroPrevio = s;
                }
            }

            // Histéresis: ante puntales casi equidistantes se conserva el que ya estaba señalado, para que el
            // foco no salte solo mientras se lleva el cursor hacia una flecha.
            if (nudoAnterior != null && distPrevio <= mejorDist + HisteresisPx)
            {
                mejorDist = distPrevio;
                nudo = nudoAnterior;
                punto = puntoPrevio;
                centro = centroPrevio;
            }

            // 2) Flechas válidas de ese puntal (solo las direcciones en las que se puede crecer).
            ManualAssembly.Direccion? flecha = null;
            double radioRoseta = RadioCabezalPx + MargenRosetaPx;

            if (nudo != null)
            {
                double dxCursor = pos.X - centro.X;
                double dyCursor = pos.Y - centro.Y;
                double lenCursor = Math.Sqrt((dxCursor * dxCursor) + (dyCursor * dyCursor));
                double mejorCoincidencia = CoincidenciaMinima;

                foreach (ManualAssembly.Direccion dir in Enum.GetValues<ManualAssembly.Direccion>())
                {
                    if (!_armado.PuedeExtender(nudo.Value, dir, MedidaDe(nudo.Value, dir)))
                    {
                        continue;
                    }

                    (Point pf, _) = Proyectar(PosicionFlecha(punto, dir), right, up, fwd, scale);
                    double distFlecha = Distancia(centro, pf);
                    radioRoseta = Math.Max(radioRoseta, distFlecha + MargenRosetaPx);

                    // Fuera de la placa del cabezal, la flecha se elige por ALINEACIÓN con el cursor: se
                    // mueve el ratón hacia ella y se enciende, sin tener que acertarle al pixel.
                    if (lenCursor <= RadioCabezalPx || distFlecha < 1e-6)
                    {
                        continue;
                    }

                    double coincidencia =
                        (((pf.X - centro.X) * dxCursor) + ((pf.Y - centro.Y) * dyCursor)) / (distFlecha * lenCursor);
                    if (coincidencia > mejorCoincidencia)
                    {
                        mejorCoincidencia = coincidencia;
                        flecha = dir;
                    }
                }

                // Demasiado lejos del puntal: no hay nada señalado.
                if (mejorDist > radioRoseta)
                {
                    nudo = null;
                    flecha = null;
                }
            }

            _nudoHover = nudo;
            _flechaHover = flecha;
            _fantasma = flecha != null && nudo != null
                ? _armado.VistaPrevia(nudo.Value, flecha.Value, MedidaDe(nudo.Value, flecha.Value))
                : null;

            bool cambio = !Nullable.Equals(nudoAnterior, _nudoHover) || !Nullable.Equals(flechaAnterior, _flechaHover);
            if (cambio)
            {
                InvalidateVisual();
            }
            return _nudoHover != null;
        }

        private static double Distancia(Point a, Point b)
            => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

        // ── MENÚ DE MEDIDAS DEL CATÁLOGO ─────────────────────────────────────────────────────────────
        // Las flechas colocan la medida estándar de un solo clic. Si en cambio se hace clic sobre el
        // CABEZAL del puntal, se abre este menú con todas las medidas del catálogo: los separadores para
        // crecer en vertical y los rieles para crecer en horizontal. Al recorrer el menú se ve el fantasma
        // de cada opción, así que no hace falta adivinar dónde va a caer.

        private const double MenuAncho = 268;
        private const double MenuAltoFila = 24;
        private const double MenuAltoCabecera = 23;
        private const double MenuMargen = 6;

        private void AbrirMenuMedidas(ManualAssembly.Nudo nudo, Point pos)
        {
            if (_armado == null)
            {
                return;
            }

            var opciones = new List<OpcionMenu>();
            AgregarGrupoMenu(opciones, nudo, "SEPARADOR  ·  crecer en vertical", true);
            AgregarGrupoMenu(opciones, nudo, "RIEL  ·  crecer en horizontal", false);

            if (opciones.Count == 0)
            {
                return;
            }

            _menu = opciones;
            _menuNudo = nudo;
            _menuIndice = -1;
            _fantasma = null;
            _flechaHover = null;

            _menuOrigen = PosicionMenu(nudo, pos, AltoMenu(opciones));

            InvalidateVisual();
        }

        /// <summary>
        /// Sitio donde se abre el menú de medidas.
        ///
        /// El menú NO se ancla al cursor: eso era lo que estorbaba, porque el fantasma de la pieza aparece
        /// justo alrededor del puntal señalado y el menú le caía encima. Ahora se acuesta contra el BORDE
        /// LATERAL MÁS LEJANO al puntal —si el puntal está en la mitad izquierda del lienzo, el menú se va al
        /// filo derecho, y al revés—, centrado en vertical. Así queda toda la zona del puntal despejada y se
        /// ve cómo cae cada medida mientras se recorren las filas.
        /// </summary>
        private Point PosicionMenu(ManualAssembly.Nudo nudo, Point pos, double alto)
        {
            const double margen = 14;
            double w = Bounds.Width;
            double h = Bounds.Height;

            double puntalX = pos.X;
            if (_armado != null)
            {
                (Vec3 right, Vec3 up, Vec3 fwd) = _camera.Basis();
                double scale = _camera.Scale(w, h);
                CoreGeom.Point cabezal = _armado.Nudos().FirstOrDefault(x => x.Nudo.Equals(nudo)).Punto;
                (Point proyectado, _) = Proyectar(
                    new Vec3(cabezal.X, cabezal.Y, Preview3DScene.AlturaCabezalPuntal),
                    right, up, fwd, scale);
                puntalX = proyectado.X;
            }

            double x = puntalX < w / 2
                ? w - MenuAncho - margen   // puntal a la izquierda → menú al filo derecho
                : margen;                  // puntal a la derecha  → menú al filo izquierdo
            double y = (h - alto) / 2;

            return new Point(
                Math.Clamp(x, margen, Math.Max(margen, w - MenuAncho - margen)),
                Math.Clamp(y, margen, Math.Max(margen, h - alto - margen)));
        }

        private void AgregarGrupoMenu(List<OpcionMenu> opciones, ManualAssembly.Nudo nudo, string titulo, bool separadores)
        {
            if (_armado == null)
            {
                return;
            }

            ManualAssembly armado = _armado;
            double[] medidas = separadores ? LayoutCatalog.SeparatorLengths : LayoutCatalog.RailLengths;
            ManualAssembly.Direccion[] sentidos = separadores
                ? new[] { ManualAssembly.Direccion.SeparadorMas, ManualAssembly.Direccion.SeparadorMenos }
                : new[] { ManualAssembly.Direccion.RielMas, ManualAssembly.Direccion.RielMenos };

            List<ManualAssembly.Direccion> validos = sentidos
                .Where(d => medidas.Any(m => armado.PuedeExtender(nudo, d, m)))
                .ToList();
            if (validos.Count == 0)
            {
                return;
            }

            int antes = opciones.Count;
            opciones.Add(new OpcionMenu(titulo, sentidos[0], 0, true));

            string nombre = separadores ? "Separador" : "Riel";
            foreach (double medida in medidas.OrderByDescending(x => x))
            {
                foreach (ManualAssembly.Direccion dir in validos)
                {
                    if (!armado.PuedeExtender(nudo, dir, medida))
                    {
                        continue;
                    }
                    string sufijo = validos.Count > 1 ? $"   {Sentido(dir)}" : string.Empty;
                    opciones.Add(new OpcionMenu($"{nombre} de {medida:0.00} m{sufijo}", dir, medida, false));
                }
            }

            // Grupo sin ninguna medida utilizable: se retira su cabecera.
            if (opciones.Count == antes + 1)
            {
                opciones.RemoveAt(antes);
            }
        }

        private static string Sentido(ManualAssembly.Direccion dir) => dir switch
        {
            ManualAssembly.Direccion.RielMas => "▶",
            ManualAssembly.Direccion.RielMenos => "◀",
            ManualAssembly.Direccion.SeparadorMas => "▲",
            _ => "▼"
        };

        private static double AltoMenu(List<OpcionMenu> opciones)
            => opciones.Sum(o => o.EsCabecera ? MenuAltoCabecera : MenuAltoFila) + (MenuMargen * 2);

        /// <summary>Fila del menú bajo el cursor, o -1 si el cursor está fuera.</summary>
        private int FilaMenuEn(Point pos)
        {
            if (_menu == null)
            {
                return -1;
            }

            double alto = AltoMenu(_menu);
            if (pos.X < _menuOrigen.X || pos.X > _menuOrigen.X + MenuAncho
                || pos.Y < _menuOrigen.Y || pos.Y > _menuOrigen.Y + alto)
            {
                return -1;
            }

            double y = _menuOrigen.Y + MenuMargen;
            for (int i = 0; i < _menu.Count; i++)
            {
                double h = _menu[i].EsCabecera ? MenuAltoCabecera : MenuAltoFila;
                if (pos.Y >= y && pos.Y < y + h)
                {
                    return _menu[i].EsCabecera ? -1 : i;
                }
                y += h;
            }
            return -1;
        }

        /// <summary>Actualiza la fila señalada del menú y su fantasma. Devuelve true si el cursor está dentro.</summary>
        private bool ActualizarHoverMenu(Point pos)
        {
            if (_menu == null || _menuNudo == null || _armado == null)
            {
                return false;
            }

            int indice = FilaMenuEn(pos);
            if (indice != _menuIndice)
            {
                _menuIndice = indice;
                _fantasma = indice >= 0
                    ? _armado.VistaPrevia(_menuNudo.Value, _menu[indice].Dir, _menu[indice].Medida)
                    : null;
                InvalidateVisual();
            }

            double alto = AltoMenu(_menu);
            return pos.X >= _menuOrigen.X && pos.X <= _menuOrigen.X + MenuAncho
                && pos.Y >= _menuOrigen.Y && pos.Y <= _menuOrigen.Y + alto;
        }

        private void CerrarMenu()
        {
            if (_menu == null)
            {
                return;
            }
            _menu = null;
            _menuNudo = null;
            _menuIndice = -1;
            _fantasma = null;
            InvalidateVisual();
        }

        /// <summary>Coloca la medida elegida en el menú. Devuelve true si se colocó algo.</summary>
        private bool AplicarOpcionMenu(Point pos)
        {
            if (_menu == null || _menuNudo == null || _armado == null)
            {
                return false;
            }

            int indice = FilaMenuEn(pos);
            if (indice < 0)
            {
                return false;
            }

            ManualAssembly armado = _armado;
            OpcionMenu op = _menu[indice];
            ManualAssembly.Nudo nudo = _menuNudo.Value;
            CerrarMenu();

            AntesDeCambiarArmado?.Invoke();
            if (!armado.TryExtender(nudo, op.Dir, op.Medida, out string desc))
            {
                return false;
            }

            _ultimaAccion = "Colocado: " + desc;
            ReconstruirArmado();
            return true;
        }

        private void DibujarMenu(DrawingContext context)
        {
            if (_menu == null)
            {
                return;
            }

            double alto = AltoMenu(_menu);
            var marco = new Rect(_menuOrigen.X, _menuOrigen.Y, MenuAncho, alto);
            context.DrawRectangle(MenuFondo, MenuBorde, marco, 6, 6);

            double y = _menuOrigen.Y + MenuMargen;
            for (int i = 0; i < _menu.Count; i++)
            {
                OpcionMenu op = _menu[i];
                double h = op.EsCabecera ? MenuAltoCabecera : MenuAltoFila;

                if (i == _menuIndice && !op.EsCabecera)
                {
                    context.FillRectangle(MenuSeleccion, new Rect(_menuOrigen.X + 3, y, MenuAncho - 6, h), 4);
                }

                var texto = new FormattedText(
                    op.Texto, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TooltipFont,
                    op.EsCabecera ? 11 : 12.5,
                    op.EsCabecera ? MenuTextoCabecera : (i == _menuIndice ? MenuTextoActivo : TooltipFg));
                context.DrawText(texto, new Point(_menuOrigen.X + 12, y + ((h - texto.Height) / 2)));

                y += h;
            }
        }

        /// <summary>Coloca la extensión señalada por la flecha activa. Devuelve true si colocó algo.</summary>
        private bool ColocarExtension()
        {
            if (!_modoManual || _armado == null || _nudoHover == null || _flechaHover == null)
            {
                return false;
            }

            AntesDeCambiarArmado?.Invoke();
            if (!_armado.TryExtender(_nudoHover.Value, _flechaHover.Value, MedidaDe(_nudoHover.Value, _flechaHover.Value), out string desc))
            {
                return false;
            }

            _ultimaAccion = "Colocado: " + desc;
            _fantasma = null;
            _flechaHover = null;
            ReconstruirArmado();
            return true;
        }

        private (Point, double) Proyectar(Vec3 v, Vec3 right, Vec3 up, Vec3 fwd, double scale)
            => _camera.Project(ApplyOffset(v, false), right, up, fwd, scale, Bounds.Width, Bounds.Height);

        /// <summary>
        /// Puntales que caen sobre (o demasiado cerca de) una columna con el desplazamiento actual. El riel no
        /// puede apoyar sobre una columna, así que esto es un aviso para reubicar el armado.
        /// </summary>
        private int ColumnasInvadidas()
        {
            if (_layout == null || _columns.Count == 0)
            {
                return 0;
            }

            int invadidos = 0;
            foreach (CoreGeom.Point p in _layout.Puntales)
            {
                var movido = new CoreGeom.Point(p.X + _offX, p.Y + _offY);
                if (_columns.Any(c => c.Contains(movido, ColumnClearance.MinClearance)))
                {
                    invadidos++;
                }
            }
            return invadidos;
        }

        /// <summary>Recalcula el layout desde el lienzo 2D y reencuadra la cámara (equivale a resetCamera=true).</summary>
        public void Actualizar(DrawingCanvas canvas)
        {
            // En modo manual el motor automático NO interviene: solo se refresca el plano de referencia.
            if (_modoManual && _armado != null)
            {
                if (LayoutEngineAdapter.TryGetPlano(canvas, out List<CoreGeom.Point> planoSlab,
                        out List<ColumnSnapshot> planoCols,
                        out double pMinX, out double pMaxX, out double pMinY, out double pMaxY, out _))
                {
                    _slab = planoSlab;
                    _columns = planoCols;
                    _slabMinX = pMinX; _slabMaxX = pMaxX; _slabMinY = pMinY; _slabMaxY = pMaxY;
                    _armado.Plano = new ManualAssembly.Losa(pMinX, pMaxX, pMinY, pMaxY);
                }
                ReconstruirArmado(reencuadrar: true);
                return;
            }

            // Un nuevo cálculo reencuadra la cámara: la estructura vuelve a su posición original.
            _offX = 0;
            _offY = 0;
            if (!LayoutEngineAdapter.TryBuildLayout(
                    canvas,
                    out PreviewLayout? layout,
                    out List<CoreGeom.Point> slab,
                    out List<ColumnSnapshot> columns,
                    out double minX, out double maxX, out double minY, out double maxY,
                    out string? error)
                || layout == null)
            {
                _layout = null;
                _scene = null;
                _hoverLabel = null;
                _hoverItem = null;
                Voladizo = default;
                _mensaje = error ?? "No hay una losa válida para mostrar en 3D.";
                InvalidateVisual();
                return;
            }

            _layout = layout;
            _slab = slab;
            _columns = columns;
            _slabMinX = minX; _slabMaxX = maxX; _slabMinY = minY; _slabMaxY = maxY;
            _mensaje = null;
            Voladizo = LayoutEngine.ComputeOverhangs(layout, minX, maxX, minY, maxY);

            RebuildScene();
            _camera.Frame(minX, maxX, minY, maxY, _scene?.SlabHeight ?? 3.23);
            InvalidateVisual();
        }

        private void RebuildScene()
        {
            _hoverLabel = null;
            _hoverItem = null;
            _fantasma = null;
            _menu = null;
            _menuNudo = null;
            _menuIndice = -1;
            if (_layout == null)
            {
                _scene = null;
                return;
            }
            // En modo manual se dibuja la huella y el contorno interno del armado. Los tableros solo
            // aparecen cuando la losa ya está lista y se han colocado.
            CoreGeom.Rect? huella = _modoManual && _armado != null ? _armado.CajaEnvolvente : null;
            IReadOnlyList<ManualAssembly.SegmentoContorno>? contorno =
                _modoManual && _armado != null ? _armado.ContornoInterno(Preview3DScene.AnchoRiel) : null;
            bool tableros = _modoManual ? _layout.Boards.Count > 0 : _mostrarTableros;
            _scene = Preview3DScene.Build(_modo, tableros, _layout, _slab, _columns, huella, contorno,
                _voladizoContorno, _mostrarVoladizoContorno);
        }

        // ── RENDER ───────────────────────────────────────────────────────────────────────────────────
        public override void Render(DrawingContext context)
        {
            double cw = Bounds.Width;
            double ch = Bounds.Height;
            context.FillRectangle(BackgroundBrush, new Rect(0, 0, cw, ch));

            if (_scene == null || cw < 4 || ch < 4)
            {
                if (_mensaje != null)
                {
                    DrawCentered(context, _mensaje, cw, ch);
                }
                return;
            }

            (Vec3 right, Vec3 up, Vec3 fwd) = _camera.Basis();
            double scale = _camera.Scale(cw, ch);

            // 1) Caras: proyectar, descartar traseras, ordenar por profundidad y rellenar.
            var visibles = new List<(double depth, Point[] pts, Color color)>(_scene.Faces.Count);
            foreach (Face3D f in _scene.Faces)
            {
                if (f.Normal.Dot(fwd) <= 0)
                {
                    continue; // cara trasera
                }

                (Point sa, double da) = _camera.Project(ApplyOffset(f.A, f.Fixed), right, up, fwd, scale, cw, ch);
                (Point sb, double db) = _camera.Project(ApplyOffset(f.B, f.Fixed), right, up, fwd, scale, cw, ch);
                (Point sc, double dc) = _camera.Project(ApplyOffset(f.C, f.Fixed), right, up, fwd, scale, cw, ch);
                (Point sd, double dd) = _camera.Project(ApplyOffset(f.D, f.Fixed), right, up, fwd, scale, cw, ch);

                double intensity = Ambient + (Diffuse * Math.Max(0, f.Normal.Dot(LightDir)));
                Color shaded = Shade(f.Color, intensity);
                double avgDepth = (da + db + dc + dd) / 4;
                visibles.Add((avgDepth, new[] { sa, sb, sc, sd }, shaded));
            }

            visibles.Sort((a, b) => a.depth.CompareTo(b.depth)); // lejos → cerca

            foreach ((double _, Point[] pts, Color color) in visibles)
            {
                context.DrawGeometry(BrushFor(color), null, QuadGeometry(pts));
            }

            // 2) Aristas (contorno de losa, alámbrico, punteado de tableros).
            foreach (Edge3D e in _scene.Edges)
            {
                (Point pa, _) = _camera.Project(ApplyOffset(e.A, e.Fixed), right, up, fwd, scale, cw, ch);
                (Point pb, _) = _camera.Project(ApplyOffset(e.B, e.Fixed), right, up, fwd, scale, cw, ch);
                context.DrawLine(PenFor(e.Color, e.Thickness, e.Dashed), pa, pb);
            }

            // 3) Cotas ROJAS de holgura entre las piezas del armado y la cara de cada columna.
            if (_modoManual && _mostrarCotasColumnas)
            {
                DibujarCotasColumnas(context, right, up, fwd, scale);
            }

            // 4) Resaltado de la pieza bajo el puntero: línea central entrecortada (a lo largo del elemento)
            //    + dos líneas verticales en los extremos, para señalar exactamente qué riel/separador se mide.
            if (_hoverItem != null)
            {
                DrawHighlight(context, _hoverItem, right, up, fwd, scale, cw, ch);
            }

            // 5) Armado manual: cabezal señalado, flechas de crecimiento y fantasma de lo que se colocará.
            if (_modoManual && _armado != null)
            {
                DibujarAyudasArmado(context, right, up, fwd, scale);
            }

            // 6) Tooltip de medida. En el armado manual se recuerda ahí mismo que Suprimir borra la red.
            if (_hoverLabel != null)
            {
                // Las columnas son geometría fija del plano: no se borran, así que ahí no se ofrece el aviso.
                bool sePuedeBorrar = _modoManual && _armado != null && !_armado.Congelado
                    && _hoverItem != null && !_hoverItem.Fixed;
                DrawTooltip(context, sePuedeBorrar ? _hoverLabel + "\nSupr: eliminar la red" : _hoverLabel, _hoverPos);
            }

            // 7) Menú de medidas del catálogo (siempre por encima de todo lo demás).
            DibujarMenu(context);
        }

        // ── COTAS DE HOLGURA CONTRA LAS COLUMNAS ─────────────────────────────────────────────────────
        // Líneas ROJAS acotadas entre la cara de la columna y el filo de la pieza más cercana: el filo
        // INTERNO del riel y el filo del separador. Es la comprobación de la holgura de plegamiento: por
        // debajo de 5 cm no entra la mano para desmontar, y de 15 cm en adelante se trabaja cómodo.

        /// <summary>Holgura mínima de plegamiento (m): por debajo, la cota sale en rojo.</summary>
        private const double HolguraPlegadoMin = 0.05;

        /// <summary>Holgura de trabajo holgada (m): entre el mínimo y ésta, la cota sale en verde.</summary>
        private const double HolguraPlegadoBuena = 0.15;

        /// <summary>Más lejos de esto no se acota: ya no es la pieza vecina de la columna (m).</summary>
        private const double CotaAlcanceMax = 1.50;

        private void DibujarCotasColumnas(DrawingContext context, Vec3 right, Vec3 up, Vec3 fwd, double scale)
        {
            if (_layout == null || _columns.Count == 0)
            {
                return;
            }

            double zRiel = Preview3DScene.AlturaRieles;
            double zSep = Preview3DScene.AlturaSeparadores;
            double medioRiel = Preview3DScene.AnchoRiel / 2;
            double medioSep = Preview3DScene.DiametroSeparador / 2;

            foreach (ColumnSnapshot col in _columns)
            {
                // Piezas que corren en X → acotan las caras inferior y superior (eje Y) de la columna.
                AcotarFamilia(context, col, _layout.HorizontalRails, true, medioRiel, zRiel, "Riel", right, up, fwd, scale);
                AcotarFamilia(context, col, _layout.HorizontalSeparators, true, medioSep, zSep, "Sep.", right, up, fwd, scale);

                // Piezas que corren en Y → acotan las caras izquierda y derecha (eje X).
                AcotarFamilia(context, col, _layout.VerticalRails, false, medioRiel, zRiel, "Riel", right, up, fwd, scale);
                AcotarFamilia(context, col, _layout.VerticalSeparators, false, medioSep, zSep, "Sep.", right, up, fwd, scale);
            }
        }

        /// <summary>
        /// Acota la columna contra la pieza más cercana de una familia paralela. <paramref name="corrienteEnX"/>
        /// indica que las piezas corren a lo largo de X (su coordenada fija es Y), así que se miden las caras
        /// inferior y superior de la columna; con false se miden la izquierda y la derecha.
        /// </summary>
        private void AcotarFamilia(
            DrawingContext context, ColumnSnapshot col, IReadOnlyList<LinearPiece> piezas,
            bool corrienteEnX, double medioAncho, double z, string etiqueta,
            Vec3 right, Vec3 up, Vec3 fwd, double scale)
        {
            if (piezas.Count == 0)
            {
                return;
            }

            // La estructura se desplaza con el arrastre; la columna no. Se compensa aquí para que la cota
            // siga siendo real mientras se mueve el armado.
            double offRecorrido = corrienteEnX ? _offX : _offY;
            double offFija = corrienteEnX ? _offY : _offX;

            double caraMenor = corrienteEnX ? col.MinY : col.MinX;
            double caraMayor = corrienteEnX ? col.MaxY : col.MaxX;
            double tramoMin = corrienteEnX ? col.MinX : col.MinY;
            double tramoMax = corrienteEnX ? col.MaxX : col.MaxY;
            double centro = (tramoMin + tramoMax) / 2;

            double holguraMenor = double.MaxValue, filoMenor = 0;
            double holguraMayor = double.MaxValue, filoMayor = 0;

            foreach (LinearPiece p in piezas)
            {
                double desde = Math.Min(p.Start, p.End) + offRecorrido;
                double hasta = Math.Max(p.Start, p.End) + offRecorrido;
                if (hasta < tramoMin - 0.01 || desde > tramoMax + 0.01)
                {
                    continue;   // la pieza no pasa por delante de esta columna
                }

                double eje = p.FixedCoord + offFija;
                double filoHaciaMas = eje + medioAncho;
                double filoHaciaMenos = eje - medioAncho;

                if (filoHaciaMas <= caraMenor)
                {
                    double d = caraMenor - filoHaciaMas;
                    if (d < holguraMenor)
                    {
                        holguraMenor = d;
                        filoMenor = filoHaciaMas;
                    }
                }
                else if (filoHaciaMenos >= caraMayor)
                {
                    double d = filoHaciaMenos - caraMayor;
                    if (d < holguraMayor)
                    {
                        holguraMayor = d;
                        filoMayor = filoHaciaMenos;
                    }
                }
            }

            if (holguraMenor <= CotaAlcanceMax)
            {
                DibujarCota(context,
                    PuntoCota(centro, filoMenor, z, corrienteEnX),
                    PuntoCota(centro, caraMenor, z, corrienteEnX),
                    holguraMenor, etiqueta, right, up, fwd, scale);
            }

            if (holguraMayor <= CotaAlcanceMax)
            {
                DibujarCota(context,
                    PuntoCota(centro, caraMayor, z, corrienteEnX),
                    PuntoCota(centro, filoMayor, z, corrienteEnX),
                    holguraMayor, etiqueta, right, up, fwd, scale);
            }
        }

        private static Vec3 PuntoCota(double aLoLargo, double perpendicular, double z, bool corrienteEnX)
            => corrienteEnX ? new Vec3(aLoLargo, perpendicular, z) : new Vec3(perpendicular, aLoLargo, z);

        /// <summary>Línea roja con topes en los extremos y la distancia en centímetros sobre un chip oscuro.</summary>
        private void DibujarCota(
            DrawingContext context, Vec3 a, Vec3 b, double metros, string etiqueta,
            Vec3 right, Vec3 up, Vec3 fwd, double scale)
        {
            // Ambos extremos ya vienen en coordenadas de mundo con el desplazamiento incorporado.
            (Point pa, _) = _camera.Project(a, right, up, fwd, scale, Bounds.Width, Bounds.Height);
            (Point pb, _) = _camera.Project(b, right, up, fwd, scale, Bounds.Width, Bounds.Height);

            double dx = pb.X - pa.X;
            double dy = pb.Y - pa.Y;
            double largo = Math.Sqrt((dx * dx) + (dy * dy));
            if (largo < 1.0)
            {
                return;   // en pantalla no se distinguiría
            }

            double nx = -dy / largo * 5;
            double ny = dx / largo * 5;

            context.DrawLine(CotaHaloPen, pa, pb);
            context.DrawLine(CotaPen, pa, pb);
            context.DrawLine(CotaPen, new Point(pa.X - nx, pa.Y - ny), new Point(pa.X + nx, pa.Y + ny));
            context.DrawLine(CotaPen, new Point(pb.X - nx, pb.Y - ny), new Point(pb.X + nx, pb.Y + ny));

            IBrush color = metros < HolguraPlegadoMin
                ? CotaTextoJusto
                : metros <= HolguraPlegadoBuena ? CotaTextoBueno : CotaTextoAmplio;

            var texto = new FormattedText(
                $"{etiqueta} {Math.Round(metros * 100)} cm",
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TooltipFont, 11, color);

            double cx = (pa.X + pb.X) / 2;
            double cy = (pa.Y + pb.Y) / 2;
            var chip = new Rect(cx - (texto.Width / 2) - 4, cy - (texto.Height / 2) - 2, texto.Width + 8, texto.Height + 4);
            context.DrawRectangle(CotaChip, null, chip, 3, 3);
            context.DrawText(texto, new Point(chip.X + 4, chip.Y + 2));
        }

        /// <summary>
        /// Dibuja las ayudas del armado por clic: el cabezal del puntal señalado, sus 4 flechas de
        /// crecimiento y —si hay una flecha apuntada— el FANTASMA de los dos puntales y las piezas que se
        /// van a colocar, con su medida.
        /// </summary>
        private void DibujarAyudasArmado(DrawingContext context, Vec3 right, Vec3 up, Vec3 fwd, double scale)
        {
            // Con el menú abierto se señala el puntal de trabajo, pero sin flechas: la dirección la da el menú.
            ManualAssembly.Nudo? senalado = _menu != null ? _menuNudo : _nudoHover;
            if (_armado == null || senalado == null)
            {
                return;
            }

            CoreGeom.Point cabezal = _armado.Nudos()
                .FirstOrDefault(x => x.Nudo.Equals(senalado.Value)).Punto;
            double z = Preview3DScene.AlturaCabezalPuntal;
            double half = Preview3DScene.LadoCabezalPuntal / 2;

            // Cabezal resaltado.
            (Point c0, _) = Proyectar(new Vec3(cabezal.X - half, cabezal.Y - half, z), right, up, fwd, scale);
            (Point c1, _) = Proyectar(new Vec3(cabezal.X + half, cabezal.Y - half, z), right, up, fwd, scale);
            (Point c2, _) = Proyectar(new Vec3(cabezal.X + half, cabezal.Y + half, z), right, up, fwd, scale);
            (Point c3, _) = Proyectar(new Vec3(cabezal.X - half, cabezal.Y + half, z), right, up, fwd, scale);
            context.DrawGeometry(CabezalRelleno, CabezalPen, QuadGeometry(new[] { c0, c1, c2, c3 }));

            // Flechas: solo las direcciones en las que el armado PUEDE crecer (y solo sin menú abierto).
            if (_menu == null)
            {
                (Point origen, _) = Proyectar(new Vec3(cabezal.X, cabezal.Y, z), right, up, fwd, scale);
                foreach (ManualAssembly.Direccion dir in Enum.GetValues<ManualAssembly.Direccion>())
                {
                    if (!_armado.PuedeExtender(senalado.Value, dir, MedidaDe(senalado.Value, dir)))
                    {
                        continue;
                    }

                    (Point p, _) = Proyectar(PosicionFlecha(cabezal, dir), right, up, fwd, scale);
                    bool activa = _flechaHover == dir;
                    DibujarFlecha(context, origen, p, activa);
                }
            }

            // Fantasma de la extensión apuntada.
            if (_fantasma != null)
            {
                DibujarFantasma(context, _fantasma, right, up, fwd, scale, z, half);
            }
        }

        private void DibujarFlecha(DrawingContext context, Point origen, Point punta, bool activa)
        {
            double dx = punta.X - origen.X;
            double dy = punta.Y - origen.Y;
            double len = Math.Sqrt((dx * dx) + (dy * dy));
            if (len < 1e-3)
            {
                return;
            }
            dx /= len;
            dy /= len;

            // Triángulo apuntando hacia afuera, con la base perpendicular al eje.
            double lado = activa ? 11 : 8;
            Point cabeza = punta;
            Point baseA = new(punta.X - (dx * lado * 1.6) + (-dy * lado * 0.7), punta.Y - (dy * lado * 1.6) + (dx * lado * 0.7));
            Point baseB = new(punta.X - (dx * lado * 1.6) - (-dy * lado * 0.7), punta.Y - (dy * lado * 1.6) - (dx * lado * 0.7));

            context.DrawLine(activa ? FlechaEjePenActiva : FlechaEjePen, origen, punta);

            var geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(cabeza, true);
                ctx.LineTo(baseA);
                ctx.LineTo(baseB);
                ctx.EndFigure(true);
            }
            context.DrawGeometry(activa ? FlechaRellenoActiva : FlechaRelleno, null, geo);
        }

        private void DibujarFantasma(
            DrawingContext context, ManualAssembly.Extension ext,
            Vec3 right, Vec3 up, Vec3 fwd, double scale, double z, double half)
        {
            (Point anclaA, _) = Proyectar(new Vec3(ext.AnclaA.X, ext.AnclaA.Y, z), right, up, fwd, scale);
            (Point anclaB, _) = Proyectar(new Vec3(ext.AnclaB.X, ext.AnclaB.Y, z), right, up, fwd, scale);
            (Point nuevoA, _) = Proyectar(new Vec3(ext.NuevoA.X, ext.NuevoA.Y, z), right, up, fwd, scale);
            (Point nuevoB, _) = Proyectar(new Vec3(ext.NuevoB.X, ext.NuevoB.Y, z), right, up, fwd, scale);
            (Point pieA, _) = Proyectar(new Vec3(ext.NuevoA.X, ext.NuevoA.Y, 0), right, up, fwd, scale);
            (Point pieB, _) = Proyectar(new Vec3(ext.NuevoB.X, ext.NuevoB.Y, 0), right, up, fwd, scale);

            // Piezas de unión y amarre.
            context.DrawLine(FantasmaPen, anclaA, nuevoA);
            context.DrawLine(FantasmaPen, anclaB, nuevoB);
            context.DrawLine(FantasmaPen, nuevoA, nuevoB);

            // Los dos puntales nuevos (cabezal + fuste hasta el suelo).
            context.DrawLine(FantasmaPen, nuevoA, pieA);
            context.DrawLine(FantasmaPen, nuevoB, pieB);
            DibujarCabezalFantasma(context, ext.NuevoA, right, up, fwd, scale, z, half);
            DibujarCabezalFantasma(context, ext.NuevoB, right, up, fwd, scale, z, half);

            // Aspa de la cruceta, solo cuando el brazo sale de la base principal (la cruz arriostrada).
            if (ext.ConCruceta)
            {
                double zBajo = ext.CrucetaGrande ? 0.50 : 0.40;
                double zAlto = ext.CrucetaGrande ? 1.50 : 1.70;
                (Point aBajo, _) = Proyectar(new Vec3(ext.NuevoA.X, ext.NuevoA.Y, zBajo), right, up, fwd, scale);
                (Point aAlto, _) = Proyectar(new Vec3(ext.NuevoA.X, ext.NuevoA.Y, zAlto), right, up, fwd, scale);
                (Point bBajo, _) = Proyectar(new Vec3(ext.NuevoB.X, ext.NuevoB.Y, zBajo), right, up, fwd, scale);
                (Point bAlto, _) = Proyectar(new Vec3(ext.NuevoB.X, ext.NuevoB.Y, zAlto), right, up, fwd, scale);
                context.DrawLine(FantasmaPen, aBajo, bAlto);
                context.DrawLine(FantasmaPen, aAlto, bBajo);
            }

            // Etiqueta: nombre de la opción, piezas exactas y cómo colocarla.
            var medio = new Point((nuevoA.X + nuevoB.X) / 2, (nuevoA.Y + nuevoB.Y) / 2);
            DrawTooltip(context, $"{ext.Nombre}\n{ext.Descripcion}\nClic izquierdo para colocar", medio);
        }

        private void DibujarCabezalFantasma(
            DrawingContext context, CoreGeom.Point centro,
            Vec3 right, Vec3 up, Vec3 fwd, double scale, double z, double half)
        {
            (Point p0, _) = Proyectar(new Vec3(centro.X - half, centro.Y - half, z), right, up, fwd, scale);
            (Point p1, _) = Proyectar(new Vec3(centro.X + half, centro.Y - half, z), right, up, fwd, scale);
            (Point p2, _) = Proyectar(new Vec3(centro.X + half, centro.Y + half, z), right, up, fwd, scale);
            (Point p3, _) = Proyectar(new Vec3(centro.X - half, centro.Y + half, z), right, up, fwd, scale);
            context.DrawGeometry(FantasmaRelleno, FantasmaPen, QuadGeometry(new[] { p0, p1, p2, p3 }));
        }

        /// <summary>
        /// Dibuja la marca de medición sobre la pieza señalada: la línea central discontinua recorre el eje
        /// largo del elemento (de extremo a extremo) y en cada punta se traza una línea vertical de referencia.
        /// </summary>
        private void DrawHighlight(DrawingContext context, HoverItem item, Vec3 right, Vec3 up, Vec3 fwd, double scale, double cw, double ch)
        {
            (Vec3 e1, Vec3 e2) = ExtremosPieza(item);
            e1 = ApplyOffset(e1, item.Fixed);
            e2 = ApplyOffset(e2, item.Fixed);
            (Point pa, _) = _camera.Project(e1, right, up, fwd, scale, cw, ch);
            (Point pb, _) = _camera.Project(e2, right, up, fwd, scale, cw, ch);

            const double medio = 15;
            Point a1 = new(pa.X, pa.Y - medio), a2 = new(pa.X, pa.Y + medio);
            Point b1 = new(pb.X, pb.Y - medio), b2 = new(pb.X, pb.Y + medio);

            // 1) Halo oscuro (trazos más gruesos por debajo).
            context.DrawLine(HighlightDashHaloPen, pa, pb);
            context.DrawLine(HighlightEndHaloPen, a1, a2);
            context.DrawLine(HighlightEndHaloPen, b1, b2);

            // 2) Trazo blanco encima: línea central entrecortada + verticales de extremo.
            context.DrawLine(HighlightDashPen, pa, pb);
            context.DrawLine(HighlightEndPen, a1, a2);
            context.DrawLine(HighlightEndPen, b1, b2);
        }

        /// <summary>Puntos medios de las dos caras extremas (a lo largo del eje largo) de la cara superior.</summary>
        private static (Vec3, Vec3) ExtremosPieza(HoverItem h)
        {
            double lado01 = (h.T1 - h.T0).Length;
            double lado12 = (h.T2 - h.T1).Length;
            if (lado01 >= lado12)
            {
                // Ejes largos: T0→T1 y T3→T2. Extremos = puntos medios de los lados cortos T1-T2 y T3-T0.
                return ((h.T1 + h.T2) * 0.5, (h.T3 + h.T0) * 0.5);
            }
            // Ejes largos: T1→T2 y T0→T3. Extremos = puntos medios de los lados cortos T0-T1 y T2-T3.
            return ((h.T0 + h.T1) * 0.5, (h.T2 + h.T3) * 0.5);
        }

        private void DrawCentered(DrawingContext context, string text, double cw, double ch)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TooltipFont, 14, new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)));
            context.DrawText(ft, new Point((cw - ft.Width) / 2, (ch - ft.Height) / 2));
        }

        private void DrawTooltip(DrawingContext context, string text, Point at)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TooltipFont, 12, TooltipFg);
            double padX = 6, padY = 4;
            double w = ft.Width + (padX * 2);
            double h = ft.Height + (padY * 2);
            double x = Math.Min(at.X + 14, Bounds.Width - w - 2);
            double y = Math.Min(at.Y + 14, Bounds.Height - h - 2);
            context.DrawRectangle(TooltipBg, null, new Rect(x, y, w, h), 4, 4);
            context.DrawText(ft, new Point(x + padX, y + padY));
        }

        private static Color Shade(Color c, double intensity)
        {
            byte Ch(byte v) => (byte)Math.Clamp(Math.Round(v * intensity), 0, 255);
            return Color.FromArgb(c.A, Ch(c.R), Ch(c.G), Ch(c.B));
        }

        private static StreamGeometry QuadGeometry(Point[] pts)
        {
            var geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(pts[0], true);
                for (int i = 1; i < pts.Length; i++)
                {
                    ctx.LineTo(pts[i]);
                }
                ctx.EndFigure(true);
            }
            return geo;
        }

        private IBrush BrushFor(Color color)
        {
            uint key = color.ToUInt32();
            if (!_brushCache.TryGetValue(key, out IBrush? brush))
            {
                brush = new SolidColorBrush(color);
                _brushCache[key] = brush;
            }
            return brush;
        }

        private IPen PenFor(Color color, double thickness, bool dashed)
        {
            uint key = color.ToUInt32() ^ ((uint)(thickness * 10) << 1) ^ (dashed ? 0x8000_0000u : 0u);
            if (!_penCache.TryGetValue(key, out IPen? pen))
            {
                var p = new Pen(new SolidColorBrush(color), thickness);
                if (dashed)
                {
                    p.DashStyle = new DashStyle(new double[] { 3, 2 }, 0);
                }
                pen = p;
                _penCache[key] = pen;
            }
            return pen;
        }

        // ── INTERACCIÓN ────────────────────────────────────────────────────────────────────────────
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            PointerPoint pp = e.GetCurrentPoint(this);
            _lastPointer = pp.Position;
            // Clic DERECHO = órbita (rotar 360°), como en la v1. Central = pan. Izquierdo = pan, salvo que el
            // modo "mover estructura" esté activo: entonces el clic izquierdo ARRASTRA el encofrado.
            if (_modoManual)
            {
                // 1) Menú de medidas abierto: el clic IZQUIERDO elige la fila; cualquier otro clic lo cierra.
                if (_menu != null)
                {
                    if (!pp.Properties.IsLeftButtonPressed || !AplicarOpcionMenu(pp.Position))
                    {
                        CerrarMenu();
                    }
                    e.Pointer.Capture(this);
                    Focus();
                    return;
                }

                // 2) Clic DERECHO sobre el cabezal del puntal: abre el menú con las medidas del catálogo.
                if (pp.Properties.IsRightButtonPressed && _nudoHover != null)
                {
                    AbrirMenuMedidas(_nudoHover.Value, pp.Position);
                    e.Pointer.Capture(this);
                    Focus();
                    return;
                }

                // 3) Clic IZQUIERDO sobre una flecha: coloca la medida estándar de un solo golpe.
                if (pp.Properties.IsLeftButtonPressed && _flechaHover != null)
                {
                    ColocarExtension();
                    e.Pointer.Capture(this);
                    Focus();
                    return;
                }
            }

            if (pp.Properties.IsRightButtonPressed)
            {
                _dragOrbit = true;
            }
            else if (pp.Properties.IsLeftButtonPressed)
            {
                if (_moverEstructura)
                {
                    _dragStructure = true;
                }
                else
                {
                    _dragPan = true;
                }
            }
            else if (pp.Properties.IsMiddleButtonPressed)
            {
                _dragPan = true;
            }
            e.Pointer.Capture(this);
            Focus();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            Point pos = e.GetCurrentPoint(this).Position;

            // Basta con pasar el puntero para que el teclado (Supr, Ctrl+Z, W A S D) llegue aquí: sin esto
            // habría que hacer clic antes de poder eliminar una red o ajustar la estructura.
            if ((_modoManual || _moverEstructura) && !IsFocused)
            {
                Focus();
            }

            if (_dragStructure)
            {
                MoverEstructuraPorArrastre(pos);
                _lastPointer = pos;
                return;
            }

            if (_dragOrbit)
            {
                double dx = pos.X - _lastPointer.X;
                double dy = pos.Y - _lastPointer.Y;
                _camera.Orbit(dx * 0.01, dy * 0.01);
                _lastPointer = pos;
                _hoverLabel = null;
                _hoverItem = null;
                _nudoHover = null;
                _flechaHover = null;
                _fantasma = null;
                CerrarMenu();
                InvalidateVisual();
                return;
            }
            if (_dragPan)
            {
                double dx = pos.X - _lastPointer.X;
                double dy = pos.Y - _lastPointer.Y;
                _camera.Pan(dx, dy);
                _lastPointer = pos;
                InvalidateVisual();
                return;
            }

            UpdateHover(pos);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            // En modo manual, al soltar el arrastre el desplazamiento se INCORPORA al origen del armado:
            // así los nudos, las flechas y el fantasma siguen coincidiendo con lo que se ve en pantalla.
            if (_dragStructure && _modoManual && _armado != null && (_offX != 0 || _offY != 0))
            {
                _armado.OrigenX += _offX;
                _armado.OrigenY += _offY;
                _offX = 0;
                _offY = 0;
                ReconstruirArmado();
            }

            _dragOrbit = false;
            _dragPan = false;
            _dragStructure = false;
            e.Pointer.Capture(null);
        }

        /// <summary>
        /// Convierte el arrastre del ratón (píxeles en pantalla) en un desplazamiento del encofrado sobre el
        /// plano XY del mundo (Z fijo), invirtiendo la proyección ortográfica de la cámara. Así la estructura
        /// "sigue" al puntero por el suelo, se vea la escena desde el ángulo que se vea.
        /// </summary>
        private void MoverEstructuraPorArrastre(Point pos)
        {
            double dsx = pos.X - _lastPointer.X;
            double dsy = pos.Y - _lastPointer.Y;

            (Vec3 right, Vec3 up, _) = _camera.Basis();
            double scale = _camera.Scale(Bounds.Width, Bounds.Height);
            if (scale > 1e-6)
            {
                // Proyección: dsx = scale·(D·right), dsy = −scale·(D·up), con D=(dX,dY,0) en el plano.
                double a = dsx / scale;
                double b = -dsy / scale;
                double det = (right.X * up.Y) - (right.Y * up.X);
                if (Math.Abs(det) > 1e-9)
                {
                    _offX += ((a * up.Y) - (b * right.Y)) / det;
                    _offY += ((right.X * b) - (up.X * a)) / det;
                }
            }

            _hoverLabel = null;
            _hoverItem = null;
            InvalidateVisual();

            // En modo manual las cotas de centrado se actualizan en vivo mientras se arrastra.
            if (_modoManual)
            {
                MedidasCambiadas?.Invoke();
            }
        }

        /// <summary>Ctrl+Z sobre la vista 3D deshace el último paso del armado manual.</summary>
        public event Action? DeshacerSolicitado;

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Escape && _menu != null)
            {
                CerrarMenu();
                e.Handled = true;
                return;
            }

            if (_modoManual && e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                DeshacerSolicitado?.Invoke();
                e.Handled = true;
                return;
            }

            // W A S D (o las flechas del teclado) con el modo "mover estructura" activo: ajuste FINO, que es
            // lo que el arrastre con el ratón no da.
            if (_moverEstructura && MoverEstructuraPorTeclado(e.Key, e.KeyModifiers))
            {
                e.Handled = true;
                return;
            }

            // SUPRIMIR con el puntero sobre una pieza: se va la red entera (la pieza, los puntales que se
            // quedan sin amarre y todo lo que colgaba de ellos).
            if (_modoManual && (e.Key == Key.Delete || e.Key == Key.Back))
            {
                EliminarRedSenalada();
                e.Handled = true;
            }
        }

        /// <summary>Paso de ajuste fino con el teclado (m).</summary>
        private const double PasoTeclado = 0.01;

        /// <summary>Paso con Mayúsculas pulsadas (m).</summary>
        private const double PasoTecladoGrande = 0.10;

        /// <summary>
        /// Mueve la estructura un paso EXACTO con el teclado. La dirección se toma tal como se ve en
        /// pantalla —D la corre hacia la derecha del monitor, W hacia arriba— aunque la cámara esté girada:
        /// se invierte la proyección igual que en el arrastre, pero avanzando una distancia medida en metros
        /// en vez de en píxeles. Devuelve false si la tecla no es de movimiento.
        /// </summary>
        private bool MoverEstructuraPorTeclado(Key tecla, KeyModifiers modificadores)
        {
            double sx = 0, sy = 0;   // dirección pedida, en coordenadas de PANTALLA
            switch (tecla)
            {
                case Key.A:
                case Key.Left:
                    sx = -1;
                    break;
                case Key.D:
                case Key.Right:
                    sx = 1;
                    break;
                case Key.W:
                case Key.Up:
                    sy = -1;
                    break;
                case Key.S:
                case Key.Down:
                    sy = 1;
                    break;
                default:
                    return false;
            }

            if (_armado != null && _armado.Congelado)
            {
                _ultimaAccion = "La losa está lista: reábrala para mover la estructura.";
                MedidasCambiadas?.Invoke();
                return true;
            }

            (Vec3 right, Vec3 up, _) = _camera.Basis();
            double det = (right.X * up.Y) - (right.Y * up.X);
            if (Math.Abs(det) < 1e-9)
            {
                return true;   // cámara a plomo sobre el eje: no hay plano donde correr
            }

            double a = sx;
            double b = -sy;
            double dx = ((a * up.Y) - (b * right.Y)) / det;
            double dy = ((right.X * b) - (up.X * a)) / det;

            double largo = Math.Sqrt((dx * dx) + (dy * dy));
            if (largo < 1e-9)
            {
                return true;
            }

            double paso = modificadores.HasFlag(KeyModifiers.Shift) ? PasoTecladoGrande : PasoTeclado;
            dx = dx / largo * paso;
            dy = dy / largo * paso;

            if (_armado != null)
            {
                // En manual el desplazamiento se incorpora al origen, para que nudos, flechas y fantasma
                // sigan coincidiendo con lo que se ve.
                _armado.OrigenX += dx;
                _armado.OrigenY += dy;
                _offX = 0;
                _offY = 0;
                _ultimaAccion = $"Estructura movida {Math.Round(paso * 100)} cm con el teclado.";
                ReconstruirArmado();
                return true;
            }

            _offX += dx;
            _offY += dy;
            _hoverLabel = null;
            _hoverItem = null;
            InvalidateVisual();
            MedidasCambiadas?.Invoke();
            return true;
        }

        /// <summary>
        /// Elimina la RED de la pieza que hay bajo el puntero: la pieza señalada más los puntales que se
        /// quedan sin amarre y sus piezas y crucetas. Devuelve true si se borró algo.
        /// </summary>
        private bool EliminarRedSenalada()
        {
            if (_armado == null || _hoverItem == null)
            {
                _ultimaAccion = "Lleve el puntero sobre un riel o un separador y pulse Suprimir.";
                MedidasCambiadas?.Invoke();
                return false;
            }

            // Los extremos del resalte están en coordenadas de pantalla del mundo YA desplazado: se le resta
            // el arrastre en curso para volver a las coordenadas propias del armado.
            (Vec3 e1, Vec3 e2) = ExtremosPieza(_hoverItem);
            var extremoA = new CoreGeom.Point(e1.X - _offX, e1.Y - _offY);
            var extremoB = new CoreGeom.Point(e2.X - _offX, e2.Y - _offY);

            ManualAssembly.RedSenalada? red = _armado.LocalizarRed(extremoA, extremoB, out string motivo);
            if (red == null)
            {
                _ultimaAccion = motivo;
                MedidasCambiadas?.Invoke();
                return false;
            }

            AntesDeCambiarArmado?.Invoke();
            ManualAssembly.Borrado borrado = _armado.EliminarRed(red);

            _ultimaAccion = $"Eliminada la red de {red.Nombre}: {borrado}.";
            _hoverItem = null;
            _hoverLabel = null;
            _nudoHover = null;
            _flechaHover = null;
            _fantasma = null;
            ReconstruirArmado();
            return true;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            CerrarMenu();   // el menú está anclado a la pantalla: al hacer zoom dejaría de corresponder
            _camera.ZoomBy(e.Delta.Y > 0 ? 1.1 : (1.0 / 1.1));
            InvalidateVisual();
            e.Handled = true;
        }

        private void UpdateHover(Point pos)
        {
            // Con el menú de medidas abierto, el cursor solo recorre sus filas.
            if (_modoManual && _menu != null)
            {
                ActualizarHoverMenu(pos);
                if (_hoverLabel != null || _hoverItem != null)
                {
                    _hoverLabel = null;
                    _hoverItem = null;
                    InvalidateVisual();
                }
                return;
            }

            // En modo manual mandan los cabezales de los puntales: si el cursor está sobre uno (o sobre una
            // de sus flechas), no se muestra el tooltip de medida de piezas para no tapar el fantasma.
            if (_modoManual && ActualizarHoverArmado(pos))
            {
                if (_hoverLabel != null || _hoverItem != null)
                {
                    _hoverLabel = null;
                    _hoverItem = null;
                    InvalidateVisual();
                }
                return;
            }

            if (_scene == null || _scene.Hovers.Count == 0)
            {
                if (_hoverLabel != null || _hoverItem != null)
                {
                    _hoverLabel = null;
                    _hoverItem = null;
                    InvalidateVisual();
                }
                return;
            }

            (Vec3 right, Vec3 up, Vec3 fwd) = _camera.Basis();
            double scale = _camera.Scale(Bounds.Width, Bounds.Height);

            string? best = null;
            HoverItem? bestItem = null;
            double bestDepth = double.NegativeInfinity;
            foreach (HoverItem h in _scene.Hovers)
            {
                (Point p0, double d0) = _camera.Project(ApplyOffset(h.T0, h.Fixed), right, up, fwd, scale, Bounds.Width, Bounds.Height);
                (Point p1, double d1) = _camera.Project(ApplyOffset(h.T1, h.Fixed), right, up, fwd, scale, Bounds.Width, Bounds.Height);
                (Point p2, double d2) = _camera.Project(ApplyOffset(h.T2, h.Fixed), right, up, fwd, scale, Bounds.Width, Bounds.Height);
                (Point p3, double d3) = _camera.Project(ApplyOffset(h.T3, h.Fixed), right, up, fwd, scale, Bounds.Width, Bounds.Height);
                if (!PointInQuad(pos, p0, p1, p2, p3))
                {
                    continue;
                }
                double depth = (d0 + d1 + d2 + d3) / 4;
                if (depth > bestDepth)
                {
                    bestDepth = depth;
                    best = h.Label;
                    bestItem = h;
                }
            }

            bool cambio = !ReferenceEquals(bestItem, _hoverItem)
                || (best != null && (Math.Abs(pos.X - _hoverPos.X) > 1 || Math.Abs(pos.Y - _hoverPos.Y) > 1));
            if (cambio)
            {
                _hoverLabel = best;
                _hoverItem = bestItem;
                _hoverPos = pos;
                InvalidateVisual();
            }
        }

        private static bool PointInQuad(Point p, Point a, Point b, Point c, Point d)
        {
            return PointInTriangle(p, a, b, c) || PointInTriangle(p, a, c, d);
        }

        private static bool PointInTriangle(Point p, Point a, Point b, Point c)
        {
            double Sign(Point p1, Point p2, Point p3)
                => ((p1.X - p3.X) * (p2.Y - p3.Y)) - ((p2.X - p3.X) * (p1.Y - p3.Y));

            double d1 = Sign(p, a, b);
            double d2 = Sign(p, b, c);
            double d3 = Sign(p, c, a);
            bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
            bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(hasNeg && hasPos);
        }
    }
}
