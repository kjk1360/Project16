using System;
using System.IO;

namespace Project16.Foundation.Persistence
{
    /// <summary>
    /// Stores one complete MessagePack envelope per unit. Replacements are atomic per file,
    /// not across files. Keep values requiring a shared commit in the same unit.
    /// </summary>
    public sealed class LocalFileSaveStore : ILocalSaveStore
    {
        public const int MaximumFileBytes = 8 * 1024 * 1024;

        public LocalFileSaveStore(string storageRoot)
        {
            if (string.IsNullOrWhiteSpace(storageRoot))
                throw new ArgumentException("A storage directory is required.", nameof(storageRoot));
            StorageRoot = Path.GetFullPath(storageRoot);
        }

        public string StorageRoot { get; }

        public string GetPrimaryPath(string key)
        {
            SaveKey.Validate(key);
            return Path.Combine(StorageRoot, key + ".mpk");
        }

        public string GetBackupPath(string key) => GetPrimaryPath(key) + ".bak";

        public byte[] ReadPrimary(string key) => Read(GetPrimaryPath(key));
        public byte[] ReadBackup(string key) => Read(GetBackupPath(key));

        private static byte[] Read(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length > MaximumFileBytes)
                    throw new InvalidDataException($"Save file exceeds {MaximumFileBytes} bytes.");
                var bytes = new byte[checked((int)stream.Length)];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int count = stream.Read(bytes, offset, bytes.Length - offset);
                    if (count == 0) throw new EndOfStreamException("Save file ended during reading.");
                    offset += count;
                }
                return bytes;
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        public void Write(string key, byte[] envelope, bool preserveBackup)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (envelope.Length > MaximumFileBytes)
                throw new InvalidDataException($"Save envelope exceeds {MaximumFileBytes} bytes.");
            string primary = GetPrimaryPath(key);
            string temporary = primary + ".tmp";
            Directory.CreateDirectory(StorageRoot);

            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(envelope, 0, envelope.Length);
                    stream.Flush(true);
                }

                if (File.Exists(primary))
                {
                    // A recovered unit must not rotate its corrupt primary over the valid backup.
                    // Unsupported atomic replacement fails closed; there is no delete/move fallback.
                    File.Replace(temporary, primary, preserveBackup ? null : GetBackupPath(key));
                }
                else
                {
                    File.Move(temporary, primary);
                }
            }
            finally
            {
                // Cleanup is best effort and must never turn a committed write into a reported failure.
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
