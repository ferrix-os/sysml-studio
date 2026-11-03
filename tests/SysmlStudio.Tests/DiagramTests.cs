using SysmlStudio.Diagrams;
using SysmlStudio.Model;
using Xunit;

namespace SysmlStudio.Tests;

/// <summary>
/// Every node is an element the model declares and every edge is a relation it
/// writes down, and after layout nothing sits on top of anything else.
/// </summary>
public sealed class DiagramTests
{
    private static SysmlWorkspace Fixture() =>
        SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures"));

    [Fact]
    public void APackageOffersDefinitionAndRequirementDiagrams()
    {
        var workspace = Fixture();
        var kinds = DiagramBuilder.KindsFor(workspace.Find("Sample")!);

        Assert.Contains(DiagramKind.Definition, kinds);
        Assert.Contains(DiagramKind.Requirements, kinds);
    }

    [Fact]
    public void ADefinitionDiagramHoldsTheDefinitionsOfItsPackage()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Definition, Fixture().Find("Sample")!);

        Assert.Contains(diagram.Nodes, n => n.Label == "Engine");
        Assert.Contains(diagram.Nodes, n => n.Label == "Vehicle");
        Assert.All(diagram.Nodes, n => Assert.True(n.Element.IsDefinition));
        Assert.Contains(diagram.Edges, e =>
            e.Source.Label == "Wheel" && e.Target.Label == "RollingThing" && e.Kind == RelationKind.Specialization);
    }

    [Fact]
    public void ADefinitionNodeListsItsFeatures()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Definition, Fixture().Find("Sample")!);
        var engine = diagram.Nodes.Single(n => n.Label == "Engine");

        Assert.Contains("power : Real", engine.Features);
    }

    [Fact]
    public void AnInterconnectionDiagramDrawsTheConnection()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Interconnection, Fixture().Find("Sample::Vehicle")!);

        Assert.Equal(2, diagram.Nodes.Count);
        var edge = Assert.Single(diagram.Edges);
        Assert.Equal("engine : Engine", edge.Source.Label);
        Assert.Equal("wheels : Wheel[4]", edge.Target.Label);
    }

    [Fact]
    public void ARequirementsDiagramTracesSatisfyToItsRequirement()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Requirements, Fixture().Find("Sample")!);

        Assert.Contains(diagram.Nodes, n => n.Element.ShortName == "R.1");
        Assert.Contains(diagram.Edges, e => e.Kind == RelationKind.Satisfy
            && e.Target.Element.ShortName == "R.1"
            && e.Source.Element.QualifiedName == "Sample::Vehicle::engine");
    }

    [Fact]
    public void AnActionFlowFollowsItsSuccessions()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.ActionFlow, Fixture().Find("Sample::StartUp")!);

        Assert.Equal(2, diagram.Nodes.Count);
        var edge = Assert.Single(diagram.Edges);
        Assert.Equal("crank", edge.Source.Label);
        Assert.Equal("idle", edge.Target.Label);
    }

    [Fact]
    public void AStateMachineFollowsItsTransitions()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.StateMachine, Fixture().Find("Sample::Running")!);

        var edge = Assert.Single(diagram.Edges, e => e.Kind == RelationKind.Transition);
        Assert.Equal("off", edge.Source.Label);
        Assert.Equal("on", edge.Target.Label);
    }

    [Fact]
    public void LayoutGivesEveryNodeASizeAndNoOverlap()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Definition, Fixture().Find("Sample")!);
        DiagramLayout.Apply(diagram);

        Assert.All(diagram.Nodes, n =>
        {
            Assert.True(n.Width > 0, "node has a width");
            Assert.True(n.Height > 0, "node has a height");
        });

        foreach (var a in diagram.Nodes)
        {
            foreach (var b in diagram.Nodes.Where(other => !ReferenceEquals(other, a)))
            {
                var apart = a.X + a.Width <= b.X || b.X + b.Width <= a.X
                    || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y;
                Assert.True(apart, $"'{a.Label}' overlaps '{b.Label}'");
            }
        }
    }

    [Fact]
    public void LayoutRoutesEveryEdge()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Definition, Fixture().Find("Sample")!);
        DiagramLayout.Apply(diagram);

        Assert.All(diagram.Edges, e => Assert.NotEmpty(e.Waypoints));
    }

    [Fact]
    public void ADefinitionDiagramDrawsCompositionFromTheOwnerToThePartsType()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Definition, Fixture().Find("Sample")!);

        Assert.Contains(diagram.Edges, e => e.Kind == RelationKind.Composition
            && e.Source.Label == "Vehicle" && e.Target.Label == "Engine" && e.Label == "engine");
    }

    [Fact]
    public void OneDefinitionShowsWhatItIsMadeOfAndWhatIsMadeOfIt()
    {
        var workspace = Fixture();
        var engine = DiagramBuilder.Build(DiagramKind.Definition, workspace.Find("Sample::Engine")!);

        // Vehicle has a part typed by Engine, so it is one relation away.
        Assert.Contains(engine.Nodes, n => n.Label == "Vehicle");
        Assert.Contains(engine.Edges, e => e.Kind == RelationKind.Composition && e.Target.Label == "Engine");
    }

    [Fact]
    public void ARequirementOffersItsOwnDiagram()
    {
        var requirement = Fixture().Find("Sample::MustStart")!;
        Assert.Contains(DiagramKind.Requirements, DiagramBuilder.KindsFor(requirement));

        var diagram = DiagramBuilder.Build(DiagramKind.Requirements, requirement);
        Assert.Contains(diagram.Edges, e => e.Kind == RelationKind.Satisfy && ReferenceEquals(e.Target.Element, requirement));
    }

    [Fact]
    public void ThenShorthandBecomesAFlowFromStartToDone()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.ActionFlow, Fixture().Find("Sample::Boot")!);

        static string Label(DiagramNode n) => n.Label;
        var edges = diagram.Edges.ConvertAll(e => $"{Label(e.Source)}>{Label(e.Target)}");
        Assert.Equal(["start>check", "check>load", "load>done"], edges);
        Assert.Contains(diagram.Nodes, n => n.Pseudo == "start");
        Assert.Contains(diagram.Nodes, n => n.Pseudo == "done");
    }

    [Fact]
    public void DependenciesRunFromClientToSupplier()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Requirements, Fixture().Find("Sample")!);

        Assert.Contains(diagram.Edges, e => e.Kind == RelationKind.Dependency
            && e.Source.Element.Name == "MustStopToo" && e.Target.Element.Name == "MustStart");
    }

    [Fact]
    public void NoEdgeRunsThroughABoxItDoesNotConnect()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Definition, Fixture().Find("Sample")!);
        DiagramLayout.Apply(diagram);

        foreach (var edge in diagram.Edges)
        {
            Assert.NotEmpty(edge.Waypoints);
            foreach (var box in diagram.Nodes.Where(n => !ReferenceEquals(n, edge.Source) && !ReferenceEquals(n, edge.Target)))
            {
                var inside = edge.Waypoints.Any(p => p.X > box.X + 2 && p.X < box.X + box.Width - 2
                                                  && p.Y > box.Y + 2 && p.Y < box.Y + box.Height - 2);
                Assert.False(inside, $"{edge} runs through {box.Label}");
            }
        }
    }

    [Fact]
    public void BoxesThatGrewAfterPlacementArePushedApart()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Definition, Fixture().Find("Sample")!);
        DiagramLayout.Apply(diagram);

        // As if the canvas had drawn every box three times taller than estimated.
        foreach (var node in diagram.Nodes)
            node.Height *= 3;
        DiagramLayout.RemoveOverlaps(diagram);

        foreach (var a in diagram.Nodes)
        {
            foreach (var b in diagram.Nodes.Where(o => !ReferenceEquals(o, a)))
            {
                var apart = a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y;
                Assert.True(apart, $"'{a.Label}' overlaps '{b.Label}'");
            }
        }
    }

    [Fact]
    public void UnrelatedGroupsAreLaidOutApartAndNeverInterleave()
    {
        var diagram = DiagramBuilder.Build(DiagramKind.Definition, Fixture().Find("Sample")!);
        DiagramLayout.Apply(diagram);

        // Everything reachable from Vehicle along edges is one group; the rest is not connected to it.
        var group = new List<DiagramNode> { diagram.Nodes.Single(n => n.Label == "Vehicle") };
        for (var i = 0; i < group.Count; i++)
        {
            var node = group[i];
            group.AddRange([.. diagram.Edges
                .Where(e => ReferenceEquals(e.Source, node) || ReferenceEquals(e.Target, node))
                .Select(e => ReferenceEquals(e.Source, node) ? e.Target : e.Source)
                .Where(n => !group.Contains(n)).Distinct()]);
        }

        var others = diagram.Nodes.Except(group).ToList();
        Assert.NotEmpty(others);

        var left = group.Min(n => n.X);
        var right = group.Max(n => n.X + n.Width);
        var top = group.Min(n => n.Y);
        var bottom = group.Max(n => n.Y + n.Height);
        foreach (var other in others)
        {
            var outside = other.X >= right || other.X + other.Width <= left || other.Y >= bottom || other.Y + other.Height <= top;
            Assert.True(outside, $"'{other.Label}' sits inside the Vehicle group");
        }
    }

    // ----- the structural UML kinds --------------------------------------------

    private static SysmlWorkspace Garage() =>
        SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "UmlFixtures"));

    private static Diagram GarageDiagram(DiagramKind kind, string root) => DiagramBuilder.Build(kind, Garage().Find(root)!);

    [Fact]
    public void APackageDiagramDrawsNestingAndImportsAsUmlSaysThem()
    {
        var diagram = GarageDiagram(DiagramKind.Package, "Garage");

        Assert.All(diagram.Nodes, n => Assert.Equal(NodeShape.Folder, n.Shape));
        Assert.Contains(diagram.Edges, e => e.Kind == RelationKind.Containment && e.Source.Label == "Garage" && e.Target.Label == "Tools");
        Assert.Contains(diagram.Edges, e => e.Kind == RelationKind.Import && e.Source.Label == "Garage" && e.Target.Label == "Parts" && e.Label == "«import»");
        Assert.Contains(diagram.Edges, e => e.Kind == RelationKind.Import && e.Source.Label == "Garage" && e.Target.Label == "Helpers" && e.Label == "«access»");

        // What is imported from outside the loaded model can only be named.
        Assert.DoesNotContain(diagram.Nodes, n => n.Label == "ScalarValues");
        Assert.Contains("«access» ScalarValues", diagram.Nodes.Single(n => n.Label == "Garage").Features);
    }

    [Fact]
    public void APackageThatOnlyImportsWhatIsNotLoadedHasNoPackageDiagram()
    {
        var workspace = Garage();
        Assert.DoesNotContain(DiagramKind.Package, DiagramBuilder.KindsFor(workspace.Find("Empty")!));
        Assert.Contains(DiagramKind.Package, DiagramBuilder.KindsFor(workspace.Find("Parts")!));
    }

    [Fact]
    public void AClassDiagramHasCompartmentsAndUmlsEdges()
    {
        var diagram = GarageDiagram(DiagramKind.Class, "Garage");
        var lift = diagram.Nodes.Single(n => n.Label == "Lift");
        var machine = diagram.Nodes.Single(n => n.Label == "Machine");

        Assert.Equal(NodeShape.Class, lift.Shape);
        Assert.Contains("+ load : Real", lift.Features);
        Assert.Contains("- hours : Real", lift.Features);
        Assert.Contains("+ «port» supply : ~Mains", lift.Features);
        Assert.Contains("+ start(in key : String, out ok : Boolean)", machine.Operations);
        Assert.True(machine.IsAbstract);
        Assert.Equal("{abstract}", machine.Stereotype);
        Assert.Null(lift.Stereotype);
        Assert.Contains("petrol", diagram.Nodes.Single(n => n.Label == "Fuel").Features);
        Assert.Equal("«enumeration»", diagram.Nodes.Single(n => n.Label == "Fuel").Stereotype);

        Assert.Contains(diagram.Edges, e => e.Kind == RelationKind.Specialization && e.Source == lift && e.Target == machine);
        var composition = Assert.Single(diagram.Edges, e => e.Kind == RelationKind.Composition && e.Source == lift && e.Target.Label == "Arm");
        Assert.True(composition.IsHeadless);
        Assert.Equal("arm", composition.Label);
        Assert.Contains(diagram.Edges, e => e.Kind == RelationKind.Association && e.Source == lift && e.Target.Label == "Mechanic" && e.Label == "owner");

        // A property is an edge or a compartment line, not both.
        Assert.DoesNotContain(lift.Features, f => f.Contains("arm", StringComparison.Ordinal));

        // Behaviour and metadata have diagrams of their own.
        Assert.DoesNotContain(diagram.Nodes, n => n.Label == "Safety");
    }

    [Fact]
    public void AnObjectDiagramUnderlinesInstancesWithTheirSlotsAndLinks()
    {
        var diagram = GarageDiagram(DiagramKind.ObjectDiagram, "Garage");
        var mine = diagram.Nodes.Single(n => n.Label == "mine : MyLift");

        Assert.Equal(NodeShape.Instance, mine.Shape);
        Assert.Equal("«individual»", mine.Stereotype);
        Assert.Contains("load = 2.5", mine.Features);

        var hoist = diagram.Nodes.Single(n => n.Label == "hoist : Arm");
        Assert.Contains("serial = \"H-1\"", hoist.Features);
        Assert.Contains(diagram.Edges, e => e.Kind == RelationKind.Composition && e.Source == mine && e.Target == hoist);

        // A value that names another object on the diagram is a link.
        Assert.Contains(diagram.Edges, e => e.Kind == RelationKind.Link && e.Source == mine && e.Target.Label == "alice : Mechanic" && e.Label == "keeper");
        Assert.DoesNotContain(mine.Features, f => f.StartsWith("keeper", StringComparison.Ordinal));

        // A usage is an object because it binds values, not for being a usage.
        Assert.Contains(diagram.Nodes, n => n.Label == "configured : Lift");
        Assert.DoesNotContain(diagram.Nodes, n => n.Element.Name == "lift");
    }

    [Fact]
    public void AComponentDiagramPutsPortsOnTheBorderAndWiresThem()
    {
        var diagram = GarageDiagram(DiagramKind.Component, "Garage");
        var socket = diagram.Nodes.Single(n => n.Label == "Socket");
        var lift = diagram.Nodes.Single(n => n.Label == "Lift");

        Assert.Equal(NodeShape.Component, socket.Shape);
        Assert.DoesNotContain(diagram.Nodes, n => n.Label == "Quiet"); // no ports, no component
        Assert.Equal(PortFlow.Provided, socket.Ports.Single(p => p.Name == "out1").Flow);
        Assert.Equal(PortFlow.Required, lift.Ports.Single(p => p.Name == "supply").Flow); // ~Mains turns "out" round
        Assert.Contains("«provides» Mains", socket.Features);
        Assert.Contains("«requires» Mains", lift.Features);

        var wire = Assert.Single(diagram.Edges);
        Assert.Equal("out1", wire.SourcePort?.Name);
        Assert.Equal("supply", wire.TargetPort?.Name);
    }

    [Fact]
    public void ADefinitionsComponentDiagramIsItsPartsTypesInAFrame()
    {
        var diagram = GarageDiagram(DiagramKind.Component, "Garage::Workshop");

        Assert.Equal("«component» Workshop", diagram.FrameLabel);
        Assert.Equal(["Lift", "Socket"], diagram.Nodes.Select(n => n.Label));
    }

    [Fact]
    public void AnUnconnectedPortStillSitsOnTheBorder()
    {
        var diagram = GarageDiagram(DiagramKind.Component, "Garage");
        DiagramLayout.Apply(diagram);

        var spare = diagram.Nodes.Single(n => n.Label == "Socket").Ports.Single(p => p.Name == "spare");
        var socket = diagram.Nodes.Single(n => n.Label == "Socket");
        Assert.InRange(spare.X, 0, socket.Width);
        Assert.True(spare.Y == 0 || Math.Abs(spare.Y - socket.Height) < 0.01, "on the top or bottom border");
    }

    [Fact]
    public void ACompositeStructureIsTheInterconnectionInUmlsNotation()
    {
        var workspace = Garage();
        var plain = DiagramBuilder.Build(DiagramKind.Interconnection, workspace.Find("Garage::Workshop")!);
        var diagram = DiagramBuilder.Build(DiagramKind.CompositeStructure, workspace.Find("Garage::Workshop")!);

        // The same parts and connectors the interconnection diagram finds...
        Assert.Equal(plain.Nodes.Select(n => n.Label), diagram.Nodes.Select(n => n.Label));
        Assert.Equal(plain.Edges.Count, diagram.Edges.Count);

        // ...in a frame, with a dashed reference and the connector on its ports.
        Assert.Equal("Workshop", diagram.FrameLabel);
        Assert.True(diagram.Nodes.Single(n => n.Label == "boss : Mechanic").IsReference);
        Assert.False(diagram.Nodes.Single(n => n.Label == "lift : Lift").IsReference);
        Assert.Equal("«port»", diagram.Nodes.Single(n => n.Label == "door : Mains").Stereotype);
        var edge = Assert.Single(diagram.Edges);
        Assert.Equal("out1", edge.SourcePort?.Name);
        Assert.Equal("supply", edge.TargetPort?.Name);
    }

    [Fact]
    public void ConnectorsEndOnThePortsTheyName()
    {
        var diagram = GarageDiagram(DiagramKind.CompositeStructure, "Garage::Workshop");
        DiagramLayout.Apply(diagram);

        var edge = Assert.Single(diagram.Edges);
        var (x, y) = edge.Waypoints[0];
        Assert.Equal(edge.Source.X + edge.SourcePort!.X, x, 3);
        Assert.Equal(edge.Source.Y + edge.SourcePort.Y, y, 0.01 + 5);
        (x, y) = edge.Waypoints[^1];
        Assert.Equal(edge.Target.X + edge.TargetPort!.X, x, 3);
        Assert.Equal(edge.Target.Y + edge.TargetPort.Y, y, 0.01 + 5);
    }

    [Fact]
    public void ADeploymentDiagramPutsArtifactsOnNodes()
    {
        var diagram = GarageDiagram(DiagramKind.Deployment, "Garage");

        var lift = diagram.Nodes.Single(n => n.Label == "lift : Lift");
        var jack = diagram.Nodes.Single(n => n.Label == "Jack");
        Assert.Equal(NodeShape.Device, lift.Shape);
        Assert.Equal(NodeShape.Artifact, jack.Shape);
        Assert.Equal("«node»", lift.Stereotype);

        var deploy = Assert.Single(diagram.Edges, e => e.Source == jack);
        Assert.Equal(RelationKind.Allocate, deploy.Kind);
        Assert.Equal("«deploy»", deploy.Label);
        Assert.Equal(lift, deploy.Target);
        Assert.Contains(diagram.Edges, e => e.Name == "armOn" && e.Source.Label == "Arm");
    }

    [Fact]
    public void AnElementWithNoAllocationHasNoDeploymentDiagram()
    {
        var workspace = Garage();
        Assert.DoesNotContain(DiagramKind.Deployment, DiagramBuilder.KindsFor(workspace.Find("Garage::Quiet")!));
        Assert.Contains(DiagramKind.Deployment, DiagramBuilder.KindsFor(workspace.Find("Garage::Workshop")!));
        Assert.Contains(DiagramKind.Deployment, DiagramBuilder.KindsFor(workspace.Find("Garage")!));
        Assert.DoesNotContain(DiagramKind.Deployment, DiagramBuilder.KindsFor(workspace.Find("Helpers")!));
    }

    [Fact]
    public void AProfileDiagramDrawsStereotypesAndWhatTheyExtend()
    {
        var diagram = GarageDiagram(DiagramKind.Profile, "Garage");
        var safety = diagram.Nodes.Single(n => n.Label == "Safety");

        Assert.Equal("«stereotype»", safety.Stereotype);
        Assert.Contains("+ level : Integer", safety.Features);
        Assert.Equal("«profile» Garage", diagram.FrameLabel);

        // What it says it annotates, what it is written on, and how often.
        Assert.Contains(diagram.Edges, e => e.Kind == RelationKind.Extension && e.Source == safety && e.Target.Label == "ItemDefinition" && e.Label is null);
        Assert.Contains(diagram.Edges, e => e.Kind == RelationKind.Extension && e.Source == safety && e.Target.Label == "PartDefinition" && e.Label == "2 uses");
        Assert.Equal("«metaclass»", diagram.Edges.First(e => e.Source == safety).Target.Stereotype);
        Assert.True(diagram.Edges.First(e => e.Source == safety).Target.IsPseudo);
    }

    [Fact]
    public void WhatHasNoStereotypeHasNoProfileDiagram()
    {
        var workspace = Garage();
        Assert.DoesNotContain(DiagramKind.Profile, DiagramBuilder.KindsFor(workspace.Find("Parts")!));
        Assert.Contains(DiagramKind.Profile, DiagramBuilder.KindsFor(workspace.Find("Garage::Safety")!));
    }

    [Theory]
    [InlineData(DiagramKind.Package, "Garage")]
    [InlineData(DiagramKind.Class, "Garage")]
    [InlineData(DiagramKind.ObjectDiagram, "Garage")]
    [InlineData(DiagramKind.Component, "Garage")]
    [InlineData(DiagramKind.CompositeStructure, "Garage::Workshop")]
    [InlineData(DiagramKind.Deployment, "Garage")]
    [InlineData(DiagramKind.Profile, "Garage")]
    public void EveryKindLaysOutWithNoOverlapAndNoEdgeThroughABox(DiagramKind kind, string root)
    {
        var diagram = GarageDiagram(kind, root);
        DiagramLayout.Apply(diagram);

        foreach (var a in diagram.Nodes)
        {
            foreach (var b in diagram.Nodes.Where(o => !ReferenceEquals(o, a)))
            {
                var apart = a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y;
                Assert.True(apart, $"'{a.Label}' overlaps '{b.Label}'");
            }
        }

        foreach (var edge in diagram.Edges)
        {
            Assert.NotEmpty(edge.Waypoints);
            foreach (var box in diagram.Nodes.Where(n => !ReferenceEquals(n, edge.Source) && !ReferenceEquals(n, edge.Target)))
            {
                var inside = edge.Waypoints.Any(p => p.X > box.X + 2 && p.X < box.X + box.Width - 2 && p.Y > box.Y + 2 && p.Y < box.Y + box.Height - 2);
                Assert.False(inside, $"{edge} runs through {box.Label}");
            }
        }
    }

    [Theory]
    [InlineData(DiagramKind.Package, "Garage", "url(#containment)")]
    [InlineData(DiagramKind.Package, "Garage", "«access»")]
    [InlineData(DiagramKind.Class, "Garage", "url(#diamond)")]
    [InlineData(DiagramKind.Class, "Garage", "url(#triangle)")]
    [InlineData(DiagramKind.Class, "Garage", "font-style=\"italic\"")]
    [InlineData(DiagramKind.ObjectDiagram, "Garage", "text-decoration=\"underline\"")]
    [InlineData(DiagramKind.Component, "Garage", "«component»")]
    [InlineData(DiagramKind.Component, "Garage", "<circle")]
    [InlineData(DiagramKind.CompositeStructure, "Garage::Workshop", "stroke-dasharray=\"4 3\"")]
    [InlineData(DiagramKind.CompositeStructure, "Garage::Workshop", ">Workshop</text>")]
    [InlineData(DiagramKind.Deployment, "Garage", "«deploy»")]
    [InlineData(DiagramKind.Deployment, "Garage", "«node»")]
    [InlineData(DiagramKind.Profile, "Garage", "url(#extension)")]
    [InlineData(DiagramKind.Profile, "Garage", "«metaclass»")]
    public void TheSvgSaysWhatTheCanvasSays(DiagramKind kind, string root, string expected)
    {
        var diagram = GarageDiagram(kind, root);
        DiagramLayout.Apply(diagram);

        var svg = SvgExporter.ToSvg(diagram);
        Assert.Contains(expected, svg, StringComparison.Ordinal);
        _ = System.Xml.Linq.XDocument.Parse(svg); // and it is well formed
    }

    /// <summary>A connector ends on the port's square, where a head would hide the socket.</summary>
    [Fact]
    public void AConnectorInTheSvgHasNoHead()
    {
        var diagram = GarageDiagram(DiagramKind.CompositeStructure, "Garage::Workshop");
        DiagramLayout.Apply(diagram);

        var connector = SvgExporter.ToSvg(diagram).Split('\n').Single(l => l.Contains(">feed<", StringComparison.Ordinal));
        Assert.DoesNotContain("marker-end", connector, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFramedSvgHoldsTheWholeFrame()
    {
        var diagram = GarageDiagram(DiagramKind.CompositeStructure, "Garage::Workshop");
        DiagramLayout.Apply(diagram);

        var svg = System.Xml.Linq.XDocument.Parse(SvgExporter.ToSvg(diagram)).Root!;
        var (_, _, width, height) = diagram.FrameBounds()!.Value;
        Assert.True(double.Parse(svg.Attribute("width")!.Value, System.Globalization.CultureInfo.InvariantCulture) >= width);
        Assert.True(double.Parse(svg.Attribute("height")!.Value, System.Globalization.CultureInfo.InvariantCulture) >= height);
    }

    [Theory]
    [InlineData("structure")]
    [InlineData("deployment")]
    [InlineData("fleet")]
    [InlineData("lifecycle")]
    public void TheDroneSampleStillParses(string file)
    {
        var workspace = SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "Samples", "drone"));
        var source = workspace.Files.Single(f => Path.GetFileNameWithoutExtension(f.Path) == file);
        Assert.Empty(source.Errors);
    }

    [Theory]
    [InlineData(DiagramKind.Package, "DroneSoftware")]
    [InlineData(DiagramKind.Class, "DroneStructure")]
    [InlineData(DiagramKind.ObjectDiagram, "DroneFleet")]
    [InlineData(DiagramKind.Component, "DroneStructure")]
    [InlineData(DiagramKind.CompositeStructure, "DroneStructure::Drone")]
    [InlineData(DiagramKind.Deployment, "DroneSoftware")]
    [InlineData(DiagramKind.Profile, "DroneLifecycle")]
    public void TheDroneSampleHasSomethingForEveryStructuralKind(DiagramKind kind, string root)
    {
        var element = SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "Samples", "drone")).Find(root)!;
        Assert.Contains(kind, DiagramBuilder.KindsFor(element));

        var diagram = DiagramBuilder.Build(kind, element);
        Assert.NotEmpty(diagram.Nodes);
        Assert.NotEmpty(diagram.Edges);
    }

    [Fact]
    public void TheUmlKindsComeAfterTheSysmlOnes()
    {
        var kinds = DiagramBuilder.KindsFor(Fixture().Find("Sample")!);
        Assert.Equal(DiagramKind.Definition, kinds[0]);
    }

    private static Diagram Drone(DiagramKind kind, string root)
        => DiagramBuilder.Build(kind, SysmlWorkspace.Load(Path.Combine(AppContext.BaseDirectory, "Samples", "drone")).Find(root)!);

    /// <summary>
    /// A label on a box is hidden by it. The drone's parts sit in three rows,
    /// so edges from Drone to the lower row pass the upper one, and their
    /// labels ("motors[4]", "props[4]") used to land on the boxes they passed.
    /// </summary>
    [Theory]
    [InlineData("layout")]
    [InlineData("route")]
    public void NoLabelSitsOnABox(string how)
    {
        var diagram = Drone(DiagramKind.Definition, "DroneStructure");
        DiagramLayout.Apply(diagram);
        if (how == "route")
        {
            // As after a drag: the boxes stay, every edge is routed again.
            var gps = diagram.Nodes.Single(n => n.Label == "Gps");
            gps.X += 40;
            DiagramLayout.Route(diagram);
        }

        foreach (var edge in diagram.Edges.Where(e => e.Label is { Length: > 0 }))
        {
            Assert.True(edge.LabelCentre is not null, $"{edge} has no label place");
            var (x, y) = edge.LabelCentre.Value;
            var halfWidth = ((edge.Label!.Length * 6.7) + 8) / 2;
            foreach (var box in diagram.Nodes)
            {
                var clear = x + halfWidth <= box.X || x - halfWidth >= box.X + box.Width
                    || y + 7 <= box.Y || y - 7 >= box.Y + box.Height;
                Assert.True(clear, $"the label of {edge} ('{edge.Label}') sits on {box.Label}");
            }
        }
    }

    /// <summary>
    /// FlightController has three ports typed by DataPort: three edges between
    /// the same two boxes, which the routers draw a few pixels apart, as one
    /// line. They are fanned out so each can be told apart.
    /// </summary>
    [Fact]
    public void EdgesBetweenTheSameTwoBoxesAreDrawnApart()
    {
        var diagram = Drone(DiagramKind.Definition, "DroneStructure");
        DiagramLayout.Apply(diagram);

        var parallel = diagram.Edges.Where(e => e.Source.Label == "FlightController" && e.Target.Label == "DataPort").ToList();
        Assert.Equal(3, parallel.Count);

        var middles = parallel.ConvertAll(e => e.Waypoints[e.Waypoints.Count / 2]);
        for (var i = 0; i < middles.Count; i++)
        {
            for (var j = i + 1; j < middles.Count; j++)
            {
                var apart = Math.Sqrt(Math.Pow(middles[i].X - middles[j].X, 2) + Math.Pow(middles[i].Y - middles[j].Y, 2));
                Assert.True(apart >= 10, $"'{parallel[i].Label}' and '{parallel[j].Label}' are {apart:F1} apart in the middle");
            }
        }
    }
}
