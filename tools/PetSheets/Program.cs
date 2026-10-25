using System.Drawing;
using System.Drawing.Imaging;
using DeskNote.Companion.Core;
using DeskNote.Companion.Rig;

// Draws the pictures the five rig characters need.
//
//   dotnet run --project tools/PetSheets -- sheets <assets folder>
//       Writes <key>-walk.png, -rest.png and -sleep.png: the four-frame strips every other pet has,
//       for the screens that show a still of the pet (settings, dashboard, growth guide). Prints
//       the PetArtBounds table for them.
//
//   dotnet run --project tools/PetSheets -- contact <folder>
//       Writes contact sheets of every state and every signature move, to look at.

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: PetSheets sheets|contact <folder>");
    return 1;
}

var folder = Path.GetFullPath(args[1]);
Directory.CreateDirectory(folder);

switch (args[0])
{
    case "sheets":
        foreach (var pet in RigCatalog())
        {
            Sheets.Write(pet, folder);
        }

        break;

    case "contact":
        foreach (var pet in RigCatalog())
        {
            Contact.Write(pet, folder);
        }

        break;

    default:
        Console.Error.WriteLine("usage: PetSheets sheets|contact <folder>");
        return 1;
}

return 0;

static IEnumerable<CompanionPetKind> RigCatalog() =>
    CompanionPetCatalog.All.Where(RigCharacters.IsRig);

/// <summary>One character on a surface, played forward the way the window would, one tick at a time.</summary>
internal sealed class Stage
{
    public const double Tick = 1.0 / 30;

    private readonly RigSurface _surface;
    private readonly double _footFraction;
    private int _serial;

    public Stage(CompanionPetKind pet, int width, int height, double footFraction = RigCharacters.FootFraction)
    {
        RigCharacters.TryGet(pet, out var character);
        Character = character;
        Width = width;
        Height = height;
        _footFraction = footFraction;
        _surface = new RigSurface(character, width, height);
        Director = new RigDirector(character, new Random(11));
    }

    public RigCharacter Character { get; }

    public RigDirector Director { get; }

    public int Width { get; }

    public int Height { get; }

    public int NextRestSerial() => ++_serial;

    public Bitmap Snapshot(RigFrame frame, double characterPixels)
    {
        _surface.Render(frame, new RigPlacement(Width / 2.0, Height * _footFraction, characterPixels));
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        System.Runtime.InteropServices.Marshal.Copy(_surface.Pixels.ToArray(), 0, data.Scan0, Width * Height * 4);
        bitmap.UnlockBits(data);
        return bitmap;
    }
}

internal static class Sheets
{
    private const int FrameWidth = 543;
    private const int FrameHeight = 724;
    private const int Frames = 4;

    public static void Write(CompanionPetKind pet, string folder)
    {
        RigCharacters.TryGet(pet, out var character);
        var key = pet.AssetKey();
        // What the window draws: the figure as tall as it stands on the desktop. The bounds are
        // measured off this, because they are about where the live drawing sits in its frame.
        var livePixels = RigCharacters.HeightFraction(character) * FrameHeight;
        var lefts = new List<double>();
        var rights = new List<double>();
        var tops = new List<double>();
        var bottoms = new List<double>();
        foreach (var shot in WalkFrames(pet, livePixels))
        {
            Measure(shot, lefts, rights, tops, bottoms);
            shot.Dispose();
        }

        // What the sheet shows: the same figure, enlarged to fill a portrait tile. The tallest
        // character fills most of the frame and the others keep their height against him.
        var characterPixels = 0.80 * FrameHeight * RigCharacters.HeightFactor(character) / 1.18;
        using var walk = new Bitmap(FrameWidth * Frames, FrameHeight, PixelFormat.Format32bppPArgb);
        var index = 0;
        foreach (var shot in WalkFrames(pet, characterPixels))
        {
            using var g = Graphics.FromImage(walk);
            g.DrawImageUnscaled(shot, index++ * FrameWidth, 0);
            shot.Dispose();
        }

        walk.Save(Path.Combine(folder, $"{key}-walk.png"), ImageFormat.Png);

        // Rest: sitting, four moments a second apart so the blink and the sway differ.
        using var rest = new Bitmap(FrameWidth * Frames, FrameHeight, PixelFormat.Format32bppPArgb);
        var restStage = new Stage(pet, FrameWidth, FrameHeight);
        var serial = restStage.NextRestSerial();
        for (var i = 0; i < Frames; i++)
        {
            RigFrame f = null!;
            for (var tick = 0; tick < 30; tick++)
            {
                f = restStage.Director.Advance(Stage.Tick, Resting(PetRestPose.Sit, serial, (i * 30 + tick) * Stage.Tick, characterPixels));
            }

            using var shot = restStage.Snapshot(f, characterPixels);
            using var g = Graphics.FromImage(rest);
            g.DrawImageUnscaled(shot, i * FrameWidth, 0);
        }

        rest.Save(Path.Combine(folder, $"{key}-rest.png"), ImageFormat.Png);

        // Sleep: the same held pose four times; the breathing is the window's.
        using var sleep = new Bitmap(FrameWidth * Frames, FrameHeight, PixelFormat.Format32bppPArgb);
        var sleepStage = new Stage(pet, FrameWidth, FrameHeight);
        var sleepSerial = sleepStage.NextRestSerial();
        RigFrame s = null!;
        for (var tick = 0; tick < 60; tick++)
        {
            s = sleepStage.Director.Advance(Stage.Tick, Resting(PetRestPose.Sleep, sleepSerial, tick * Stage.Tick, characterPixels));
        }

        for (var i = 0; i < Frames; i++)
        {
            using var shot = sleepStage.Snapshot(s, characterPixels);
            using var g = Graphics.FromImage(sleep);
            g.DrawImageUnscaled(shot, i * FrameWidth, 0);
        }

        sleep.Save(Path.Combine(folder, $"{key}-sleep.png"), ImageFormat.Png);

        var median = bottoms.OrderBy(b => b).ElementAt(bottoms.Count / 2);
        Console.WriteLine(
            $"        CompanionPetKind.{pet} => new PetInk({lefts.Min():0.000}, {tops.Min():0.000}, {rights.Max():0.000}, {median:0.000}),");
    }

    /// <summary>One stride of the walk, sampled at four evenly spaced points once the legs have found their rhythm.</summary>
    private static IEnumerable<Bitmap> WalkFrames(CompanionPetKind pet, double characterPixels)
    {
        var stage = new Stage(pet, FrameWidth, FrameHeight);
        var strideStep = 74.0 * (characterPixels / 571.0) / 16;
        for (var tick = 0; tick < 70; tick++)
        {
            var frame = stage.Director.Advance(Stage.Tick, Walking(strideStep, characterPixels));
            var phase = tick - 54;
            if (phase is 0 or 4 or 8 or 12)
            {
                yield return stage.Snapshot(frame, characterPixels);
            }
        }
    }

    private static void Measure(Bitmap shot, List<double> lefts, List<double> rights, List<double> tops, List<double> bottoms)
    {
        int left = shot.Width, top = shot.Height, right = -1, bottom = -1;
        var data = shot.LockBits(new Rectangle(0, 0, shot.Width, shot.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        var bytes = new byte[data.Stride * shot.Height];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        shot.UnlockBits(data);

        for (var y = 0; y < shot.Height; y++)
        {
            for (var x = 0; x < shot.Width; x++)
            {
                if (bytes[(y * data.Stride) + (x * 4) + 3] > 8)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        lefts.Add(left / (double)shot.Width);
        rights.Add((right + 1) / (double)shot.Width);
        tops.Add(top / (double)shot.Height);
        bottoms.Add((bottom + 1) / (double)shot.Height);
    }

    public static RigInput Walking(double moved, double characterPixels) => new(
        PetMotionState.Walking, PetRestPose.Sit, 0, 0, 0, 0, moved, 0, characterPixels, 0, 0, false, false);

    public static RigInput Resting(PetRestPose pose, int serial, double elapsed, double characterPixels, double duration = 4.6) => new(
        PetMotionState.Resting, pose, serial, elapsed, elapsed, duration, 0, 0, characterPixels, 0, 0, false, false);
}

internal static class Contact
{
    private const int CellWidth = 280;
    private const int CellHeight = 250;

    public static void Write(CompanionPetKind pet, string folder)
    {
        RigCharacters.TryGet(pet, out var character);
        var key = pet.AssetKey();

        // The window draws the figure 112 pixels tall at its largest; the contact sheet does the same.
        var characterPixels = 112 * RigCharacters.HeightFactor(character);

        var states = new List<(string Label, Func<Stage, int, RigFrame> Make)>();

        states.Add(("walk", (stage, i) => RunWalk(stage, characterPixels, 60 + (i * 3))));
        states.Add(("sit", (stage, i) => RunRest(stage, PetRestPose.Sit, characterPixels, 1.0 + (i * 0.9), 5.0)));
        states.Add(("sleep", (stage, i) => RunRest(stage, PetRestPose.Sleep, characterPixels, 1.0 + (i * 0.9), 8.0)));
        states.Add(("daydream", (stage, i) => RunRest(stage, PetRestPose.Daydream, characterPixels, 1.0 + (i * 0.9), 6.0)));
        states.Add(("stretch", (stage, i) => RunRest(stage, PetRestPose.Stretch, characterPixels, 0.6 + (i * 0.55), 4.2)));
        states.Add(("fall", (stage, i) => Run(stage, PetMotionState.Falling, characterPixels, 0.2 + (i * 0.4))));
        states.Add(("thrown", (stage, i) => Run(stage, PetMotionState.Thrown, characterPixels, 0.1 + (i * 0.15))));
        states.Add(("held", (stage, i) => Run(stage, PetMotionState.Held, characterPixels, 0.2 + (i * 0.3))));
        states.Add(("wall", (stage, i) => RunClimb(stage, PetMotionState.Wall, characterPixels, 50 + (i * 3))));
        states.Add(("dash", (stage, i) => Run(stage, PetMotionState.Dashing, characterPixels, 0.1 + (i * 0.1))));
        states.Add(("hop", (stage, i) => Run(stage, PetMotionState.Hopping, characterPixels, i * 0.1)));
        states.Add(("landing", (stage, i) => Run(stage, PetMotionState.Landing, characterPixels, i * 0.05)));

        Console.WriteLine($"{key}: {states.Count} state rows");
        using var sheet = new Bitmap(CellWidth * 8, CellHeight * states.Count, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(214, 226, 238));
            for (var row = 0; row < states.Count; row++)
            {
                var stage = new Stage(pet, CellWidth, CellHeight, 0.82);
                for (var col = 0; col < 8; col++)
                {
                    var frame = states[row].Make(stage, col);
                    using var cell = stage.Snapshot(frame, characterPixels);
                    g.DrawImageUnscaled(cell, col * CellWidth, row * CellHeight);
                }

                using var font = new Font("Segoe UI", 11, FontStyle.Bold);
                g.DrawString(states[row].Label, font, Brushes.DimGray, 4, (row * CellHeight) + 2);
            }
        }

        sheet.Save(Path.Combine(folder, $"states_{key}.png"), ImageFormat.Png);

        for (var index = 0; index < RigSignatures.PerCharacter; index++)
        {
            WriteSignature(pet, character, index, characterPixels, folder);
        }
    }

    private static void WriteSignature(CompanionPetKind pet, RigCharacter character, int index, double characterPixels, string folder)
    {
        const int Columns = 4;
        const int Rows = 3;
        const int Wide = 640;
        var duration = RigSignatures.Duration(character, index);
        using var sheet = new Bitmap(Wide * Columns, CellHeight * Rows, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(214, 226, 238));
        var stage = new Stage(pet, Wide, CellHeight, 0.82);
        var director = stage.Director;
        var serial = stage.NextRestSerial();
        director.ArmSignature(index);

        var cells = Columns * Rows;
        var tick = 0;
        for (var cell = 0; cell < cells; cell++)
        {
            var target = duration * (cell + 0.5) / cells;
            RigFrame frame = null!;
            while (tick * Stage.Tick < target)
            {
                var t = tick * Stage.Tick;
                frame = director.Advance(
                    Stage.Tick,
                    new RigInput(PetMotionState.Resting, PetRestPose.Sit, serial, t, t, duration, 0, 0, characterPixels, 0, 0, false, false));
                tick++;
            }

            using var shot = stage.Snapshot(frame, characterPixels);
            g.DrawImageUnscaled(shot, (cell % Columns) * Wide, (cell / Columns) * CellHeight);
            var text = RigSignatures.Text(character, index, "en");
            using var font = new Font("Segoe UI", 10, FontStyle.Bold);
            g.DrawString($"{text.Name}  t={target:0.0}s  {text.ShoutAt(target)}", font, Brushes.DimGray, ((cell % Columns) * Wide) + 4, ((cell / Columns) * CellHeight) + 2);
        }

        sheet.Save(Path.Combine(folder, $"sig_{pet.AssetKey()}_{index}.png"), ImageFormat.Png);
    }

    private static RigFrame RunWalk(Stage stage, double characterPixels, int ticks)
    {
        var director = new RigDirector(stage.Character, new Random(11));
        var step = 74.0 * (characterPixels / 571.0) / 16;
        RigFrame frame = null!;
        for (var tick = 0; tick < ticks; tick++)
        {
            frame = director.Advance(Stage.Tick, Sheets.Walking(step, characterPixels));
        }

        return frame;
    }

    private static RigFrame RunClimb(Stage stage, PetMotionState state, double characterPixels, int ticks)
    {
        var director = new RigDirector(stage.Character, new Random(11));
        var step = 74.0 * (characterPixels / 571.0) / 16;
        RigFrame frame = null!;
        for (var tick = 0; tick < ticks; tick++)
        {
            frame = director.Advance(
                Stage.Tick,
                new RigInput(state, PetRestPose.Sit, 0, tick * Stage.Tick, 0, 0, step, 0, characterPixels, 0, 0, false, false));
        }

        return frame;
    }

    private static RigFrame RunRest(Stage stage, PetRestPose pose, double characterPixels, double seconds, double duration)
    {
        var director = new RigDirector(stage.Character, new Random(11));
        RigFrame frame = null!;
        var ticks = (int)(seconds / Stage.Tick);
        for (var tick = 0; tick < ticks; tick++)
        {
            var t = tick * Stage.Tick;
            frame = director.Advance(
                Stage.Tick,
                new RigInput(PetMotionState.Resting, pose, 1, t, t, duration, 0, 0, characterPixels, 0, 0, false, false));
        }

        return frame;
    }

    private static RigFrame Run(Stage stage, PetMotionState state, double characterPixels, double seconds)
    {
        var director = new RigDirector(stage.Character, new Random(11));
        RigFrame frame = null!;
        var ticks = Math.Max(1, (int)(seconds / Stage.Tick));
        for (var tick = 0; tick < ticks; tick++)
        {
            var t = tick * Stage.Tick;
            frame = director.Advance(
                Stage.Tick,
                new RigInput(state, PetRestPose.Sit, 0, t, 0, 0, 0, state == PetMotionState.Held ? 80 * Math.Sin(t * 3) : 0, characterPixels, 0, -characterPixels * 0.8, false, false));
        }

        return frame;
    }
}
