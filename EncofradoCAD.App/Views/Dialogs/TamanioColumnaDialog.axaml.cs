using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EncofradoCAD.App.Views.Dialogs
{
    /// <summary>Diálogo simple para capturar un tamaño de columna personalizado (ancho × largo en cm).</summary>
    public partial class TamanioColumnaDialog : Window
    {
        /// <summary>Ancho capturado en centímetros (válido sólo si el diálogo se aceptó).</summary>
        public double AnchoCm { get; private set; }

        /// <summary>Largo capturado en centímetros (válido sólo si el diálogo se aceptó).</summary>
        public double LargoCm { get; private set; }

        public TamanioColumnaDialog()
        {
            InitializeComponent();
        }

        public TamanioColumnaDialog(double anchoCm = 30, double largoCm = 30) : this()
        {
            txtAncho.Text = anchoCm.ToString("0.##", CultureInfo.CurrentCulture);
            txtLargo.Text = largoCm.ToString("0.##", CultureInfo.CurrentCulture);
            txtAncho.Focus();
            txtAncho.SelectAll();
        }

        private void BtnAceptar_Click(object? sender, RoutedEventArgs e)
        {
            if (!double.TryParse(txtAncho.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double ancho) || ancho <= 0 ||
                !double.TryParse(txtLargo.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double largo) || largo <= 0)
            {
                txtError.Text = "Ingrese un ancho y un largo válidos (números mayores a 0).";
                txtError.IsVisible = true;
                return;
            }

            if (ancho > 500 || largo > 500)
            {
                txtError.Text = "El tamaño no puede superar los 500 cm.";
                txtError.IsVisible = true;
                return;
            }

            AnchoCm = ancho;
            LargoCm = largo;
            Close(true);
        }

        private void BtnCancelar_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
