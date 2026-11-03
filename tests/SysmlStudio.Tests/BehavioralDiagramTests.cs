using System.Xml.Linq;
using SysmlStudio.Diagrams;
using SysmlStudio.Editing;
using SysmlStudio.Model;
using Xunit;

namespace SysmlStudio.Tests;

/// <summary>
/// The behavioural and interaction diagrams: what each one draws of what the
/// model says, that it is offered only where there is something to draw, and
/// that it still says so in an exported file.
/// </summary>
public sealed class BehavioralDiagramTests
{
    private static SysmlWorkspace Fixture() =>
        SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures"));

    private static Diagram Laid(DiagramKind kind, string root)
    {
        var diagram = DiagramBuilder.Build(kind, Fixture().Find(root)!);
        DiagramLayout.Apply(diagram);
        return diagram;
    }

    private static string Edge(DiagramEdge edge) => $"{edge.Source.Label}>{edge.Target.Label}";

    private static readonly List<string> GaugeMarks = ["full", "draining", "dry"];

    // ----- use case ------------------------------------------------------------

    [Fact]
    public void AUseCaseDiagramDrawsItsActorSubjectAndWhatItIncludes()
    {
        var diagram = Laid(DiagramKind.UseCase, "SampleBehaviour::Refuel");

        var actor = Assert.Single(diagram.Nodes, n => n.Notation == NodeNotation.Actor);
        Assert.Equal("Driver", actor.Label);

        var boundary = Assert.Single(diagram.Nodes, n => n.Notation == NodeNotation.Boundary);
        Assert.Equal("Vehicle", boundary.Label);

        var refuel = diagram.Nodes.Single(n => n.Label == "Refuel");
        Assert.Equal(NodeNotation.UseCase, refuel.Notation);
        Assert.Same(boundary, refuel.Container);

        var include = Assert.Single(diagram.Edges, e => e.Label == "«include»");
        Assert.Equal("Refuel>PayForFuel", Edge(include));
        Assert.Equal(RelationKind.Dependency, include.Kind);

        var association = Assert.Single(diagram.Edges, e => e.Notation == EdgeNotation.Association);
        Assert.Equal("Driver>Refuel", Edge(association));
    }

    [Fact]
    public void AUseCaseStandsInsideItsBoundaryAndAnActorOutsideIt()
    {
        var diagram = Laid(DiagramKind.UseCase, "SampleBehaviour::Refuel");
        var boundary = diagram.Nodes.Single(n => n.Notation == NodeNotation.Boundary);

        foreach (var useCase in diagram.Nodes.Where(n => n.Notation == NodeNotation.UseCase))
        {
            Assert.True(useCase.X >= boundary.X && useCase.X + useCase.Width <= boundary.X + boundary.Width
                        && useCase.Y >= boundary.Y && useCase.Y + useCase.Height <= boundary.Y + boundary.Height,
                $"{useCase.Label} is not inside the boundary");
        }

        var actor = diagram.Nodes.Single(n => n.Notation == NodeNotation.Actor);
        Assert.True(actor.X + actor.Width <= boundary.X, "the actor stands inside the boundary");
    }

    [Fact]
    public void APackageDrawsOneFigureForAnActorManyUseCasesName()
    {
        var workspace = SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "Samples", "drone"));
        var diagram = DiagramBuilder.Build(DiagramKind.UseCase, workspace.Find("DroneInteractions")!);

        Assert.Single(diagram.Nodes, n => n.Notation == NodeNotation.Actor && n.Label == "Operator");
        Assert.Single(diagram.Nodes, n => n.Notation == NodeNotation.Boundary);
        Assert.True(diagram.Edges.Count(e => e.Source.Label == "Operator") >= 3);
    }

    // ----- activity -------------------------------------------------------------

    [Fact]
    public void AnActivityDrawsControlNodesAndGuards()
    {
        var diagram = Laid(DiagramKind.Activity, "SampleBehaviour::Check");

        Assert.Contains(diagram.Nodes, n => n.Pseudo == "start");
        Assert.Contains(diagram.Nodes, n => n.Pseudo == "done");
        Assert.Equal(2, diagram.Nodes.Count(n => n.Notation == NodeNotation.Decision)); // the merge and the decide
        Assert.Equal(2, diagram.Nodes.Count(n => n.Notation == NodeNotation.Bar)); // the fork and the join

        Assert.Contains(diagram.Edges, e => e.Label == "[tooLow]" && e.Target.Label == "fill");
        Assert.Contains(diagram.Edges, e => e.Label == "[else]" && e.Target.Label == "go");
    }

    /// <summary>"fork; then left; then right;" leaves the fork twice: it is not a chain.</summary>
    [Fact]
    public void AForkFansOutAndAJoinTakesBothBack()
    {
        var edges = Laid(DiagramKind.Activity, "SampleBehaviour::Check").Edges;

        var fork = edges.Single(e => e.Target.Label == "left").Source;
        Assert.Equal(NodeNotation.Bar, fork.Notation);
        Assert.Same(fork, edges.Single(e => e.Target.Label == "right").Source);
        Assert.Equal(2, fork.Span);

        var join = edges.Single(e => e.Source.Label == "left").Target;
        Assert.Equal("joined", join.Label);
        Assert.Same(join, edges.Single(e => e.Source.Label == "right").Target);
    }

    [Fact]
    public void AnActivityIsTheActionFlowsBuilderInUmlsNotation()
    {
        var flow = Laid(DiagramKind.ActionFlow, "Sample::Boot");
        var activity = Laid(DiagramKind.Activity, "Sample::Boot");

        Assert.Equal(flow.Edges.Select(Edge), activity.Edges.Select(Edge));
        Assert.All(flow.Nodes.Where(n => !n.IsPseudo), n => Assert.Equal(NodeNotation.Box, n.Notation));
        Assert.All(activity.Nodes.Where(n => !n.IsPseudo), n =>
        {
            Assert.Equal(NodeNotation.Rounded, n.Notation);
            Assert.Null(n.Stereotype);
        });
    }

    // ----- state ----------------------------------------------------------------

    [Fact]
    public void AStateMachineDrawsInitialAndFinalPseudostates()
    {
        var diagram = Laid(DiagramKind.StateMachine, "SampleBehaviour::Tank");

        var initial = diagram.Edges.Single(e => e.Source.Pseudo == "start" && e.Source.Container is null);
        Assert.Equal("empty", initial.Target.Label);

        var final = diagram.Edges.Single(e => e.Target.Pseudo == "done");
        Assert.Equal("filling", final.Source.Label);
    }

    [Fact]
    public void AStateListsEntryDoAndExit()
    {
        var empty = Laid(DiagramKind.StateMachine, "SampleBehaviour::Tank").Nodes.Single(n => n.Label == "empty");

        Assert.Equal(["entry / closeValve", "do / watchLevel", "exit / openValve"], empty.Features);
        Assert.Equal(NodeNotation.Rounded, empty.Notation);
    }

    [Fact]
    public void ATransitionIsLabelledTriggerGuardEffect()
    {
        var diagram = Laid(DiagramKind.StateMachine, "SampleBehaviour::Tank");
        var open = diagram.Edges.Single(e => e.Source.Label == "empty" && e.Target.Label == "filling");

        Assert.Equal("nozzle [pumpOn] / startMeter", open.Label);
        Assert.Equal("open", open.Name);
    }

    [Fact]
    public void ACompositeStateHoldsItsStatesAndTheirTransitions()
    {
        var diagram = Laid(DiagramKind.StateMachine, "SampleBehaviour::Tank");
        var filling = diagram.Nodes.Single(n => n.Label == "filling");
        var slow = diagram.Nodes.Single(n => n.Label == "slow");
        var fast = diagram.Nodes.Single(n => n.Label == "fast");

        Assert.Equal(NodeNotation.CompositeState, filling.Notation);
        Assert.Same(filling, slow.Container);
        Assert.Same(filling, fast.Container);
        Assert.Contains(diagram.Nodes, n => n.Pseudo == "start" && ReferenceEquals(n.Container, filling));

        foreach (var inner in new[] { slow, fast })
        {
            Assert.True(inner.X >= filling.X && inner.X + inner.Width <= filling.X + filling.Width
                        && inner.Y >= filling.Y && inner.Y + inner.Height <= filling.Y + filling.Height,
                $"{inner.Label} is not inside its composite state");
        }

        var speedUp = diagram.Edges.Single(e => e.Name == "speedUp");
        Assert.All(speedUp.Waypoints, p => Assert.True(p.X >= filling.X && p.X <= filling.X + filling.Width && p.Y >= filling.Y && p.Y <= filling.Y + filling.Height));
    }

    [Fact]
    public void ATransitionToItselfIsALoopThatLeavesTheBox()
    {
        var diagram = Laid(DiagramKind.StateMachine, "SampleBehaviour::Tank");
        var loop = diagram.Edges.Single(e => e.Name == "overflow");

        Assert.Same(loop.Source, loop.Target);
        Assert.True(loop.Waypoints.Count >= 3);
        Assert.Contains(loop.Waypoints, p => p.X > loop.Source.X + loop.Source.Width);
    }

    [Fact]
    public void AParallelStatesRegionsStandSideBySideWithALineBetween()
    {
        var diagram = Laid(DiagramKind.StateMachine, "SampleBehaviour::Cooling");
        var fan = diagram.Nodes.Single(n => n.Label == "fan");
        var pump = diagram.Nodes.Single(n => n.Label == "pump");

        Assert.Equal(fan.Y, pump.Y);
        Assert.True(fan.X + fan.Width < pump.X);

        // There is no line unless the model says "parallel", and no composite box around the regions but the root's.
        var drawn = DiagramBuilder.Build(DiagramKind.StateMachine, Fixture().Find("SampleBehaviour::Tank")!);
        DiagramLayout.Apply(drawn);
        Assert.Empty(drawn.Nodes.Single(n => n.Label == "filling").Ticks);
    }

    [Fact]
    public void AParallelStateInsideAStateHasADashedLineBetweenItsRegions()
    {
        var workspace = SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "Samples", "drone"));
        var diagram = DiagramBuilder.Build(DiagramKind.StateMachine, workspace.Find("DroneBehaviour::Airframe")!);
        DiagramLayout.Apply(diagram);

        var regions = diagram.Nodes.Single(n => n.Label == "watching");
        var line = Assert.Single(regions.Ticks);
        var lights = diagram.Nodes.Single(n => n.Label == "lights");
        var radio = diagram.Nodes.Single(n => n.Label == "radio");
        Assert.True(lights.X + lights.Width <= regions.X + line && regions.X + line <= radio.X, $"the line at {line} is not between the regions");
        Assert.Contains(NodeMarks.For(regions).OfType<LineMark>(), l => l.Dashed);
    }

    // ----- sequence --------------------------------------------------------------

    /// <summary>
    /// The text writes reply first; "first hello then count" and "first count
    /// then reply" say it comes last. Written order only settles what they leave open.
    /// </summary>
    [Fact]
    public void ASequenceRunsInTheOrderTheModelStatesNotTheOrderItIsWritten()
    {
        var diagram = Laid(DiagramKind.Sequence, "SampleBehaviour::Handshake");

        var rows = diagram.Edges.OrderBy(e => e.Waypoints[0].Y).Select(e => e.Name).ToList();
        Assert.Equal(["hello", "count", "reply"], rows);

        var byRow = diagram.Edges.OrderBy(e => e.Row).Select(e => e.Name).ToList();
        Assert.Equal(["hello", "count", "reply"], byRow);
    }

    [Fact]
    public void AMessageFollowsThenAcrossTheWholeInteractionInTheDrone()
    {
        var workspace = SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "Samples", "drone"));
        var diagram = DiagramBuilder.Build(DiagramKind.Sequence, workspace.Find("DroneInteractions::RequestClearance")!);
        DiagramLayout.Apply(diagram);

        Assert.Equal(["order", "queryFix", "fix", "clearance"], diagram.Edges.OrderBy(e => e.Waypoints[0].Y).Select(e => e.Name));
    }

    [Fact]
    public void AMessageIsLevelAndRunsBetweenTheCentresOfItsLifelines()
    {
        var diagram = Laid(DiagramKind.Sequence, "SampleBehaviour::Handshake");
        var hello = diagram.Edges.Single(e => e.Name == "hello");

        Assert.Equal(hello.Waypoints[0].Y, hello.Waypoints[^1].Y);
        Assert.Equal("driver", hello.Source.Label);
        Assert.Equal("pump", hello.Target.Label);
        Assert.Equal(EdgeNotation.AsyncMessage, hello.Notation);
        Assert.Equal("hello : Fuel", hello.Label);

        var source = hello.Source.X + (hello.Source.Width / 2);
        Assert.Equal(source, hello.Waypoints[0].X, 1);
    }

    /// <summary>A succession flow's target waits for the transfer: the one thing SysML v2 says that reads as synchronous.</summary>
    [Fact]
    public void ASuccessionFlowIsDrawnFilledAndAMessageOpen()
    {
        var workspace = SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "Samples", "drone"));
        var diagram = DiagramBuilder.Build(DiagramKind.Sequence, workspace.Find("DroneInteractions::HandOver")!);
        DiagramLayout.Apply(diagram);

        var parcel = diagram.Edges.Single(e => e.Name == "parcel");
        Assert.Equal(EdgeNotation.SyncMessage, parcel.Notation);
        Assert.False(parcel.HasOpenHead);
        Assert.Equal("parcel : Parcel", parcel.Label);

        var receipt = diagram.Edges.Single(e => e.Name == "receipt");
        Assert.Equal(EdgeNotation.AsyncMessage, receipt.Notation);
        Assert.True(receipt.HasOpenHead);
        Assert.Equal("customer>bay", Edge(receipt));

        var svg = XDocument.Parse(SvgExporter.ToSvg(diagram)).Descendants().Where(e => e.Name.LocalName == "path").ToList();
        Assert.Contains(svg, p => p.Attribute("marker-end")?.Value == "url(#arrow)");
        Assert.Contains(svg, p => p.Attribute("marker-end")?.Value == "url(#open)");
    }

    /// <summary>"message ping;" names no ends: "event ping.sourceEvent" on a part says where it starts.</summary>
    [Fact]
    public void EventsOnALifelineNameTheEndsOfAMessageThatDoesNot()
    {
        var diagram = Laid(DiagramKind.Sequence, "SampleBehaviour::Silent");

        Assert.Equal("a>b", Edge(Assert.Single(diagram.Edges, e => e.Name == "ping")));
    }

    [Fact]
    public void AnActionOnALifelineIsAnActivationBarOverItsMessages()
    {
        var diagram = Laid(DiagramKind.Sequence, "SampleBehaviour::Handshake");
        var pump = diagram.Nodes.Single(n => n.Label == "pump");

        var bar = Assert.Single(pump.Activations);
        foreach (var name in new[] { "hello", "reply" })
        {
            var y = diagram.Edges.Single(e => e.Name == name).Waypoints[0].Y - pump.Y;
            Assert.True(bar.Top < y && y < bar.Bottom, $"{name} at {y} is outside the bar {bar}");
        }

        Assert.Empty(diagram.Nodes.Single(n => n.Label == "driver").Activations);
    }

    [Fact]
    public void AMessageToItsOwnLifelineIsALoop()
    {
        var diagram = Laid(DiagramKind.Sequence, "SampleBehaviour::Handshake");
        var count = diagram.Edges.Single(e => e.Name == "count");

        Assert.Same(count.Source, count.Target);
        Assert.Equal(4, count.Waypoints.Count);
        Assert.Contains(count.Waypoints, p => p.Y > count.Waypoints[0].Y);
    }

    [Fact]
    public void AnEventNoMessageUsesStandsOnItsLifelineAtItsPlace()
    {
        var pump = Laid(DiagramKind.Sequence, "SampleBehaviour::Handshake").Nodes.Single(n => n.Label == "pump");

        Assert.Equal("idle", Assert.Single(pump.Events).Label);
    }

    [Fact]
    public void ALifelineDraggedSidewaysKeepsItsRowAndTakesItsMessagesAlong()
    {
        var diagram = Laid(DiagramKind.Sequence, "SampleBehaviour::Handshake");
        var hello = diagram.Edges.Single(e => e.Name == "hello");
        var row = hello.Waypoints[0].Y;
        var pump = hello.Target;
        var top = pump.Y;

        pump.X += 240;
        pump.Y += 300; // a drag that also went down, which is not a place in time
        DiagramLayout.Route(diagram);

        Assert.Equal(top, pump.Y);
        Assert.Equal(row, hello.Waypoints[0].Y);
        Assert.Equal(pump.X + (pump.Width / 2) - 5, hello.Waypoints[^1].X, 1); // ends on the bar's left edge
    }

    // ----- communication ----------------------------------------------------------

    [Fact]
    public void ACommunicationDiagramNumbersTheSameMessagesOnOneLink()
    {
        var diagram = Laid(DiagramKind.Communication, "SampleBehaviour::Handshake");

        Assert.Equal(["driver : Driver", "pump : Pump"], diagram.Nodes.Select(n => n.Label));
        var link = diagram.Edges.Single(e => !ReferenceEquals(e.Source, e.Target));
        Assert.Equal(EdgeNotation.Association, link.Notation);

        Assert.Equal([1, 3], link.Messages.Select(m => m.Number));
        Assert.Equal(["hello : Fuel", "reply"], link.Messages.Select(m => m.Text));

        var loop = diagram.Edges.Single(e => ReferenceEquals(e.Source, e.Target));
        Assert.Equal(2, Assert.Single(loop.Messages).Number);
    }

    [Fact]
    public void ACommunicationLabelPointsTheWayEachMessageGoes()
    {
        var diagram = Laid(DiagramKind.Communication, "SampleBehaviour::Handshake");
        var link = diagram.Edges.Single(e => !ReferenceEquals(e.Source, e.Target));

        var lines = link.Label!.Split('\n');
        Assert.StartsWith("1: hello : Fuel ", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("3: reply ", lines[1], StringComparison.Ordinal);
        Assert.NotEqual(lines[0][^1], lines[1][^1]); // one each way

        // Swapping the boxes turns both arrows round.
        var before = (lines[0][^1], lines[1][^1]);
        (link.Source.X, link.Target.X, link.Source.Y, link.Target.Y) = (link.Target.X, link.Source.X, link.Target.Y, link.Source.Y);
        DiagramLayout.Route(diagram);
        var after = link.Label.Split('\n');
        Assert.NotEqual(before.Item1, after[0][^1]);
        Assert.NotEqual(before.Item2, after[1][^1]);
    }

    // ----- interaction overview ----------------------------------------------------

    [Fact]
    public void AnInteractionOverviewDrawsInteractionsAsReferencesInAFlow()
    {
        var diagram = Laid(DiagramKind.InteractionOverview, "SampleBehaviour::Overview");

        Assert.Equal(["Handshake", "Handshake"], diagram.Nodes.Where(n => n.Notation == NodeNotation.InteractionUse).Select(n => n.Label));
        Assert.Equal(NodeNotation.Rounded, diagram.Nodes.Single(n => n.Label == "pause").Notation);
        Assert.Contains(diagram.Nodes, n => n.Pseudo == "start");
        Assert.Contains(diagram.Nodes, n => n.Pseudo == "done");
        Assert.Equal(4, diagram.Edges.Count);
        Assert.Contains(diagram.Edges, e => e.Source.Pseudo == "start" && e.Target.Element.Name == "greet");
        Assert.Contains(diagram.Edges, e => e.Source.Element.Name == "pause" && e.Target.Element.Name == "greetAgain");
    }

    // ----- timing ------------------------------------------------------------------

    [Fact]
    public void TimingPlacesPortionsInTheOrderStatedAlongEachLife()
    {
        var diagram = Laid(DiagramKind.Timing, "SampleBehaviour::OneTank");

        Assert.Equal(["gauge : Pump", "tap : Pump"], diagram.Nodes.Where(n => n.Notation == NodeNotation.TimeRow).Select(n => n.Label));

        var gauge = GaugeMarks.ConvertAll(n => diagram.Nodes.Single(m => m.Label == n));
        Assert.Equal([NodeNotation.TimePoint, NodeNotation.TimeBand, NodeNotation.TimePoint], gauge.Select(n => n.Notation));
        Assert.True(gauge[0].X < gauge[1].X && gauge[1].X < gauge[2].X);
    }

    /// <summary>"first gauge.dry then tap.running" is the only thing that lines the two lives up.</summary>
    [Fact]
    public void AnOrderStatedBetweenLivesMovesTheLaterOneAlongTheAxis()
    {
        var diagram = Laid(DiagramKind.Timing, "SampleBehaviour::OneTank");
        var dry = diagram.Nodes.Single(n => n.Label == "dry");
        var running = diagram.Nodes.Single(n => n.Label == "running");

        Assert.True(running.X > dry.X, $"running at {running.X} is not after dry at {dry.X}");
        Assert.Equal("dry>running", Edge(Assert.Single(diagram.Edges)));
    }

    /// <summary>No durations are written, so none are drawn: equal columns, and an axis that says so.</summary>
    [Fact]
    public void TheTimeAxisInventsNoNumbers()
    {
        var diagram = Laid(DiagramKind.Timing, "SampleBehaviour::OneTank");
        var axis = diagram.Nodes.Single(n => n.Notation == NodeNotation.TimeAxis);

        Assert.Contains("no durations", axis.Label, StringComparison.Ordinal);
        Assert.DoesNotContain(NodeMarks.For(axis).OfType<TextMark>(), t => t.Text.Any(char.IsDigit));

        var gaps = axis.Ticks.Zip(axis.Ticks.Skip(1), (a, b) => Math.Round(b - a, 3)).Distinct();
        Assert.Single(gaps);
    }

    [Fact]
    public void APortionsLabelCarriesWhatTheModelSaysItsValuesAre()
    {
        var full = Laid(DiagramKind.Timing, "SampleBehaviour::OneTank").Nodes.Single(n => n.Label == "full");

        Assert.Equal("level = 100", full.Detail);
    }

    [Fact]
    public void NothingOnATimingDiagramCanBeDragged()
    {
        var diagram = Laid(DiagramKind.Timing, "SampleBehaviour::OneTank");
        Assert.All(diagram.Nodes, n => Assert.True(n.IsFixed, $"{n.Label} can be dragged"));
    }

    // ----- what is offered ----------------------------------------------------------

    [Theory]
    [InlineData("SampleBehaviour::Refuel", DiagramKind.UseCase)]
    [InlineData("SampleBehaviour", DiagramKind.UseCase)]
    [InlineData("SampleBehaviour::Check", DiagramKind.Activity)]
    [InlineData("SampleBehaviour::Check", DiagramKind.ActionFlow)]
    [InlineData("SampleBehaviour::Tank", DiagramKind.StateMachine)]
    [InlineData("SampleBehaviour::Handshake", DiagramKind.Sequence)]
    [InlineData("SampleBehaviour::Handshake", DiagramKind.Communication)]
    [InlineData("SampleBehaviour::Overview", DiagramKind.InteractionOverview)]
    [InlineData("SampleBehaviour::OneTank", DiagramKind.Timing)]
    public void AKindIsOfferedWhereThereIsSomethingToDraw(string element, DiagramKind kind)
    {
        Assert.Contains(kind, DiagramBuilder.KindsFor(Fixture().Find(element)!));
    }

    [Theory]
    [InlineData("SampleBehaviour::NothingHappens", DiagramKind.Sequence)]
    [InlineData("SampleBehaviour::NothingHappens", DiagramKind.Communication)]
    [InlineData("SampleBehaviour::NothingHappens", DiagramKind.Timing)]
    [InlineData("SampleBehaviour::NothingHappens", DiagramKind.UseCase)]
    [InlineData("SampleBehaviour::NothingHappens", DiagramKind.InteractionOverview)]
    [InlineData("SampleBehaviour::Check", DiagramKind.InteractionOverview)]
    [InlineData("SampleBehaviour::Check", DiagramKind.Sequence)]
    [InlineData("SampleBehaviour::Handshake", DiagramKind.Activity)]
    [InlineData("SampleBehaviour::Handshake", DiagramKind.Timing)]
    [InlineData("SampleBehaviour::Tank", DiagramKind.Timing)]
    [InlineData("Sample", DiagramKind.UseCase)]
    public void AKindIsNotOfferedWhereItWouldDrawNothing(string element, DiagramKind kind)
    {
        Assert.DoesNotContain(kind, DiagramBuilder.KindsFor(Fixture().Find(element)!));
    }

    [Fact]
    public void ASysmlKindStillComesFirstOnWhatItAlwaysDrew()
    {
        var workspace = Fixture();
        Assert.Equal(DiagramKind.ActionFlow, DiagramBuilder.KindsFor(workspace.Find("Sample::Boot")!)[0]);
        Assert.Equal(DiagramKind.StateMachine, DiagramBuilder.KindsFor(workspace.Find("Sample::Running")!)[0]);
    }

    // ----- no overlaps, and the export ---------------------------------------------

    public static TheoryData<DiagramKind, string> EveryKind => new()
    {
        { DiagramKind.UseCase, "SampleBehaviour::Refuel" },
        { DiagramKind.Activity, "SampleBehaviour::Check" },
        { DiagramKind.StateMachine, "SampleBehaviour::Tank" },
        { DiagramKind.Sequence, "SampleBehaviour::Handshake" },
        { DiagramKind.Communication, "SampleBehaviour::Handshake" },
        { DiagramKind.InteractionOverview, "SampleBehaviour::Overview" },
        { DiagramKind.Timing, "SampleBehaviour::OneTank" },
    };

    /// <summary>A box inside its container is the point; anything else on top of another is a defect.</summary>
    [Theory]
    [MemberData(nameof(EveryKind))]
    public void NoTwoNodesOverlapUnlessOneHoldsTheOther(DiagramKind kind, string root)
    {
        var diagram = Laid(kind, root);
        var nodes = diagram.Nodes.Where(n => n.Notation is not (NodeNotation.Frame or NodeNotation.TimeRow or NodeNotation.TimeAxis)).ToList();

        foreach (var a in nodes)
        {
            foreach (var b in nodes.Where(o => !ReferenceEquals(o, a) && !Within(o, a) && !Within(a, o)))
            {
                var apart = a.X + a.Width <= b.X + 0.5 || b.X + b.Width <= a.X + 0.5 || a.Y + a.Height <= b.Y + 0.5 || b.Y + b.Height <= a.Y + 0.5;
                Assert.True(apart, $"'{a.Label}' overlaps '{b.Label}'");
            }
        }
    }

    private static bool Within(DiagramNode inner, DiagramNode container)
    {
        for (var c = inner.Container; c is not null; c = c.Container)
        {
            if (ReferenceEquals(c, container))
                return true;
        }

        return false;
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void EveryEdgeHasARoute(DiagramKind kind, string root)
    {
        Assert.All(Laid(kind, root).Edges, e => Assert.True(e.Waypoints.Count >= 2, $"{e} has no route"));
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void TheSvgIsWellFormedAndKeepsEveryLabel(DiagramKind kind, string root)
    {
        var diagram = Laid(kind, root);
        var svg = XDocument.Parse(SvgExporter.ToSvg(diagram));
        var texts = string.Concat(svg.Descendants().Where(e => e.Name.LocalName == "text").Select(e => e.Value));

        // An unnamed decision or fork has nothing to say; a frame says its name in its tag.
        foreach (var node in diagram.Nodes.Where(n => !n.IsPseudo && !(n.Notation is NodeNotation.Decision or NodeNotation.Bar && n.Element.Name is null)))
            Assert.Contains(node.Label.Split(' ')[0], texts, StringComparison.Ordinal);
    }

    private static List<XElement> SvgOf(DiagramKind kind, string root, string element)
        => [.. XDocument.Parse(SvgExporter.ToSvg(Laid(kind, root))).Descendants().Where(e => e.Name.LocalName == element)];

    [Fact]
    public void TheSvgDrawsAUseCaseAsAnEllipseAndAnActorWithLimbs()
    {
        Assert.Equal(2 + 1, SvgOf(DiagramKind.UseCase, "SampleBehaviour::Refuel", "ellipse").Count); // two use cases and a head
        Assert.Equal(4, SvgOf(DiagramKind.UseCase, "SampleBehaviour::Refuel", "line").Count);
    }

    [Fact]
    public void TheSvgDrawsALifelineDashedAndAMessageOpenHeaded()
    {
        var lines = SvgOf(DiagramKind.Sequence, "SampleBehaviour::Handshake", "line");
        Assert.Equal(2, lines.Count(l => l.Attribute("stroke-dasharray") is not null));

        var messages = SvgOf(DiagramKind.Sequence, "SampleBehaviour::Handshake", "path")
            .Where(p => p.Attribute("marker-end") is not null).ToList();
        Assert.Equal(3, messages.Count);
        Assert.All(messages, m =>
        {
            Assert.Equal("url(#open)", m.Attribute("marker-end")!.Value);
            Assert.Null(m.Attribute("stroke-dasharray"));
        });
    }

    [Fact]
    public void TheSvgDrawsDecisionsAsDiamondsAndForksAsBars()
    {
        var diamonds = SvgOf(DiagramKind.Activity, "SampleBehaviour::Check", "polygon");
        Assert.Equal(2, diamonds.Count);
        Assert.All(diamonds, d => Assert.Equal(4, d.Attribute("points")!.Value.Split(' ').Length));

        var bars = SvgOf(DiagramKind.Activity, "SampleBehaviour::Check", "rect").Where(r => r.Attribute("height")!.Value == "7");
        Assert.Equal(2, bars.Count());
    }

    [Fact]
    public void TheSvgDrawsAnAssociationWithNoHead()
    {
        var paths = SvgOf(DiagramKind.UseCase, "SampleBehaviour::Refuel", "path");
        Assert.Single(paths, p => p.Attribute("marker-end") is null && p.Parent!.Name.LocalName == "g");
    }

    // ----- the model and edits ------------------------------------------------------

    [Fact]
    public void ControlNodesAndMessagesAreElementsOfTheModel()
    {
        var workspace = Fixture();
        var check = workspace.Find("SampleBehaviour::Check")!;

        Assert.Contains(check.Children, c => c.Kind == "decide" && c.Name is null);
        Assert.Contains(check.Children, c => c.Kind == "merge" && c.Name == "again");
        Assert.Contains(check.Children, c => c.Kind == "fork");
        Assert.Contains(check.Children, c => c.Kind == "join" && c.Name == "joined");

        var hello = workspace.Find("SampleBehaviour::Handshake::hello")!;
        Assert.Equal("message", hello.Kind);
        Assert.Equal("Fuel", hello.Payload);
        var relation = Assert.Single(hello.Relations, r => r.Kind == RelationKind.Message);
        Assert.Equal("driver.ask", relation.OriginReference);
        Assert.Equal("pump.serve.listen", relation.TargetReference);
    }

    [Fact]
    public void APortionKnowsWhichKindItIs()
    {
        var workspace = Fixture();
        Assert.Equal("snapshot", workspace.Find("SampleBehaviour::OneTank::gauge::full")!.PortionKind);
        Assert.Equal("timeslice", workspace.Find("SampleBehaviour::OneTank::gauge::draining")!.PortionKind);
    }

    /// <summary>What a message carries may have a name and a type of its own; neither is the message's.</summary>
    [Fact]
    public void AMessagesNameIsNotItsPayloads()
    {
        var workspace = Fixture();
        var flow = workspace.Find("SampleBehaviour::Handshake::hello")!;
        Assert.DoesNotContain(flow.Relations, r => r.Kind == RelationKind.Typing);

        var named = workspace.Elements.Single(e => e.Kind == "message" && e.Payload == "litres : Fuel");
        Assert.Null(named.Name);
    }

    /// <summary>"then retry;" is a reference like any other: renaming what it names has to rewrite it.</summary>
    [Fact]
    public void RenameFollowsAReferenceWrittenAfterThen()
    {
        var workspace = Fixture();
        var plan = ModelEditor.Rename(workspace, workspace.Find("SampleBehaviour::Check::joined")!, "together");

        Assert.Equal(3, plan.Edits.Count); // the declaration and two "then joined"
    }

    [Fact]
    public void DrawingAMessageWritesOneThatParsesAndFollowsTheOthers()
    {
        var workspace = Fixture();
        var handshake = workspace.Find("SampleBehaviour::Handshake")!;
        var plan = ModelEditor.AddRelation(NewRelation.Message, workspace.Find("SampleBehaviour::Handshake::driver")!,
            workspace.Find("SampleBehaviour::Handshake::pump")!, handshake);
        EditApplier.Apply(workspace, plan);

        Assert.Empty(workspace.Files.SelectMany(f => f.Errors.Where(e => !e.Message.Contains("Broken", StringComparison.Ordinal))));
        var diagram = DiagramBuilder.Build(DiagramKind.Sequence, workspace.Find("SampleBehaviour::Handshake")!);
        DiagramLayout.Apply(diagram);
        Assert.Equal("message1", diagram.Edges.OrderBy(e => e.Waypoints[0].Y).Last().Name);
    }

    [Fact]
    public void DrawingAnIncludeWritesOneThatDrawsItsArrow()
    {
        var workspace = Fixture();
        var plan = ModelEditor.AddRelation(NewRelation.Include, workspace.Find("SampleBehaviour::PayForFuel")!,
            workspace.Find("SampleBehaviour::Refuel")!, workspace.Find("SampleBehaviour::PayForFuel")!);
        EditApplier.Apply(workspace, plan);

        var diagram = DiagramBuilder.Build(DiagramKind.UseCase, workspace.Find("SampleBehaviour::PayForFuel")!);
        Assert.Contains(diagram.Edges, e => e.Label == "«include»" && e.Source.Label == "PayForFuel" && e.Target.Label == "Refuel");
    }
}
