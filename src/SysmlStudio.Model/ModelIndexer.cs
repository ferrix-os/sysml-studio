using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using SysmlStudio.Syntax;
using SysmlStudio.Syntax.Generated;

namespace SysmlStudio.Model;

/// <summary>
/// <para>
/// Turns a parse tree into the element tree the browser, the properties pane
/// and the diagrams read.
/// </para>
/// <para>
/// It works off rule names rather than generated context types on purpose: the
/// grammar has ~450 rules, almost all of them shaped the same way. Recognising that shape costs one pass and keeps working when
/// the grammar is re-vendored with rules this file has never heard of.
/// </para>
/// </summary>
public static class ModelIndexer
{
    /// <summary>Element rules whose names do not follow the Definition/Usage pattern.</summary>
    private static readonly Dictionary<string, string> ExtraElementRules = new()
    {
        ["package"] = "package",
        ["libraryPackage"] = "library package",
        ["dependency"] = "dependency",
        ["namespace"] = "namespace",
        ["message"] = "message",
        ["mergeNode"] = "merge",
        ["decisionNode"] = "decide",
        ["joinNode"] = "join",
        ["forkNode"] = "fork",
        ["sendNode"] = "send action",
        ["acceptNode"] = "accept action",
        ["terminateNode"] = "terminate action",
    };

    /// <summary>Bodies read in order, where "then" means whatever was written before it.</summary>
    private static readonly HashSet<string> OrderedBodies = ["actionBody", "definitionBody", "caseBody", "actionBodyParameter"];

    /// <summary>Rules that look like element rules by name but are not.</summary>
    private static readonly HashSet<string> NotElementRules =
    [
        "definition", "usage", "definitionElement", "usageElement",
        "nonOccurrenceUsageElement", "occurrenceUsageElement", "structureUsageElement",
        "behaviorUsageElement", "variantUsageElement", "endOccurrenceUsageElement",
        "extendedDefinition", "extendedUsage",
    ];

    /// <summary>Kind labels for element rules whose rule name does not read well.</summary>
    private static readonly Dictionary<string, string> KindOverrides = new()
    {
        ["referenceUsage"] = "ref",
        ["defaultReferenceUsage"] = "ref",
        ["endFeatureUsage"] = "end",
        ["satisfyRequirementUsage"] = "satisfy",
        ["requirementVerificationUsage"] = "verify",
        ["requirementConstraintUsage"] = "requirement constraint",
        ["eventOccurrenceUsage"] = "event occurrence",
        ["performActionUsage"] = "perform action",
        ["exhibitStateUsage"] = "exhibit state",
        ["includeUseCaseUsage"] = "include use case",
        ["assertConstraintUsage"] = "assert constraint",
        ["successionFlowUsage"] = "succession flow",
        ["successionAsUsage"] = "succession",
        ["emptySuccessionMember"] = "succession",
    };

    /// <summary>Indexes one file under <paramref name="root"/>.</summary>
    public static void Index(SourceFile file, Element root)
    {
        Walk(file.Tree, root, file);
    }

    private static void Walk(IParseTree node, Element parent, SourceFile file)
    {
        for (var i = 0; i < node.ChildCount; i++)
        {
            var child = node.GetChild(i);
            if (child is not ParserRuleContext ctx)
                continue;

            var rule = RuleName(ctx);
            if (rule == "importRule")
            {
                var imported = FindShallow(ctx, "importDeclaration").FirstOrDefault();
                if (imported is not null)
                    parent.Add(Relate(RelationKind.Import, parent, imported));
                continue;
            }

            if (OrderedBodies.Contains(rule))
            {
                WalkOrderedBody(ctx, parent, file);
                continue;
            }

            if (rule is "stateDefBody" or "stateUsageBody")
            {
                WalkStateBody(ctx, parent, file);
                continue;
            }

            if (TryKind(rule, out var kind) && CoversText(ctx))
            {
                var element = new Element(kind, file, ctx);
                Describe(element, ctx);
                parent.Add(element);
                Walk(ctx, element, file);
            }
            else
            {
                Walk(child, parent, file);
            }
        }
    }

    /// <summary>What "then" follows: a name as written, and the element when it was read in place.</summary>
    private readonly record struct Step(string Reference, Element? Node)
    {
        public static Step Of(Element element) => new(element.Name ?? element.Kind, element);
    }

    /// <summary>
    /// <para>
    /// A body read in order so that the shorthand successions become relations:
    /// "first start; then action a; then action b; then done;" is start, a, b,
    /// done in that order, though only "first" and "then" say so.
    /// </para>
    /// <para>
    /// "then b; then c;" written after a fork or a decision leaves that node
    /// each time, and so does anything with a condition. Written after anything
    /// else it continues from b, which is how models chain references.
    /// </para>
    /// </summary>
    private static void WalkOrderedBody(ParserRuleContext body, Element owner, SourceFile file)
    {
        Step? previous = null;
        for (var i = 0; i < body.ChildCount; i++)
        {
            if (body.GetChild(i) is not ParserRuleContext item)
                continue;

            var before = owner.Children.Count;
            WalkOne(item, owner, file);
            var added = owner.Children.Skip(before).LastOrDefault(c => c.Name is not null || IsAnonymousStep(c));

            var initial = FindShallow(item, "initialNodeMember").FirstOrDefault();
            if (initial is not null)
            {
                previous = FindShallow(initial, "qualifiedName").FirstOrDefault()?.GetText() is { } first ? new Step(first, null) : null;
            }
            else if (added is not null)
            {
                if (FindShallow(item, "sourceSuccessionMember").Count > 0 && previous is { } from)
                    owner.Add(Succession(owner, from, Step.Of(added)));
                previous = Step.Of(added);
            }

            var fans = added?.Kind is "fork" or "decide" or "merge" or "join";
            foreach (var succession in FindShallow(item, "actionTargetSuccessionMember"))
            {
                if (FindShallow(succession, "connectorEndMember").FirstOrDefault() is not { } end || previous is not { } source)
                    continue;

                var guard = GuardOf(succession);
                owner.Add(Succession(owner, source, new Step(Written(end), null), guard, end));
                if (guard is null && !fans)
                    previous = new Step(Written(end), null);
            }

            foreach (var guarded in FindShallow(item, "guardedSuccessionMember"))
            {
                var from = FindShallow(guarded, "featureChainMember").FirstOrDefault();
                var to = FindShallow(guarded, "connectorEndMember").FirstOrDefault();
                if (from is not null && to is not null)
                    owner.Add(Succession(owner, new Step(Written(from), null), new Step(Written(to), null), GuardOf(guarded), to));
            }
        }
    }

    /// <summary>A step that has no name to be followed by, but is still a place in the flow.</summary>
    private static bool IsAnonymousStep(Element element) => element.Kind is "fork" or "decide" or "merge" or "join"
        or "send action" or "accept action" or "terminate action" or "perform action" or "event occurrence";

    private static Relation Succession(Element owner, Step from, Step to, string? guard = null, ParserRuleContext? writtenTarget = null)
        => new(RelationKind.Succession, owner, to.Reference, originReference: from.Reference)
        {
            OriginNode = from.Node,
            TargetNode = to.Node,
            Guard = guard,
            TargetSpan = writtenTarget is null ? null : Span(writtenTarget),
        };

    /// <summary>The condition of a guarded succession as written, "else" for the default one, null without one.</summary>
    private static string? GuardOf(ParserRuleContext succession)
    {
        if (FindShallow(succession, "defaultTargetSuccession").Count > 0)
            return "else";
        return FindShallow(succession, "guardExpressionMember").FirstOrDefault() is { } guard
            ? WithoutKeyword(Written(guard), "if")
            : null;
    }

    private static string WithoutKeyword(string written, string keyword)
        => written.StartsWith(keyword, StringComparison.Ordinal) ? written[keyword.Length..].Trim() : written.Trim();

    /// <summary>
    /// A state's body, read in order: "entry; then off;" is the transition from
    /// the initial pseudostate, an action written "entry", "do" or "exit" says
    /// so, and "state a; accept x then b;" is a transition out of a.
    /// </summary>
    private static void WalkStateBody(ParserRuleContext body, Element owner, SourceFile file)
    {
        owner.IsParallel = body.GetChild(0)?.GetText() == "parallel";

        Element? previous = null;
        for (var i = 0; i < body.ChildCount; i++)
        {
            if (body.GetChild(i) is not ParserRuleContext item)
                continue;

            var before = owner.Children.Count;
            WalkOne(item, owner, file);
            var added = owner.Children.Skip(before).ToList();

            foreach (var (rule, word) in new[] { ("entryActionMember", "entry"), ("doActionMember", "do"), ("exitActionMember", "exit") })
            {
                if (FindShallow(item, rule).Count > 0 && added.Find(c => c.Kind == "state action") is { } action)
                    action.StateAction = word;
            }

            foreach (var entry in FindShallow(item, "entryTransitionMember"))
            {
                if (FindShallow(entry, "connectorEndMember").FirstOrDefault() is { } end)
                {
                    var guard = FindShallow(entry, "guardExpressionMember").FirstOrDefault();
                    owner.Add(new Relation(RelationKind.Transition, owner, Written(end),
                        guard is null ? null : $"[{WithoutKeyword(Written(guard), "if")}]", originReference: EntryOrigin)
                    {
                        TargetSpan = Span(end),
                    });
                }
            }

            var state = added.Find(c => c.Kind is "state" or "exhibit state" && c.Name is not null);
            if (state is null)
                continue;

            if (FindShallow(item, "sourceSuccessionMember").Count > 0 && previous is not null)
            {
                owner.Add(new Relation(RelationKind.Transition, owner, state.Name!, originReference: previous.Name)
                {
                    OriginNode = previous,
                    TargetNode = state,
                });
            }

            foreach (var outgoing in added.Where(c => c.Kind == "target transition"))
                ReadTransition(outgoing, outgoing.Context, state.Name);

            previous = state;
        }
    }

    /// <summary>The origin an initial transition names: it has none in the model, only "entry; then s;".</summary>
    public const string EntryOrigin = "entry";

    /// <summary>
    /// A transition's relation, labelled UML's way: "trigger [guard] / effect".
    /// <paramref name="origin"/> is the state it leaves when the text does not
    /// name one, which it does not for "state a; accept x then b;".
    /// </summary>
    private static void ReadTransition(Element element, ParserRuleContext ctx, string? origin)
    {
        // Given an origin there is no source in the text, and a chain found
        // here would be one inside the guard's expression.
        var source = origin is null ? FindShallow(ctx, "featureChainMember").FirstOrDefault() : null;
        var target = FindShallow(ctx, "transitionSuccessionMember").FirstOrDefault();
        if (target is null || (source is null && origin is null))
            return;

        var trigger = FindShallow(ctx, "triggerActionMember").FirstOrDefault();
        var guard = FindShallow(ctx, "guardExpressionMember").FirstOrDefault();
        var effect = FindShallow(ctx, "effectBehaviorMember").FirstOrDefault();

        var parts = new List<string>();
        if (trigger is not null)
            parts.Add(WithoutKeyword(Written(trigger), "accept"));
        if (guard is not null)
            parts.Add($"[{WithoutKeyword(Written(guard), "if")}]");
        if (effect is not null)
            parts.Add("/ " + WithoutKeyword(WithoutKeyword(Written(effect), "do"), "action"));
        var label = parts.Count == 0 ? null : string.Join(" ", parts);

        if (source is not null)
        {
            element.Add(Relate(RelationKind.Transition, element, target, label, source));
            element.Value = Written(source);
        }
        else
        {
            element.Add(new Relation(RelationKind.Transition, element, Written(target), label, origin) { TargetSpan = Span(target) });
        }
    }

    /// <summary>Walks one context as if it were a node's only child.</summary>
    private static void WalkOne(ParserRuleContext ctx, Element parent, SourceFile file)
    {
        if (TryKind(RuleName(ctx), out var kind) && CoversText(ctx))
        {
            var element = new Element(kind, file, ctx);
            Describe(element, ctx);
            parent.Add(element);
            Walk(ctx, element, file);
        }
        else
        {
            Walk(ctx, parent, file);
        }
    }

    /// <summary>
    /// Whether a rule matched any text. The grammar has rules that match
    /// nothing, and an element with no text is not an element anyone wrote.
    /// </summary>
    private static bool CoversText(ParserRuleContext ctx)
        => ctx.Stop is not null && ctx.Stop.TokenIndex >= ctx.Start.TokenIndex;

    private static bool TryKind(string rule, out string kind)
    {
        if (ExtraElementRules.TryGetValue(rule, out var extra))
        {
            kind = extra;
            return true;
        }

        kind = string.Empty;
        if (NotElementRules.Contains(rule))
            return false;
        if (!rule.EndsWith("Definition", StringComparison.Ordinal) && !rule.EndsWith("Usage", StringComparison.Ordinal))
            return false;

        if (KindOverrides.TryGetValue(rule, out var over))
        {
            kind = over;
            return true;
        }

        var isDefinition = rule.EndsWith("Definition", StringComparison.Ordinal);
        var stem = rule[..^(isDefinition ? "Definition".Length : "Usage".Length)];
        kind = SplitCamel(stem) + (isDefinition ? " def" : string.Empty);
        return true;
    }

    /// <summary>"partDefinition" -> "part", "useCaseDefinition" -> "use case".</summary>
    private static string SplitCamel(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsUpper(c) && sb.Length > 0)
                sb.Append(' ');
            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    /// <summary>Fills in name, documentation, metadata and the element's own relations.</summary>
    private static void Describe(Element element, ParserRuleContext ctx)
    {
        var identification = FindShallow(ctx, "identification", "featureIdentification").FirstOrDefault();
        if (identification is not null)
            ReadIdentification(element, identification);

        foreach (var meta in FindShallow(ctx, "prefixMetadataMember", "prefixMetadataAnnotation"))
        {
            var keyword = Written(meta).TrimStart('#').Trim();
            element.Metadata.Add("#" + keyword);
            element.PrefixSpans.Add((keyword, Span(meta)));
        }

        foreach (var meta in FindShallow(ctx, "metadataFeature"))
            element.Metadata.Add(Written(meta).Trim());

        foreach (var doc in FindShallow(ctx, "documentation"))
        {
            element.Documentation = AppendDoc(element.Documentation, DocText(doc));
            if (element.DocSpan is null && doc.GetChild(doc.ChildCount - 1) is ITerminalNode comment)
                element.DocSpan = new TextSpan(comment.Symbol.StartIndex, comment.Symbol.StopIndex);
        }

        var multiplicity = FindShallow(ctx, "ownedMultiplicity").FirstOrDefault();
        if (multiplicity is not null)
            element.Multiplicity = Written(multiplicity);

        var value = FindShallow(ctx, "valuePart").FirstOrDefault();
        if (value is not null)
            element.Value = Written(value).TrimStart('=').Trim();

        ReadRelations(element, ctx);
    }

    private static void ReadIdentification(Element element, ParserRuleContext identification)
    {
        var names = FindShallow(identification, "name").ToList();
        var hasShort = identification.GetChild(0) is ITerminalNode t && t.GetText() == "<";
        var nameContext = hasShort ? names.ElementAtOrDefault(1) : names.ElementAtOrDefault(0);
        if (hasShort)
            element.ShortName = Unquote(names.ElementAtOrDefault(0)?.GetText());
        element.Name = Unquote(nameContext?.GetText());
        if (nameContext is not null)
            element.NameSpan = Span(nameContext);
    }

    private static void ReadRelations(Element element, ParserRuleContext ctx)
    {
        // Specialization: ":>" on a definition, ":>" / "subsets" on a usage.
        foreach (var part in FindShallow(ctx, "subclassificationPart"))
        {
            foreach (var target in FindShallow(part, "ownedSubclassification"))
                element.Add(Relate(RelationKind.Specialization, element, target));
        }

        foreach (var part in FindShallow(ctx, "featureSpecializationPart"))
        {
            foreach (var typing in FindShallow(part, "ownedFeatureTyping"))
                element.Add(Relate(RelationKind.Typing, element, typing));
            foreach (var subset in FindShallow(part, "ownedSubsetting"))
                element.Add(Relate(RelationKind.Specialization, element, subset));
            foreach (var redef in FindShallow(part, "ownedRedefinition"))
                element.Add(Relate(RelationKind.Redefinition, element, redef));
        }

        // Connectors: connect / interface / allocate / flow all carry end members.
        var connectorKind = element.Kind switch
        {
            "connection" => RelationKind.Connect,
            "interface" => RelationKind.Interface,
            "allocation" => RelationKind.Allocate,
            "flow" or "succession flow" => RelationKind.Flow,
            "message" => RelationKind.Message,
            _ => (RelationKind?)null,
        };

        if (connectorKind is { } ck)
        {
            var ends = FindShallow(ctx, "connectorEndMember", "interfaceEndMember", "messageEventMember", "flowEndMember");
            for (var i = 0; i + 1 < ends.Count; i++)
                element.Add(Relate(ck, element, ends[i + 1], element.Name, ends[i]));
        }

        if (FindShallow(ctx, "portionKind").FirstOrDefault() is { } portion)
            element.PortionKind = portion.GetText();

        if (FindShallow(ctx, "flowPayloadFeatureMember", "payloadFeatureMember").FirstOrDefault() is { } payload)
            element.Payload = Written(payload);

        switch (element.Kind)
        {
            case "send action" or "accept action":
                {
                    // What is sent is a parameter; what is accepted, the payload parameter.
                    if (FindShallow(ctx, "payloadParameterMember", "nodeParameterMember").FirstOrDefault() is { } carried)
                        element.Payload = Written(carried);
                    break;
                }

            case "perform action" or "state perform action" or "transition perform action" or "include use case" or "event occurrence" or "exhibit state":
                {
                    // "perform x", "include x", "event m.sourceEvent": the usage is a subset of what it names.
                    foreach (var named in FindShallow(ctx, "ownedReferenceSubsetting"))
                        element.Add(Relate(RelationKind.Specialization, element, named));
                    break;
                }

            case "satisfy":
                {
                    // "satisfy R by x": one arrow, from what satisfies to what is satisfied.
                    var requirement = FindShallow(ctx, "ownedReferenceSubsetting").FirstOrDefault();
                    var subject = FindShallow(ctx, "satisfactionSubjectMember").FirstOrDefault();
                    if (requirement is not null)
                        element.Add(Relate(RelationKind.Satisfy, element, requirement, origin: subject));
                    break;
                }

            case "verify":
                {
                    var requirement = FindShallow(ctx, "ownedReferenceSubsetting").FirstOrDefault();
                    if (requirement is not null)
                        element.Add(Relate(RelationKind.Verify, element, requirement));
                    break;
                }

            case "transition":
                ReadTransition(element, ctx, origin: null);
                break;

            case "succession":
                {
                    var ends = FindShallow(ctx, "connectorEndMember");
                    for (var i = 0; i + 1 < ends.Count; i++)
                        element.Add(Relate(RelationKind.Succession, element, ends[i + 1], element.Name, ends[i]));
                    break;
                }

            case "dependency":
                {
                    // "dependency from a, b to c, d": an arrow from each client to each supplier.
                    var (clients, suppliers) = DependencyEnds(ctx);
                    foreach (var supplier in suppliers)
                    {
                        if (clients.Count == 0)
                            element.Add(Relate(RelationKind.Dependency, element, supplier));
                        foreach (var client in clients)
                            element.Add(Relate(RelationKind.Dependency, element, supplier, origin: client));
                    }

                    break;
                }
        }
    }

    /// <summary>A relation whose ends are written in the text, with where they are written.</summary>
    private static Relation Relate(RelationKind kind, Element source, ParserRuleContext target,
                                    string? label = null, ParserRuleContext? origin = null)
        => new(kind, source, Written(target), label, origin is null ? null : Written(origin))
        {
            TargetSpan = Span(target),
            OriginSpan = origin is null ? null : Span(origin),
        };

    private static TextSpan Span(ParserRuleContext ctx)
        => new(ctx.Start.StartIndex, Math.Max(ctx.Start.StartIndex, (ctx.Stop ?? ctx.Start).StopIndex));

    /// <summary>The names a dependency lists after "from" and after "to".</summary>
    private static (List<ParserRuleContext> Clients, List<ParserRuleContext> Suppliers) DependencyEnds(ParserRuleContext ctx)
    {
        var clients = new List<ParserRuleContext>();
        var suppliers = new List<ParserRuleContext>();
        var afterTo = false;
        for (var i = 0; i < ctx.ChildCount; i++)
        {
            var child = ctx.GetChild(i);
            if (child is ITerminalNode terminal)
            {
                if (terminal.GetText() == "to")
                    afterTo = true;
                continue;
            }

            if (child is ParserRuleContext q && RuleName(q) == "qualifiedName")
                (afterTo ? suppliers : clients).Add(q);
        }

        return (clients, suppliers);
    }

    /// <summary>
    /// Descendants with one of the given rule names, without crossing into a
    /// nested element.
    /// </summary>
    private static List<ParserRuleContext> FindShallow(ParserRuleContext ctx, params string[] ruleNames)
    {
        var wanted = new HashSet<string>(ruleNames);
        var found = new List<ParserRuleContext>();
        Collect(ctx, true);
        return found;

        void Collect(IParseTree node, bool isRoot)
        {
            for (var i = 0; i < node.ChildCount; i++)
            {
                if (node.GetChild(i) is not ParserRuleContext child)
                    continue;

                var rule = RuleName(child);
                if (wanted.Contains(rule))
                {
                    found.Add(child);
                    continue;
                }

                if (!isRoot && TryKind(rule, out _))
                    continue; // a nested element owns whatever is inside it

                if (rule is "metadataFeature" or "prefixMetadataMember" or "prefixMetadataAnnotation")
                    continue; // metadata is read on its own, never as the element's value or type

                if (rule is "flowPayloadFeatureMember" or "payloadFeatureMember")
                    continue; // what a message carries has a name and a type, and neither is the message's

                Collect(child, false);
            }
        }
    }

    private static string RuleName(ParserRuleContext ctx) => SysMLv2Parser.ruleNames[ctx.RuleIndex];

    /// <summary>The source text of a context, exactly as written.</summary>
    private static string Written(ParserRuleContext ctx)
    {
        var stop = ctx.Stop ?? ctx.Start;
        return ctx.Start.InputStream.GetText(new Antlr4.Runtime.Misc.Interval(ctx.Start.StartIndex, stop.StopIndex));
    }

    private static string DocText(ParserRuleContext documentation)
    {
        var comment = documentation.GetChild(documentation.ChildCount - 1).GetText();
        return CleanComment(comment);
    }

    /// <summary>Strips "/*", "*/" and the leading "*" of continuation lines.</summary>
    public static string CleanComment(string comment)
    {
        var text = comment.Trim();
        if (text.StartsWith("/*", StringComparison.Ordinal))
            text = text[2..];
        if (text.EndsWith("*/", StringComparison.Ordinal))
            text = text[..^2];

        var lines = text.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.TrimStart().TrimStart('*').Trim());
        return string.Join(" ", lines.Where(l => l.Length > 0)).Trim();
    }

    private static string AppendDoc(string? existing, string added)
        => string.IsNullOrEmpty(existing) ? added : existing + " " + added;

    private static string? Unquote(string? name)
    {
        if (name is null)
            return null;
        if (name.Length >= 2 && name[0] == '\'' && name[^1] == '\'')
            return name[1..^1];
        return name;
    }
}
