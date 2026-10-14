using Microsoft.Xna.Framework;
using NUnit.Framework;
using Voltage.Editor.Tools.Tilemap;
using Voltage.Editor.Undo.Core;

namespace Voltage.Effects.Tests;

[TestFixture, NonParallelizable]
public class TileSelectionTests
{
    private sealed class TestTilemap : TilemapRenderer
    {
        public bool Visible { set => _isVisible = value; }
    }

    private TestTilemap _map;
    private TilePaintTool _tool;

    [SetUp]
    public void SetUp()
    {
        Physics.Reset();
        EditorChangeTracker.Clear();
        _map = new Entity("tiles").AddComponent(new TestTilemap { AutoBuildColliders = false });
        _tool = new TilePaintTool { Target = _map, Tool = TileTool.Select };
    }

    [TearDown]
    public void TearDown() => EditorChangeTracker.Clear();

    [Test]
    public void Picking_uses_occupied_cells_transforms_visibility_and_render_order()
    {
        _map.Visible = true;
        _map.LayerDepth = .8f;
        _map.SetTile(0, 0, 1);
        _map.Entity.Position = new Vector2(100, 50);
        var front = new Entity("front").AddComponent(new TestTilemap { Visible = true, LayerDepth = .1f });
        front.Entity.Position = _map.Entity.Position;
        front.SetTile(0, 0, 2);
        var maps = new[] { _map, front };
        Assert.That(TilemapSceneUtils.PickTilemap(new Vector2(101, 51), maps), Is.SameAs(front));
        front.Enabled = false;
        Assert.That(TilemapSceneUtils.PickTilemap(new Vector2(101, 51), maps), Is.SameAs(_map));
        _map.Visible = false;
        Assert.That(TilemapSceneUtils.PickTilemap(new Vector2(101, 51), maps), Is.Null);
        _map.Visible = true;
        Assert.That(TilemapSceneUtils.PickTilemap(new Vector2(150, 51), maps), Is.Null);
    }

    [Test]
    public void Sparse_marquee_moves_only_occupied_cells_and_preserves_destination_holes()
    {
        _map.SetTile(0, 0, 1);
        _map.SetTile(2, 0, 2);
        _map.SetTile(6, 0, 9);
        _tool.SelectRegion(new Rectangle(0, 0, 3, 1));
        Assert.That(_tool.SelectedCellCount, Is.EqualTo(2));
        Assert.That(_tool.MoveSelection(new Point(5, 0)), Is.True);
        Assert.That(_map.GetTile(5, 0), Is.EqualTo(1));
        Assert.That(_map.GetTile(6, 0), Is.EqualTo(9));
        Assert.That(_map.GetTile(7, 0), Is.EqualTo(2));
        EditorChangeTracker.Undo();
        Assert.That(_map.GetTile(0, 0), Is.EqualTo(1));
        Assert.That(_map.GetTile(2, 0), Is.EqualTo(2));
        Assert.That(_map.GetTile(5, 0), Is.EqualTo(-1));
        EditorChangeTracker.Redo();
        Assert.That(_map.GetTile(7, 0), Is.EqualTo(2));
    }

    [Test]
    public void Self_overlap_moves_full_stack_orientation_and_collision_and_undo_restores_selection()
    {
        _map.SetCellStack(0, 0, new[] { 3, 8 });
        _map.SetOrientation(0, 0, TilemapRenderer.MakeOrientation(1, true, false));
        _map.SetCollision(0, 0, true);
        _map.SetTile(1, 0, 4);
        _tool.SelectRegion(new Rectangle(0, 0, 2, 1));
        Assert.That(_tool.MoveSelection(new Point(1, 0)), Is.True);
        Assert.That(_map.GetCellStack(1, 0), Is.EqualTo(new[] { 3, 8 }));
        Assert.That(_map.GetOrientation(1, 0), Is.EqualTo(TilemapRenderer.MakeOrientation(1, true, false)));
        Assert.That(_map.GetCollision(1, 0), Is.True);
        Assert.That(_map.GetTile(2, 0), Is.EqualTo(4));
        Assert.That(EditorChangeTracker.UndoActions.Count, Is.EqualTo(1));
        EditorChangeTracker.Undo();
        Assert.That(_map.GetCellStack(0, 0), Is.EqualTo(new[] { 3, 8 }));
        EditorChangeTracker.Redo();
        Assert.That(_map.GetOrientation(1, 0), Is.EqualTo(TilemapRenderer.MakeOrientation(1, true, false)));
        Assert.That(_map.GetCollision(1, 0), Is.True);
        EditorChangeTracker.Undo();
        Assert.That(_tool.MoveSelection(new Point(-2, 0)), Is.True);
        Assert.That(_map.GetTile(-1, 0), Is.EqualTo(4), "Undo must restore selected cell coordinates too");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Occupied_destination_blocks_whole_move_without_an_undo_entry(bool collision)
    {
        _map.SetTile(0, 0, 1);
        if (collision) _map.SetCollision(1, 0, true);
        else _map.SetTile(1, 0, 2);
        _tool.SelectRegion(new Rectangle(0, 0, 1, 1));
        Assert.That(_tool.MoveSelection(new Point(1, 0)), Is.False);
        Assert.That(_map.GetTile(0, 0), Is.EqualTo(1));
        Assert.That(EditorChangeTracker.CanUndo, Is.False);
        Assert.That(_tool.SelectionMessage, Does.Contain("blocked"));
    }

    [Test]
    public void Additive_marquees_deduplicate_and_move_collision_only_cells()
    {
        _map.SetTile(-3, -2, 1);
        _map.SetCollision(-1, -2, true);
        _tool.SelectRegion(new Rectangle(-3, -2, 3, 1));
        _tool.SelectRegion(new Rectangle(-1, -2, 1, 1), true);
        Assert.That(_tool.SelectedCellCount, Is.EqualTo(2));
        Assert.That(_tool.MoveSelection(new Point(0, 2)), Is.True);
        Assert.That(_map.GetCollision(-1, 0), Is.True);
        Assert.That(_map.GetCollision(-1, -2), Is.False);
        Assert.That(Physics.GetAllColliders(), Is.Empty, "Selection must respect disabled automatic collider generation");
    }

    [Test]
    public void Delete_and_undo_restore_sparse_selection_and_a_zero_move_is_not_an_edit()
    {
        _map.SetTile(0, 0, 1);
        _map.SetCollision(2, 0, true);
        _tool.SelectRegion(new Rectangle(0, 0, 3, 1));
        Assert.That(_tool.MoveSelection(Point.Zero), Is.False);
        Assert.That(EditorChangeTracker.CanUndo, Is.False);
        _tool.DeleteSelection();
        Assert.That(_tool.HasTileSelection, Is.False);
        Assert.That(_map.GetCollision(2, 0), Is.False);
        EditorChangeTracker.Undo();
        Assert.That(_tool.SelectedCellCount, Is.EqualTo(2));
        Assert.That(_map.GetCollision(2, 0), Is.True);
        EditorChangeTracker.Redo();
        Assert.That(_map.GetTile(0, 0), Is.EqualTo(-1));
    }

    [Test]
    public void Enabled_automatic_collision_generation_rebuilds_after_move_and_undo()
    {
        _map.AutoBuildColliders = true;
        _map.SetCollision(0, 0, true);
        _tool.SelectRegion(new Rectangle(0, 0, 1, 1));
        _tool.MoveSelection(new Point(3, 0));
        Assert.That(Physics.GetAllColliders(), Is.Not.Empty);
        EditorChangeTracker.Undo();
        Assert.That(_map.GetCollision(0, 0), Is.True);
        Assert.That(_map.GetCollision(3, 0), Is.False);
    }

    [Test]
    public void Switching_layer_or_scene_clears_selection_and_cancelling_does_not_edit_tiles()
    {
        _map.SetTile(0, 0, 1);
        _tool.SelectRegion(new Rectangle(0, 0, 1, 1));
        _tool.CancelSelectionGesture();
        Assert.That(_tool.SelectedCellCount, Is.EqualTo(1));
        Assert.That(_map.GetTile(0, 0), Is.EqualTo(1));
        _tool.Target = new Entity("other").AddComponent(new TilemapRenderer());
        Assert.That(_tool.HasTileSelection, Is.False);
        _tool.Target = _map;
        _tool.SelectRegion(new Rectangle(0, 0, 1, 1));
        _tool.OnSceneChanged();
        Assert.That(_tool.HasTileSelection, Is.False);
        Assert.That(EditorChangeTracker.CanUndo, Is.False);
    }

    [Test]
    public void Sparse_rotate_undo_redo_preserves_selection_and_tile_data()
    {
        _map.SetTile(0, 0, 1);
        _map.SetTile(2, 0, 2);
        _tool.SelectRegion(new Rectangle(0, 0, 3, 1));
        _tool.RotateSelection(1);
        Assert.That(_tool.SelectedCellCount, Is.EqualTo(2));
        Assert.That(_map.GetTile(1, -1), Is.EqualTo(1));
        Assert.That(_map.GetTile(1, 1), Is.EqualTo(2));
        EditorChangeTracker.Undo();
        EditorChangeTracker.Redo();
        Assert.That(_tool.MoveSelection(new Point(3, 0)), Is.True);
        Assert.That(_map.GetTile(4, 1), Is.EqualTo(2));
    }
}
