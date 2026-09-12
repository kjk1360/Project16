using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using MessagePack;

namespace Project16.Foundation.Persistence
{
    internal sealed class SaveEnvelope
    {
        public string Key;
        public int SchemaVersion;
        public long Revision;
        public byte[] Payload;
    }

    internal sealed class UnsupportedSaveFormatException : Exception
    {
        public UnsupportedSaveFormatException(int version)
            : base($"Save envelope format {version} is unsupported; expected 1.") { }
    }

    /// <summary>Explicit wire codec, usable on IL2CPP without generated DTO formatters.</summary>
    internal static class SaveEnvelopeCodec
    {
        private const string Magic = "Project16.LocalSave";
        private const int FormatVersion = 1;

        public static byte[] Encode(string key, int schemaVersion, long revision, byte[] payload)
        {
            var envelope = new SaveEnvelope
            {
                Key = key,
                SchemaVersion = schemaVersion,
                Revision = revision,
                Payload = payload
            };
            byte[] checksum = ComputeChecksum(envelope);
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new MessagePackWriter(buffer);
            writer.WriteArrayHeader(7);
            WriteContent(ref writer, envelope);
            writer.Write(checksum);
            writer.Flush();
            return buffer.WrittenSpan.ToArray();
        }

        public static SaveEnvelope Decode(string key, byte[] bytes)
        {
            if (bytes.Length > LocalFileSaveStore.MaximumFileBytes)
                throw new InvalidDataException("Save file is larger than the supported limit.");
            var reader = new MessagePackReader(new ReadOnlyMemory<byte>(bytes));
            if (reader.ReadArrayHeader() != 7 || reader.ReadString() != Magic)
                throw new InvalidDataException("Invalid save envelope header.");
            int formatVersion = reader.ReadInt32();
            if (formatVersion != FormatVersion)
                throw new UnsupportedSaveFormatException(formatVersion);

            var envelope = new SaveEnvelope
            {
                Key = reader.ReadString(),
                SchemaVersion = reader.ReadInt32(),
                Revision = reader.ReadInt64(),
                Payload = reader.ReadBytes()?.ToArray()
            };
            byte[] checksum = reader.ReadBytes()?.ToArray();
            if (!reader.End || envelope.Key != key || envelope.SchemaVersion < 1 ||
                envelope.Revision < 1 || envelope.Payload == null || checksum == null || checksum.Length != 32)
                throw new InvalidDataException("Invalid save envelope values or trailing bytes.");

            byte[] expected = ComputeChecksum(envelope);
            int differences = 0;
            for (int i = 0; i < expected.Length; i++) differences |= expected[i] ^ checksum[i];
            if (differences != 0) throw new InvalidDataException("Save checksum does not match its contents.");
            return envelope;
        }

        private static byte[] ComputeChecksum(SaveEnvelope envelope)
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new MessagePackWriter(buffer);
            writer.WriteArrayHeader(6);
            WriteContent(ref writer, envelope);
            writer.Flush();
            using var sha = SHA256.Create();
            return sha.ComputeHash(buffer.WrittenSpan.ToArray());
        }

        private static void WriteContent(ref MessagePackWriter writer, SaveEnvelope envelope)
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(envelope.Key);
            writer.Write(envelope.SchemaVersion);
            writer.Write(envelope.Revision);
            writer.Write(envelope.Payload);
        }
    }
}
