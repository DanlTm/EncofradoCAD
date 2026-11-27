using Avalonia.Controls;
using Avalonia.Interactivity;
using EncofradoCAD.App.ViewModels;

namespace EncofradoCAD.App.Views
{
    /// <summary>
    /// Shell de la aplicación con ribbon superior estilo AutoCAD: una tira de pestañas
    /// (Proyecto, Modelado 2D, Modelado 3D, Cálculo) que revela el ribbon y el área de
    /// trabajo del módulo activo.
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Opened += (_, _) => ActualizarPestanaActiva();
        }

        private MainViewModel? Vm => DataContext as MainViewModel;

        // ── Pestañas del ribbon ──────────────────────────────────────────────
        private void OnTabProyecto(object? sender, RoutedEventArgs e)
        {
            if (Vm != null)
            {
                Vm.ModuloActivo = ModuloApp.Proyecto;
            }
            ActualizarPestanaActiva();
        }

        private void OnTab2D(object? sender, RoutedEventArgs e)
        {
            if (Vm != null)
            {
                Vm.ModuloActivo = ModuloApp.Modelado2D;
            }
            ActualizarPestanaActiva();
        }

        private void OnTab3D(object? sender, RoutedEventArgs e)
        {
            if (Vm != null)
            {
                Vm.ModuloActivo = ModuloApp.Modelado3D;
            }
            // Reconstruir la vista 3D con el estado actual del lienzo 2D.
            Vista3D.RefrescarDesdeCanvas();
            ActualizarPestanaActiva();
        }

        private void OnTabCalculo(object? sender, RoutedEventArgs e)
        {
            if (Vm != null)
            {
                Vm.ModuloActivo = ModuloApp.Calculo;
            }
            ActualizarPestanaActiva();
        }

        private void ActualizarPestanaActiva()
        {
            ModuloApp modulo = Vm?.ModuloActivo ?? ModuloApp.Modelado2D;
            SetActiva(TabProyecto, modulo == ModuloApp.Proyecto);
            SetActiva(TabModelado2D, modulo == ModuloApp.Modelado2D);
            SetActiva(TabModelado3D, modulo == ModuloApp.Modelado3D);
            SetActiva(TabCalculo, modulo == ModuloApp.Calculo);
        }

        private static void SetActiva(Button pestana, bool activa)
        {
            if (activa)
            {
                if (!pestana.Classes.Contains("active"))
                {
                    pestana.Classes.Add("active");
                }
            }
            else
            {
                pestana.Classes.Remove("active");
            }
        }
    }
}
