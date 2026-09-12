using Kylin.DI;
using Kylin.DI.Layered;

namespace Project16.Foundation.Preferences
{
    public interface IUserPreferencesDomain : IDomainServiceLayer<UserPreferencesData>
    {
        void SetMusicVolume(float value);
        void SetEffectsVolume(float value);
        void SetLanguage(string value);
    }

    public sealed class UserPreferencesDomain : IUserPreferencesDomain
    {
        [Inject] private UserPreferencesData _data = null;
        public void SetMusicVolume(float value) => _data.SetMusicVolume(value);
        public void SetEffectsVolume(float value) => _data.SetEffectsVolume(value);
        public void SetLanguage(string value) => _data.SetLanguage(value);
    }

    public interface IUserPreferencesApplication : IApplicationServiceLayer
    {
        void SetMusicVolume(float value);
        void SetEffectsVolume(float value);
        void SetLanguage(string value);
    }

    public sealed class UserPreferencesApplication : IUserPreferencesApplication
    {
        [Inject] private IUserPreferencesDomain _domain = null;
        public void SetMusicVolume(float value) => _domain.SetMusicVolume(value);
        public void SetEffectsVolume(float value) => _domain.SetEffectsVolume(value);
        public void SetLanguage(string value) => _domain.SetLanguage(value);
    }
}
