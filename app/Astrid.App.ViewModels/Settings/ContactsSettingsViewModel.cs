using System.Collections.ObjectModel;
using System.Text.Json;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>The Contacts page (task 438494c7): who has been imported, and clearing them.</summary>
public sealed class ContactsSettingsViewModel : ObservableObject
{
    private readonly SettingsSession _session;
    private int _contactsTotal;
    private bool _contactsLoaded;

    public ContactsSettingsViewModel(SettingsSession session)
    {
        _session = session;
    }

    /// <summary>The contacts this account imported.</summary>
    public ObservableCollection<ContactSummary> Contacts { get; } = [];

    /// <summary>How many the server holds, which can exceed what is listed.</summary>
    public int ContactsTotal
    {
        get => _contactsTotal;
        private set
        {
            if (Set(ref _contactsTotal, value))
            {
                Raise(nameof(HasContacts));
                Raise(nameof(ContactsAreEmpty));
            }
        }
    }

    /// <summary>Whether the page has asked the server yet; the empty state waits for it.</summary>
    public bool ContactsLoaded
    {
        get => _contactsLoaded;
        private set
        {
            if (Set(ref _contactsLoaded, value))
            {
                Raise(nameof(ContactsAreEmpty));
            }
        }
    }

    public bool HasContacts => ContactsTotal > 0;

    public bool ContactsAreEmpty => ContactsLoaded && ContactsTotal == 0;

    /// <summary>Load the Contacts page. Online-only, like the web's.</summary>
    public async Task<bool> LoadContactsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _session.Core.CallAsync(Commands.Contacts(), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.IsStillPending
                ? "Contacts need a connection."
                : response.Error?.Message;
            return false;
        }
        _session.ErrorMessage = null;
        Contacts.Clear();
        if (response.Value.TryGetProperty("contacts", out var contacts))
        {
            foreach (var contact in contacts.EnumerateArray())
            {
                if (contact.Deserialize<ContactSummary>(CommandJson.Options) is { } row)
                {
                    Contacts.Add(row);
                }
            }
        }
        ContactsTotal = response.Value.TryGetProperty("total", out var total) ? total.GetInt32() : Contacts.Count;
        ContactsLoaded = true;
        return true;
    }

    /// <summary>Remove every imported contact, then show the empty page.</summary>
    public async Task<bool> ClearContactsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _session.Core.CallAsync(Commands.ClearContacts(), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.IsStillPending
                ? "Clearing contacts needs a connection."
                : response.Error?.Message;
            return false;
        }
        _session.ErrorMessage = null;
        Contacts.Clear();
        ContactsTotal = 0;
        ContactsLoaded = true;
        return true;
    }
}
