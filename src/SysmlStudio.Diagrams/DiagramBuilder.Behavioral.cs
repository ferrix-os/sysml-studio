using SysmlStudio.Model;

namespace SysmlStudio.Diagrams;

/// <summary>
/// <para>
/// The behavioural and interaction diagrams. Each is a view of SysML v2
/// elements drawn in a UML diagram's notation: the model holds no UML, and what
/// it does not say is not drawn. A lifeline is a part or an occurrence, a
/// message is a "message", an axis of time has the order the model states and
/// no numbers it does not.
/// </para>
/// </summary>
public static partial class DiagramBuilder
{
    private const string FrameTagSequence = "sd";
    private const string FrameTagTiming = "timing";

    /// <summary>The behavioural and interaction diagrams that have something to show of <paramref name="element"/>.</summary>
    private static IEnumerable<DiagramKind> BehavioralKindsFor(Element element)
    {
        if (IsPackage(element) ? element.Descendants().Any(IsUseCase) : HasUseCaseContent(element))
            yield return DiagramKind.UseCase;

        if (element.Kind is "action def" or "action" or "perform action" && element.Children.Any(c => IsAction(c) || IsControlNode(c)))
            yield return DiagramKind.Activity;

        if (Interaction.HoldsMessages(element) && Interaction.Read(element).Steps.Exists(s => s.To is not null))
        {
            yield return DiagramKind.Sequence;
            yield return DiagramKind.Communication;
        }

        if (element.Kind is "action def" or "action" or "perform action" or "occurrence def" or "occurrence" or "individual def" or "individual"
            && element.Children.Any(c => InteractionOf(c) is not null))
        {
            yield return DiagramKind.InteractionOverview;
        }

        if (TimeRows(element).Sum(r => r.Steps.Count) >= 2)
            yield return DiagramKind.Timing;
    }

    // ----- activity and interaction overview ----------------------------------

    private static bool IsControlNode(Element element) => element.Kind is "decide" or "merge" or "fork" or "join";

    private static bool IsFlowStep(Element element, DiagramKind kind)
        => IsAction(element) || IsControlNode(element)
            || element.Kind is "send action" or "accept action" or "terminate action"
            || (element.Kind == "perform action" && ReferenceOf(element) is not null)
            || (kind == DiagramKind.InteractionOverview && InteractionOf(element) is not null);

    /// <summary>The name a usage with none of its own is a usage of: "perform x".</summary>
    private static string? ReferenceOf(Element element)
        => element.Relations.FirstOrDefault(r => r.Kind == RelationKind.Specialization)?.TargetReference.Trim();

    /// <summary>
    /// The interaction a step stands for: itself if it holds messages, or what
    /// it is typed by or a usage of. A step with neither is an ordinary action.
    /// </summary>
    private static Element? InteractionOf(Element step)
    {
        if (step.IsDefinition || step.Kind is not ("action" or "perform action" or "occurrence" or "individual" or "ref" or "event occurrence"))
            return null;
        if (Interaction.HoldsMessages(step))
            return step;

        return step.Relations
            .Where(r => r.Kind is RelationKind.Typing or RelationKind.Specialization)
            .Select(r => r.Target)
            .FirstOrDefault(t => t is not null && Interaction.HoldsMessages(t));
    }

    private static DiagramNode StepNode(Element step, DiagramKind kind)
    {
        var label = step.Name is null ? ReferenceOf(step) ?? step.Payload ?? step.Kind : ActionLabel(step);
        return step.Kind switch
        {
            "decide" or "merge" => new DiagramNode(step, label) { Notation = NodeNotation.Decision },
            "fork" or "join" => new DiagramNode(step, label) { Notation = NodeNotation.Bar },
            "terminate action" => new DiagramNode(step, label) { Notation = NodeNotation.Final },
            "send action" => new DiagramNode(step, step.Payload ?? label) { Notation = NodeNotation.Send },
            "accept action" => new DiagramNode(step, step.Payload ?? label) { Notation = NodeNotation.Accept },
            _ when kind == DiagramKind.InteractionOverview && InteractionOf(step) is { } interaction
                => new DiagramNode(step, ReferenceEquals(interaction, step) ? label : interaction.DisplayName) { Notation = NodeNotation.InteractionUse },
            _ when kind != DiagramKind.ActionFlow => new DiagramNode(step, label) { Notation = NodeNotation.Rounded },
            _ => new DiagramNode(step, label, $"«{step.Kind}»"),
        };
    }

    private static DiagramEdge FlowEdge(DiagramNode from, DiagramNode to, Relation relation)
    {
        if (relation.Kind == RelationKind.Flow && relation.Source.Kind == "flow")
            return new DiagramEdge(from, to, RelationKind.Flow, relation.Source.Payload ?? relation.Source.Name);

        if (relation.Guard is { } guard)
            return new DiagramEdge(from, to, RelationKind.Succession, $"[{guard}]");

        return new DiagramEdge(from, to, RelationKind.Succession, relation.Source.Kind == "succession flow" ? relation.Source.Payload : null);
    }

    /// <summary>A fork or a join is drawn wide enough for every edge that meets it to meet it apart.</summary>
    private static void WidenBars(Diagram diagram)
    {
        foreach (var bar in diagram.Nodes.Where(n => n.Notation == NodeNotation.Bar))
        {
            bar.Span = Math.Max(diagram.Edges.Count(e => ReferenceEquals(e.Source, bar)),
                diagram.Edges.Count(e => ReferenceEquals(e.Target, bar)));
        }
    }

    // ----- state machine ------------------------------------------------------

    /// <summary>
    /// A state's substates and the transitions between them, in UML's notation:
    /// initial and final pseudostates, composite states holding theirs, entry,
    /// do and exit in the state, and transitions labelled "trigger [guard] / effect".
    /// </summary>
    private static Diagram StateMachine(Element root)
    {
        var diagram = new Diagram(DiagramKind.StateMachine, root);
        AddStates(diagram, root, container: null);
        AddTransitions(diagram, root, container: null);
        return diagram;
    }

    private static void AddStates(Diagram diagram, Element scope, DiagramNode? container)
    {
        foreach (var state in scope.Children.Where(IsState))
        {
            var composite = state.Children.Any(IsState);
            var node = new DiagramNode(state, state.DisplayName)
            {
                Notation = composite ? NodeNotation.CompositeState : NodeNotation.Rounded,
                Container = container,
            };
            node.Features.AddRange(StateActions(state));
            diagram.Nodes.Add(node);

            if (composite)
                AddStates(diagram, state, node);
        }
    }

    /// <summary>"entry / lockMotors", "do / holdPosition": what a state's compartment lists.</summary>
    private static IEnumerable<string> StateActions(Element state)
    {
        foreach (var action in state.Children.Where(c => c.StateAction is not null))
        {
            var inner = action.Children.Count > 0 ? action.Children[0] : null;
            var what = inner?.Name ?? (inner is null ? null : ReferenceOf(inner) ?? Written(inner));
            if (what is not null)
                yield return $"{action.StateAction} / {what}";
        }
    }

    private static string Written(Element element)
        => element.File.Text.Substring(element.Span.Start, element.Span.Length).Trim().TrimEnd(';').Trim();

    private static void AddTransitions(Diagram diagram, Element scope, DiagramNode? container)
    {
        foreach (var relation in scope.Relations.Where(r => r.Kind == RelationKind.Transition))
            AddTransition(diagram, scope, container, relation);

        foreach (var transition in scope.Children.Where(c => c.Kind is "transition" or "target transition"))
        {
            foreach (var relation in transition.Relations.Where(r => r.Kind == RelationKind.Transition))
                AddTransition(diagram, scope, container, relation);
        }

        foreach (var composite in diagram.Nodes.Where(n => ReferenceEquals(n.Container, container) && n.Notation == NodeNotation.CompositeState).ToList())
            AddTransitions(diagram, composite.Element, composite);
    }

    private static void AddTransition(Diagram diagram, Element scope, DiagramNode? container, Relation relation)
    {
        var source = StateEnd(diagram, scope, container, relation.Origin, relation.OriginReference);
        var target = StateEnd(diagram, scope, container, relation.Target, relation.TargetReference);
        if (source is null || target is null)
            return;

        // A transition's name is not UML's label, but where it has nothing else
        // it is what the writer called it, and an arrow should say something.
        var name = relation.Source.Kind is "transition" or "target transition" ? relation.Source.Name : null;
        diagram.Edges.Add(new DiagramEdge(source, target, RelationKind.Transition, relation.Label ?? name) { Name = name });
    }

    /// <summary>A transition's end: a state, or the pseudostate of the scope it is written in.</summary>
    private static DiagramNode? StateEnd(Diagram diagram, Element scope, DiagramNode? container, Element? resolved, string? reference)
    {
        var name = reference?.Trim();
        var pseudo = name switch
        {
            ModelIndexer.EntryOrigin => "start",
            "done" => "done",
            _ => null,
        };

        if (pseudo is not null)
        {
            var existing = diagram.Nodes.Find(n => n.Pseudo == pseudo && ReferenceEquals(n.Element, scope));
            if (existing is not null)
                return existing;

            var created = new DiagramNode(scope, pseudo, null, pseudo) { Container = container };
            diagram.Nodes.Add(created);
            return created;
        }

        return (resolved is not null ? diagram.NodeFor(resolved) : null) ?? FindEnd(diagram, name);
    }

    // ----- use case -----------------------------------------------------------

    private static bool IsUseCase(Element element) => element.Kind is "use case def" or "use case";

    /// <summary>A use case with nothing around it is one ellipse, which no diagram needs to say.</summary>
    private static bool HasUseCaseContent(Element element)
        => IsUseCase(element) && element.Children.Any(c => c.Kind is "actor" or "subject" or "include use case" || IsUseCase(c));

    /// <summary>
    /// For a package: its use cases. For one use case: it and what it includes.
    /// Each is an ellipse, inside the boundary its subject draws; actors stand
    /// outside and are one figure however many use cases name them.
    /// </summary>
    private static Diagram UseCase(Element root)
    {
        var diagram = new Diagram(DiagramKind.UseCase, root);
        var cases = IsPackage(root)
            ? root.Descendants().Where(e => IsUseCase(e) && e.Parent is { } owner && !IsUseCase(owner)).ToList()
            : [root];

        // Boundaries go first, so everything else is drawn over them.
        var boundaries = new Dictionary<string, DiagramNode>(StringComparer.Ordinal);
        var withSubject = new List<(Element Case, DiagramNode? Boundary)>();
        foreach (var useCase in cases)
            withSubject.Add((useCase, BoundaryOf(diagram, useCase, boundaries)));

        var nodes = new Dictionary<Element, DiagramNode>();
        foreach (var (useCase, boundary) in withSubject)
            AddUseCase(diagram, useCase, boundary, nodes, boundaries);

        var actors = new Dictionary<string, DiagramNode>(StringComparer.Ordinal);
        foreach (var (useCase, node) in nodes)
        {
            foreach (var actor in useCase.Children.Where(c => c.Kind == "actor"))
            {
                var key = RoleKey(actor);
                if (!actors.TryGetValue(key, out var figure))
                {
                    figure = new DiagramNode(actor, RoleLabel(actor)) { Notation = NodeNotation.Actor };
                    actors[key] = figure;
                    diagram.Nodes.Add(figure);
                }

                if (!diagram.Edges.Exists(e => ReferenceEquals(e.Source, figure) && ReferenceEquals(e.Target, node)))
                    diagram.Edges.Add(new DiagramEdge(figure, node, RelationKind.Connect) { Notation = EdgeNotation.Association });
            }

            foreach (var parent in useCase.Relations.Where(r => r.Kind == RelationKind.Specialization).Select(r => r.Target).OfType<Element>())
            {
                if (nodes.TryGetValue(parent, out var general))
                    diagram.Edges.Add(new DiagramEdge(node, general, RelationKind.Specialization));
            }
        }

        return diagram;
    }

    private static void AddUseCase(Diagram diagram, Element useCase, DiagramNode? boundary, Dictionary<Element, DiagramNode> nodes, Dictionary<string, DiagramNode> boundaries)
    {
        if (nodes.ContainsKey(useCase))
            return;

        var node = new DiagramNode(useCase, useCase.DisplayName) { Notation = NodeNotation.UseCase, Container = boundary };
        nodes[useCase] = node;
        diagram.Nodes.Add(node);

        foreach (var nested in useCase.Children.Where(IsUseCase))
            AddUseCase(diagram, nested, boundary, nodes, boundaries);

        foreach (var include in useCase.Children.Where(c => c.Kind == "include use case"))
        {
            // "include use case check : CheckWeather" and "include CheckWeather" both
            // mean CheckWeather; "include use case check { ... }" is its own use case.
            var target = include.Relations
                .Where(r => r.Kind is RelationKind.Typing or RelationKind.Specialization)
                .Select(r => r.Target)
                .FirstOrDefault(t => t is not null && IsUseCase(t)) ?? include;

            AddUseCase(diagram, target, BoundaryOf(diagram, target, boundaries) ?? boundary, nodes, boundaries);
            diagram.Edges.Add(new DiagramEdge(node, nodes[target], RelationKind.Dependency, "«include»"));
        }
    }

    private static DiagramNode? BoundaryOf(Diagram diagram, Element useCase, Dictionary<string, DiagramNode> boundaries)
    {
        if (useCase.Children.FirstOrDefault(c => c.Kind == "subject") is not { } subject)
            return null;

        var key = RoleKey(subject);
        if (!boundaries.TryGetValue(key, out var boundary))
        {
            boundary = new DiagramNode(subject, RoleLabel(subject)) { Notation = NodeNotation.Boundary };
            boundaries[key] = boundary;
            diagram.Nodes.Insert(0, boundary);
        }

        return boundary;
    }

    /// <summary>What makes two actors or subjects the same one: their type, or else their name.</summary>
    private static string RoleKey(Element role)
    {
        var type = role.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing);
        return type?.Target?.QualifiedName ?? type?.TargetReference.Trim() ?? role.DisplayName;
    }

    private static string RoleLabel(Element role)
        => role.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing)?.TargetReference.Trim().Split("::")[^1] ?? role.DisplayName;

    // ----- sequence and communication ------------------------------------------

    /// <summary>Lifelines left to right, messages top to bottom in the order the model gives them.</summary>
    private static Diagram Sequence(Element root)
    {
        var diagram = new Diagram(DiagramKind.Sequence, root);
        var interaction = Interaction.Read(root);

        diagram.Nodes.Add(new DiagramNode(root, root.DisplayName, null, "frame")
        {
            Notation = NodeNotation.Frame,
            Tag = FrameTagSequence,
            IsFixed = true,
        });

        var lines = new Dictionary<Element, DiagramNode>();
        foreach (var lifeline in interaction.Lifelines)
        {
            var type = lifeline.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing)?.TargetReference.Trim();
            var node = new DiagramNode(lifeline, lifeline.DisplayName + lifeline.Multiplicity) { Notation = NodeNotation.Lifeline, Detail = type };
            lines[lifeline] = node;
            diagram.Nodes.Add(node);
        }

        for (var row = 0; row < interaction.Steps.Count; row++)
        {
            var step = interaction.Steps[row];
            if (step.To is { } to)
            {
                diagram.Edges.Add(new DiagramEdge(lines[step.From], lines[to], RelationKind.Message, step.Label)
                {
                    Notation = step.Waits ? EdgeNotation.SyncMessage : EdgeNotation.AsyncMessage,
                    Row = row,
                    Name = step.Element.Name,
                });
            }
            else
            {
                diagram.EventRows.Add((lines[step.From], row, step.Label));
            }
        }

        diagram.RowCount = interaction.Steps.Count;
        foreach (var bar in interaction.Bars)
            diagram.BarRows.Add((lines[bar.Lifeline], bar.First, bar.Last));
        return diagram;
    }

    /// <summary>The participants as boxes, one link for each pair that exchanges anything, with its messages numbered in order.</summary>
    private static Diagram Communication(Element root)
    {
        var diagram = new Diagram(DiagramKind.Communication, root);
        var interaction = Interaction.Read(root);

        var boxes = new Dictionary<Element, DiagramNode>();
        foreach (var lifeline in interaction.Lifelines)
        {
            boxes[lifeline] = new DiagramNode(lifeline, LifelineLabel(lifeline));
            diagram.Nodes.Add(boxes[lifeline]);
        }

        var number = 0;
        foreach (var step in interaction.Steps.Where(s => s.To is not null))
        {
            var from = boxes[step.From];
            var to = boxes[step.To!];
            var link = diagram.Edges.Find(e => (ReferenceEquals(e.Source, from) && ReferenceEquals(e.Target, to))
                                               || (ReferenceEquals(e.Source, to) && ReferenceEquals(e.Target, from)));
            if (link is null)
            {
                link = new DiagramEdge(from, to, RelationKind.Connect) { Notation = EdgeNotation.Association };
                diagram.Edges.Add(link);
            }

            link.Messages.Add(new LinkMessage(++number, step.Label, from));
            link.Label = DiagramLayout.LinkLabel(link);
        }

        return diagram;
    }

    private static string LifelineLabel(Element lifeline)
        => lifeline.Relations.FirstOrDefault(r => r.Kind == RelationKind.Typing) is { } type
            ? $"{lifeline.DisplayName} : {type.TargetReference.Trim()}{lifeline.Multiplicity}"
            : lifeline.DisplayName + lifeline.Multiplicity;

    // ----- timing --------------------------------------------------------------

    /// <summary>What can have a life drawn along a time axis.</summary>
    private static bool IsTimeLifeline(Element element)
        => element.Kind is "part" or "item" or "occurrence" or "individual" or "ref" or "actor" or "part def" or "item def" or "occurrence def" or "individual def"
           && element.PortionKind is null;

    /// <summary>A portion of a life, or an occurrence written as one.</summary>
    private static bool IsTimeStep(Element element)
        => element.PortionKind is not null || (element.Kind == "occurrence" && !element.Children.Any(IsTimeStep));

    /// <summary>
    /// The lives on a timing diagram: the element's own if it has portions of
    /// its own, and each feature's that has. A life with nothing but plain
    /// occurrences needs at least one portion among them or a "then" between
    /// them, or there is no time in it to draw.
    /// </summary>
    private static List<(Element Lifeline, List<Element> Steps)> TimeRows(Element root)
    {
        var rows = new List<(Element, List<Element>)>();
        if (IsPackage(root) || !IsTimeLifeline(root))
            return rows;

        foreach (var candidate in new[] { root }.Concat(root.Children.Where(IsTimeLifeline)))
        {
            var steps = candidate.Children.Where(IsTimeStep).ToList();
            if (steps.Count > 0 && (steps.Exists(s => s.PortionKind is not null) || HasOrder(candidate)))
                rows.Add((candidate, steps));
        }

        return rows;
    }

    private static bool HasOrder(Element element)
        => element.Relations.Any(r => r.Kind == RelationKind.Succession) || element.Children.Any(c => c.Kind == "succession");

    /// <summary>
    /// A row per life and a mark per portion, placed by the order the model
    /// states. The axis says this is all it knows.
    /// </summary>
    private static Diagram Timing(Element root)
    {
        var diagram = new Diagram(DiagramKind.Timing, root);
        diagram.Nodes.Add(new DiagramNode(root, root.DisplayName, null, "frame")
        {
            Notation = NodeNotation.Frame,
            Tag = FrameTagTiming,
            IsFixed = true,
        });

        var order = new TimeOrder();
        var marks = new Dictionary<Element, DiagramNode>();
        foreach (var (lifeline, steps) in TimeRows(root))
        {
            var row = new DiagramNode(lifeline, LifelineLabel(lifeline)) { Notation = NodeNotation.TimeRow, IsFixed = true };
            diagram.Nodes.Add(row);

            var inOrder = order.AddLife(lifeline, steps);
            foreach (var step in inOrder)
            {
                var isPoint = step.PortionKind == "snapshot";
                var mark = new DiagramNode(step, step.DisplayName)
                {
                    Notation = isPoint ? NodeNotation.TimePoint : NodeNotation.TimeBand,
                    IsFixed = true,
                    Detail = ValuesOf(step),
                };
                marks[step] = mark;
                diagram.Nodes.Add(mark);
                diagram.TimeMarks.Add(new TimeMark(mark, row, step));
            }
        }

        // Order stated between lives, and messages between portions of them.
        foreach (var relation in root.Relations.Concat(root.Children.Where(c => c.Kind is "succession" or "message").SelectMany(c => c.Relations))
                     .Where(r => r.Kind is RelationKind.Succession or RelationKind.Message))
        {
            if (relation.Origin is not { } from || relation.Target is not { } to)
                continue;

            order.AddOrder(from, to);
            if (marks.TryGetValue(from, out var a) && marks.TryGetValue(to, out var b) && !ReferenceEquals(a.Element.Parent, b.Element.Parent))
                diagram.Edges.Add(new DiagramEdge(a, b, relation.Kind, relation.Source.Kind == "message" ? relation.Source.Name : null));
        }

        var ranks = order.Ranks();
        foreach (var mark in diagram.TimeMarks)
            mark.Rank = ranks[mark.Step];

        diagram.Nodes.Add(new DiagramNode(root, "order only: the model states what comes after what, and no durations", null, "axis")
        {
            Notation = NodeNotation.TimeAxis,
            IsFixed = true,
            Detail = order.AssumedWrittenOrder
                ? "where it states no order the written order is used; marks in one column are not stated to be simultaneous"
                : "marks in one column are not stated to be simultaneous",
        });
        return diagram;
    }

    /// <summary>"charge = 100, armed = true": what a portion says its features are.</summary>
    private static string? ValuesOf(Element step)
    {
        var values = step.Children
            .Where(c => c.Kind is "attribute" or "ref" && c.Value is not null)
            .Select(c => $"{c.Name ?? c.Relations.FirstOrDefault(r => r.Kind == RelationKind.Redefinition)?.TargetReference.Trim() ?? "?"} = {c.Value}")
            .ToList();
        return values.Count == 0 ? null : Shorten(string.Join(", ", values), 44);
    }
}
