using System;
using MessagePack;
using MessagePack.Formatters;

namespace Project16.Foundation.Preferences
{
    [MessagePackFormatter(typeof(UserPreferencesSnapshotFormatter))]
    public readonly struct UserPreferencesSnapshot
    {
        public float MusicVolume { get; }
        public float EffectsVolume { get; }
        public string Language { get; }

        public UserPreferencesSnapshot(float musicVolume, float effectsVolume, string language)
        {
            ValidateVolume(musicVolume);
            ValidateVolume(effectsVolume);
            if (language == null || language.Length > 64)
                throw new ArgumentException("Language must be an identifier of up to 64 characters.", nameof(language));
            MusicVolume = musicVolume;
            EffectsVolume = effectsVolume;
            Language = language;
        }

        public static UserPreferencesSnapshot Default() => new(1, 1, "");

        internal static void ValidateVolume(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1)
                throw new ArgumentOutOfRangeException(nameof(value), "Volume must be finite and between 0 and 1.");
        }
    }

    /// <summary>Explicit formatter keeps the base save path usable under IL2CPP without dynamic code generation.</summary>
    [UnityEngine.Scripting.Preserve]
    public sealed class UserPreferencesSnapshotFormatter : IMessagePackFormatter<UserPreferencesSnapshot>
    {
        public void Serialize(ref MessagePackWriter writer, UserPreferencesSnapshot value, MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(3);
            writer.Write(value.MusicVolume);
            writer.Write(value.EffectsVolume);
            writer.Write(value.Language);
        }

        public UserPreferencesSnapshot Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            var length = reader.ReadArrayHeader();
            if (length != 3) throw new MessagePackSerializationException("Unsupported preferences payload.");
            return new UserPreferencesSnapshot(reader.ReadSingle(), reader.ReadSingle(), reader.ReadString());
        }
    }
}
