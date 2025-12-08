using System;

namespace Voltage.Editor.Persistence
{
	public class PersistentString
	{
		public string Key { get; }
		private string _value;

		public PersistentString(string key, string defaultValue = "")
		{
			Key = key;
			_value = EditorSettingsLoader.LoadSetting(key, defaultValue);
		}

		public string Value
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

		/// <summary>
		/// Implicit conversion to string for easier usage
		/// </summary>
		public static implicit operator string(PersistentString persistentString) => persistentString.Value;
	}
}