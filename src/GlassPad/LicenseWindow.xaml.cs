using System.IO;
using System.Windows;

namespace GlassPad;

public partial class LicenseWindow : Window
{
    public LicenseWindow()
    {
        InitializeComponent();
        MaxWidth = SystemParameters.WorkArea.Width;
        MaxHeight = SystemParameters.WorkArea.Height;
        GlassPadLicense.Text = ReadLicense("GlassPad");
        HIDMaestroLicense.Text = ReadLicense("HIDMaestro");
        ThirdPartyNotices.Text = ReadLicense("ThirdParty");
    }

    private static string ReadLicense(string name)
    {
        using var stream = typeof(LicenseWindow).Assembly
            .GetManifestResourceStream($"GlassPad.Licenses.{name}")
            ?? throw new InvalidOperationException($"Missing embedded license: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
