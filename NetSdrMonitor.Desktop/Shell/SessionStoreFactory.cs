using NetSdrMonitor.Core.Abstractions.Persistence;
using NetSdrMonitor.Desktop.Settings;

namespace NetSdrMonitor.Desktop.Shell;

/// <summary>
/// Реалізація порту сховища сесії: за поточними настройками створює летке (обмежене лімітом) або файлове
/// SQLite-сховище й повідомляє, чи воно персистентне. Уся «звідки беруться настройки» лишається тут.
/// </summary>
public sealed class SessionStoreFactory(JsonSettingsStore store, ISignalRecordRepositoryFactory repositoryFactory)
    : ISessionStoreFactory
{
    public async Task<SessionStore> CreateAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = store.Load();
        ISignalRecordRepository repository =
            await repositoryFactory.CreateAsync(settings.UseInMemoryStorage, settings.MaxUiRecords, cancellationToken);

        return new SessionStore
        {
            Repository   = repository,
            IsPersistent = !settings.UseInMemoryStorage,
        };
    }
}
