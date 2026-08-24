using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private bool _isFullScreenSelectionRestricted;
    private bool _suppressSelectionPreviewLoad;

    public bool IsFullScreenSelectionRestricted =>
        _isFullScreenSelectionRestricted;

    public string FullScreenSelectionBadgeText
    {
        get
        {
            if (!_isFullScreenSelectionRestricted)
            {
                return string.Empty;
            }

            var members = GetFullScreenSelectionMembers();
            var position = members.IndexOf(SelectedImage!);
            return position >= 0
                ? $"SELECTION · {position + 1} / {members.Count}"
                : string.Empty;
        }
    }

    private void ArmFullScreenSelection()
    {
        var members = GetFullScreenSelectionMembers();
        SetFullScreenSelectionRestricted(members.Count >= 2);
        if (_isFullScreenSelectionRestricted)
        {
            AnchorFullScreenSelection(members[0], suppressPreviewLoad: true);
        }
    }

    private void ReconcileFullScreenSelection()
    {
        if (!_isFullScreenSelectionRestricted)
        {
            return;
        }

        var members = GetFullScreenSelectionMembers();
        if (members.Count < 2)
        {
            SetFullScreenSelectionRestricted(false);
            return;
        }

        ReanchorFullScreenSelection(members);
        NotifyFullScreenSelectionBadgeChanged();
    }

    private void ReleaseFullScreenSelection() =>
        SetFullScreenSelectionRestricted(false);

    private bool TryMoveWithinFullScreenSelection(int offset)
    {
        if (!_isFullScreenSelectionRestricted)
        {
            return false;
        }

        var members = GetFullScreenSelectionMembers();
        var currentIndex = members.IndexOf(SelectedImage!);
        if (members.Count < 2 || currentIndex < 0)
        {
            ReconcileFullScreenSelection();
            if (!_isFullScreenSelectionRestricted)
            {
                return false;
            }

            members = GetFullScreenSelectionMembers();
            currentIndex = members.IndexOf(SelectedImage!);
        }

        var destination = Math.Clamp(
            currentIndex + offset,
            0,
            members.Count - 1);
        SelectedImage = members[destination];
        return true;
    }

    private bool TrySelectFullScreenSelectionBoundary(bool last)
    {
        if (!_isFullScreenSelectionRestricted)
        {
            return false;
        }

        var members = GetFullScreenSelectionMembers();
        if (members.Count < 2)
        {
            ReconcileFullScreenSelection();
            return false;
        }

        SelectedImage = last ? members[^1] : members[0];
        return true;
    }

    private List<ImageFile> GetFullScreenSelectionMembers() =>
        Browse.VisibleImages.Where(image => image.IsSelected).ToList();

    private bool ReanchorFullScreenSelection(
        IReadOnlyList<ImageFile> members,
        bool suppressPreviewLoad = false)
    {
        if (SelectedImage != null && members.Contains(SelectedImage))
        {
            return false;
        }

        var anchor = FindNearestFullScreenSelectionMember(members);
        if (anchor == null)
        {
            return false;
        }

        return AnchorFullScreenSelection(anchor, suppressPreviewLoad);
    }

    private bool AnchorFullScreenSelection(
        ImageFile anchor,
        bool suppressPreviewLoad = false)
    {
        if (ReferenceEquals(SelectedImage, anchor))
        {
            return false;
        }

        _suppressSelectionPreviewLoad = suppressPreviewLoad;
        try
        {
            SelectedImage = anchor;
        }
        finally
        {
            _suppressSelectionPreviewLoad = false;
        }
        return true;
    }

    private ImageFile? FindNearestFullScreenSelectionMember(
        IReadOnlyList<ImageFile> members)
    {
        if (members.Count == 0)
        {
            return null;
        }

        var activeIndex = SelectedImage == null
            ? -1
            : Browse.VisibleImages.IndexOf(SelectedImage);
        if (activeIndex < 0)
        {
            return members[0];
        }

        var nearest = members[0];
        var nearestIndex = Browse.VisibleImages.IndexOf(nearest);
        var nearestDistance = Math.Abs(nearestIndex - activeIndex);
        foreach (var member in members.Skip(1))
        {
            var memberIndex = Browse.VisibleImages.IndexOf(member);
            var distance = Math.Abs(memberIndex - activeIndex);
            var winsForwardTie = distance == nearestDistance &&
                                 nearestIndex < activeIndex &&
                                 memberIndex > activeIndex;
            if (distance < nearestDistance || winsForwardTie)
            {
                nearest = member;
                nearestIndex = memberIndex;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private void SetFullScreenSelectionRestricted(bool value)
    {
        if (_isFullScreenSelectionRestricted == value)
        {
            NotifyFullScreenSelectionBadgeChanged();
            return;
        }

        _isFullScreenSelectionRestricted = value;
        OnPropertyChanged(nameof(IsFullScreenSelectionRestricted));
        NotifyFullScreenSelectionBadgeChanged();
    }

    private void NotifyFullScreenSelectionBadgeChanged() =>
        OnPropertyChanged(nameof(FullScreenSelectionBadgeText));
}
