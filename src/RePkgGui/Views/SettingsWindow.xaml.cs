using System.IO;
using System.Windows;
using Microsoft.Win32;
using RePkgGui.Models;
using RePkgGui.Services;

namespace RePkgGui.Views;

public partial class SettingsWindow
{
    private readonly AppSettings _working;

    public SettingsWindow()
    {
        InitializeComponent();

        _working = SettingsService.Instance.Current.Clone();

        OutputBox.Text = _working.OutputDirectory;
        ThemeSystem.IsChecked = _working.Theme == AppThemeMode.System;
        ThemeLight.IsChecked = _working.Theme == AppThemeMode.Light;
        ThemeDark.IsChecked = _working.Theme == AppThemeMode.Dark;

        OpenWhenDoneCheck.IsChecked = _working.OpenOutputWhenDone;
        OverwriteRadio.IsChecked = _working.Overwrite == OverwriteMode.Overwrite;
        SkipRadio.IsChecked = _working.Overwrite == OverwriteMode.Skip;

        NoTexConvertCheck.IsChecked = _working.NoTexConvert;
        KeepRawTexCheck.IsChecked = _working.KeepRawTex;
        MediaOnlyCheck.IsChecked = _working.MediaOnly;
        SkipExtensionsBox.Text = _working.SkipExtensions;
        FlatOutputCheck.IsChecked = _working.FlatOutput;

        SyncTexOptions();
        Loaded += (_, _) => BackdropHelper.EnsureAvailable(this);
    }

    public event EventHandler? SettingsSaved;

    /// <summary>选了“不转换 TEX”后，“保留原始 .tex”就没意义了，直接禁用。</summary>
    private void NoTexConvert_Changed(object sender, RoutedEventArgs e) => SyncTexOptions();

    private void SyncTexOptions()
    {
        if (KeepRawTexCheck == null)
            return;

        var noConvert = NoTexConvertCheck.IsChecked == true;
        KeepRawTexCheck.IsEnabled = !noConvert;

        if (noConvert)
            KeepRawTexCheck.IsChecked = false;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择默认输出目录",
            Multiselect = false
        };

        if (Directory.Exists(OutputBox.Text))
            dialog.InitialDirectory = OutputBox.Text;

        if (dialog.ShowDialog(this) == true)
            OutputBox.Text = dialog.FolderName;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var output = (OutputBox.Text ?? string.Empty).Trim().Trim('"');
        if (output.Length == 0)
            output = AppSettings.DefaultOutputDirectory;

        try
        {
            Directory.CreateDirectory(output);
        }
        catch (Exception ex)
        {
            ErrorBar.Message = $"目录不可用：{ex.Message}";
            ErrorBar.IsOpen = true;
            return;
        }

        _working.OutputDirectory = output;
        _working.Theme = ThemeDark.IsChecked == true
            ? AppThemeMode.Dark
            : ThemeLight.IsChecked == true
                ? AppThemeMode.Light
                : AppThemeMode.System;

        _working.OpenOutputWhenDone = OpenWhenDoneCheck.IsChecked == true;
        _working.Overwrite = SkipRadio.IsChecked == true ? OverwriteMode.Skip : OverwriteMode.Overwrite;

        _working.NoTexConvert = NoTexConvertCheck.IsChecked == true;
        _working.KeepRawTex = KeepRawTexCheck.IsChecked == true;
        _working.MediaOnly = MediaOnlyCheck.IsChecked == true;
        _working.SkipExtensions = (SkipExtensionsBox.Text ?? string.Empty).Trim();
        _working.FlatOutput = FlatOutputCheck.IsChecked == true;

        SettingsService.Instance.Save(_working);
        SettingsSaved?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
