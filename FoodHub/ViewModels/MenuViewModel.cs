using CommunityToolkit.Mvvm.ComponentModel;
using FoodHub.Core.Services;
using FoodHub.Services;

namespace FoodHub.ViewModels;

/// <summary>Owns public menu loading and filtering without retaining page objects.</summary>
public sealed class MenuViewModel(MenuService menu, BasketState basket, MenuBrowseState browse) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private Catalogue? _catalogue;
    /// <summary>Lightweight persistent browsing preferences.</summary>
    public MenuBrowseState Browse => browse;
    /// <summary>Ordered category options.</summary>
    public IReadOnlyList<CategorySummary> Categories => _catalogue?.Categories ?? [];
    /// <summary>Filtered meals in the chosen presentation order.</summary>
    public IReadOnlyList<MealDetails> Meals => Filter(_catalogue?.Meals ?? [], browse);
    /// <summary>Distinguishes an unpopulated catalogue from a search with no matches.</summary>
    public string EmptyMessage => _catalogue is null ? "" : _catalogue.Meals.Count == 0
        ? "No meals are on the menu yet." : "No meals match these filters.";
    /// <summary>Safe feedback or empty text.</summary>
    public string Message { get; private set; } = "";
    /// <summary>Whether a catalogue operation is running.</summary>
    public bool IsBusy { get; private set; }
    /// <summary>Loads current public data and reconciles basket preview prices.</summary>
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true; OnPropertyChanged(string.Empty);
        try
        {
            var result = await menu.LoadAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            Message = result.Message;
            if (result.Succeeded) { _catalogue = result.Value; basket.Reconcile(_catalogue!); }
        }
        catch (OperationCanceledException) { }
        finally { IsBusy = false; OnPropertyChanged(string.Empty); }
    }
    /// <summary>Applies deterministic name search, category, availability and ordering.</summary>
    public static IReadOnlyList<MealDetails> Filter(IReadOnlyList<MealDetails> meals, MenuBrowseState state)
    {
        var selected = meals.Where(m => (state.Category is null || state.Category == m.CategoryId)
            && (!state.AvailableOnly || m.Available) && m.Name.Contains(state.Search.Trim(), StringComparison.CurrentCultureIgnoreCase));
        return (state.Sort switch
        {
            1 => selected.OrderBy(m => m.Name),
            2 => selected.OrderBy(m => m.Price).ThenBy(m => m.Name),
            3 => selected.OrderByDescending(m => m.Price).ThenBy(m => m.Name),
            _ => selected
        }).ToArray();
    }
    /// <summary>Refreshes the displayed filter projection without SQL work.</summary>
    public void FilterChanged() => OnPropertyChanged(nameof(Meals));
    /// <summary>Cancels stale public reads when the page leaves the frame.</summary>
    public void Dispose() { _lifetime.Cancel(); }
}
