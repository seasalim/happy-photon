using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

internal sealed class OneTimeNotice
{
    private string? _key;
    private string? _outcome;
    private bool _presented;

    internal string? Text { get; private set; }

    internal bool IsPending => Text != null && !_presented;

    internal void Offer(string? text, string key, string outcome)
    {
        if (IsPending || text == null) return;

        Text = text;
        _key = key;
        _outcome = outcome;
        _presented = false;
    }

    internal void ClearPresented()
    {
        if (_presented) Text = null;
    }

    internal async Task AcknowledgeAsync(CatalogService catalog, string? displayed)
    {
        if (!IsPending || displayed != Text) return;

        _presented = true;

        try
        {
            await catalog.SetAppSettingAsync(_key!, _outcome!);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Notice acknowledgement: {exception.Message}");
        }
    }
}
