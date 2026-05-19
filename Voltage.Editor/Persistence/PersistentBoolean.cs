namespace Voltage.Editor.Persistence
{
	/// <summary>
	/// A boolean setting that persists its value across sessions using EditorSettingsLoader.
	/// </summary>
	public class PersistentBool
	{
		public string Key { get; }
		private bool _value;

		public PersistentBool(string key, bool defaultValue = false)
		{
			Key = key;
			_value = EditorSettingsLoader.LoadSetting(key, defaultValue);
		}

		public bool Value
		{
			get => _value;
			set
			{
				if (_value != value)
				{
					_value = value;
					EditorSettingsLoader.SaveSetting(Key, _value);
				}
			}
		}
	}
}