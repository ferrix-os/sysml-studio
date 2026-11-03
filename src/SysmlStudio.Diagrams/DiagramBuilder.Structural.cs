using SysmlStudio.Model;

namespace SysmlStudio.Diagrams;

/// <summary>The seven structural diagrams of UML's taxonomy, each a view of SysML v2 elements drawn in that diagram's notation.</summary>
public static partial class DiagramBuilder
{
    /// <summary>The structural UML diagrams that have something to show of <paramref name="element"/>.</summary>
    private static IEnumerable<DiagramKind> StructuralKindsFor(Element element)
    {
        _ = element;
        return [];
    }
}
