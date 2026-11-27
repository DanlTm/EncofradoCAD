using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EncofradoCAD.App.Views.Dialogs
{
    public partial class MedidaSegmentoDialog : Window
    {
        public double ValorIngresado { get; private set; }

        public MedidaSegmentoDialog()
        {
            InitializeComponent();
        }

        public MedidaSegmentoDialog(double valorInicial) : this()
        {
            txtMedida.Text = valorInicial.ToString("0.00");
            txtMedida.Focus();
            txtMedida.SelectAll();
        }

        private void BtnAceptar_Click(object? sender, RoutedEventArgs e)
        {
            if (double.TryParse(txtMedida.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double valor) && valor > 0)
            {
                ValorIngresado = valor;
                Close(true);
            }
            else
            {
                txtError.Text = "Por favor ingrese un número válido mayor a 0.";
                txtError.IsVisible = true;
            }
        }

        private void BtnCancelar_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
