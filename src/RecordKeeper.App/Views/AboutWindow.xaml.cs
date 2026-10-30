using System.Diagnostics;
using System.Windows;
using RecordKeeper.Core.Data;
using RecordKeeper.Core.Models;

namespace RecordKeeper.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        ProductLine.Text = $"{Brand.Product}  {Brand.Version}";
        PoweredByLine.Text = Brand.PoweredBy;
        DataFolderBox.Text = AppPaths.Root;

        CloseButton.Click += (_, _) => Close();
        OpenFolderButton.Click += (_, _) => OpenDataFolder();

        Loaded += (_, _) => CloseButton.Focus();
    }

    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.Root) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            App.Log("Opening the data folder from About failed", exception);
            AppServices.Current.Announcer.SayError(
                "The folder could not be opened. It is at " + AppPaths.Root);
        }
    }
}
