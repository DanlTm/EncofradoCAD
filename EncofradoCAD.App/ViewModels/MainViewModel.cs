using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EncofradoCAD.App.Controls;
using EncofradoCAD.App.Infrastructure;
using EncofradoCAD.App.Services;
using EncofradoCAD.Core.Enums;
using EncofradoCAD.Core.Layout;
using EncofradoCAD.Core.Models;

namespace EncofradoCAD.App.ViewModels
{
    /// <summary>Módulos de la interfaz gráfica (pestañas del ribbon superior estilo AutoCAD).</summary>
    public enum ModuloApp
    {
        Proyecto,
        Modelado2D,
        Modelado3D,
        Calculo
    }

    public partial class MainViewModel : ObservableObject
    {
        private DrawingCanvas? _canvas2D;

        public MainViewModel()
        {
            ProyectoActual = new Proyecto { Nombre = "Nuevo Proyecto Losa" };
            BOMVM = new BOMViewModel();
        }

        /// <summary>Lienzo 2D compartido; lo registra el módulo de Modelado 2D y lo consumen 3D y Cálculo.</summary>
        public DrawingCanvas? Canvas2D => _canvas2D;

        /// <summary>
        /// Armado MANUAL activo (lo publica el módulo 3D). Cuando no es null, la lista de materiales cuenta
        /// las piezas que el usuario colocó a mano en vez de las que resuelve el motor automático.
        /// </summary>
        public ManualAssembly? ArmadoManual { get; set; }

        public void RegistrarCanvas2D(DrawingCanvas canvas) => _canvas2D = canvas;

        private Window? Owner => _canvas2D != null ? TopLevel.GetTopLevel(_canvas2D) as Window : null;

        [ObservableProperty]
        private Proyecto _proyectoActual;

        [ObservableProperty]
        private BOMViewModel _bOMVM;

        // ── Navegación modular (pestañas del ribbon) ─────────────────────────
        [ObservableProperty]
        private ModuloApp _moduloActivo = ModuloApp.Modelado2D;

        public bool ProyectoVisible => ModuloActivo == ModuloApp.Proyecto;
        public bool Modelado2DVisible => ModuloActivo == ModuloApp.Modelado2D;
        public bool Modelado3DVisible => ModuloActivo == ModuloApp.Modelado3D;
        public bool CalculoVisible => ModuloActivo == ModuloApp.Calculo;

        partial void OnModuloActivoChanged(ModuloApp value)
        {
            OnPropertyChanged(nameof(ProyectoVisible));
            OnPropertyChanged(nameof(Modelado2DVisible));
            OnPropertyChanged(nameof(Modelado3DVisible));
            OnPropertyChanged(nameof(CalculoVisible));
        }

        [ObservableProperty]
        private string _direccionOptimaDescripcion = "—";

        /// <summary>Área de la losa y del encofrado (losa + 20 cm de voladizo por lado), en m².</summary>
        [ObservableProperty]
        private string _areaDescripcion = "—";

        // La navegación entre módulos NO pasa por comandos: las pestañas del ribbon son botones con
        // handler (MainWindow.OnTab*) que asignan ModuloActivo directamente. Aquí hubo cuatro
        // [RelayCommand] SwitchTo* que ningún binding invocaba nunca.

        [RelayCommand]
        private async Task Calcular()
        {
            if (_canvas2D == null)
            {
                await MessageService.ShowAsync(Owner, "CADStudio", "No se pudo acceder al lienzo 2D.");
                return;
            }

            List<(string Nombre, string Cantidad)> materiales;
            DireccionBase direccion;

            if (ArmadoManual != null)
            {
                // ARMADO MANUAL: el BOM cuenta EXACTAMENTE las piezas que el usuario colocó en el 3D.
                if (!LayoutEngineAdapter.TryGetPlano(_canvas2D, out var slabManual, out var columnasManual,
                        out _, out _, out _, out _, out var errorPlano))
                {
                    await MessageService.ShowAsync(Owner, "CADStudio", errorPlano ?? "No se pudo calcular.");
                    return;
                }

                materiales = LayoutEngine.BuildMaterialBill(ArmadoManual.Build(), slabManual, columnasManual);
                direccion = ArmadoManual.RielesHorizontales ? DireccionBase.Horizontal : DireccionBase.Vertical;

                DireccionOptimaDescripcion = direccion == DireccionBase.Horizontal
                    ? "Armado manual — rieles en el eje X"
                    : "Armado manual — rieles en el eje Y";
            }
            else
            {
                // Motor único (Core): la misma lógica exacta que dibuja el preview 3D, para que BOM y 3D coincidan.
                if (!LayoutEngineAdapter.TryComputeMaterialBill(_canvas2D, out materiales, out direccion, out var error))
                {
                    await MessageService.ShowAsync(Owner, "CADStudio", error ?? "No se pudo calcular.");
                    return;
                }

                DireccionOptimaDescripcion = direccion == DireccionBase.Horizontal
                    ? "Horizontal — rieles principales 3 m en el eje X (vano mayor)"
                    : "Vertical — rieles principales 3 m en el eje Y (vano mayor)";
            }

            ProyectoActual.DireccionOptima = direccion;

            // Área de la losa y del encofrado (losa + 20 cm de voladizo por lado).
            var slab = LayoutEngineAdapter.GetSlabPolygon(_canvas2D);
            if (slab.Count >= 3)
            {
                double areaLosa = GeometryUtil.PolygonArea(slab);
                double areaEncofrado = GeometryUtil.PolygonArea(GeometryUtil.OffsetPolygon(slab, 0.20));
                AreaDescripcion = $"Losa: {areaLosa:0.00} m²   ·   Encofrado (+20 cm/lado): {areaEncofrado:0.00} m²";
            }
            else
            {
                AreaDescripcion = "—";
            }

            BOMVM.ActualizarBOM(materiales);
        }
    }
}
