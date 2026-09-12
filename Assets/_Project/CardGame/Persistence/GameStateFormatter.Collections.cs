using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using MessagePack;

namespace Project16.CardGame.Persistence
{
    public sealed partial class GameStateFormatter
    {
        public const int MaximumCollectionEntries = 16384;
        public const int MaximumTotalCollectionEntries = 100000;
        public const int MaximumStringBytes = 4096;

        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private delegate T ReadValue<T>(ref MessagePackReader reader, CodecLimits limits);
        private delegate void WriteValue<T>(ref MessagePackWriter writer, T value, CodecLimits limits);

        private sealed class CodecLimits
        {
            public readonly MessagePackSerializerOptions Options;
            private int _entries;

            public CodecLimits(MessagePackSerializerOptions options) => Options = options;

            public void Count(int count)
            {
                if (count < 0 || count > MaximumCollectionEntries ||
                    count > MaximumTotalCollectionEntries - _entries)
                    throw Invalid("Game snapshot collection limit exceeded.");
                _entries += count;
            }
        }

        private static MessagePackSerializationException Invalid(string message) =>
            new MessagePackSerializationException(message);

        private static void ReadShape(ref MessagePackReader reader, int expected, string type)
        {
            if (reader.TryReadNil() || reader.ReadArrayHeader() != expected)
                throw Invalid(type + " has missing or unsupported fields.");
        }

        private static string ReadText(ref MessagePackReader reader, CodecLimits limits)
        {
            var bytes = reader.ReadStringSequence();
            if (!bytes.HasValue) return null;
            if (bytes.Value.Length > MaximumStringBytes)
                throw Invalid("Game snapshot string limit exceeded.");
            return StrictUtf8.GetString(bytes.Value.ToArray());
        }

        private static string ReadRequiredText(ref MessagePackReader reader, CodecLimits limits)
        {
            var text = ReadText(ref reader, limits);
            if (string.IsNullOrEmpty(text)) throw Invalid("Game snapshot identifier is missing.");
            return text;
        }

        private static void WriteText(ref MessagePackWriter writer, string value, CodecLimits limits)
        {
            if (value != null && StrictUtf8.GetByteCount(value) > MaximumStringBytes)
                throw Invalid("Game snapshot string limit exceeded.");
            writer.Write(value);
        }

        private static void WriteRequiredText(ref MessagePackWriter writer, string value, CodecLimits limits)
        {
            if (string.IsNullOrEmpty(value)) throw Invalid("Game snapshot identifier is missing.");
            WriteText(ref writer, value, limits);
        }

        private static int ReadNumber(ref MessagePackReader reader, CodecLimits limits) => reader.ReadInt32();
        private static void WriteNumber(ref MessagePackWriter writer, int value, CodecLimits limits) => writer.Write(value);

        private static List<T> ReadList<T>(ref MessagePackReader reader, CodecLimits limits, ReadValue<T> read)
        {
            if (reader.TryReadNil()) throw Invalid("Game snapshot collection is null.");
            int count = reader.ReadArrayHeader();
            limits.Count(count);
            limits.Options.Security.DepthStep(ref reader);
            try
            {
                var result = new List<T>(count);
                for (int i = 0; i < count; i++) result.Add(read(ref reader, limits));
                return result;
            }
            finally { reader.Depth--; }
        }

        private static void WriteList<T>(ref MessagePackWriter writer, List<T> values, CodecLimits limits, WriteValue<T> write)
        {
            if (values == null) throw Invalid("Game snapshot collection is null.");
            limits.Count(values.Count);
            writer.WriteArrayHeader(values.Count);
            foreach (var value in values) write(ref writer, value, limits);
        }

        private static Dictionary<string, T> ReadMap<T>(ref MessagePackReader reader, CodecLimits limits, ReadValue<T> read)
        {
            if (reader.TryReadNil()) throw Invalid("Game snapshot map is null.");
            int count = reader.ReadMapHeader();
            limits.Count(count);
            limits.Options.Security.DepthStep(ref reader);
            try
            {
                var result = new Dictionary<string, T>(count, limits.Options.Security.GetEqualityComparer<string>());
                for (int i = 0; i < count; i++)
                {
                    var key = ReadRequiredText(ref reader, limits);
                    if (result.ContainsKey(key)) throw Invalid("Game snapshot contains a duplicate map key.");
                    result.Add(key, read(ref reader, limits));
                }
                return result;
            }
            finally { reader.Depth--; }
        }

        private static void WriteMap<T>(ref MessagePackWriter writer, Dictionary<string, T> values,
            CodecLimits limits, WriteValue<T> write)
        {
            if (values == null) throw Invalid("Game snapshot map is null.");
            limits.Count(values.Count);
            writer.WriteMapHeader(values.Count);
            var keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                WriteRequiredText(ref writer, key, limits);
                write(ref writer, values[key], limits);
            }
        }
    }
}
