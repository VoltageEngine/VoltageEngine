using System;
using ImGuiNET;
using NUnit.Framework;
using Voltage.Editor.ImGuiCore;

namespace Voltage.Effects.Tests;

[TestFixture, NonParallelizable]
public class ImGuiFrameTests
{
	[Test]
	public void Starting_another_layout_finishes_a_frame_that_was_not_drawn()
	{
		if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("VOLTAGE_TEST_GPU") != "1")
			Assert.Ignore("Requires a native desktop graphics session.");
		using var game = new FrameGame();
		game.Run();
	}

	private sealed class FrameGame : Core
	{
		public FrameGame() : base(32, 32, windowTitle: "ImGui frame lifecycle check") { }

		protected override void Initialize()
		{
			base.Initialize();
			var renderer = new ImGuiRenderer(this);
			var context = ImGui.GetCurrentContext();
			try
			{
				renderer.RebuildFontAtlas(new ImGuiOptions());
				var originalTextScale = renderer.TextScalePercent;
				var uiSize = renderer.DefaultFontPtr.FontSize;
				var codeSize = renderer.LogFontPtr.FontSize;
				var iconSize = renderer.IconFontPtr.FontSize;
				var displayScale = renderer.AppliedDisplayScale;
				try
				{
					renderer.TextScalePercent = 160;
					renderer.BeforeLayout(1f / 60f);
					Assert.That(renderer.DefaultFontPtr.FontSize, Is.GreaterThan(uiSize));
					Assert.That(renderer.DefaultFontPtr.FontSize % 1, Is.Zero);
					Assert.That(renderer.LogFontPtr.FontSize, Is.EqualTo(codeSize));
					Assert.That(renderer.IconFontPtr.FontSize, Is.EqualTo(iconSize));
					Assert.That(renderer.AppliedDisplayScale, Is.EqualTo(displayScale));
				}
				finally { renderer.TextScalePercent = originalTextScale; }
				renderer.BeforeLayout(0);
				Assert.That(renderer.FrameOpen, Is.True);
				var firstFrame = ImGui.GetFrameCount();
				ImGui.Begin("Skipped draw");
				ImGui.Text("This frame will not render.");
				ImGui.End();
				renderer.BeforeLayout(1f / 60f);
				Assert.That(ImGui.GetFrameCount(), Is.EqualTo(firstFrame + 1));
				Assert.That(renderer.FrameOpen, Is.True);
				renderer.AfterLayout();
				Assert.That(renderer.FrameOpen, Is.False);
				renderer.BeforeLayout(1f / 60f);
				renderer.DiscardFrame();
				renderer.DiscardFrame();
				Assert.That(renderer.FrameOpen, Is.False);
				renderer.BeforeLayout(1f / 60f);
				renderer.AfterLayout();
				Assert.That(renderer.FrameOpen, Is.False);
			}
			finally { ImGui.DestroyContext(context); }
			Exit();
		}
	}
}
