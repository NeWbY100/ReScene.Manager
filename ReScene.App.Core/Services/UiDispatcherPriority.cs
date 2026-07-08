namespace ReScene.App.Core.Services;

/// <summary>Framework-neutral UI-dispatch priority; each head maps it to its platform priority.</summary>
public enum UiDispatcherPriority
{
    /// <summary>Normal dispatch priority (the platform's default).</summary>
    Normal,

    /// <summary>Lower-than-normal priority, used for work that should yield to pending UI input/render passes.</summary>
    Background,
}
