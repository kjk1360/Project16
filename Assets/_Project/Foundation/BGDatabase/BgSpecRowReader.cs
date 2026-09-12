using System;
using System.Collections.Generic;
using BansheeGz.BGDatabase;
using Project16.Foundation.Specs;

namespace Project16.Foundation.BGDatabase
{
    /// <summary>
    /// Read-only mapper input, valid only while its source is open. Copy values into
    /// an immutable ISpecRow; never retain this reader in the resulting snapshot.
    /// </summary>
    public sealed class BgSpecRowReader
    {
        private readonly BgSpecSource _source;
        private readonly BGEntity _entity;

        internal BgSpecRowReader(BgSpecSource source, BGEntity entity)
        {
            _source = source;
            _entity = entity;
        }

        public string Name
        {
            get
            {
                _source.RequireAlive();
                return _entity.Name;
            }
        }

        public string ReadString(string field) => Read<string>(field);
        public int ReadInt(string field) => Read<int>(field);
        public long ReadLong(string field) => Read<long>(field);
        public float ReadFloat(string field) => Read<float>(field);
        public double ReadDouble(string field) => Read<double>(field);
        public bool ReadBool(string field) => Read<bool>(field);

        public IReadOnlyList<string> ReadStrings(string field)
        {
            var values = Read<List<string>>(field);
            return new List<string>(values ?? new List<string>()).AsReadOnly();
        }

        public string ReadOptionalRelationKey(string field, string targetTable)
        {
            var target = Read<BGEntity>(field);
            return target == null ? null : ReadRequiredRelationKey(field, targetTable);
        }

        public IReadOnlyList<string> ReadRelationKeys(string field, string targetTable)
        {
            var targets = Read<List<BGEntity>>(field);
            var keys = new List<string>();
            if (targets != null)
                foreach (var target in targets)
                {
                    if (target == null || !string.Equals(target.MetaName, targetTable, StringComparison.Ordinal) ||
                        !ReferenceEquals(target.Repo, _entity.Repo) || !ReferenceEquals(target.Meta.GetEntity(target.Id), target) ||
                        string.IsNullOrWhiteSpace(target.Name))
                        throw Invalid(field, "a relation-list target is missing or belongs to an unexpected table");
                    keys.Add(target.Name);
                }
            return keys.AsReadOnly();
        }

        /// <summary>
        /// Resolves a required relationSingle in this private repository and exports
        /// only its target key. Omit targetKeyField to use the target row's Name.
        /// </summary>
        public string ReadRequiredRelationKey(string field, string targetTable, string targetKeyField = null)
        {
            _source.RequireAlive();
            if (string.IsNullOrWhiteSpace(targetTable))
                throw new ArgumentException("A target table is required.", nameof(targetTable));
            var target = Read<BGEntity>(field);
            if (target == null)
                throw Invalid(field, "the required relation target is missing");
            if (!string.Equals(target.MetaName, targetTable, StringComparison.Ordinal))
                throw Invalid(field, $"expected relation target table '{targetTable}', found '{target.MetaName}'");
            if (!ReferenceEquals(target.Repo, _entity.Repo) || !ReferenceEquals(target.Meta.GetEntity(target.Id), target))
                throw Invalid(field, "the relation target does not belong to this repository");

            string key;
            try
            {
                key = targetKeyField == null ? target.Name : target.Get<string>(targetKeyField);
            }
            catch (Exception error)
            {
                throw Invalid(field, $"could not read relation target key '{targetKeyField}'", error);
            }
            if (string.IsNullOrWhiteSpace(key))
                throw Invalid(field, "the required relation target has an empty key");
            return key;
        }

        private T Read<T>(string field)
        {
            _source.RequireAlive();
            if (string.IsNullOrWhiteSpace(field)) throw new ArgumentException("A field name is required.", nameof(field));
            try
            {
                return _entity.Get<T>(field);
            }
            catch (Exception error)
            {
                throw Invalid(field, $"expected a {typeof(T).Name} value", error);
            }
        }

        private SpecValidationException Invalid(string field, string reason, Exception error = null)
            => new SpecValidationException($"BG table '{_entity.MetaName}', row {_entity.Index}, field '{field}': {reason}.", error);
    }
}
