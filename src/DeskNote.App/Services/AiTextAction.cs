namespace DeskNote.App.Services;

/// <summary>
/// The AI actions that propose replacement text for a note.
/// </summary>
/// <remarks>
/// Only the actions whose result is "this text, rewritten" live here. 할 일 추출 and 자동 태그
/// produce structured proposals that need their own confirmation step, and 메모 Q&amp;A answers
/// rather than edits — none of them belong on the same 적용 button.
/// </remarks>
public enum AiTextAction
{
    Summarize,
    Organize,
    Rewrite,
}

/// <summary>
/// The AI actions that propose a list of separate items rather than replacement text.
/// </summary>
/// <remarks>
/// Each item stands on its own — the model can be right about two tasks and wrong about a third —
/// so these go through a confirmation list instead of the diff preview (report p11).
/// </remarks>
public enum AiListAction
{
    ExtractTasks,
    SuggestTags,
}
