using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SysmlStudio.App.ViewModels;
using SysmlStudio.App.Views;
using SysmlStudio.Diagrams;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(SysmlStudio.App.Tests.TestApp))]

namespace SysmlStudio.App.Tests;

/// <summary>The real application, rendered by Skia into memory instead of onto a screen.</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        // Recent folders go to a throwaway place, never the user's own list.
        Environment.SetEnvironmentVariable("SYSML_STUDIO_HOME",
            Path.Combine(Path.GetTempPath(), "sysml-studio-tests", "home"));

        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}

/// <summary>
/// Opens the window on a model and drives it the way a user would, through
/// the view models. Each test saves what it rendered when
/// SYSML_STUDIO_SHOTS names a folder, so the look can be checked by eye.
/// </summary>
public sealed class WindowTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static string ModelFolder
        => Environment.GetEnvironmentVariable("SYSML_STUDIO_MODEL") is { Length: > 0 } model && Directory.Exists(model)
            ? model
            : Fixture;

    private static (MainWindow Window, ShellViewModel Shell) Open(string folder)
    {
        var shell = new ShellViewModel();
        var window = new MainWindow { DataContext = shell, Width = 1600, Height = 1000 };
        shell.Attach(window);
        window.Show();
        shell.Open(folder);
        Settle();
        return (window, shell);
    }

    /// <summary>Lets layout, bindings and the fit-to-view retries run.</summary>
    private static void Settle()
    {
        for (var i = 0; i < 30; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(10);
        }
    }

    private static void Shoot(MainWindow window, string name)
    {
        if (Environment.GetEnvironmentVariable("SYSML_STUDIO_SHOTS") is not { Length: > 0 } folder)
            return;

        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame();
        if (frame is null)
            return;

        using var file = File.Create(Path.Combine(folder, name + ".png"));
        frame.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    [AvaloniaFact]
    public void TheStartPageShowsWhenNothingIsOpen()
    {
        var shell = new ShellViewModel();
        var window = new MainWindow { DataContext = shell, Width = 1600, Height = 1000 };
        window.Show();
        Settle();
        Shoot(window, "empty");

        Assert.False(shell.HasWorkspace);
        Assert.Null(shell.ActiveDocument);
        Assert.Equal("Ready", shell.SaveState);
    }

    [AvaloniaFact]
    public void OpeningAFolderFillsTheBrowserAndTheStatusBar()
    {
        var (window, shell) = Open(ModelFolder);
        Shoot(window, "workspace");

        Assert.True(shell.HasWorkspace);
        Assert.NotEmpty(shell.Browser.Roots);
        Assert.Equal("Saved", shell.SaveState);
        Assert.Contains("file", shell.Browser.FilesText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void EveryDiagramKindOpensAsADocument()
    {
        var (window, shell) = Open(Fixture);
        var workspace = shell.Workspace!;

        foreach (var (kind, root) in new[]
                 {
                     (DiagramKind.Definition, "Sample"),
                     (DiagramKind.Requirements, "Sample"),
                     (DiagramKind.Interconnection, "Sample::Vehicle"),
                     (DiagramKind.ActionFlow, "Sample::StartUp"),
                     (DiagramKind.StateMachine, "Sample::Running"),
                     (DiagramKind.UseCase, "SampleBehaviour::Refuel"),
                     (DiagramKind.Activity, "SampleBehaviour::Check"),
                     (DiagramKind.Sequence, "SampleBehaviour::Handshake"),
                     (DiagramKind.Communication, "SampleBehaviour::Handshake"),
                     (DiagramKind.InteractionOverview, "SampleBehaviour::Overview"),
                     (DiagramKind.Timing, "SampleBehaviour::OneTank"),
                 })
        {
            shell.Select(workspace.Find(root)!);
            Assert.True(shell.OpenDiagramCommand.CanExecute(kind), $"{kind} of {root}");
            shell.OpenDiagramCommand.Execute(kind);
            Settle();

            Assert.Equal(kind, shell.ActiveKind);
            Shoot(window, $"diagram-{kind}");
        }
    }

    /// <summary>
    /// Each behavioural kind, over the sample that writes its elements, in both
    /// themes. A UML shape is drawn from marks a canvas has to paint: this is
    /// where one that paints nothing, or fails to, shows.
    /// </summary>
    [AvaloniaFact]
    public void EveryBehaviouralKindDrawsTheDroneInBothThemes()
    {
        var (window, shell) = Open(Path.Combine(AppContext.BaseDirectory, "DroneModel"));
        var workspace = shell.Workspace!;

        foreach (var dark in new[] { false, true })
        {
            shell.IsDark = dark;
            foreach (var (kind, root) in new[]
                     {
                         (DiagramKind.UseCase, "DroneInteractions::DeliverParcel"),
                         (DiagramKind.UseCase, "DroneInteractions"),
                         (DiagramKind.Activity, "DroneBehaviour::PlanFlight"),
                         (DiagramKind.StateMachine, "DroneBehaviour::FlightModes"),
                         (DiagramKind.StateMachine, "DroneBehaviour::Airframe"),
                         (DiagramKind.Sequence, "DroneInteractions::HandOver"),
                         (DiagramKind.Sequence, "DroneInteractions::RequestClearance"),
                         (DiagramKind.Sequence, "DroneInteractions::FlyToAddress"),
                         (DiagramKind.Communication, "DroneInteractions::RequestClearance"),
                         (DiagramKind.Communication, "DroneInteractions::FlyToAddress"),
                         (DiagramKind.InteractionOverview, "DroneInteractions::DeliveryOverview"),
                         (DiagramKind.Timing, "DroneInteractions::DeliveryFlight"),
                     })
            {
                shell.Select(workspace.Find(root)!);
                Assert.True(shell.OpenDiagramCommand.CanExecute(kind), $"{kind} of {root}");
                shell.OpenDiagramCommand.Execute(kind);
                Settle();

                Assert.Equal(kind, shell.ActiveKind);
                var diagram = Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
                Assert.NotEmpty(diagram.Nodes);
                Assert.Contains(diagram.Nodes, n => n.IsBox || (n.IsDrawn && n.Marks.Count > 0));
                Shoot(window, $"drone-{kind}-{root.Split("::")[^1]}-{(dark ? "dark" : "light")}");
                shell.CloseDocumentCommand.Execute(shell.ActiveDocument);
                Settle();
            }
        }

        shell.IsDark = false;
    }

    [AvaloniaFact]
    public void SelectingAnElementFillsThePropertiesAndFindsItsUsages()
    {
        var (window, shell) = Open(Fixture);
        shell.Select(shell.Workspace!.Find("Sample::Engine")!);
        shell.FindUsagesCommand.Execute(null);
        Settle();
        Shoot(window, "properties");

        Assert.Equal("Engine", shell.Properties.Name);
        Assert.Equal("Part definition · P.1", shell.Properties.KindCaption);
        Assert.Equal("Used in 1 place", shell.Properties.UsedIn);
        Assert.True(shell.Bottom.IsOpen);
        Assert.Equal(2, shell.Bottom.Usages.Count); // the declaration, and "engine : Engine"
        Assert.Equal(["declared", "typed by"], shell.Bottom.Usages.Select(u => u.Usage));
    }

    [AvaloniaFact]
    public void ASourceTabShowsTheFileAndItsErrors()
    {
        var (window, shell) = Open(Fixture);
        var path = shell.Workspace!.Files.First().Path;
        shell.OpenSource(path, 10);
        Settle();

        var source = Assert.IsType<SourceDocumentViewModel>(shell.ActiveDocument);
        Assert.False(source.HasErrors);

        source.Text.Insert(source.Text.TextLength, "\npackage Broken {");
        Assert.True(source.IsDirty);
        source.FlushPendingReparse(); // the real editor waits a quarter of a second after the last keystroke
        source.GoToLine(source.ErrorLine);
        Settle();
        Shoot(window, "source");

        Assert.True(source.HasErrors);
        Assert.True(source.IsDirty);
        Assert.True(source.HasDot);
        Assert.Equal("Unexpected end of file", source.ErrorMessage);
        Assert.True(shell.ErrorCount > 0);
        Assert.True(shell.DiagramKinds.Single(k => k.Kind is null).IsActive); // the switcher shows "Text" over a source tab

        shell.ToggleProblemsCommand.Execute(null);
        Settle();
        Shoot(window, "problems");
        Assert.True(shell.Bottom.IsOpen);
        Assert.True(shell.Bottom.ShowsProblems);
    }

    /// <summary>
    /// Browsing with nothing open shows each element's text, in one tab; the
    /// switcher draws it as a diagram in that same tab, and browsing then
    /// keeps drawing diagrams.
    /// </summary>
    [AvaloniaFact]
    public void BrowsingShowsTheTextThenTheSwitcherDrawsIt()
    {
        var (_, shell) = Open(Fixture);
        var switcher = shell.DiagramKinds;
        Assert.Equal("Text", switcher[0].Label);

        shell.Select(shell.Workspace!.Find("Sample::Engine")!);
        Settle();
        var text = Assert.IsType<SourceDocumentViewModel>(shell.ActiveDocument);
        Assert.Equal(shell.Workspace.Find("Sample::Engine")!.File.Path, text.Path);
        Assert.True(switcher[0].IsActive);
        Assert.True(switcher[0].IsAvailable);

        shell.Select(shell.Workspace.Find("Sample")!);
        Settle();
        Assert.Single(shell.Documents);
        Assert.IsType<SourceDocumentViewModel>(shell.ActiveDocument);

        shell.SwitchKindCommand.Execute(switcher.Single(k => k.Kind == DiagramKind.Definition));
        Settle();
        Assert.Single(shell.Documents);
        Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
        Assert.False(switcher[0].IsActive);

        shell.SwitchKindCommand.Execute(switcher[0]);
        Settle();
        Assert.Single(shell.Documents);
        Assert.IsType<SourceDocumentViewModel>(shell.ActiveDocument);
    }

    /// <summary>The parser's messages, as the error box and the problems list word them.</summary>
    [AvaloniaFact]
    public void ParserMessagesAreShortened()
    {
        Assert.Equal("Expected ';'", SourceDocumentViewModel.Shorten("missing ';' at '}' expecting ';'"));
        Assert.Equal("Expected ';' or '{'", SourceDocumentViewModel.Shorten("mismatched input 'x' expecting {';', '{'}"));
        Assert.Equal("Unexpected 'x'", SourceDocumentViewModel.Shorten("extraneous input 'x' expecting {'a', 'b', 'c', 'd'}"));
        Assert.Equal("unresolved reference 'X'", SourceDocumentViewModel.Shorten("unresolved reference 'X'"));
    }

    [AvaloniaFact]
    public void TheSourceColoursEveryKeywordTheGrammarDefines()
    {
        var keywords = SysmlHighlighting.Keywords;
        Assert.Contains("part", keywords);
        Assert.Contains("requirement", keywords);
        Assert.Contains("satisfy", keywords);
        Assert.True(keywords.Count > 100, $"only {keywords.Count} keywords");
    }

    /// <summary>Typing a type's first letters in the editor opens the list, and Tab takes the suggestion.</summary>
    [AvaloniaFact]
    public void TheEditorCompletesWhatIsTyped()
    {
        var (window, shell) = Open(Fixture);
        shell.OpenSource(shell.Workspace!.Files.First().Path);
        Settle();

        var editor = window.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();
        var line = editor.Document.GetLineByNumber(23); // "        part engine : Engine;" in Vehicle
        const string typed = "\n        part spare : ";
        editor.Document.Insert(line.EndOffset, typed);
        editor.CaretOffset = line.EndOffset + typed.Length;
        editor.TextArea.Focus();
        window.KeyTextInput("V");
        window.KeyTextInput("e");
        window.KeyTextInput("h");
        Settle();
        Shoot(window, "completion");

        var list = window.GetVisualDescendants().OfType<AvaloniaEdit.CodeCompletion.CompletionList>().SingleOrDefault()
                   ?? Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(window)
                       .OfType<AvaloniaEdit.CodeCompletion.CompletionList>().SingleOrDefault();
        Assert.NotNull(list);
        Assert.Equal("Vehicle", list.SelectedItem?.Text);

        window.KeyPress(Avalonia.Input.Key.Tab, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Tab, "\t");
        Settle();
        Assert.Contains("part spare : Vehicle", editor.Document.Text, StringComparison.Ordinal);
    }

    /// <summary>Keywords, type names and numbers each get their own colour; a declared name keeps the text colour.</summary>
    [AvaloniaFact]
    public void TheSourceColoursKeywordsTypesAndNumbers()
    {
        Avalonia.Media.Color keyword = Avalonia.Media.Colors.Purple, comment = Avalonia.Media.Colors.Gray,
            text = Avalonia.Media.Colors.Brown, number = Avalonia.Media.Colors.Green,
            metadata = Avalonia.Media.Colors.Blue, type = Avalonia.Media.Colors.Teal;
        var definition = SysmlHighlighting.Create(keyword, comment, text, number, metadata, type);
        const string line = "part motors : Motor[4]; part def Hub :> Base::Part;";
        var document = new AvaloniaEdit.Document.TextDocument(line);
        var sections = new AvaloniaEdit.Highlighting.DocumentHighlighter(document, definition)
            .HighlightLine(1).Sections;

        Avalonia.Media.Color? ColourOf(string word)
        {
            var at = line.IndexOf(word, StringComparison.Ordinal);
            var section = sections.LastOrDefault(s => s.Offset <= at && at < s.Offset + s.Length);
            return (section?.Color.Foreground as AvaloniaEdit.Highlighting.SimpleHighlightingBrush)?.GetColor(null);
        }

        Assert.Equal(keyword, ColourOf("part"));
        Assert.Equal(keyword, ColourOf("def"));
        Assert.Equal(type, ColourOf("Motor"));
        Assert.Equal(number, ColourOf("4"));
        Assert.Equal(type, ColourOf("Base::Part"));
        Assert.Null(ColourOf("motors"));
        Assert.Null(ColourOf("Hub"));
    }

    [AvaloniaFact]
    public void TheDarkThemeRendersToo()
    {
        var (window, shell) = Open(Fixture);
        shell.Select(shell.Workspace!.Find("Sample")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Definition);
        shell.IsDark = true;
        Settle();
        Shoot(window, "dark");

        Assert.True(shell.IsDark);
        shell.IsDark = false;
    }

    /// <summary>
    /// The diagrams a real model offers have something in them. Runs against
    /// SYSML_STUDIO_MODEL when it is set; the Ferrix model's names are used
    /// when they are there and skipped when they are not.
    /// </summary>
    [AvaloniaFact]
    public void RealModelDiagramsHaveContent()
    {
        if (Environment.GetEnvironmentVariable("SYSML_STUDIO_MODEL") is not { Length: > 0 } model || !Directory.Exists(model))
            return;

        var (window, shell) = Open(model);
        foreach (var (kind, name) in new[]
                 {
                     (DiagramKind.Requirements, "FerrixRoadmap::stage13Isolation"),
                     (DiagramKind.Requirements, "FerrixRoadmap"),
                     (DiagramKind.ActionFlow, "FerrixBoot::LoaderSequence"),
                     (DiagramKind.Definition, "FerrixStructure::Kernel"),
                     (DiagramKind.Definition, "FerrixDrivers::DevMgr"),
                     (DiagramKind.Definition, "FerrixBoot"),
                     (DiagramKind.Package, "FerrixLifecycle"),
                     (DiagramKind.Package, "FerrixStructure"),
                     (DiagramKind.Class, "FerrixStructure"),
                     (DiagramKind.Class, "FerrixStructure::Kernel"),
                     (DiagramKind.Component, "FerrixStructure"),
                     (DiagramKind.Component, "FerrixStructure::Kernel"),
                     (DiagramKind.CompositeStructure, "FerrixStructure::Ferrix"),
                     (DiagramKind.Deployment, "FerrixStructure"),
                     (DiagramKind.Deployment, "FerrixStructure::Ferrix"),
                     (DiagramKind.Profile, "FerrixLifecycle"),
                     (DiagramKind.Profile, "FerrixStructure"),
                     (DiagramKind.Activity, "FerrixBoot::KernelBringUp"),
                     (DiagramKind.Activity, "FerrixBoot::Dispatch"),
                     (DiagramKind.Activity, "FerrixMemory::DemandFault"),
                     (DiagramKind.StateMachine, "FerrixScheduling::DomainLifecycle"),
                 })
        {
            if (shell.Workspace!.Find(name) is not { } element)
                continue;

            shell.Select(element);
            shell.OpenDiagramCommand.Execute(kind);
            Settle();

            // A model this size outlasts one settle: frame it again before the picture is taken.
            Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument).Fit();
            Settle();
            Shoot(window, "real-" + name.Replace("::", "-", StringComparison.Ordinal) + "-" + kind);

            var diagram = Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
            Assert.NotEmpty(diagram.Nodes);
        }
    }

    /// <summary>
    /// While a box is dragged its arrows follow it, before the mouse is
    /// released, not only after the drop.
    /// </summary>
    [AvaloniaFact]
    public void ArrowsFollowABoxWhileItIsDragged()
    {
        var (window, shell) = Open(Fixture);
        shell.Select(shell.Workspace!.Find("Sample::Vehicle")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Interconnection);
        Settle();

        var diagram = Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
        var connection = Assert.Single(diagram.Connections);
        var container = window.GetVisualDescendants().OfType<Nodify.Avalonia.ItemContainer>()
            .First(c => ReferenceEquals(c.DataContext, connection.Source));

        var grab = container.TranslatePoint(new Point(container.Bounds.Width / 2, 12), window)!.Value;
        var before = connection.Source.Anchor;
        var lineBefore = connection.Line.Bounds;

        window.MouseMove(grab);
        window.MouseDown(grab, Avalonia.Input.MouseButton.Left);
        for (var step = 1; step <= 10; step++)
        {
            window.MouseMove(grab + new Point(step * 12, step * 8), Avalonia.Input.RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
        }

        Settle();
        var during = connection.Source.Anchor;
        var lineDuring = connection.Line.Bounds;
        window.MouseUp(grab + new Point(120, 80), Avalonia.Input.MouseButton.Left);
        Settle();

        Assert.NotEqual(before, during);
        Assert.NotEqual(lineBefore, lineDuring); // the drawn line followed, before the drop
    }

    /// <summary>Opens a kind of diagram of an element in the fixture and returns what is on the canvas.</summary>
    private static (MainWindow Window, DiagramDocumentViewModel Diagram) Draw(DiagramKind kind, string root)
    {
        var (window, shell) = Open(Fixture);
        shell.Select(shell.Workspace!.Find(root)!);
        shell.OpenDiagramCommand.Execute(kind);
        Settle();
        return (window, Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument));
    }

    /// <summary>Drags a node by a point inside it, and drops it.</summary>
    private static void Drag(MainWindow window, DiagramNodeViewModel node, Point grip, Point by, Action? whileHeld = null)
    {
        var container = window.GetVisualDescendants().OfType<Nodify.Avalonia.ItemContainer>()
            .First(c => ReferenceEquals(c.DataContext, node));

        var from = container.TranslatePoint(grip, window)!.Value;
        window.MouseMove(from);
        window.MouseDown(from, Avalonia.Input.MouseButton.Left);
        for (var step = 1; step <= 10; step++)
        {
            window.MouseMove(from + new Point(by.X * step / 10, by.Y * step / 10), Avalonia.Input.RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
        }

        Settle();
        whileHeld?.Invoke();
        window.MouseUp(from + by, Avalonia.Input.MouseButton.Left);
        Settle();
    }

    /// <summary>
    /// A lifeline goes sideways and its messages go with it, level the whole way:
    /// a row is when something happens, which no drag can change.
    /// </summary>
    [AvaloniaFact]
    public void ALifelineIsDraggedSidewaysAndKeepsItsPlaceInTime()
    {
        var (window, diagram) = Draw(DiagramKind.Sequence, "SampleBehaviour::Handshake");
        var pump = diagram.Nodes.Single(n => n.Element.Name == "pump");
        var message = diagram.Connections.First(c => c.Label == "hello : Fuel");
        var before = pump.Location;
        var lineBefore = message.Line.Bounds;

        Drag(window, pump, new Point(pump.Width / 2, 20), new Point(120, 90), () =>
        {
            Assert.NotEqual(lineBefore.Right, message.Line.Bounds.Right); // the arrow follows while held
            Assert.Equal(0, message.Line.Bounds.Height, 1); // and stays level
            Assert.Equal(lineBefore.Top, message.Line.Bounds.Top, 1);
        });

        Assert.True(pump.Location.X > before.X, $"the lifeline did not move: {before} to {pump.Location}");
        Assert.Equal(before.Y, pump.Location.Y);
        Assert.Equal(lineBefore.Top, message.Line.Bounds.Top, 1);
    }

    /// <summary>What a composite state holds moves with it, and its transitions are routed again where it lands.</summary>
    [AvaloniaFact]
    public void ACompositeStateTakesItsStatesAlong()
    {
        var (window, diagram) = Draw(DiagramKind.StateMachine, "SampleBehaviour::Tank");
        var filling = diagram.Nodes.Single(n => n.Element.Name == "filling" && !n.IsPseudoNode);
        var slow = diagram.Nodes.Single(n => n.Element.Name == "slow");
        var offset = slow.Location - filling.Location;
        var before = filling.Location;

        Drag(window, filling, new Point(filling.Width / 2, 18), new Point(90, 60));
        Shoot(window, "dragged-composite");

        Assert.NotEqual(before, filling.Location);
        Assert.Equal(offset.X, (slow.Location - filling.Location).X, 1);
        Assert.Equal(offset.Y, (slow.Location - filling.Location).Y, 1);
    }

    /// <summary>A mark's place on a time axis is what it says: a drag leaves it where it is.</summary>
    [AvaloniaFact]
    public void AMarkOnATimeAxisCannotBeDragged()
    {
        var (window, diagram) = Draw(DiagramKind.Timing, "SampleBehaviour::OneTank");
        var band = diagram.Nodes.Single(n => n.Element.Name == "draining");
        var before = band.Location;

        Assert.False(band.IsDraggable);
        Drag(window, band, new Point(band.Width / 2, band.Height / 2), new Point(80, 40));

        Assert.Equal(before, band.Location);
    }

    /// <summary>The list greys out a behavioural kind with nothing to draw, and offers it where there is.</summary>
    [AvaloniaFact]
    public void TheSwitcherOffersABehaviouralKindOnlyWhereItCanBeDrawn()
    {
        var (_, shell) = Open(Fixture);
        var sequence = shell.DiagramKinds.Single(k => k.Kind == DiagramKind.Sequence);
        var timing = shell.DiagramKinds.Single(k => k.Kind == DiagramKind.Timing);
        Assert.Equal(DiagramKindCatalog.Interaction, sequence.Group);
        Assert.Equal(DiagramKindCatalog.Behavior, shell.DiagramKinds.Single(k => k.Kind == DiagramKind.StateMachine).Group);
        Assert.Equal(DiagramKindCatalog.Sysml, shell.DiagramKinds.Single(k => k.Kind == DiagramKind.ActionFlow).Group);

        shell.Select(shell.Workspace!.Find("SampleBehaviour::NothingHappens")!);
        Settle();
        Assert.False(sequence.IsAvailable);
        Assert.False(timing.IsAvailable);

        shell.Select(shell.Workspace!.Find("SampleBehaviour::Handshake")!);
        Settle();
        Assert.True(sequence.IsAvailable);
        Assert.False(timing.IsAvailable);

        shell.SwitchKindCommand.Execute(sequence);
        Settle();
        Assert.Equal(DiagramKind.Sequence, shell.ActiveKind);
        Assert.Equal("Sequence", shell.DiagramKindLabel);
        Assert.Equal("Handshake · Sequence", shell.DocumentCaption);
    }

    /// <summary>A drawn node's right-click and selection are still its element's, and only a real element's.</summary>
    [AvaloniaFact]
    public void ClickingADrawnNodeSelectsItsElement()
    {
        var (window, shell) = Open(Fixture);
        shell.Select(shell.Workspace!.Find("SampleBehaviour::Handshake")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Sequence);
        Settle();

        var diagram = Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
        var pump = diagram.Nodes.Single(n => n.Element.Name == "pump");
        var container = window.GetVisualDescendants().OfType<Nodify.Avalonia.ItemContainer>().First(c => ReferenceEquals(c.DataContext, pump));
        var point = container.TranslatePoint(new Point(pump.Width / 2, 20), window)!.Value;
        window.MouseDown(point, Avalonia.Input.MouseButton.Left);
        window.MouseUp(point, Avalonia.Input.MouseButton.Left);
        Settle();

        Assert.True(pump.IsSelected);
        Assert.Equal("SampleBehaviour::Handshake::pump", shell.SelectedElement?.QualifiedName);
    }

    /// <summary>Selecting another element redraws the diagram in front, in the same tab.</summary>
    [AvaloniaFact]
    public void TheDiagramFollowsTheSelection()
    {
        var (_, shell) = Open(Fixture);
        var workspace = shell.Workspace!;

        shell.Select(workspace.Find("Sample::Engine")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Definition);
        Settle();

        // RollingThing is not on Engine's diagram: the tab is redrawn for it.
        shell.Select(workspace.Find("Sample::RollingThing")!);
        Settle();
        var redrawn = Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
        Assert.Equal("Sample::RollingThing", redrawn.Diagram.Root.QualifiedName);
        Assert.Equal(DiagramKind.Definition, redrawn.Diagram.Kind);

        // Wheel is on RollingThing's diagram (it specializes it): selected there, not redrawn.
        shell.Select(workspace.Find("Sample::Wheel")!);
        Settle();
        Assert.Same(redrawn, shell.ActiveDocument);
        Assert.True(redrawn.Nodes.Single(n => n.Element.Name == "Wheel").IsSelected);

        // A requirement cannot be drawn as a definition diagram: it gets its own kind.
        shell.Select(workspace.Find("Sample::MustStart")!);
        Settle();
        Assert.Equal(DiagramKind.Requirements, Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument).Diagram.Kind);
    }

    /// <summary>The title bar's switcher redraws the tab in front as another kind of diagram.</summary>
    [AvaloniaFact]
    public void TheSwitcherRedrawsTheTabAsAnotherKind()
    {
        var (window, shell) = Open(Fixture);
        shell.Select(shell.Workspace!.Find("Sample")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Definition);
        Settle();

        var requirements = shell.DiagramKinds.Single(k => k.Kind == DiagramKind.Requirements);
        var state = shell.DiagramKinds.Single(k => k.Kind == DiagramKind.StateMachine);
        Assert.True(requirements.IsAvailable);
        Assert.False(state.IsAvailable); // a package has no states of its own
        Assert.True(shell.DiagramKinds.Single(k => k.Kind == DiagramKind.Definition).IsActive);

        shell.SwitchKindCommand.Execute(requirements);
        Settle();
        Shoot(window, "switched-requirements");

        Assert.Single(shell.Documents);
        Assert.Equal(DiagramKind.Requirements, shell.ActiveKind);
        Assert.True(requirements.IsActive);
        Assert.Equal("Sample · Requirements", shell.DocumentCaption);
        var diagram = Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
        Assert.Contains(diagram.Connections, c => c.Label == "satisfies");
    }

    /// <summary>Each structural UML kind opens as a document, with something in it.</summary>
    [AvaloniaFact]
    public void EveryStructuralKindOpensAsADocument()
    {
        var (window, shell) = Open(Path.Combine(AppContext.BaseDirectory, "UmlFixtures"));
        var workspace = shell.Workspace!;

        foreach (var (kind, root) in new[]
                 {
                     (DiagramKind.Package, "Garage"),
                     (DiagramKind.Class, "Garage"),
                     (DiagramKind.ObjectDiagram, "Garage"),
                     (DiagramKind.Component, "Garage"),
                     (DiagramKind.CompositeStructure, "Garage::Workshop"),
                     (DiagramKind.Deployment, "Garage"),
                     (DiagramKind.Profile, "Garage"),
                 })
        {
            shell.Select(workspace.Find(root)!);
            Assert.True(shell.OpenDiagramCommand.CanExecute(kind), $"{kind} of {root}");
            shell.OpenDiagramCommand.Execute(kind);
            Settle();

            Assert.Equal(kind, shell.ActiveKind);
            Assert.NotEmpty(Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument).Nodes);
            Shoot(window, $"diagram-{kind}");
        }
    }

    /// <summary>Dragging a box in a framed diagram moves the frame with it, and the drop lands the connectors on their ports again.</summary>
    [AvaloniaFact]
    public void TheFrameAndThePortsFollowADrag()
    {
        var (_, shell) = Open(Path.Combine(AppContext.BaseDirectory, "UmlFixtures"));
        shell.Select(shell.Workspace!.Find("Garage::Workshop")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.CompositeStructure);
        Settle();

        var diagram = Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
        var frame = Assert.Single(diagram.Frames);
        Assert.Equal("Workshop", frame.Label);
        var before = frame.Size.Width;

        var socket = diagram.Nodes.Single(n => n.Element.Name == "socket");
        socket.Location += new Vector(600, 0);
        Assert.True(frame.Size.Width > before, "the frame grew to hold the box dragged out of it");

        // Zoomed out, it is still drawn around the boxes, not at its own size.
        diagram.Zoom = 0.5;
        Assert.Equal(frame.Size.Width * 0.5, frame.Width, 3);

        diagram.DragCompletedCommand.Execute(null);
        var edge = Assert.Single(diagram.Diagram.Edges, e => e.SourcePort is not null);
        Assert.Equal(edge.Source.X + edge.SourcePort!.X, edge.Waypoints[0].X, 3);
    }

    /// <summary>The structural UML kinds over the sample model the screenshots use, in both themes.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheStructuralKindsDrawTheDroneSample(bool dark)
    {
        var (window, shell) = Open(Path.Combine(AppContext.BaseDirectory, "Samples", "drone"));
        shell.IsDark = dark;
        try
        {
            foreach (var (kind, root) in new[]
                     {
                         (DiagramKind.Package, "DroneSoftware"),
                         (DiagramKind.Class, "DroneStructure"),
                         (DiagramKind.ObjectDiagram, "DroneFleet"),
                         (DiagramKind.Component, "DroneStructure"),
                         (DiagramKind.Component, "DroneStructure::Drone"),
                         (DiagramKind.CompositeStructure, "DroneStructure::Drone"),
                         (DiagramKind.Deployment, "DroneSoftware"),
                         (DiagramKind.Profile, "DroneLifecycle"),
                     })
            {
                shell.Select(shell.Workspace!.Find(root)!);
                Assert.True(shell.OpenDiagramCommand.CanExecute(kind), $"{kind} of {root}");
                shell.OpenDiagramCommand.Execute(kind);
                Settle();

                Assert.Equal(kind, shell.ActiveKind);
                var diagram = Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
                Assert.NotEmpty(diagram.Nodes);
                var name = $"uml-{(dark ? "dark" : "light")}-{kind}-{root.Replace("::", "-", StringComparison.Ordinal)}";
                Shoot(window, name);

                // And what the export writes of the same picture.
                if (Environment.GetEnvironmentVariable("SYSML_STUDIO_SHOTS") is { Length: > 0 } folder)
                    SvgExporter.Write(diagram.Diagram, Path.Combine(folder, name + ".svg"));
            }
        }
        finally
        {
            shell.IsDark = false;
        }
    }

    /// <summary>A kind added to the enum and left out of the switcher's table could never be reached.</summary>
    [AvaloniaFact]
    public void EveryDiagramKindIsInTheSwitchersTable()
    {
        var kinds = Enum.GetValues<DiagramKind>();
        Assert.Equal(kinds.Length, DiagramKindCatalog.All.Select(e => e.Kind).Distinct().Count());
        Assert.All(kinds, k => Assert.Contains(DiagramKindCatalog.All, e => e.Kind == k));
        Assert.All(DiagramKindCatalog.All, e => Assert.Contains(e.Group, DiagramKindCatalog.Groups));
        Assert.Equal(DiagramKindCatalog.All.Count, DiagramKindCatalog.All.Select(e => e.ShortcutId).Distinct().Count());
        Assert.All(DiagramKindCatalog.All, e => Assert.NotNull(ShortcutCatalog.Find(e.ShortcutId)));
    }

    /// <summary>
    /// The list under the switcher opens from its key, shows the kinds in their
    /// groups with what cannot be drawn greyed out, and a click draws the kind
    /// in the same tab and says so on the button.
    /// </summary>
    [AvaloniaFact]
    public void TheSwitchersListOffersTheKindsInGroups()
    {
        var (window, shell) = Open(Fixture);
        shell.Select(shell.Workspace!.Find("Sample")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Definition);
        Settle();
        Assert.Equal("Definition", shell.DiagramKindLabel);
        Assert.True(shell.ShowsDiagramKind);

        window.KeyPress(Avalonia.Input.Key.K, Avalonia.Input.RawInputModifiers.Alt, Avalonia.Input.PhysicalKey.K, "k");
        Settle();
        var flyout = KindListButton(window).Flyout!;
        Assert.True(flyout.IsOpen);

        var content = Assert.IsType<Avalonia.Controls.Flyout>(flyout).Content as Avalonia.Controls.Control;
        var buttons = content!.GetVisualDescendants().OfType<Avalonia.Controls.Button>().ToList();
        Assert.Equal(shell.DiagramKinds.Count - 1, buttons.Count); // every kind; the text has its own segment
        Assert.Contains(buttons, b => (string?)b.Content == "Requirements" && b.IsEnabled);
        Assert.Contains(buttons, b => (string?)b.Content == "State" && !b.IsEnabled); // a package has no states of its own
        Assert.Equal(DiagramKindCatalog.Groups.Count(g => shell.DiagramKinds.Any(k => k.Group == g)), shell.DiagramKindGroups.Count);

        var root = Avalonia.Controls.TopLevel.GetTopLevel(content)!;
        if (Environment.GetEnvironmentVariable("SYSML_STUDIO_SHOTS") is { Length: > 0 } folder)
        {
            using var frame = root.CaptureRenderedFrame();
            Directory.CreateDirectory(folder);
            using var file = File.Create(Path.Combine(folder, "switcher-list.png"));
            frame?.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }

        var requirements = buttons.Single(b => (string?)b.Content == "Requirements");
        requirements.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Settle();

        Assert.False(flyout.IsOpen);
        Assert.Single(shell.Documents);
        Assert.Equal(DiagramKind.Requirements, shell.ActiveKind);
        Assert.Equal("Requirements", shell.DiagramKindLabel);

        shell.SwitchKindCommand.Execute(shell.TextOption);
        Settle();
        Assert.Equal("Diagram", shell.DiagramKindLabel);
        Assert.False(shell.ShowsDiagramKind);
        Shoot(window, "switcher-over-text");
    }

    private static Avalonia.Controls.Button KindListButton(MainWindow window)
        => window.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Single(b => b.Name == "KindListButton");

    /// <summary>The switcher fits the narrowest window the title bar is allowed.</summary>
    [AvaloniaFact]
    public void TheSwitcherFitsTheNarrowestWindow()
    {
        var (window, shell) = Open(Fixture);
        window.Width = window.MinWidth;
        shell.Select(shell.Workspace!.Find("Sample::Vehicle")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Interconnection);
        Settle();
        Shoot(window, "switcher-narrow");

        var button = KindListButton(window);
        var search = window.GetVisualDescendants().OfType<Avalonia.Controls.AutoCompleteBox>().Single(b => b.Name == "SearchBox");
        var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), window)!.Value.X;
        var searchLeft = search.TranslatePoint(default, window)!.Value.X;
        Assert.True(right <= searchLeft, $"the switcher ends at {right} and the search starts at {searchLeft}");
    }

    /// <summary>Tabs: a source tab opens beside the diagram, and closing the one in front shows its neighbour.</summary>
    [AvaloniaFact]
    public void TabsOpenAndClose()
    {
        var (_, shell) = Open(Fixture);
        shell.Select(shell.Workspace!.Find("Sample")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Definition);
        shell.OpenSource(shell.Workspace!.Files.First().Path);
        Settle();

        Assert.Equal(2, shell.Documents.Count);
        var source = Assert.IsType<SourceDocumentViewModel>(shell.ActiveDocument);
        Assert.True(source.IsActive);
        Assert.False(shell.Documents[0].IsActive);

        shell.CloseDocumentCommand.Execute(source);
        Settle();
        Assert.Single(shell.Documents);
        Assert.IsType<DiagramDocumentViewModel>(shell.ActiveDocument);
        Assert.True(shell.Documents[0].IsActive);
    }

    /// <summary>The export dialog offers both formats and follows the format with the file name.</summary>
    [AvaloniaFact]
    public void TheExportDialogOffersJsonAndXmi()
    {
        var (window, shell) = Open(Fixture);
        shell.Select(shell.Workspace!.Find("Sample")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Requirements);
        Settle();

        _ = shell.ExportCommand.ExecuteAsync(null);
        Settle();
        var dialog = Assert.IsType<ExportDialogViewModel>(shell.Dialog);
        Shoot(window, "export");

        Assert.True(dialog.IsJson);
        Assert.Equal("export/fixtures.json", dialog.Path);
        dialog.IsXmi = true;
        Assert.Equal("export/fixtures.xmi", dialog.Path);
        Assert.Equal(Path.Combine(Fixture, "export", "fixtures.xmi"), dialog.FullPath);
        Assert.Equal("Whole workspace · 1 file", dialog.Scope);

        dialog.CancelCommand.Execute(null);
        Settle();
        Assert.Null(shell.Dialog);
    }

    /// <summary>
    /// The side panels are glass over the canvas: a box panned under the model
    /// panel shows through it, blurred, instead of being hidden.
    /// </summary>
    [AvaloniaFact]
    public void TheSidePanelsShowTheCanvasThroughFrostedGlass()
    {
        var (window, shell) = Open(Fixture);
        shell.Select(shell.Workspace!.Find("Sample")!);
        shell.OpenDiagramCommand.Execute(DiagramKind.Definition);
        Settle();

        var editor = window.GetVisualDescendants().OfType<Nodify.Avalonia.NodifyEditor>().Single();
        var glass = window.GetVisualDescendants().OfType<FrostPanel>().First(p => p.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Left);
        using var before = window.CaptureRenderedFrame();

        // Drag the empty canvas to the left, so the diagram slides under the model panel.
        var start = new Point(1200, 800);
        var viewport = editor.ViewportLocation;
        window.MouseMove(start);
        window.MouseDown(start, Avalonia.Input.MouseButton.Right);
        for (var step = 1; step <= 14; step++)
        {
            window.MouseMove(start - new Point(step * 30, 0), Avalonia.Input.RawInputModifiers.RightMouseButton);
            Dispatcher.UIThread.RunJobs();
        }

        window.MouseUp(start - new Point(420, 0), Avalonia.Input.MouseButton.Right);
        Settle();
        Assert.NotEqual(viewport, editor.ViewportLocation);
        Shoot(window, "frosted");
        using var after = window.CaptureRenderedFrame();

        Assert.NotNull(before);
        Assert.NotNull(after);
        var region = glass.Bounds.Translate((Vector)glass.TranslatePoint(default, window)!.Value);
        Assert.NotEqual(Pixels(before!, region), Pixels(after!, region)); // what moved underneath shows through
    }

    /// <summary>The sum of the colour values in a region of a rendered frame.</summary>
    private static long Pixels(Avalonia.Media.Imaging.Bitmap frame, Rect region)
    {
        var width = frame.PixelSize.Width;
        var buffer = new byte[width * frame.PixelSize.Height * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(frame.PixelSize), handle.AddrOfPinnedObject(), buffer.Length, width * 4);
        }
        finally
        {
            handle.Free();
        }

        long sum = 0;
        for (var y = (int)region.Top; y < (int)region.Bottom; y += 2)
        {
            for (var x = (int)region.Left; x < (int)region.Right; x += 2)
                sum += buffer[((y * width) + x) * 4] + buffer[(((y * width) + x) * 4) + 1];
        }

        return sum;
    }

    /// <summary>No two default shortcuts share a gesture, and every default parses.</summary>
    [AvaloniaFact]
    public void TheDefaultShortcutsAreDistinct()
    {
        var gestures = ShortcutCatalog.All.Where(a => a.DefaultGesture.Length > 0).ToList();
        Assert.All(gestures, a => Assert.NotNull(ShortcutMap.Parse(a.DefaultGesture)));
        Assert.Empty(gestures.GroupBy(a => a.DefaultGesture).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.True(ShortcutCatalog.All.Count >= 40, $"only {ShortcutCatalog.All.Count} shortcuts");
        Assert.Equal("Ctrl+Shift+1", ShortcutCatalog.Display("Ctrl+Shift+D1"));
        Assert.Equal("Ctrl++", ShortcutCatalog.Display("Ctrl+OemPlus"));
    }

    /// <summary>
    /// The cogwheel opens the settings; a shortcut is re-recorded by clicking
    /// it and pressing keys, a clash blocks saving, and a saved shortcut works.
    /// </summary>
    [AvaloniaFact]
    public void ShortcutsAreRecordedSavedAndUsed()
    {
        var settingsFile = Services.AppHome.PathOf("settings.json");
        try
        {
            var (window, shell) = Open(Fixture);
            _ = shell.OpenSettingsCommand.ExecuteAsync(null);
            Settle();
            var settings = Assert.IsType<SettingsViewModel>(shell.Dialog);
            Shoot(window, "settings");

            var toggle = settings.Groups.SelectMany(g => g.Rows).Single(r => r.Action.Id == "toggleModel");
            settings.RecordCommand.Execute(toggle);
            window.Focus(); // a click on the shortcut would have focused the window
            Settle();
            Assert.Same(toggle, settings.Recording);

            // A clash first: Ctrl+S is Save's.
            window.KeyPress(Avalonia.Input.Key.S, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.S, "s");
            Settle();
            Assert.Equal("Ctrl+S", toggle.Gesture);
            Assert.True(settings.HasConflict);
            Assert.False(settings.ConfirmCommand.CanExecute(null));
            Assert.True(shell.HasWorkspace); // and Save did not run: the dialog caught the keys

            settings.RecordCommand.Execute(toggle);
            window.KeyPress(Avalonia.Input.Key.J, Avalonia.Input.RawInputModifiers.Control | Avalonia.Input.RawInputModifiers.Shift,
                Avalonia.Input.PhysicalKey.J, "J");
            Settle();
            Assert.Equal("Ctrl+Shift+J", toggle.Gesture);
            Assert.False(settings.HasConflict);

            settings.ConfirmCommand.Execute(null);
            Settle();
            Assert.Null(shell.Dialog);
            Assert.Equal("Show or hide the model panel (Ctrl+Shift+J)", shell.Keys["toggleModel"]);
            Assert.Contains("Ctrl+Shift+J", File.ReadAllText(settingsFile), StringComparison.Ordinal);

            Assert.True(shell.ShowsSide);
            window.KeyPress(Avalonia.Input.Key.J, Avalonia.Input.RawInputModifiers.Control | Avalonia.Input.RawInputModifiers.Shift,
                Avalonia.Input.PhysicalKey.J, "J");
            Settle();
            Assert.False(shell.ShowsSide);
            Assert.Equal(0, shell.CanvasInsets.Left);
        }
        finally
        {
            File.Delete(settingsFile);
        }
    }
}
