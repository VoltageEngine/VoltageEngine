using Voltage.Editor.Persistence;

namespace Voltage.Editor.Hotkeys
{
	/// <summary>One rebindable editor command, with an optional second binding. Both are persisted.</summary>
	public sealed class HotkeyAction
	{
		private readonly PersistentString _storedPrimary;
		private readonly PersistentString _storedAlternate;

		public string Id { get; }
		public string Category { get; }
		public string Label { get; }
		public string Tooltip { get; }
		public HotkeyBinding DefaultPrimary { get; }
		public HotkeyBinding DefaultAlternate { get; }
		public HotkeyBinding Primary { get; private set; }
		public HotkeyBinding Alternate { get; private set; }

		internal HotkeyAction(string id, string category, string label, string tooltip,
			HotkeyBinding defaultPrimary, HotkeyBinding defaultAlternate)
		{
			Id = id;
			Category = category;
			Label = label;
			Tooltip = tooltip;
			DefaultPrimary = defaultPrimary;
			DefaultAlternate = defaultAlternate;

			_storedPrimary = new PersistentString($"Hotkey_{id}", defaultPrimary.ToString());
			_storedAlternate = new PersistentString($"Hotkey_{id}_Alt", defaultAlternate.ToString());

			Primary = HotkeyBinding.TryParse(_storedPrimary.Value, out var primary) ? primary : defaultPrimary;
			Alternate = HotkeyBinding.TryParse(_storedAlternate.Value, out var alt) ? alt : defaultAlternate;
		}

		public bool IsDefault => Primary == DefaultPrimary && Alternate == DefaultAlternate;

		/// <summary>Menu shortcut column: the primary only, blank when unbound.</summary>
		public string MenuLabel => Primary.IsBound ? Primary.ToDisplayString() : string.Empty;

		public bool Pressed(bool repeat = false) => Primary.Pressed(repeat) || Alternate.Pressed(repeat);

		public bool Down() => Primary.Down() || Alternate.Down();

		public HotkeyBinding Get(bool alternate) => alternate ? Alternate : Primary;

		public void Rebind(HotkeyBinding binding, bool alternate = false)
		{
			if (alternate)
			{
				Alternate = binding;
				_storedAlternate.Value = binding.ToString();
				return;
			}

			Primary = binding;
			_storedPrimary.Value = binding.ToString();
		}

		public void ResetToDefault()
		{
			Rebind(DefaultPrimary);
			Rebind(DefaultAlternate, alternate: true);
		}
	}
}
