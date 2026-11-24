// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using FoodHub.Services;
using FoodHub.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.
namespace FoodHub.Pages;
/// <summary>Controls appearance and persistent, next-launch database configuration.</summary>
public sealed partial class SettingsPage : Page
{
    private bool _ready;
    /// <summary>Page-owned configuration commands and editable fields.</summary>
    public DatabaseSettingsViewModel ViewModel { get; } = App.GetService<ViewModelFactory>().CreateDatabaseSettings();
    /// <summary>Loads appearance without triggering a redundant save.</summary>
    public SettingsPage()
    {
        InitializeComponent();
        ThemePicker.SelectedIndex = (int)App.GetService<AppearanceSettings>().Theme;
        _ready = true;
        ViewModel.PropertyChanged += FormChanged;
        UpdateFeedback();
        Unloaded += (_, _) => { ViewModel.PropertyChanged -= FormChanged; ViewModel.Dispose(); };
    }
    /// <summary>Updates view-only enabled and feedback state.</summary>
    private void FormChanged(object? sender, PropertyChangedEventArgs e) => UpdateFeedback();
    /// <summary>Leaves cancellation available while disabling candidate edits.</summary>
    private void UpdateFeedback()
    {
        foreach (var control in DatabaseFields.Children.OfType<Control>()) control.IsEnabled = !ViewModel.IsBusy;
        DatabaseFeedback.IsOpen = !string.IsNullOrEmpty(ViewModel.Message);
    }
    /// <summary>Saves and applies the existing per-user appearance preference.</summary>
    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        try { App.GetService<AppearanceSettings>().SetTheme((ElementTheme)ThemePicker.SelectedIndex); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        { Feedback.Message = "Could not save your appearance setting."; Feedback.IsOpen = true; }
    }
}