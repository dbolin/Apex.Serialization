namespace Apex.Serialization
{
    public enum Mode
    {
        Tree = 0,
        Graph = 1
    }

    public sealed class Settings
    {
        public static Settings Default { get; set; } = new Settings();

        public Mode SerializationMode { get; set; }

        public bool AllowFunctionSerialization { get; set; }

        public bool SupportSerializationHooks { get; set; }

        public static implicit operator ImmutableSettings(Settings s)
        {
            return new ImmutableSettings(s.SerializationMode, s.AllowFunctionSerialization, s.SupportSerializationHooks);
        }
    }

    public sealed class ImmutableSettings
    {
        public Mode SerializationMode { get; }
        public bool AllowFunctionSerialization { get; }
        public bool SupportSerializationHooks { get; }
        public bool UseConstructors { get; } = true;
        public bool EnableInlining { get; } = true;

        public ImmutableSettings(Mode serializationMode, bool allowFunctionSerialization, bool supportSerializationHooks)
        {
            SerializationMode = serializationMode;
            AllowFunctionSerialization = allowFunctionSerialization;
            SupportSerializationHooks = supportSerializationHooks;
        }

        internal const int MaxSettingsIndex = 0b111;
        internal int SettingsIndex => (int) SerializationMode | (AllowFunctionSerialization ? 0b10 : 0) | (SupportSerializationHooks ? 0b100 : 0);
    }
}
