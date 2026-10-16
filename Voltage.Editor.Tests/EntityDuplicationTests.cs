using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class EntityDuplicationTests
{
    private static JsonElement Get(string name) => Call("entity.get", new { entity = name });
    private static int Id(string name) => Get(name).GetProperty("id").GetInt32();
    private static int[] Children(string name) => Get(name).GetProperty("children").EnumerateArray().Select(x => x.GetInt32()).ToArray();
    private static void SameTransform(JsonElement source, JsonElement clone)
    {
        foreach (var member in new[] { "position", "localPosition", "scale" })
            foreach (var axis in new[] { "x", "y" })
                Assert.That(clone.GetProperty(member).GetProperty(axis).GetSingle(),
                    Is.EqualTo(source.GetProperty(member).GetProperty(axis).GetSingle()).Within(.001), member + "." + axis);
        Assert.That(clone.GetProperty("rotation").GetSingle(), Is.EqualTo(source.GetProperty("rotation").GetSingle()).Within(.001));
    }

    [TearDown]
    public void Cleanup()
    {
        Call("input.release");
        foreach (var entity in Call("entity.list", new { filter = "DuplicateTest" }).EnumerateArray().ToArray())
            if (!entity.TryGetProperty("parent", out var parent) || parent.ValueKind == JsonValueKind.Null)
                Call("entity.delete", new { entity = entity.GetProperty("id").GetInt32().ToString() });
    }

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(12)]
    public void Nested_duplicate_keeps_exact_parent_transform_subtree_and_sibling_order_through_undo(int depth)
    {
        Call("entity.create", new { name = "DuplicateTestRoot", x = 80, y = -25 });
        var parent = "DuplicateTestRoot";
        for (int i = 1; i < depth; i++)
        {
            var next = "DuplicateTestParent" + i;
            Call("entity.create", new { name = next, parent }); parent = next;
        }
        Call("entity.set", new { entity = parent, rotation = 37, scaleX = -2, scaleY = 3 });
        Call("entity.create", new { name = "DuplicateTestChild", parent, x = 12, y = -7 });
        Call("entity.set", new { entity = "DuplicateTestChild", rotation = 19, scaleX = 1.4, scaleY = .8 });
        Call("entity.create", new { name = "DuplicateTestLeaf", parent = "DuplicateTestChild", x = 4, y = 8 });
        Call("entity.create", new { name = "DuplicateTestSibling", parent });
        var source = Get("DuplicateTestChild");
        var leaf = Get("DuplicateTestLeaf");
        var clone = Call("entity.duplicate", new { entity = "DuplicateTestChild", name = "DuplicateTestCopy" });
        Assert.That(clone.GetProperty("parent").GetInt32(), Is.EqualTo(Id(parent)));
        SameTransform(source, clone);
        var cloneId = clone.GetProperty("id").GetInt32();
        var expected = new[] { Id("DuplicateTestChild"), cloneId, Id("DuplicateTestSibling") };
        Assert.That(Children(parent), Is.EqualTo(expected));
        var clonedLeafId = clone.GetProperty("children")[0].GetInt32();
        var clonedLeaf = Get(clonedLeafId.ToString());
        Assert.That(clonedLeaf.GetProperty("parent").GetInt32(), Is.EqualTo(cloneId));
        SameTransform(leaf, clonedLeaf);
        Call("undo");
        Assert.That(Children(parent), Is.EqualTo(new[] { Id("DuplicateTestChild"), Id("DuplicateTestSibling") }));
        Call("redo");
        Assert.That(Children(parent), Is.EqualTo(expected));
        SameTransform(source, Get("DuplicateTestCopy"));
    }

    [Test]
    public void Root_duplicate_remains_a_root()
    {
        Call("entity.create", new { name = "DuplicateTestRoot", x = 17, y = 31 });
        var clone = Call("entity.duplicate", new { entity = "DuplicateTestRoot" });
        Assert.That(!clone.TryGetProperty("parent", out var parent) || parent.ValueKind == JsonValueKind.Null, Is.True);
        SameTransform(Get("DuplicateTestRoot"), clone);
    }

    [Test]
    public void Multiple_selected_children_keep_their_own_parents_and_undo_as_one_step()
    {
        foreach (var suffix in new[] { "A", "B" })
        {
            Call("entity.create", new { name = "DuplicateTestRoot" + suffix });
            Call("entity.create", new { name = "DuplicateTestChild" + suffix, parent = "DuplicateTestRoot" + suffix });
        }
        WaitForWidget("DuplicateTestRootA (1)", "Scene Graph");
        Call("ui.click", new { label = "DuplicateTestRootA (1)", window = "Scene Graph", exact = true });
        Call("entity.select", new { entities = new[] { "DuplicateTestChildA", "DuplicateTestChildB" } });
        Call("input.key", new { key = "d", modifiers = new[] { "ctrl" } });
        Thread.Sleep(250);
        foreach (var suffix in new[] { "A", "B" })
        {
            var children = Children("DuplicateTestRoot" + suffix);
            Assert.That(children.Length, Is.EqualTo(2));
            Assert.That(children[0], Is.EqualTo(Id("DuplicateTestChild" + suffix)));
            SameTransform(Get(children[0].ToString()), Get(children[1].ToString()));
        }
        Call("undo");
        Assert.That(Children("DuplicateTestRootA").Length, Is.EqualTo(1));
        Assert.That(Children("DuplicateTestRootB").Length, Is.EqualTo(1));
        Call("redo");
        Assert.That(Children("DuplicateTestRootA").Length, Is.EqualTo(2));
        Assert.That(Children("DuplicateTestRootB").Length, Is.EqualTo(2));
    }
}
