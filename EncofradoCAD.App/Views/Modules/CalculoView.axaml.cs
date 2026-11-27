using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EncofradoCAD.App.Controls;
using EncofradoCAD.App.Infrastructure;
using EncofradoCAD.App.Services;
using EncofradoCAD.App.ViewModels;

namespace EncofradoCAD.App.Views.Modules
{
    /// <summary>Módulo de Cálculo: cómputo de BOM y exportaciones (PDF / Excel / PNG).</summary>
    public partial class CalculoView : UserControl
    {
        public CalculoView()
        {
            InitializeComponent();
        }

        private MainViewModel? Vm => DataContext as MainViewModel;
        private DrawingCanvas? Canvas => Vm?.Canvas2D;
        private Window? Ventana => TopLevel.GetTopLevel(this) as Window;

        // ── Exportaciones ────────────────────────────────────────────────────
        private async void OnExportPng(object? sender, RoutedEventArgs e)
        {
            DrawingCanvas? canvas = Canvas;
            if (canvas == null)
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", "No se pudo acceder al lienzo 2D.");
                return;
            }
            string? path = await PickSaveAsync("Exportar plano PNG", "Imagen PNG", "*.png", "plano.png");
            if (path == null)
            {
                return;
            }
            try
            {
                CanvasImageExporter.ExportPng(canvas, path);
                await MessageService.ShowAsync(Ventana, "CADStudio", "Plano exportado a PNG.");
            }
            catch (Exception ex)
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", "No se pudo exportar el PNG: " + ex.Message);
            }
        }

        private async void OnExportCsv(object? sender, RoutedEventArgs e)
        {
            List<(string, string)> rows = ObtenerBom();
            if (rows.Count == 0)
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", "Primero pulsa \"Calcular materiales\".");
                return;
            }
            string? path = await PickSaveAsync("Exportar BOM (Excel/CSV)", "CSV (Excel)", "*.csv", "materiales.csv");
            if (path == null)
            {
                return;
            }
            try
            {
                BomExporter.ExportCsv("Lista de materiales para la losa", rows, path);
                await MessageService.ShowAsync(Ventana, "CADStudio", "BOM exportado (CSV, se abre en Excel).");
            }
            catch (Exception ex)
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", "No se pudo exportar: " + ex.Message);
            }
        }

        private async void OnExportPdf(object? sender, RoutedEventArgs e)
        {
            List<(string, string)> rows = ObtenerBom();
            if (rows.Count == 0)
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", "Primero pulsa \"Calcular materiales\".");
                return;
            }
            string? path = await PickSaveAsync("Exportar BOM (PDF)", "PDF", "*.pdf", "materiales.pdf");
            if (path == null)
            {
                return;
            }
            try
            {
                BomExporter.ExportPdf("Lista de materiales para la losa - CADStudio", rows, path);
                await MessageService.ShowAsync(Ventana, "CADStudio", "BOM exportado a PDF.");
            }
            catch (Exception ex)
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", "No se pudo exportar el PDF: " + ex.Message);
            }
        }

        private List<(string, string)> ObtenerBom()
        {
            var list = new List<(string, string)>();
            if (Vm?.BOMVM?.ListaMaterialesUI != null)
            {
                foreach (MaterialItem m in Vm.BOMVM.ListaMaterialesUI)
                {
                    list.Add((m.Cantidad, m.Nombre));
                }
            }
            return list;
        }

        // ── Selector de archivo ──────────────────────────────────────────────
        private async Task<string?> PickSaveAsync(string titulo, string tipoNombre, string patron, string sugerido)
        {
            TopLevel? top = TopLevel.GetTopLevel(this);
            if (top == null)
            {
                return null;
            }
            IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = titulo,
                SuggestedFileName = sugerido,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(tipoNombre) { Patterns = new[] { patron } }
                }
            });
            return file?.Path.LocalPath;
        }
    }
}
