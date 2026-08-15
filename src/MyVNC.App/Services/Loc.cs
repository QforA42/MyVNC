using MyVNC.App.Models;

namespace MyVNC.App.Services;

/// <summary>
/// Lightweight in-memory string table for the Nordic languages + English. No resx/satellite
/// assemblies — just a dictionary, since the UI's text surface is small and this keeps the
/// whole thing inspectable in one file. Falls back to English for a missing key/language.
/// </summary>
public static class Loc
{
    public static AppLanguage Current { get; private set; } = AppLanguage.Swedish;

    /// <summary>Raised after <see cref="SetLanguage"/> so open windows can refresh their text.</summary>
    public static event Action? LanguageChanged;

    public static void SetLanguage(AppLanguage language)
    {
        if (Current == language) return;
        Current = language;
        LanguageChanged?.Invoke();
    }

    public static string T(string key, params object[] args)
    {
        var template = _table.TryGetValue(key, out var byLanguage)
            ? (byLanguage.TryGetValue(Current, out var s) ? s : byLanguage[AppLanguage.English])
            : key;
        return args.Length == 0 ? template : string.Format(template, args);
    }

    private static readonly Dictionary<string, Dictionary<AppLanguage, string>> _table = new();

    private static void Add(string key, string sv, string en, string no, string da, string fi, string @is)
    {
        _table[key] = new Dictionary<AppLanguage, string>
        {
            [AppLanguage.Swedish] = sv,
            [AppLanguage.English] = en,
            [AppLanguage.Norwegian] = no,
            [AppLanguage.Danish] = da,
            [AppLanguage.Finnish] = fi,
            [AppLanguage.Icelandic] = @is,
        };
    }

    static Loc()
    {
        Add("App.Connections",
            "Anslutningar", "Connections", "Tilkoblinger", "Forbindelser", "Yhteydet", "Tengingar");
        Add("App.Search",
            "Sök...", "Search...", "Søk...", "Søg...", "Haku...", "Leita...");
        Add("App.EmptyState",
            "Inga sparade anslutningar än. Tryck på + för att lägga till en.",
            "No saved connections yet. Tap + to add one.",
            "Ingen lagrede tilkoblinger ennå. Trykk på + for å legge til en.",
            "Ingen gemte forbindelser endnu. Tryk på + for at tilføje en.",
            "Ei vielä tallennettuja yhteyksiä. Napauta + lisätäksesi yhden.",
            "Engar vistaðar tengingar ennþá. Ýttu á + til að bæta við einni.");

        Add("Form.NewConnection",
            "Ny anslutning", "New connection", "Ny tilkobling", "Ny forbindelse", "Uusi yhteys", "Ný tenging");
        Add("Form.EditConnection",
            "Redigera anslutning", "Edit connection", "Rediger tilkobling", "Rediger forbindelse", "Muokkaa yhteyttä", "Breyta tengingu");
        Add("Form.Name",
            "NAMN (VALFRITT)", "NAME (OPTIONAL)", "NAVN (VALGFRITT)", "NAVN (VALGFRIT)", "NIMI (VALINNAINEN)", "HEITI (VALKVÆTT)");
        Add("Form.Host",
            "VÄRD", "HOST", "VERT", "VÆRT", "ISÄNTÄ", "HÝSILL");
        Add("Form.Port",
            "PORT", "PORT", "PORT", "PORT", "PORTTI", "GÁTT");
        Add("Form.Username",
            "ANVÄNDARNAMN (OM SERVERN KRÄVER DET)", "USERNAME (IF THE SERVER REQUIRES IT)",
            "BRUKERNAVN (HVIS SERVEREN KREVER DET)", "BRUGERNAVN (HVIS SERVEREN KRÆVER DET)",
            "KÄYTTÄJÄTUNNUS (JOS PALVELIN VAATII SEN)", "NOTANDANAFN (EF ÞJÓNNINN KREFST ÞESS)");
        Add("Form.Fqdn",
            "FQDN (VALFRITT)", "FQDN (OPTIONAL)", "FQDN (VALGFRITT)", "FQDN (VALGFRIT)", "FQDN (VALINNAINEN)", "FQDN (VALKVÆTT)");
        Add("Form.TailscaleIp",
            "TAILSCALE IP (VALFRITT)", "TAILSCALE IP (OPTIONAL)", "TAILSCALE IP (VALGFRITT)",
            "TAILSCALE IP (VALGFRIT)", "TAILSCALE IP (VALINNAINEN)", "TAILSCALE IP (VALKVÆTT)");
        Add("Form.TailscaleFqdn",
            "TAILSCALE FQDN (VALFRITT)", "TAILSCALE FQDN (OPTIONAL)", "TAILSCALE FQDN (VALGFRITT)",
            "TAILSCALE FQDN (VALGFRIT)", "TAILSCALE FQDN (VALINNAINEN)", "TAILSCALE FQDN (VALKVÆTT)");
        Add("Form.DefaultAddress",
            "STANDARDADRESS (VID KLICK PÅ ANSLUT)", "DEFAULT ADDRESS (WHEN CLICKING CONNECT)",
            "STANDARDADRESSE (VED KLIKK PÅ KOBLE TIL)", "STANDARDADRESSE (VED KLIK PÅ FORBIND)",
            "OLETUSOSOITE (YHDISTÄ-PAINIKKEELLE)", "SJÁLFGEFIÐ VISTFANG (VIÐ SMELL Á TENGJAST)");
        Add("Address.HostIp",
            "Värd-IP", "Host IP", "Vert-IP", "Vært-IP", "Isäntä-IP", "Hýsils-IP");
        Add("Address.Fqdn",
            "FQDN", "FQDN", "FQDN", "FQDN", "FQDN", "FQDN");
        Add("Address.TailscaleIp",
            "Tailscale IP", "Tailscale IP", "Tailscale IP", "Tailscale IP", "Tailscale IP", "Tailscale IP");
        Add("Form.TestConnection",
            "Testa anslutning", "Test connection", "Test tilkobling", "Test forbindelse", "Testaa yhteys", "Prófa tengingu");
        Add("Form.TestConnectionOk",
            "Porten svarar ({0} ms)", "Port is reachable ({0} ms)", "Porten svarer ({0} ms)",
            "Porten svarer ({0} ms)", "Portti vastaa ({0} ms)", "Gáttin svarar ({0} ms)");
        Add("Form.TestConnectionFail",
            "Kunde inte nå {0}:{1}", "Couldn't reach {0}:{1}", "Kunne ikke nå {0}:{1}",
            "Kunne ikke nå {0}:{1}", "Ei saatu yhteyttä osoitteeseen {0}:{1}", "Náði ekki sambandi við {0}:{1}");
        Add("Form.ViewOnly",
            "Visa-endast-läge (skicka inget tangentbord/mus)", "View-only mode (no keyboard/mouse sent)",
            "Vis kun-modus (ingen tastatur/mus sendes)", "Vis kun-tilstand (intet tastatur/mus sendes)",
            "Vain katselu -tila (ei näppäimistöä/hiirtä)", "Aðeins-skoða-hamur (ekkert lyklaborð/mús sent)");
        Add("Form.ActualSizeDefault",
            "Starta i verklig storlek (1:1)", "Start at actual size (1:1)", "Start i faktisk størrelse (1:1)",
            "Start i faktisk størrelse (1:1)", "Käynnistä todellisessa koossa (1:1)", "Byrja í raunverulegri stærð (1:1)");
        Add("Form.ReceiveClipboard",
            "Ta emot urklipp från fjärrdatorn", "Receive clipboard from remote", "Motta utklipp fra fjernmaskinen",
            "Modtag udklip fra fjernmaskinen", "Vastaanota leikepöytä etäkoneelta", "Taka við klippispjaldi frá fjartengdri vél");
        Add("Form.SendClipboard",
            "Skicka urklipp till fjärrdatorn", "Send clipboard to remote", "Send utklipp til fjernmaskinen",
            "Send udklip til fjernmaskinen", "Lähetä leikepöytä etäkoneelle", "Senda klippispjald til fjartengdrar vélar");
        Add("Session.ActualSize",
            "Verklig storlek", "Actual size", "Faktisk størrelse", "Faktisk størrelse", "Todellinen koko", "Raunveruleg stærð");
        Add("Session.FitWindow",
            "Anpassa till fönster", "Fit window", "Tilpass vindu", "Tilpas vindue", "Sovita ikkunaan", "Passa í glugga");
        Add("Session.ViewOnlyBadge",
            "Visa-endast", "View only", "Vis kun", "Vis kun", "Vain katselu", "Aðeins skoða");

        Add("Form.ChooseAddress",
            "Välj adress", "Choose address", "Velg adresse", "Vælg adresse", "Valitse osoite", "Veldu vistfang");
        Add("Address.TailscaleFqdn",
            "Tailscale FQDN", "Tailscale FQDN", "Tailscale FQDN", "Tailscale FQDN", "Tailscale FQDN", "Tailscale FQDN");

        Add("Form.Password",
            "LÖSENORD", "PASSWORD", "PASSORD", "ADGANGSKODE", "SALASANA", "LYKILORÐ");
        Add("Form.Remember",
            "Kom ihåg den här anslutningen", "Remember this connection", "Husk denne tilkoblingen",
            "Husk denne forbindelse", "Muista tämä yhteys", "Muna þessa tengingu");
        Add("Form.Connect",
            "Anslut", "Connect", "Koble til", "Forbind", "Yhdistä", "Tengjast");
        Add("Form.SaveChanges",
            "Spara ändringar", "Save changes", "Lagre endringer", "Gem ændringer", "Tallenna muutokset", "Vista breytingar");
        Add("Form.ErrorHost",
            "Ange en värddator (IP eller hostnamn).", "Enter a host (IP address or hostname).",
            "Angi en vert (IP-adresse eller vertsnavn).", "Angiv en vært (IP-adresse eller værtsnavn).",
            "Anna isäntä (IP-osoite tai isäntänimi).", "Sláðu inn hýsil (IP-tölu eða hýsilheiti).");
        Add("Form.ErrorPort",
            "Porten måste vara ett tal mellan 1 och 65535.", "The port must be a number between 1 and 65535.",
            "Porten må være et tall mellom 1 og 65535.", "Porten skal være et tal mellem 1 og 65535.",
            "Portin on oltava luku välillä 1–65535.", "Gáttin verður að vera tala milli 1 og 65535.");
        Add("Form.ErrorConnect",
            "Kunde inte ansluta: {0}", "Couldn't connect: {0}", "Kunne ikke koble til: {0}",
            "Kunne ikke forbinde: {0}", "Yhteyden muodostaminen epäonnistui: {0}", "Ekki tókst að tengjast: {0}");

        Add("Card.Edit",
            "Redigera", "Edit", "Rediger", "Rediger", "Muokkaa", "Breyta");
        Add("Card.Delete",
            "Ta bort", "Delete", "Slett", "Slet", "Poista", "Eyða");
        Add("Card.Pin",
            "Fäst", "Pin", "Fest", "Fastgør", "Kiinnitä", "Festa");
        Add("Card.Unpin",
            "Lossa", "Unpin", "Løsne", "Frigør", "Irrota", "Losa");

        Add("JumpList.Pinned",
            "Fästa", "Pinned", "Festet", "Fastgjort", "Kiinnitetyt", "Fest");
        Add("JumpList.Recent",
            "Senaste", "Recent", "Nylige", "Seneste", "Viimeisimmät", "Nýlegt");

        Add("Settings.Title",
            "Inställningar", "Settings", "Innstillinger", "Indstillinger", "Asetukset", "Stillingar");
        Add("Settings.Language",
            "SPRÅK", "LANGUAGE", "SPRÅK", "SPROG", "KIELI", "TUNGUMÁL");
        Add("Settings.SessionMode",
            "NYA SESSIONER", "NEW SESSIONS", "NYE ØKTER", "NYE FORBINDELSER", "UUDET ISTUNNOT", "NÝJAR SETUR");
        Add("Settings.SessionModeWindow",
            "Eget fönster", "Separate window", "Eget vindu", "Eget vindue", "Oma ikkuna", "Eigin gluggi");
        Add("Settings.SessionModeTab",
            "Flik i samma fönster", "Tab in the same window", "Fane i samme vindu", "Fane i samme vindue",
            "Välilehti samassa ikkunassa", "Flipi í sama glugga");
        Add("Settings.AutoReconnect",
            "ÅTERANSLUTNING", "AUTO-RECONNECT", "GJENTILKOBLING", "GENOPRETTELSE", "AUTOM. UUDELLEENYHDISTÄMINEN", "SJÁLFVIRK ENDURTENGING");
        Add("Settings.AutoReconnectOn",
            "Återansluter automatiskt", "Reconnects automatically", "Kobler til automatisk",
            "Genopretter automatisk", "Yhdistää uudelleen automaattisesti", "Tengist sjálfkrafa aftur");
        Add("Settings.AutoReconnectOff",
            "Av", "Off", "Av", "Fra", "Pois", "Af");

        Add("Session.Connecting",
            "Ansluter till {0}:{1}...", "Connecting to {0}:{1}...", "Kobler til {0}:{1} …",
            "Forbinder til {0}:{1} …", "Muodostetaan yhteyttä {0}:{1}…", "Tengist við {0}:{1}...");
        Add("Session.Disconnected",
            "Anslutningen avbröts: {0}", "Connection lost: {0}", "Tilkoblingen ble brutt: {0}",
            "Forbindelsen blev afbrudt: {0}", "Yhteys katkesi: {0}", "Tengingin rofnaði: {0}");
        Add("Session.Close",
            "Stäng", "Close", "Lukk", "Luk", "Sulje", "Loka");
        Add("Session.Fullscreen",
            "Helskärm (F11)", "Fullscreen (F11)", "Fullskjerm (F11)", "Fuld skærm (F11)", "Koko näyttö (F11)", "Fylla skjá (F11)");
        Add("Session.ExitFullscreen",
            "Avsluta helskärm (F11)", "Exit fullscreen (F11)", "Avslutt fullskjerm (F11)",
            "Afslut fuld skærm (F11)", "Poistu koko näytöstä (F11)", "Loka fylliskjá (F11)");
        Add("Session.Disconnect",
            "Koppla från", "Disconnect", "Koble fra", "Afbryd", "Katkaise yhteys", "Aftengja");
        Add("Session.ConnectedFallbackName",
            "Ansluten", "Connected", "Tilkoblet", "Forbundet", "Yhdistetty", "Tengt");
        Add("Session.ToolbarHint",
            "Håll Höger Ctrl + peka mot toppen för verktygsfältet",
            "Hold Right Ctrl + point at the top for the toolbar",
            "Hold Høyre Ctrl + pek mot toppen for verktøylinjen",
            "Hold Højre Ctrl + peg mod toppen for værktøjslinjen",
            "Pidä Oikea Ctrl painettuna ja osoita yläreunaan saadaksesi työkalurivin",
            "Haltu Hægri Ctrl + bentu efst til að fá tækjastikuna");
        Add("Session.Reconnecting",
            "Anslutningen bröts: {0}\nFörsöker igen om {1}s (försök {2})…",
            "Connection lost: {0}\nRetrying in {1}s (attempt {2})…",
            "Tilkoblingen ble brutt: {0}\nPrøver igjen om {1}s (forsøk {2}) …",
            "Forbindelsen blev afbrudt: {0}\nPrøver igen om {1}s (forsøg {2}) …",
            "Yhteys katkesi: {0}\nYritetään uudelleen {1}s kuluttua (yritys {2})…",
            "Tengingin rofnaði: {0}\nReynt aftur eftir {1}s (tilraun {2})…");
    }
}
