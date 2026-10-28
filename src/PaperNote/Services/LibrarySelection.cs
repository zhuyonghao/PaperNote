namespace PaperNote.Services;

// Selection is always scoped to notes visible in the current search/filter.
public sealed class LibrarySelection
{
    private readonly HashSet<Guid> _selected = [];
    private List<Guid> _visible = [];
    public bool IsActive { get; private set; }
    public int Count => _selected.Count;
    public bool Contains(Guid id) => _selected.Contains(id);

    public void SetVisible(IEnumerable<Guid> ids)
    {
        _visible = ids.ToList();
        _selected.IntersectWith(_visible);
    }

    public void Begin() => IsActive = true;
    public void End() { IsActive = false; Clear(); }
    public void Clear() => _selected.Clear();
    public void SelectAll()
    {
        Begin();
        _selected.UnionWith(_visible);
    }

    public void Toggle(Guid id)
    {
        if (!_visible.Contains(id)) return;
        Begin();
        if (!_selected.Add(id)) _selected.Remove(id);
    }
}
