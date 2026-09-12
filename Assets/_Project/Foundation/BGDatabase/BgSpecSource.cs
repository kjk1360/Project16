using System;
using System.Collections.Generic;
using BansheeGz.BGDatabase;
using Project16.Foundation.Specs;
using UnityEngine;

namespace Project16.Foundation.BGDatabase
{
    /// <summary>
    /// Composition-only reader for a private BG repository. Map all required tables,
    /// dispose the source, then bind the resulting ISpecTable snapshots in KDI.
    /// </summary>
    public sealed class BgSpecSource : IDisposable
    {
        private BGRepo _repo;

        private BgSpecSource(BGRepo repo) => _repo = repo;

        public static BgSpecSource Load(TextAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            return Load(asset.bytes);
        }

        public static BgSpecSource Load(byte[] content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            // BG's binary reader treats fewer than five bytes as an empty database.
            // Reject a missing/truncated asset instead of publishing empty Specs.
            if (content.Length < 5)
                throw new SpecValidationException("The BG specification database is empty or truncated.");

            try
            {
                // The instance reader avoids BGRepo.I and the mutable global Reader.
                // It reads content without activating default-repository addons.
                return new BgSpecSource(new BGRepoBinary().Read(content));
            }
            catch (Exception error)
            {
                throw new SpecValidationException("Could not read the BG specification database.", error);
            }
        }

        public SpecTable<T> ReadTable<T>(string tableName, Func<BgSpecRowReader, T> map)
            where T : struct, ISpecRow
        {
            RequireAlive();
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentException("A table name is required.", nameof(tableName));
            if (map == null) throw new ArgumentNullException(nameof(map));
            var table = _repo.GetMeta(tableName);
            if (table == null) throw new SpecValidationException($"The BG specification table '{tableName}' is missing.");

            var rows = new List<T>(table.CountEntities);
            for (var index = 0; index < table.CountEntities; index++)
            {
                try
                {
                    rows.Add(map(new BgSpecRowReader(this, table.GetEntity(index))));
                }
                catch (SpecValidationException) { throw; }
                catch (Exception error)
                {
                    throw new SpecValidationException($"Could not map BG table '{tableName}', row {index}.", error);
                }
            }
            return new SpecTable<T>(rows);
        }

        internal void RequireAlive()
        {
            if (_repo == null) throw new ObjectDisposedException(nameof(BgSpecSource));
        }

        public void Dispose()
        {
            var repo = _repo;
            _repo = null;
            repo?.Clear();
        }
    }
}
