using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using EncofradoCAD.App.Controls;
using EncofradoCAD.App.Infrastructure;
using EncofradoCAD.App.Services;
using EncofradoCAD.App.ViewModels;
using EncofradoCAD.App.Views.Dialogs;

namespace EncofradoCAD.App.Views.Modules
{
    /// <summary>Módulo de Modelado 2D: aloja el lienzo de dibujo y su toolbar propia.</summary>
    public partial class Modelado2DView : UserControl
    {
        private readonly DrawingCanvas _canvas = new DrawingCanvas();
        private bool _inicializado;

        // Índice de la opción "Personalizada…" en ComboColumna y control de reversión al cancelar.
        private const int IndicePersonalizada = 6;
        private int _indiceColumnaPrevio = 2;
        private bool _actualizandoCombo;

        public Modelado2DView()
        {
            InitializeComponent();

            // El lienzo (transformado por zoom/pan) y una capa de regla superpuesta que NO se transforma,
            // de modo que los números de metros quedan fijos y sólo la figura interior se acerca/aleja.
            var reglaOverlay = new Canvas { IsHitTestVisible = false, Background = null };
            var contenedor = new Grid();
            contenedor.Children.Add(_canvas);
            contenedor.Children.Add(reglaOverlay);
            CanvasHost.Child = contenedor;

            _canvas.ViewportManager.DibujarCuadricula(_canvas.PixelesPorMetro);
            _canvas.ViewportManager.SetReglaOverlay(reglaOverlay);
            reglaOverlay.SizeChanged += (_, _) => _canvas.ViewportManager.ActualizarReglas();

            _canvas.ViewportManager.EscalaCambiada += ActualizarZoom;
            ActualizarZoom(1.0);

            AttachedToVisualTree += (_, _) => RegistrarEnViewModel();
            DataContextChanged += (_, _) => RegistrarEnViewModel();
        }

        /// <summary>Lienzo compartido con los módulos 3D y Cálculo.</summary>
        public DrawingCanvas Canvas => _canvas;

        private void RegistrarEnViewModel()
        {
            if (_inicializado)
            {
                return;
            }
            if (DataContext is MainViewModel vm)
            {
                vm.RegistrarCanvas2D(_canvas);
                _inicializado = true;
                _canvas.Focus();
            }
        }

        // ── Handlers de la toolbar 2D ────────────────────────────────────────
        private void OnLimpiar(object? sender, RoutedEventArgs e) => _canvas.Limpiar();

        private async void OnAgregarColumna(object? sender, RoutedEventArgs e)
            => await _canvas.AgregarColumnaDesdeBoton();

        private void OnToggleMedidasCurva(object? sender, RoutedEventArgs e)
            => _canvas.AlternarMedidasCurva(ToggleMedidasCurva.IsChecked == true);

        // ── Exportar el modelo 2D (losa + columnas + medidas + voladizo) a PNG ───────────────────────
        private async void OnExportarPlano(object? sender, RoutedEventArgs e)
        {
            var ventana = TopLevel.GetTopLevel(this) as Window;
            TopLevel? top = TopLevel.GetTopLevel(this);
            if (top == null)
            {
                return;
            }

            IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Exportar modelo 2D (PNG)",
                SuggestedFileName = "plano-2d.png",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Imagen PNG") { Patterns = new[] { "*.png" } }
                }
            });
            if (file == null)
            {
                return;
            }

            try
            {
                CanvasImageExporter.ExportPng(_canvas, file.Path.LocalPath);
                await MessageService.ShowAsync(ventana, "CADStudio", "Modelo 2D exportado a PNG.");
            }
            catch (Exception ex)
            {
                await MessageService.ShowAsync(ventana, "CADStudio", "No se pudo exportar: " + ex.Message);
            }
        }

        // ── Convertir imagen de plano en losa 2D ─────────────────────────────
        private async void OnConvertirImagen(object? sender, RoutedEventArgs e)
        {
            var ventana = TopLevel.GetTopLevel(this) as Window;
            TopLevel? top = TopLevel.GetTopLevel(this);
            if (top == null)
            {
                return;
            }

            IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Convertir imagen de plano a losa",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Imágenes")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" }
                    }
                }
            });
            if (files.Count == 0)
            {
                return;
            }

            Bitmap bitmap;
            try
            {
                using var stream = await files[0].OpenReadAsync();
                bitmap = new Bitmap(stream);
            }
            catch (Exception ex)
            {
                await MessageService.ShowAsync(ventana, "CADStudio", "No se pudo abrir la imagen: " + ex.Message);
                return;
            }

            if (!PlanImageVectorizer.TryVectorizar(bitmap, out List<Point> poligono, out string? error))
            {
                await MessageService.ShowAsync(ventana, "CADStudio", error ?? "No se pudo procesar la imagen.");
                return;
            }

            double minX = poligono.Min(p => p.X);
            double maxX = poligono.Max(p => p.X);
            double minY = poligono.Min(p => p.Y);
            double anchoPx = maxX - minX;
            if (anchoPx < 1)
            {
                await MessageService.ShowAsync(ventana, "CADStudio", "El contorno detectado no es válido.");
                return;
            }

            double? anchoRealM = await InputService.AskNumberAsync(
                ventana,
                "Escala del plano",
                "¿Cuánto mide el ANCHO total del plano en metros? (para escalar el dibujo a la medida real)",
                "6.98");
            if (anchoRealM == null || anchoRealM <= 0)
            {
                return; // cancelado
            }

            double metrosPorPx = anchoRealM.Value / anchoPx;
            double ppm = _canvas.PixelesPorMetro;
            const double margen = 60;

            List<Point> puntos = poligono
                .Select(p => new Point(
                    ((p.X - minX) * metrosPorPx * ppm) + margen,
                    ((p.Y - minY) * metrosPorPx * ppm) + margen))
                .ToList();

            _canvas.CargarPoligonoLosa(puntos);
            await MessageService.ShowAsync(ventana, "CADStudio",
                "Plano convertido a losa 2D. Ya puedes verlo en 3D y calcular los materiales.");
        }

        private void OnTrazoChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ComboTrazo == null)
            {
                return;
            }
            _canvas.PolygonManager.ModoTrazoActual = ComboTrazo.SelectedIndex switch
            {
                1 => TipoSegmento.MediaLuna,
                2 => TipoSegmento.Ovalado,
                _ => TipoSegmento.Recto
            };
        }

        private async void OnColumnaChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ComboColumna == null || _actualizandoCombo)
            {
                return;
            }

            if (ComboColumna.SelectedIndex == IndicePersonalizada)
            {
                await ElegirTamanioPersonalizado();
                return;
            }

            (double anchoCm, double largoCm) = ComboColumna.SelectedIndex switch
            {
                0 => (20d, 20d),
                1 => (25d, 25d),
                2 => (30d, 30d),
                3 => (35d, 35d),
                4 => (40d, 40d),
                5 => (45d, 45d),
                _ => (30d, 30d)
            };
            _canvas.ColumnaAnchoCmActual = anchoCm;
            _canvas.ColumnaLargoCmActual = largoCm;
            _indiceColumnaPrevio = ComboColumna.SelectedIndex;
            RestaurarTextoPersonalizada();
        }

        /// <summary>Abre el diálogo de tamaño personalizado y aplica el ancho/largo elegido.</summary>
        private async Task ElegirTamanioPersonalizado()
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            var dialog = new TamanioColumnaDialog(_canvas.ColumnaAnchoCmActual, _canvas.ColumnaLargoCmActual);

            bool aceptado = owner != null && await dialog.ShowDialog<bool>(owner);

            if (aceptado)
            {
                _canvas.ColumnaAnchoCmActual = dialog.AnchoCm;
                _canvas.ColumnaLargoCmActual = dialog.LargoCm;
                _indiceColumnaPrevio = IndicePersonalizada;

                if (ComboColumna.Items[IndicePersonalizada] is ComboBoxItem item)
                {
                    item.Content = $"Personalizada ({dialog.AnchoCm:0.##}×{dialog.LargoCm:0.##} cm)";
                }
            }
            else
            {
                // Cancelado: revertir a la selección anterior sin re-disparar el diálogo.
                _actualizandoCombo = true;
                ComboColumna.SelectedIndex = _indiceColumnaPrevio;
                _actualizandoCombo = false;
            }
        }

        /// <summary>Restaura la etiqueta de la opción personalizada cuando se elige un preajuste.</summary>
        private void RestaurarTextoPersonalizada()
        {
            if (ComboColumna.Items[IndicePersonalizada] is ComboBoxItem item)
            {
                item.Content = "Personalizada…";
            }
        }

        // ── Zoom ─────────────────────────────────────────────────────────────
        private void ActualizarZoom(double escala)
        {
            if (PbZoom != null)
            {
                PbZoom.Value = escala;
            }
            if (TxtZoom != null)
            {
                TxtZoom.Text = $"{escala * 100:0}%";
            }
        }
    }
}
