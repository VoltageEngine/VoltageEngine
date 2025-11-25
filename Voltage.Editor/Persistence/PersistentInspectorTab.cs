using Nez.ImGuiTools.Persistence;

namespace Nez.ImGuiTools.Utils
{
    /// <summary>
    /// Tracks the last selected inspector tab across sessions.
    /// </summary>
    public class PersistentInspectorTab
    {
        public enum InspectorTabType
        {
            MainEntityInspector,
            CoreWindow,
            DebugWindow
        }

        public string Key { get; }
        private InspectorTabType _value;

        public PersistentInspectorTab(string key, InspectorTabType defaultValue = InspectorTabType.MainEntityInspector)
        {
            Key = key;
            
            // Load the saved value as a string and parse it to enum
            var savedValue = ImGuiSettingsLoader.LoadSetting(key, defaultValue.ToString());
            if (System.Enum.TryParse<InspectorTabType>(savedValue, out var parsedValue))
            {
                _value = parsedValue;
            }
            else
            {
                _value = defaultValue;
            }
        }

        public InspectorTabType Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    ImGuiSettingsLoader.SaveSetting(Key, _value.ToString());
                }
            }
        }

        /// <summary>
        /// Implicit conversion to InspectorTabType for easier usage
        /// </summary>
        public static implicit operator InspectorTabType(PersistentInspectorTab persistentTab) => persistentTab.Value;
    }
}