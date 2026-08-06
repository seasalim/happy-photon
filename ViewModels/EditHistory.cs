using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

/// <summary>
/// Per-image, session-scoped undo/redo stacks of color/tonal edit states.
/// Callers pass snapshots; this class never mutates or clones them.
/// </summary>
public class EditHistory
{
    private readonly Stack<EditSettings> _undoStack = new();
    private readonly Stack<EditSettings> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Records the state that precedes a new user edit. By default dedups
    /// against the top entry; pass dedup: false to always push — required for
    /// paste, whose pre-paste state may differ from the stack top only by
    /// curve, which the dedup comparison ignores. A new edit always
    /// invalidates the redo branch, even when the push dedups.
    /// </summary>
    public void PushEdit(EditSettings currentState, bool dedup = true)
    {
        if (!dedup || _undoStack.Count == 0 ||
            !currentState.EqualsIgnoringRotation(_undoStack.Peek()))
        {
            _undoStack.Push(currentState);
        }
        _redoStack.Clear();
    }

    /// <summary>Pops the previous state; currentState becomes redoable. Null when nothing to undo.</summary>
    public EditSettings? Undo(EditSettings currentState)
    {
        if (_undoStack.Count == 0) return null;
        _redoStack.Push(currentState);
        return _undoStack.Pop();
    }

    /// <summary>Pops the next state; currentState becomes undoable. Null when nothing to redo.</summary>
    public EditSettings? Redo(EditSettings currentState)
    {
        if (_redoStack.Count == 0) return null;
        _undoStack.Push(currentState);
        return _redoStack.Pop();
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
