using System;
using Kylin.DI.Layered;
using Kylin.SubscribableProperty;
using Project16.Foundation.Persistence;

namespace Project16.Foundation.Preferences
{
    public interface IUserPreferencesData : IDataLayer
    {
        IReadOnlySubscribableProperty<float> MusicVolume { get; }
        IReadOnlySubscribableProperty<float> EffectsVolume { get; }
        IReadOnlySubscribableProperty<string> Language { get; }
    }

    public sealed class UserPreferencesData : UserDataUnit<UserPreferencesSnapshot>, IUserPreferencesData, IDisposable
    {
        public const string SaveKey = "preferences";
        private readonly SubscribableProperty<float> _musicVolume;
        private readonly SubscribableProperty<float> _effectsVolume;
        private readonly SubscribableProperty<string> _language;
        private bool _disposed;

        public UserPreferencesData(SaveLoadResult<UserPreferencesSnapshot> loaded) : base(SaveKey, 1, loaded)
        {
            var snapshot = loaded.Snapshot;
            UserPreferencesSnapshot.ValidateVolume(snapshot.MusicVolume);
            UserPreferencesSnapshot.ValidateVolume(snapshot.EffectsVolume);
            _musicVolume = new SubscribableProperty<float>(snapshot.MusicVolume);
            _effectsVolume = new SubscribableProperty<float>(snapshot.EffectsVolume);
            _language = new SubscribableProperty<string>(snapshot.Language);
            MusicVolume = new ReadOnlySubscribableProperty<float>(_musicVolume);
            EffectsVolume = new ReadOnlySubscribableProperty<float>(_effectsVolume);
            Language = new ReadOnlySubscribableProperty<string>(_language);
        }

        public IReadOnlySubscribableProperty<float> MusicVolume { get; }
        public IReadOnlySubscribableProperty<float> EffectsVolume { get; }
        public IReadOnlySubscribableProperty<string> Language { get; }

        [OwnerOnly]
        public void SetMusicVolume(float value)
        {
            ThrowIfDisposed();
            UserPreferencesSnapshot.ValidateVolume(value);
            if (_musicVolume.Value == value) return;
            MarkDirty();
            _musicVolume.Value = value;
        }

        [OwnerOnly]
        public void SetEffectsVolume(float value)
        {
            ThrowIfDisposed();
            UserPreferencesSnapshot.ValidateVolume(value);
            if (_effectsVolume.Value == value) return;
            MarkDirty();
            _effectsVolume.Value = value;
        }

        [OwnerOnly]
        public void SetLanguage(string value)
        {
            ThrowIfDisposed();
            if (value == null || value.Length > 64) throw new ArgumentException("Invalid language identifier.", nameof(value));
            if (_language.Value == value) return;
            MarkDirty();
            _language.Value = value;
        }

        public override UserPreferencesSnapshot CaptureSnapshot()
        {
            ThrowIfDisposed();
            return new UserPreferencesSnapshot(_musicVolume.Value, _effectsVolume.Value, _language.Value);
        }

        public void Dispose() => _disposed = true;
        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UserPreferencesData));
        }
    }
}
