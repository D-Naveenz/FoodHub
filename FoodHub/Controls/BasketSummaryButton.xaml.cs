using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace FoodHub.Controls;

/// <summary>Native basket action composed from the supplied green artwork, icon and live two-line summary.</summary>
public sealed partial class BasketSummaryButton : UserControl
{
    private readonly AccessibilitySettings _accessibility = new();
    private readonly UISettings _uiSettings = new();
    private bool _sizing;
    private int _quantity;
    private string _total = "LKR 0.00";

    /// <summary>Raised when the native button is activated with pointer, keyboard or assistive technology.</summary>
    public event RoutedEventHandler? Click;

    /// <summary>Creates the artwork presentation and observes high-contrast preferences only while loaded.</summary>
    public BasketSummaryButton()
    {
        InitializeComponent();
        // AccessibilitySettings.HighContrastChanged requires a UWP event context in unpackaged desktop apps.
        // UISettings color notifications work in WinUI desktop windows and also cover contrast-theme changes.
        Loaded += (_, _) => { _uiSettings.ColorValuesChanged += ColorsChanged; ApplyAppearance(); };
        Unloaded += (_, _) => _uiSettings.ColorValuesChanged -= ColorsChanged;
    }

    /// <summary>Updates visible labels and the accessible action name without replacing the button's content tree.</summary>
    /// <param name="quantity">Total confirmed units in the basket.</param>
    /// <param name="formattedTotal">The existing store-currency total, including the applicable delivery fee.</param>
    public void SetSummary(int quantity, string formattedTotal)
    {
        _quantity = quantity; _total = formattedTotal;
        ItemCount.Text = quantity == 1 ? "1 item" : $"{quantity:N0} items";
        TotalPrice.Text = formattedTotal;
        AutomationProperties.SetName(ActionButton, $"View basket, {ItemCount.Text}, total {formattedTotal}");
        ToolTipService.SetToolTip(ActionButton, $"View basket · {ItemCount.Text} · {formattedTotal}");
        UpdateSize();
    }

    private void ColorsChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (IsLoaded) ApplyAppearance();
    });

    /// <summary>Uses the standard system button and inherited text colors when decorative artwork would conflict with high contrast.</summary>
    private void ApplyAppearance()
    {
        var highContrast = _accessibility.HighContrast;
        ActionButton.Style = highContrast ? null : (Style)Resources["ArtworkButtonStyle"];
        BaseImage.Visibility = BasketImage.Visibility = highContrast ? Visibility.Collapsed : Visibility.Visible;
        if (highContrast)
        {
            ItemCount.ClearValue(TextBlock.ForegroundProperty); TotalPrice.ClearValue(TextBlock.ForegroundProperty);
        }
        else
        {
            var ink = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 16, 16));
            ItemCount.Foreground = TotalPrice.Foreground = ink;
        }
        SetSummary(_quantity, _total);
    }

    private void Caption_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSize();

    /// <summary>Grows proportionally for long totals/text scaling, preserving the circle and leaving both labels untrimmed.</summary>
    private void UpdateSize()
    {
        if (_sizing || Artwork is null || ItemCount is null || TotalPrice is null) return;
        _sizing = true;
        try
        {
            if (_accessibility.HighContrast)
            {
                Artwork.Width = Artwork.Height = double.NaN; Caption.Margin = new Thickness(4);
                return;
            }
            // Measure the text independently from the existing bar width; never shrink or ellipsize a price to fit.
            ItemCount.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            TotalPrice.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var captionWidth = Math.Max(ItemCount.DesiredSize.Width, TotalPrice.DesiredSize.Width);
            var captionHeight = ItemCount.DesiredSize.Height + TotalPrice.DesiredSize.Height;
            const double aspect = 1176.0 / 401.0;
            var width = Math.Ceiling(Math.Max(212, Math.Max(captionWidth / 0.58, captionHeight / 0.52 * aspect)));
            var height = width / aspect;
            Artwork.Width = width; Artwork.Height = height;
            Caption.Margin = new Thickness(width * 0.37, 0, width * 0.05, 0);
            BasketImage.Width = BasketImage.Height = height * 0.5;
            BasketImage.Margin = new Thickness(width * 200.0 / 1176.0 - BasketImage.Width / 2, height * 0.25, 0, 0);
        }
        finally { _sizing = false; }
    }

    private void Action_Click(object sender, RoutedEventArgs e) => Click?.Invoke(this, e);
}
