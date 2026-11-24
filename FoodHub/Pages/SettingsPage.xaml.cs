// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.
namespace FoodHub.Pages;
/// <summary>Controls persisted application appearance.</summary>
public sealed partial class SettingsPage : Page
{
    private bool _ready;
    /// <summary>Loads the saved theme without triggering a redundant save.</summary>
    public SettingsPage() { InitializeComponent(); ThemePicker.SelectedIndex = (int)App.Services.Appearance.Theme; _ready = true; }
    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        try { App.Services.Appearance.SetTheme((ElementTheme)ThemePicker.SelectedIndex); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        { Feedback.Message = "Could not save your appearance setting."; Feedback.IsOpen = true; }
    }
}
