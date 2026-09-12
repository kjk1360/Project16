using Kylin.DI;
using Kylin.DI.Layered;
using Kylin.SubscribableProperty;

namespace Project16.Foundation.Preferences
{
    public interface IUserPreferencesViewModel : IViewModelLayer
    {
        IReadOnlySubscribableProperty<float> MusicVolume { get; }
        IReadOnlySubscribableProperty<float> EffectsVolume { get; }
        IReadOnlySubscribableProperty<string> Language { get; }
        void SetMusicVolume(float value);
        void SetEffectsVolume(float value);
        void SetLanguage(string value);
    }

    /// <summary>Bind in the screen/feature scope that owns the settings presentation.</summary>
    public sealed class UserPreferencesViewModel : IUserPreferencesViewModel
    {
        [Inject] private IUserPreferencesData _data = null;
        [Inject] private IUserPreferencesApplication _application = null;
        public IReadOnlySubscribableProperty<float> MusicVolume => _data.MusicVolume;
        public IReadOnlySubscribableProperty<float> EffectsVolume => _data.EffectsVolume;
        public IReadOnlySubscribableProperty<string> Language => _data.Language;
        public void SetMusicVolume(float value) => _application.SetMusicVolume(value);
        public void SetEffectsVolume(float value) => _application.SetEffectsVolume(value);
        public void SetLanguage(string value) => _application.SetLanguage(value);
    }
}
