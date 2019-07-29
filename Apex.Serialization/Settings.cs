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

        public bool UseConstructors { get; set; }

        public static implicit operator ImmutableSettings(Settings s)
        {
            return new ImmutableSettings(s.SerializationMode, s.AllowFunctionSerialization, s.SupportSerializationHooks, s.UseConstructors);
        }
    }

    public sealed class ImmutableSettings
    {
        public Mode SerializationMode { get; }
        public bool AllowFunctionSerialization { get; }
        public bool SupportSerializationHooks { get; }
        public bool UseConstructors { get; }

        public ImmutableSettings(Mode serializationMode, bool allowFunctionSerialization, bool supportSerializationHooks, bool useConstructors)
        {
            SerializationMode = serializationMode;
            AllowFunctionSerialization = allowFunctionSerialization;
            SupportSerializationHooks = supportSerializationHooks;
            UseConstructors = useConstructors;
        }

        internal const int MaxSettingsIndex = 0b1111;
        internal int SettingsIndex => (int) SerializationMode | (AllowFunctionSerialization ? 0b10 : 0) | (SupportSerializationHooks ? 0b100 : 0) | (UseConstructors ? 0b1000 : 0);
    }
}
