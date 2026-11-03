using SysmlStudio.Model;

namespace SysmlStudio.Diagrams;

/// <summary>One thing that happens: a message between two lifelines, or an event on one.</summary>
internal sealed record InteractionStep(Element Element, Element From, Element? To, string Label, bool Waits);

/// <summary>A stretch of a lifeline an action of its takes up, in steps.</summary>
internal sealed record ActivationSpan(Element Lifeline, int First, int Last);

/// <summary>
/// <para>
/// What an interaction says, read from the shapes SysML v2 writes it in. A
/// message names its ends ("message m from a.x to b.y"), or its ends name it:
/// "event m.sourceEvent" inside a part makes that part the sender of m.
/// </para>
/// <para>
/// Order comes from "then", "first a then b" and the order of the events on a
/// lifeline. What the model leaves unordered is drawn in the order it is
/// written, because a diagram has to put it somewhere, and that is the only
/// place the text suggests.
/// </para>
/// </summary>
internal sealed class Interaction
{
    private readonly Element _root;
    private readonly List<Element> _events;
    private readonly Dictionary<Element, InteractionStep> _stepOf = [];

    private Interaction(Element root)
    {
        _root = root;
        _events = [.. root.Descendants().Where(e => e.Kind == "event occurrence" && !IsInsideMessage(e))];
    }

    /// <summary>The participants that take part, in the order they are declared.</summary>
    public List<Element> Lifelines { get; private set; } = [];

    /// <summary>What happens, in order.</summary>
    public List<InteractionStep> Steps { get; private set; } = [];

    /// <summary>The actions on each lifeline that hold events, as the steps they cover.</summary>
    public List<ActivationSpan> Bars { get; } = [];

    /// <summary>Whether the element writes any message, which is what makes it an interaction.</summary>
    public static bool HoldsMessages(Element element) => element.Children.Any(c => c.Kind == "message");

    private static bool IsMessageLike(Element root, Element child)
        => child.Kind == "message"
           || (child.Kind is "flow" or "succession flow" && root.Kind is "occurrence def" or "occurrence" or "individual def" or "individual");

    private static bool IsInsideMessage(Element element) => element.Parent?.Kind == "message";

    /// <summary>A feature that can have a lifeline. Its ports and actions are places on that line, not lines of their own.</summary>
    private static bool IsParticipant(Element element)
        => element.Kind is "part" or "item" or "occurrence" or "individual" or "ref" or "actor" or "subject" && element.PortionKind is null;

    public static Interaction Read(Element root)
    {
        var interaction = new Interaction(root);
        var messages = new List<InteractionStep>();
        foreach (var message in root.Children.Where(c => IsMessageLike(root, c)))
        {
            if (interaction.ReadMessage(message) is { } step)
                messages.Add(step);
        }

        var steps = new List<InteractionStep>(messages);
        foreach (var occurrence in interaction._events.Where(e => !interaction._stepOf.ContainsKey(e)))
        {
            if (ParticipantOwning(occurrence, root) is { } lifeline)
            {
                var label = occurrence.Name ?? occurrence.Relations.FirstOrDefault(r => r.Kind == RelationKind.Specialization)?.TargetReference.Trim() ?? "event";
                var step = new InteractionStep(occurrence, lifeline, null, label, false);
                interaction._stepOf[occurrence] = step;
                steps.Add(step);
            }
        }

        interaction.Steps = interaction.InOrder(steps);
        interaction.Lifelines = interaction.ReadLifelines();
        interaction.ReadBars();
        return interaction;
    }

    private InteractionStep? ReadMessage(Element message)
    {
        var relation = message.Relations.FirstOrDefault(r => r.Kind is RelationKind.Message or RelationKind.Flow);
        var (from, fromEvent) = relation is null ? (null, null) : End(relation.OriginReference, relation.Origin);
        var (to, toEvent) = relation is null ? (null, null) : End(relation.TargetReference, relation.Target);

        // "message m of T;" names no ends: "event m.sourceEvent" and
        // "event m.targetEvent" on the lifelines say where it goes.
        if (message.Name is { } name)
        {
            foreach (var occurrence in _events)
            {
                var named = occurrence.Relations.FirstOrDefault(r => r.Kind == RelationKind.Specialization)?.TargetReference.Trim();
                if (named == name + ".sourceEvent")
                    (from, fromEvent) = (ParticipantOwning(occurrence, _root), occurrence);
                else if (named == name + ".targetEvent")
                    (to, toEvent) = (ParticipantOwning(occurrence, _root), occurrence);
            }
        }

        if (from is null || to is null)
            return null;

        var step = new InteractionStep(message, from, to, Label(message), message.Kind == "succession flow");
        _stepOf[message] = step;
        if (fromEvent is not null)
            _stepOf[fromEvent] = step;
        if (toEvent is not null)
            _stepOf[toEvent] = step;
        return step;
    }

    private static string Label(Element message)
    {
        if (message.Name is { } name)
            return message.Payload is { } payload ? $"{name} : {payload}" : name;
        return message.Payload ?? message.Kind;
    }

    /// <summary>
    /// Which lifeline a written end falls on, and the event it names if it names
    /// one. The chain is walked down what the interaction declares, so
    /// "operator.sendOrder" is the part operator even when sendOrder is only
    /// declared on what operator is typed by.
    /// </summary>
    private (Element? Lifeline, Element? Event) End(string? reference, Element? resolved)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return (null, null);

        Element? lifeline = null;
        var scope = _root;
        foreach (var segment in reference.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var name = segment.Trim('\'');
            var next = scope.Children.FirstOrDefault(c => c.Name == name && IsParticipant(c));
            if (next is null)
                break;
            lifeline = next;
            scope = next;
        }

        var occurrence = resolved?.Kind == "event occurrence" ? resolved : null;
        lifeline ??= resolved is null ? null : ParticipantOwning(resolved, stopAt: null);
        return (lifeline, occurrence);
    }

    /// <summary>The nearest participant at or above an element, short of the interaction itself.</summary>
    private static Element? ParticipantOwning(Element element, Element? stopAt)
    {
        for (var e = element; e is not null && !ReferenceEquals(e, stopAt); e = e.Parent)
        {
            if (IsParticipant(e))
                return e;
        }

        return null;
    }

    /// <summary>
    /// Sorts the steps so that everything the model says comes first does:
    /// among those it leaves free to go next, the one written first goes next.
    /// A cycle cannot be honoured; what is left of it follows in written order.
    /// </summary>
    private List<InteractionStep> InOrder(List<InteractionStep> steps)
    {
        var before = steps.ToDictionary(s => s, _ => new HashSet<InteractionStep>());
        foreach (var relation in _root.Descendants().Prepend(_root).SelectMany(e => e.Relations).Where(r => r.Kind == RelationKind.Succession))
        {
            if (StepNamed(relation.Origin, relation.OriginReference) is { } first
                && StepNamed(relation.Target, relation.TargetReference) is { } second
                && !ReferenceEquals(first, second))
            {
                before[second].Add(first);
            }
        }

        var written = steps.OrderBy(s => s.Element.Span.Start).ToList();
        var ordered = new List<InteractionStep>();
        while (written.Count > 0)
        {
            var next = written.Find(s => before[s].All(ordered.Contains)) ?? written[0];
            ordered.Add(next);
            written.Remove(next);
        }

        return ordered;
    }

    /// <summary>The step a succession's end means: a message, an event of one, or "m.sourceEvent" of a message.</summary>
    private InteractionStep? StepNamed(Element? resolved, string? reference)
    {
        if (resolved is not null && _stepOf.TryGetValue(resolved, out var step))
            return step;

        if (reference?.Split('.')[0].Trim() is not { Length: > 0 } head)
            return null;

        return _stepOf.FirstOrDefault(s => s.Key.Parent is { } owner && ReferenceEquals(owner, _root) && s.Key.Name == head).Value;
    }

    private List<Element> ReadLifelines()
    {
        var used = new List<Element>();
        foreach (var step in Steps)
        {
            if (!used.Contains(step.From))
                used.Add(step.From);
            if (step.To is { } to && !used.Contains(to))
                used.Add(to);
        }

        // Declared order, which is how the writer lined them up; a lifeline
        // declared elsewhere keeps the place its first message gave it.
        var declared = _root.Descendants().Where(used.Contains).ToList();
        return [.. declared, .. used.Where(u => !declared.Contains(u))];
    }

    /// <summary>
    /// An activation bar is an action a lifeline performs, from the first event
    /// inside that action to the last. SysML v2 has nothing else that says a
    /// lifeline is busy, so without one no bar is drawn.
    /// </summary>
    private void ReadBars()
    {
        foreach (var lifeline in Lifelines)
        {
            foreach (var action in lifeline.Children.Where(c => c.Kind is "action" or "perform action"))
            {
                var rows = action.Descendants().Where(e => e.Kind == "event occurrence")
                    .Select(e => _stepOf.TryGetValue(e, out var step) ? Steps.IndexOf(step) : -1)
                    .Where(r => r >= 0)
                    .ToList();

                if (rows.Count > 0)
                    Bars.Add(new ActivationSpan(lifeline, rows.Min(), rows.Max()));
            }
        }
    }
}

/// <summary>
/// Places portions of lives along one axis of order, as ranks. Within a life
/// the order is what the model states, then what is written; between lives only
/// what the model states.
/// </summary>
internal sealed class TimeOrder
{
    private readonly Dictionary<Element, HashSet<Element>> _after = [];

    /// <summary>Whether any two portions of one life had no stated order and took the written one.</summary>
    public bool AssumedWrittenOrder { get; private set; }

    /// <summary>Adds a life's portions and returns them in order.</summary>
    public List<Element> AddLife(Element lifeline, List<Element> steps)
    {
        foreach (var step in steps)
            _after.TryAdd(step, []);

        var relations = lifeline.Relations
            .Concat(lifeline.Children.Where(c => c.Kind == "succession").SelectMany(c => c.Relations))
            .Where(r => r.Kind == RelationKind.Succession)
            .ToList();

        var stated = new Dictionary<Element, HashSet<Element>>();
        foreach (var step in steps)
            stated[step] = [];

        foreach (var relation in relations)
        {
            if (relation.Origin is { } from && relation.Target is { } to && stated.ContainsKey(from) && stated.TryGetValue(to, out var earlier) && !ReferenceEquals(from, to))
                earlier.Add(from);
        }

        var written = steps.OrderBy(s => s.Span.Start).ToList();
        var ordered = new List<Element>();
        while (written.Count > 0)
        {
            var next = written.Find(s => stated[s].All(ordered.Contains)) ?? written[0];
            if (ordered.Count > 0 && !stated[next].Contains(ordered[^1]))
                AssumedWrittenOrder = true;

            if (ordered.Count > 0)
                _after[next].Add(ordered[^1]);
            ordered.Add(next);
            written.Remove(next);
        }

        return ordered;
    }

    /// <summary>Says that one portion comes after another, across lives; anything else is ignored.</summary>
    public void AddOrder(Element first, Element second)
    {
        if (_after.ContainsKey(first) && _after.TryGetValue(second, out var earlier))
            earlier.Add(first);
    }

    /// <summary>Each portion's place on the axis: one after the latest of what it follows. A cycle stops growing here.</summary>
    public Dictionary<Element, int> Ranks()
    {
        var ranks = _after.Keys.ToDictionary(e => e, _ => 0);
        for (var round = 0; round <= _after.Count; round++)
        {
            var changed = false;
            foreach (var (step, earlier) in _after)
            {
                foreach (var previous in earlier)
                {
                    if (ranks[previous] + 1 > ranks[step])
                    {
                        ranks[step] = ranks[previous] + 1;
                        changed = true;
                    }
                }
            }

            if (!changed)
                break;
        }

        return ranks;
    }
}
