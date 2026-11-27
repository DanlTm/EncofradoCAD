using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using EncofradoCAD.App.ViewModels;
using EncofradoCAD.App.Views.Preview3D;
using EncofradoCAD.Core.Layout;

namespace EncofradoCAD.App.Views.Modules
{
    /// <summary>Módulo de Modelado 3D: aloja el preview 3D, su toolbar y el panel de ARMADO MANUAL.</summary>
    public partial class Modelado3DView : UserControl
    {
        private readonly Preview3DControl _preview3D = new Preview3DControl();

        /// <summary>Pila de deshacer: una copia del armado ANTES de cada cambio.</summary>
        private readonly List<ManualAssembly> _historial = new();

        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

        public Modelado3DView()
        {
            InitializeComponent();
            Canvas3DHost.Child = _preview3D;
            _preview3D.MedidasCambiadas += ActualizarCotas;
            _preview3D.AntesDeCambiarArmado += GuardarEnHistorial;
            _preview3D.DeshacerSolicitado += Deshacer;   // Ctrl+Z sobre la vista 3D
            LlenarCatalogos();
        }

        /// <summary>Medida de catálogo para los combos (se muestra en metros con dos decimales).</summary>
        private sealed record Medida(double Valor)
        {
            public override string ToString() => $"{Valor.ToString("0.00", Es)} m";
        }

        private void LlenarCatalogos()
        {
            List<Medida> rieles = LayoutCatalog.RailLengths.OrderByDescending(v => v).Select(v => new Medida(v)).ToList();
            List<Medida> seps = LayoutCatalog.SeparatorLengths.OrderByDescending(v => v).Select(v => new Medida(v)).ToList();

            ComboBaseRiel.ItemsSource = rieles;
            ComboBaseSeparador.ItemsSource = seps;

            // Por defecto: riel estándar de 3,00 m y separador estándar de 1,20 m (los primeros de la lista).
            ComboBaseRiel.SelectedIndex = 0;
            ComboBaseSeparador.SelectedIndex = 0;
            AplicarMedidas();
        }

        private static double ValorDe(ComboBox combo, double porDefecto)
            => combo.SelectedItem is Medida m ? m.Valor : porDefecto;

        /// <summary>Traslada al preview las medidas que se colocarán al hacer clic en las flechas.</summary>
        private void AplicarMedidas()
        {
            _preview3D.MedidaRiel = ValorDe(ComboBaseRiel, 3.00);
            _preview3D.MedidaSeparador = ValorDe(ComboBaseSeparador, 1.20);
        }

        private void OnMedidasChanged(object? sender, SelectionChangedEventArgs e) => AplicarMedidas();

        private MainViewModel? Vm => DataContext as MainViewModel;

        /// <summary>Reconstruye la vista 3D a partir del lienzo 2D compartido.</summary>
        public void RefrescarDesdeCanvas()
        {
            if (DataContext is MainViewModel vm && vm.Canvas2D != null)
            {
                _preview3D.Actualizar(vm.Canvas2D);
                ActualizarInfoVoladizo();
                ActualizarCotas();
            }
        }

        /// <summary>Muestra en el recuadro el voladizo real por lado que el motor colocó (cm).</summary>
        private void ActualizarInfoVoladizo()
        {
            if (VoladizoCard == null || VoladizoInfo == null)
            {
                return;
            }

            if (_preview3D.ModoManual || !_preview3D.HayLayout)
            {
                VoladizoCard.IsVisible = false;
                return;
            }

            var v = _preview3D.Voladizo;
            static int Cm(double m) => (int)System.Math.Round(m * 100);
            VoladizoInfo.Text =
                $"Izquierda: {Cm(v.Izquierda)} cm    Derecha: {Cm(v.Derecha)} cm\n" +
                $"Superior: {Cm(v.Superior)} cm    Inferior: {Cm(v.Inferior)} cm";
            VoladizoCard.IsVisible = true;
        }

        // ── Handlers de la toolbar 3D ────────────────────────────────────────
        private void OnEstilo3DChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (Combo3DEstilo == null)
            {
                return;
            }
            _preview3D.Modo = Combo3DEstilo.SelectedIndex switch
            {
                1 => Modo3D.Transparente,
                2 => Modo3D.Alambrico,
                _ => Modo3D.Solido
            };
        }

        private void OnToggleTableros(object? sender, RoutedEventArgs e)
        {
            if (Chk3DTableros != null)
            {
                _preview3D.MostrarTableros = Chk3DTableros.IsChecked == true;
            }
        }

        private void OnActualizar3D(object? sender, RoutedEventArgs e) => RefrescarDesdeCanvas();

        /// <summary>
        /// Muestra u oculta las medidas rojas de los rieles y los separadores a la cara de las columnas. El
        /// botón dice siempre lo que va a hacer al pulsarlo, no el estado en que está.
        /// </summary>
        private void OnToggleCotasColumnas(object? sender, RoutedEventArgs e)
        {
            if (BtnManualCotasColumnas == null)
            {
                return;
            }

            bool mostrar = BtnManualCotasColumnas.IsChecked == true;
            _preview3D.MostrarCotasColumnas = mostrar;

            if (TextoCotasColumnas != null)
            {
                TextoCotasColumnas.Text = mostrar ? "Ocultar medidas" : "Mostrar medidas";
            }
        }

        // ── Mover estructura ─────────────────────────────────────────────────
        private void OnToggleMoverEstructura(object? sender, RoutedEventArgs e)
        {
            // El mismo handler sirve a los dos submódulos: cada uno tiene su propio botón.
            ToggleButton? origen = sender as ToggleButton
                ?? (_preview3D.ModoManual ? BtnManualMover : Btn3DMoverEstructura);
            if (origen == null)
            {
                return;
            }

            bool activo = origen.IsChecked == true;
            _preview3D.MoverEstructura = activo;

            // Se mantienen sincronizados para que al cambiar de submódulo no queden en estados distintos.
            if (Btn3DMoverEstructura != null)
            {
                Btn3DMoverEstructura.IsChecked = activo;
            }
            if (BtnManualMover != null)
            {
                BtnManualMover.IsChecked = activo;
            }

            ActualizarAyudaNavegacion();
        }

        private void ActualizarAyudaNavegacion()
        {
            if (Ayuda3DTexto == null)
            {
                return;
            }

            // En ARMADO MANUAL el bloque de ayuda desaparece del ribbon: su sitio lo ocupa el control de
            // VOLADO, que es el dato que hace falta tener a la vista mientras se arma.
            if (GrupoAyuda3D != null)
            {
                GrupoAyuda3D.IsVisible = !_preview3D.ModoManual;
            }

            if (_preview3D.ModoManual)
            {
                if (_preview3D.LosaLista)
                {
                    Ayuda3DTexto.Text = _preview3D.HayTableros
                        ? "Losa lista y tablerada · Use \"Rehacer base principal\" para volver a empezar"
                        : "Losa lista: la estructura está bloqueada · Pulse \"Colocar tableros\" para completarla";
                }
                else
                {
                    Ayuda3DTexto.Text = BtnManualMover?.IsChecked == true
                        ? "Mover estructura: clic izquierdo arrastra · W A S D la ajustan 1 cm (con Mayús, 10 cm) · Apáguelo para volver a colocar piezas"
                        : "Flecha + clic izquierdo = medida estándar · Clic derecho en el cabezal = catálogo · Supr sobre una pieza = elimina la red · Ctrl+Z: deshacer";
                }
                return;
            }

            Ayuda3DTexto.Text = Btn3DMoverEstructura?.IsChecked == true
                ? "Mover estructura: clic izquierdo arrastra · W A S D ajustan 1 cm (con Mayús, 10 cm) · Clic derecho: rotar · Rueda: zoom"
                : "Clic derecho: rotar 360° · Clic izquierdo: mover cámara · Rueda: zoom";
        }

        private void OnRestablecerEstructura(object? sender, RoutedEventArgs e)
        {
            _preview3D.RestablecerPosicionEstructura();
        }

        // ── VOLADO del armado manual ─────────────────────────────────────────

        /// <summary>Medidas de volado que ofrece el selector del ribbon, en metros.</summary>
        private static readonly double[] VoladosDisponibles = { 0.05, 0.10, 0.15, 0.20 };

        /// <summary>Volado (m) elegido ahora mismo en el ribbon del armado manual.</summary>
        private double VoladoElegido
        {
            get
            {
                int i = ComboManualVolado?.SelectedIndex ?? -1;
                return i >= 0 && i < VoladosDisponibles.Length
                    ? VoladosDisponibles[i]
                    : ManualAssembly.VoladizoMax;
            }
        }

        /// <summary>Enciende o apaga la línea entrecortada del volado en la vista 3D.</summary>
        private void OnToggleVolado(object? sender, RoutedEventArgs e)
        {
            if (BtnManualVolado == null)
            {
                return;
            }

            bool mostrar = BtnManualVolado.IsChecked == true;
            _preview3D.MostrarVoladizoContorno = mostrar;
            if (ComboManualVolado != null)
            {
                ComboManualVolado.IsEnabled = mostrar;
            }

            Registrar(mostrar
                ? $"Volado visible: {VoladoCm(VoladoElegido)} cm alrededor del filo de la losa."
                : "Volado oculto: la línea entrecortada ya no se dibuja.");
        }

        /// <summary>Traslada al 3D la medida de volado elegida y la deja escrita en el registro.</summary>
        private void OnVoladoChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ComboManualVolado == null)
            {
                return;
            }

            _preview3D.VoladizoContorno = VoladoElegido;
            Registrar($"Volado de {VoladoCm(VoladoElegido)} cm: el modelado 3D cuenta con ese margen por lado.");
        }

        /// <summary>
        /// Rueda del ratón encima del selector: sube o baja de medida sin tener que desplegar la lista.
        /// Arriba = más volado, abajo = menos.
        /// </summary>
        private void OnVoladoRueda(object? sender, Avalonia.Input.PointerWheelEventArgs e)
        {
            if (ComboManualVolado == null || ComboManualVolado.IsDropDownOpen || !ComboManualVolado.IsEnabled)
            {
                return;
            }

            int paso = e.Delta.Y > 0 ? 1 : e.Delta.Y < 0 ? -1 : 0;
            if (paso == 0)
            {
                return;
            }

            int destino = Math.Clamp(ComboManualVolado.SelectedIndex + paso, 0, VoladosDisponibles.Length - 1);
            if (destino != ComboManualVolado.SelectedIndex)
            {
                ComboManualVolado.SelectedIndex = destino;
            }

            e.Handled = true;   // la rueda NO debe hacer zoom en el 3D mientras se elige el volado
        }

        private static int VoladoCm(double metros) => (int)Math.Round(metros * 100);

        /// <summary>Deja el 3D con el volado que marca el ribbon (al entrar en armado manual).</summary>
        private void AplicarVolado()
        {
            bool mostrar = BtnManualVolado?.IsChecked != false;
            _preview3D.MostrarVoladizoContorno = mostrar;
            _preview3D.VoladizoContorno = VoladoElegido;
            if (ComboManualVolado != null)
            {
                ComboManualVolado.IsEnabled = mostrar;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  ARMADO MANUAL
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Entra o sale del modo de armado manual (el motor automático queda intacto).</summary>
        private void OnToggleArmadoManual(object? sender, RoutedEventArgs e)
        {
            MainViewModel? vm = Vm;
            if (vm?.Canvas2D == null || Btn3DArmadoManual == null)
            {
                return;
            }

            bool activo = Btn3DArmadoManual.IsChecked == true;

            if (!activo)
            {
                // Vuelve el submódulo de cálculo automático con todos sus controles.
                Grupo3DAutomatico.IsVisible = true;
                Grupo3DManual.IsVisible = false;
                PanelArmado.IsVisible = false;
                TiradorPanel.IsVisible = false;
                CotasCard.IsVisible = false;
                _historial.Clear();
                vm.ArmadoManual = null;
                _preview3D.SalirArmadoManual(vm.Canvas2D);
                // Fuera del armado manual manda la norma: contorno de voladizo visible a 20 cm.
                _preview3D.MostrarVoladizoContorno = true;
                _preview3D.VoladizoContorno = ManualAssembly.VoladizoMax;
                ActualizarInfoVoladizo();
                ActualizarAyudaNavegacion();
                return;
            }

            AplicarMedidas();
            AplicarVolado();
            ManualAssembly armado = CrearBaseDesdeUI();
            if (!_preview3D.IniciarArmadoManual(vm.Canvas2D, armado))
            {
                Btn3DArmadoManual.IsChecked = false;
                Registrar("Trace y cierre la losa en el módulo 2D antes de armar.");
                return;
            }

            // Submódulo manual: desaparecen los controles del cálculo automático.
            Grupo3DAutomatico.IsVisible = false;
            Grupo3DManual.IsVisible = true;
            TiradorPanel.IsVisible = true;
            MostrarPanelArmado(true);
            VoladizoCard.IsVisible = false;
            _historial.Clear();
            vm.ArmadoManual = armado;

            // En modo manual el clic izquierdo es para ARMAR: mover la estructura queda como acción aparte,
            // con su botón, para que un clic algo desviado no arrastre el encofrado sin querer.
            Btn3DMoverEstructura.IsChecked = false;
            BtnManualMover.IsChecked = false;
            _preview3D.MoverEstructura = false;
            ActualizarEstadoLosa();
            ActualizarAyudaNavegacion();

            Registrar("Base principal colocada en el centro. Apunte el cabezal de un puntal para crecer.");
            ActualizarCotas();
        }

        /// <summary>Pliega o despliega el panel de armado con el tirador de su costado.</summary>
        private void OnPlegarPanel(object? sender, RoutedEventArgs e)
            => MostrarPanelArmado(PanelArmado?.IsVisible != true);

        private void MostrarPanelArmado(bool visible)
        {
            if (PanelArmado == null)
            {
                return;
            }

            PanelArmado.IsVisible = visible;

            if (IconoPlegarPanel != null)
            {
                IconoPlegarPanel.Text = visible ? "‹" : "›";
            }
            if (BtnPlegarPanel != null)
            {
                ToolTip.SetTip(BtnPlegarPanel, visible ? "Ocultar el panel de armado" : "Mostrar el panel de armado");
            }
        }

        private ManualAssembly CrearBaseDesdeUI()
        {
            return ManualAssembly.CrearBasePrincipal(
                largoRiel: ValorDe(ComboBaseRiel, 3.00),
                largoSeparador: ValorDe(ComboBaseSeparador, 1.20),
                rielesHorizontales: ComboEjeRiel.SelectedIndex != 1);
        }

        /// <summary>Rehace la base principal con las medidas actuales, descartando lo armado.</summary>
        private void OnRehacerBase(object? sender, RoutedEventArgs e)
        {
            MainViewModel? vm = Vm;
            if (vm?.Canvas2D == null || !_preview3D.ModoManual)
            {
                return;
            }

            GuardarEnHistorial();
            AplicarMedidas();
            ManualAssembly armado = CrearBaseDesdeUI();
            if (_preview3D.IniciarArmadoManual(vm.Canvas2D, armado))
            {
                vm.ArmadoManual = armado;
                ActualizarEstadoLosa();
                ActualizarAyudaNavegacion();
                Registrar("Base principal rehecha y centrada.");
            }
        }

        /// <summary>
        /// LOSA LISTA: bloquea la estructura tal como quedó. Si ya estaba bloqueada, el mismo botón la
        /// reabre para poder seguir armando.
        /// </summary>
        private void OnLosaLista(object? sender, RoutedEventArgs e)
        {
            if (!_preview3D.ModoManual)
            {
                return;
            }

            GuardarEnHistorial();

            if (_preview3D.LosaLista)
            {
                _preview3D.ReabrirLosa();
                Registrar("Losa reabierta: puede seguir colocando piezas.");
            }
            else if (!_preview3D.CerrarLosa())
            {
                _historial.RemoveAt(_historial.Count - 1);
                Registrar("Arme al menos la base principal antes de cerrar la losa.");
                return;
            }
            else
            {
                Registrar("Losa lista: la estructura quedó bloqueada. Ya puede colocar los tableros.");
            }

            ActualizarEstadoLosa();
            ActualizarAyudaNavegacion();
        }

        private void OnColocarTableros(object? sender, RoutedEventArgs e)
        {
            if (!_preview3D.ModoManual || !_preview3D.LosaLista)
            {
                return;
            }

            GuardarEnHistorial();
            int n = _preview3D.ColocarTableros();
            Registrar(n > 0
                ? $"Colocados {n} tableros del catálogo sobre la estructura."
                : "No se pudo colocar ningún tablero: revise el armado.");
            ActualizarEstadoLosa();
            ActualizarAyudaNavegacion();
        }

        /// <summary>Ajusta los botones según si la losa está abierta, lista o ya tablerada.</summary>
        private void ActualizarEstadoLosa()
        {
            bool lista = _preview3D.LosaLista;

            if (BtnLosaLista != null)
            {
                BtnLosaLista.Content = lista ? "Reabrir losa" : "Losa lista";
            }
            if (BtnManualColocarTableros != null)
            {
                BtnManualColocarTableros.IsEnabled = lista;
            }
            if (BtnManualMover != null)
            {
                BtnManualMover.IsEnabled = !lista;
            }
        }

        // ── Registro / deshacer ──────────────────────────────────────────────
        private void GuardarEnHistorial()
        {
            ManualAssembly? armado = _preview3D.Armado;
            if (armado == null)
            {
                return;
            }
            _historial.Add(armado.Clonar());
            if (_historial.Count > 200)
            {
                _historial.RemoveAt(0);
            }
        }

        private void Deshacer()
        {
            MainViewModel? vm = Vm;
            if (vm?.Canvas2D == null || _historial.Count == 0)
            {
                Registrar("No hay pasos que deshacer.");
                return;
            }

            ManualAssembly previo = _historial[^1];
            _historial.RemoveAt(_historial.Count - 1);

            // Se restaura el armado anterior tal cual (medidas, piezas y posición): NO se recentra.
            _preview3D.IniciarArmadoManual(vm.Canvas2D, previo, centrar: false);
            vm.ArmadoManual = previo;
            ActualizarEstadoLosa();
            ActualizarAyudaNavegacion();
            Registrar($"Paso deshecho. Quedan {_historial.Count} en el registro.");
        }

        private void Registrar(string texto)
        {
            if (RegistroTexto != null)
            {
                RegistroTexto.Text = texto;
            }
        }

        // ── Cotas vivas del armado ───────────────────────────────────────────
        private void ActualizarCotas()
        {
            if (CotasCard == null || CotasInfo == null)
            {
                return;
            }

            ManualAssembly? armado = _preview3D.Armado;
            if (!_preview3D.ModoManual || armado == null)
            {
                CotasCard.IsVisible = false;
                return;
            }

            // REGISTRO: siempre tiene que hablar de lo que está EN PANTALLA ahora mismo.
            //  • Si acaba de ocurrir una acción, se muestra su aviso — pero UNA sola vez: se consume aquí.
            //  • Si no hay ninguna nueva (un refresco, un arrastre, un centrado, un deshacer), se muestra el
            //    inventario VIVO del armado, leído de las piezas realmente colocadas.
            // Así ya no puede quedarse pegado un "Colocado: … separador 1,00 m" describiendo una extensión
            // que se deshizo, mientras el 3D enseña una base de 3,00 × 1,20 con sus dos separadores de 1,20.
            Registrar(_preview3D.ConsumirUltimaAccion() ?? armado.ResumenVivo());

            MedidasArmado m = _preview3D.Medidas;
            static string Cm(double metros) => $"{Math.Round(metros * 100)} cm";

            // Una sola línea: la franja va anclada arriba y no debe robarle alto al plano 3D.
            CotasInfo.Text =
                $"{m.Ancho.ToString("0.00", Es)} × {m.Largo.ToString("0.00", Es)} m  ·  " +
                $"Diagonal {m.Diagonal.ToString("0.00", Es)} m  ·  " +
                $"Izq {Cm(m.Izquierda)}  ·  Der {Cm(m.Derecha)}  ·  " +
                $"Inf {Cm(m.Inferior)}  ·  Sup {Cm(m.Superior)}  ·  " +
                $"{m.Puntales} puntal(es)  ·  {m.Piezas} pieza(s)";

            bool centradoX = Math.Abs(m.Izquierda - m.Derecha) < 0.02;
            bool centradoY = Math.Abs(m.Inferior - m.Superior) < 0.02;

            string? aviso = null;
            if (m.PuntalesSobreColumna > 0)
            {
                aviso = $"⚠ {m.PuntalesSobreColumna} puntal(es) caen sobre una columna: reubique el armado.";
            }
            else if (centradoX && centradoY)
            {
                aviso = "✓ Armado centrado en la losa.";
            }

            CotasAviso.Text = aviso ?? string.Empty;
            CotasAviso.IsVisible = aviso != null;
            CotasAviso.Foreground = m.PuntalesSobreColumna > 0
                ? Avalonia.Media.Brushes.OrangeRed
                : Avalonia.Media.Brushes.LimeGreen;

            CotasCard.IsVisible = true;
        }
    }
}
