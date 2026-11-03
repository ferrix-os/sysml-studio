using SysmlStudio.Diagrams;

namespace SysmlStudio.App.ViewModels;

/// <summary>One kind of diagram as the window offers it.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Label">What the switcher calls it: "Action".</param>
/// <param name="Group">The switcher's column it is listed in.</param>
/// <param name="Caption">What the title bar and the tab's tooltip call it: "Action flow".</param>
/// <param name="ShortcutId">The key its shortcut is saved under in the settings.</param>
/// <param name="ShortcutLabel">What the settings call its shortcut: "Action flow".</param>
/// <param name="DefaultGesture">Its default keys; empty for none, and it can still be given some in the settings.</param>
public sealed record DiagramKindEntry(DiagramKind Kind, string Label, string Group, string Caption, string ShortcutId, string ShortcutLabel, string DefaultGesture);

/// <summary>
/// Every kind of diagram the window knows, in the switcher's order. A new kind
/// is one line here: the switcher, the title bar and the shortcuts all read
/// this table, and the kind needs no keys of its own unless it should have some.
/// </summary>
public static class DiagramKindCatalog
{
    public const string Structure = "Structure";
    public const string Behavior = "Behavior";
    public const string Interaction = "Interaction";
    public const string Sysml = "SysML";

    /// <summary>The switcher's columns, left to right.</summary>
    public static IReadOnlyList<string> Groups { get; } = [Structure, Behavior, Interaction, Sysml];

    public static IReadOnlyList<DiagramKindEntry> All { get; } =
    [
        // Structure
        Uml(DiagramKind.Package, "Package", Structure),
        Uml(DiagramKind.Class, "Class", Structure),
        Uml(DiagramKind.ObjectDiagram, "Object", Structure),
        Uml(DiagramKind.Component, "Component", Structure),
        Uml(DiagramKind.CompositeStructure, "Composite Structure", Structure),
        Uml(DiagramKind.Deployment, "Deployment", Structure),
        Uml(DiagramKind.Profile, "Profile", Structure),

        // Behavior
        Uml(DiagramKind.UseCase, "Use Case", Behavior),
        Uml(DiagramKind.Activity, "Activity", Behavior),
        new(DiagramKind.StateMachine, "State", Behavior, "State machine", "kindState", "State machine", "Alt+D5"),

        // Interaction
        Uml(DiagramKind.Sequence, "Sequence", Interaction),
        Uml(DiagramKind.Communication, "Communication", Interaction),
        Uml(DiagramKind.InteractionOverview, "Interaction Overview", Interaction),
        Uml(DiagramKind.Timing, "Timing", Interaction),

        // SysML
        new(DiagramKind.Definition, "Definition", Sysml, "Definition", "kindDefinition", "Definition diagram", "Alt+D1"),
        new(DiagramKind.Interconnection, "Interconnection", Sysml, "Interconnection", "kindInterconnection", "Interconnection diagram", "Alt+D2"),
        new(DiagramKind.Requirements, "Requirements", Sysml, "Requirements", "kindRequirements", "Requirements diagram", "Alt+D3"),
        new(DiagramKind.ActionFlow, "Action", Sysml, "Action flow", "kindAction", "Action flow", "Alt+D4"),
    ];

    public static DiagramKindEntry For(DiagramKind kind) => All.First(e => e.Kind == kind);

    /// <summary>A UML kind: called what UML calls it, with no keys until the user gives it some.</summary>
    private static DiagramKindEntry Uml(DiagramKind kind, string label, string group)
        => new(kind, label, group, label, "kind" + kind, label + " diagram", string.Empty);
}

/// <summary>One column of the switcher's list.</summary>
public sealed class DiagramKindGroup(string name, IReadOnlyList<DiagramKindOption> options)
{
    public string Name { get; } = name;
    public IReadOnlyList<DiagramKindOption> Options { get; } = options;
}
