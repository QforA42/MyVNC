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
        Add("Card.OpenSsh",
            "Öppna SSH-terminal", "Open SSH terminal", "Åpne SSH-terminal",
            "Åbn SSH-terminal", "Avaa SSH-pääte", "Opna SSH-flugstöð");

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
        Add("Settings.SshTerminal",
            "SSH-TERMINAL", "SSH TERMINAL", "SSH-TERMINAL", "SSH-TERMINAL", "SSH-PÄÄTE", "SSH-FLUGSTÖÐ");
        Add("SshTerminal.Auto",
            "Auto (Windows Terminal om det finns, annars PowerShell)",
            "Auto (Windows Terminal if available, otherwise PowerShell)",
            "Auto (Windows Terminal hvis tilgjengelig, ellers PowerShell)",
            "Auto (Windows Terminal hvis tilgængelig, ellers PowerShell)",
            "Auto (Windows Terminal jos saatavilla, muuten PowerShell)",
            "Sjálfvirkt (Windows Terminal ef til, annars PowerShell)");
        Add("SshTerminal.PowerShell",
            "PowerShell", "PowerShell", "PowerShell", "PowerShell", "PowerShell", "PowerShell");
        Add("SshTerminal.WindowsTerminal",
            "Windows Terminal", "Windows Terminal", "Windows Terminal", "Windows Terminal", "Windows Terminal", "Windows Terminal");
        Add("SshTerminal.Wsl",
            "WSL", "WSL", "WSL", "WSL", "WSL", "WSL");

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

        Add("Help.Title",
            "Hjälp", "Help", "Hjelp", "Hjælp", "Ohje", "Hjálp");
        Add("Help.PasteHeader",
            "Klistra in text", "Pasting text", "Lime inn tekst", "Indsæt tekst",
            "Tekstin liittäminen", "Líma inn texta");
        Add("Help.PasteBody",
            "Använd Ctrl+Shift+V för att klistra in i terminalen/Vim på fjärrdatorn — vanlig Ctrl+V tolkas ofta som ett annat kommando där (t.ex. Visual Block-läge i Vim) och ger skräptecken istället för din text.",
            "Use Ctrl+Shift+V to paste into the remote's terminal/Vim — plain Ctrl+V is often interpreted as something else there (e.g. Visual Block mode in Vim), producing garbage characters instead of your text.",
            "Bruk Ctrl+Shift+V for å lime inn i terminalen/Vim på fjernmaskinen — vanlig Ctrl+V tolkes ofte som noe annet der (f.eks. Visual Block-modus i Vim), og gir søppeltegn i stedet for teksten din.",
            "Brug Ctrl+Shift+V til at indsætte i terminalen/Vim på fjernmaskinen — almindelig Ctrl+V bliver ofte fortolket som noget andet der (f.eks. Visual Block-tilstand i Vim) og giver skrammeltegn i stedet for din tekst.",
            "Käytä Ctrl+Shift+V-yhdistelmää liittääksesi tekstiä etäkoneen päätteeseen/Vimiin — pelkkä Ctrl+V tulkitaan siellä usein joksikin muuksi (esim. Vimin Visual Block -tilaksi), jolloin tekstisi sijaan näkyy roskamerkkejä.",
            "Notaðu Ctrl+Shift+V til að líma inn í flugstöðina/Vim á fjartengdu vélinni — venjulegt Ctrl+V er oft túlkað sem eitthvað annað þar (t.d. Visual Block-hamur í Vim) og skilar rusltáknum í stað textans þíns.");

        Add("Help.HowItWorksHeader",
            "Hur MyVNC fungerar", "How MyVNC works", "Hvordan MyVNC fungerer",
            "Sådan fungerer MyVNC", "Miten MyVNC toimii", "Hvernig MyVNC virkar");
        Add("Help.HowItWorksBody",
            "MyVNC är en egenbyggd VNC-klient (RFB-protokollet) som kopplar upp direkt mot en VNC-server (t.ex. wayvnc) på fjärrdatorn — inget extra program behövs på Windows-sidan. Tangentbord och mus skickas i realtid, och Windows urklipp synkas automatiskt åt båda hållen (kan stängas av per anslutning under visa-endast-/urklippsinställningarna).",
            "MyVNC is a custom-built VNC client (the RFB protocol) that connects directly to a VNC server (e.g. wayvnc) on the remote machine — nothing extra is needed on the Windows side. Keyboard and mouse are sent in real time, and the Windows clipboard syncs automatically in both directions (can be turned off per connection under the view-only/clipboard settings).",
            "MyVNC er en egenbygget VNC-klient (RFB-protokollen) som kobler seg direkte til en VNC-server (f.eks. wayvnc) på fjernmaskinen — ingen ekstra programvare trengs på Windows-siden. Tastatur og mus sendes i sanntid, og Windows-utklippstavlen synkroniseres automatisk begge veier (kan skrus av per tilkobling under innstillingene for kun visning/utklippstavle).",
            "MyVNC er en hjemmelavet VNC-klient (RFB-protokollen), der forbinder direkte til en VNC-server (f.eks. wayvnc) på fjernmaskinen — der kræves intet ekstra på Windows-siden. Tastatur og mus sendes i realtid, og Windows-udklipsholderen synkroniseres automatisk begge veje (kan slås fra pr. forbindelse under indstillingerne for kun visning/udklipsholder).",
            "MyVNC on itse rakennettu VNC-asiakasohjelma (RFB-protokolla), joka yhdistää suoraan etäkoneen VNC-palvelimeen (esim. wayvnc) — Windows-puolella ei tarvita mitään ylimääräistä. Näppäimistö ja hiiri lähetetään reaaliajassa, ja Windowsin leikepöytä synkronoituu automaattisesti molempiin suuntiin (voidaan sammuttaa yhteyskohtaisesti vain katselu-/leikepöytäasetuksista).",
            "MyVNC er sérsmíðaður VNC-biðlari (RFB-samskiptareglan) sem tengist beint við VNC-þjón (t.d. wayvnc) á fjartengdu vélinni — ekkert aukalegt þarf á Windows-hliðinni. Lyklaborð og mús eru send í rauntíma og Windows-klippiborðið samstillist sjálfkrafa í báðar áttir (hægt að slökkva á því fyrir hverja tengingu undir stillingum fyrir aðeins-skoða/klippiborð).");

        Add("Help.WayvncHeader",
            "Konfigurera wayvnc på fjärrdatorn", "Configuring wayvnc on the remote machine",
            "Konfigurere wayvnc på fjernmaskinen", "Konfigurer wayvnc på fjernmaskinen",
            "wayvncin määrittäminen etäkoneella", "Stilla wayvnc á fjartengdu vélinni");
        Add("Help.WayvncKeyboardBody",
            "Tangentbordslayout: wayvnc följer normalt systemets egna layout automatiskt (satt vid installationen, t.ex. via localectl) — du behöver oftast inte ange något själv. Fungerar inte å, ä, ö eller liknande tangenter, kolla systemets layout (localectl status) eller sätt xkb_layout uttryckligen i wayvnc-konfigurationen.",
            "Keyboard layout: wayvnc normally follows the system's own layout automatically (set during installation, e.g. via localectl) — you usually don't need to set anything yourself. If å, ä, ö or similar keys don't work, check the system layout (localectl status) or set xkb_layout explicitly in wayvnc's config.",
            "Tastaturoppsett: wayvnc følger normalt systemets eget oppsett automatisk (satt ved installasjonen, f.eks. via localectl) — du trenger vanligvis ikke å angi noe selv. Fungerer ikke æ, ø, å eller lignende taster, sjekk systemets oppsett (localectl status) eller sett xkb_layout uttrykkelig i wayvnc-konfigurasjonen.",
            "Tastaturlayout: wayvnc følger normalt systemets eget layout automatisk (angivet ved installationen, f.eks. via localectl) — du behøver som regel ikke angive noget selv. Virker æ, ø, å eller lignende taster ikke, så tjek systemets layout (localectl status) eller angiv xkb_layout eksplicit i wayvnc-konfigurationen.",
            "Näppäimistöasettelu: wayvnc noudattaa yleensä järjestelmän omaa asettelua automaattisesti (asetettu asennuksen yhteydessä, esim. localectl-komennolla) — sinun ei yleensä tarvitse asettaa mitään itse. Jos ä, ö tai vastaavat näppäimet eivät toimi, tarkista järjestelmän asettelu (localectl status) tai aseta xkb_layout suoraan wayvnc-asetuksiin.",
            "Lyklaborðsuppsetning: wayvnc fylgir venjulega sjálfkrafa uppsetningu kerfisins (stillt við uppsetningu, t.d. með localectl) — þú þarft yfirleitt ekki að stilla neitt sjálf(ur). Ef þ, æ, ö eða áþekkir takkar virka ekki, athugaðu uppsetningu kerfisins (localectl status) eða stilltu xkb_layout beint í wayvnc-stillingunum.");
        Add("Help.WayvncAddressBody",
            "Adress/port: se till att wayvnc lyssnar på rätt adress (address=0.0.0.0 för att nås via nätverket/Tailscale) och port (port=5900 som standard).",
            "Address/port: make sure wayvnc listens on the right address (address=0.0.0.0 to be reachable over the network/Tailscale) and port (port=5900 by default).",
            "Adresse/port: sørg for at wayvnc lytter på riktig adresse (address=0.0.0.0 for å nås via nettverket/Tailscale) og port (port=5900 som standard).",
            "Adresse/port: sørg for at wayvnc lytter på den rigtige adresse (address=0.0.0.0 for at kunne nås via netværket/Tailscale) og port (port=5900 som standard).",
            "Osoite/portti: varmista, että wayvnc kuuntelee oikeaa osoitetta (address=0.0.0.0, jotta se on tavoitettavissa verkon/Tailscalen kautta) ja porttia (port=5900 oletuksena).",
            "Vistfang/gátt: gakktu úr skugga um að wayvnc hlusti á rétt vistfang (address=0.0.0.0 til að ná til þess um netið/Tailscale) og gátt (port=5900 sjálfgefið).");
        Add("Help.WayvncAuthBody",
            "Autentisering: enklast är enable_auth=true tillsammans med enable_pam=true — då loggar du in med samma användarnamn/lösenord som ditt Linux-konto (via PAM), precis vad MyVNC skickar. TLS/kryptering sköts av certificate_file/private_key_file (självsignerat certifikat genereras automatiskt av wayvnc första gången).",
            "Authentication: the simplest setup is enable_auth=true together with enable_pam=true — then you log in with the same username/password as your Linux account (via PAM), exactly what MyVNC sends. TLS/encryption is handled by certificate_file/private_key_file (wayvnc generates a self-signed certificate automatically the first time).",
            "Autentisering: den enkleste oppsettet er enable_auth=true sammen med enable_pam=true — da logger du inn med samme brukernavn/passord som Linux-kontoen din (via PAM), nøyaktig det MyVNC sender. TLS/kryptering håndteres av certificate_file/private_key_file (wayvnc genererer et selvsignert sertifikat automatisk første gang).",
            "Godkendelse: den enkleste opsætning er enable_auth=true sammen med enable_pam=true — så logger du ind med samme brugernavn/adgangskode som din Linux-konto (via PAM), præcis hvad MyVNC sender. TLS/kryptering håndteres af certificate_file/private_key_file (wayvnc genererer automatisk et selvsigneret certifikat første gang).",
            "Todennus: yksinkertaisin tapa on enable_auth=true yhdessä enable_pam=true kanssa — silloin kirjaudut sisään samalla käyttäjätunnuksella/salasanalla kuin Linux-tilisi (PAM:n kautta), täsmälleen sen MyVNC lähettää. TLS/salauksesta huolehtivat certificate_file/private_key_file (wayvnc luo itse allekirjoitetun varmenteen automaattisesti ensimmäisellä kerralla).",
            "Auðkenning: einfaldasta uppsetningin er enable_auth=true ásamt enable_pam=true — þá skráir þú þig inn með sama notandanafni/lykilorði og Linux-reikningurinn þinn (í gegnum PAM), nákvæmlega það sem MyVNC sendir. TLS/dulkóðun er séð um af certificate_file/private_key_file (wayvnc býr sjálfkrafa til sjálfundirritað skírteini í fyrsta skipti).");
        Add("Help.WayvncExampleLabel",
            "Exempel: ~/.config/wayvnc/config", "Example: ~/.config/wayvnc/config",
            "Eksempel: ~/.config/wayvnc/config", "Eksempel: ~/.config/wayvnc/config",
            "Esimerkki: ~/.config/wayvnc/config", "Dæmi: ~/.config/wayvnc/config");

        Add("Help.LimitationsHeader",
            "Kända begränsningar", "Known limitations", "Kjente begrensninger",
            "Kendte begrænsninger", "Tunnetut rajoitukset", "Þekktar takmarkanir");
        Add("Help.LimitationsBody",
            "Klick på flikar/popup-menyer i statusfält som Waybar eller Quickshell-paneler (t.ex. WiFi- eller batteriikonen) öppnar ibland inget — det beror på en känd begränsning i wlr-virtual-pointer-v1, protokollet wayvnc använder för att skicka muspekaren till kompositorn. Vanlig musrörelse, klick i fönster och tangentbord fungerar som vanligt; det är specifikt att öppna nya popup-ytor (layer-shell) via en syntetisk pekare som inte alltid stöds fullt ut av kompositorn. Detta är inget MyVNC kan åtgärda från klientsidan — lösningen (om någon finns) ligger i en nyare version av wlroots/Hyprland/wayvnc, eller i panelens egna inställningar på fjärrdatorn.",
            "Clicking flyouts/popups in status bars like Waybar or Quickshell panels (e.g. the WiFi or battery icon) sometimes opens nothing — this is a known limitation of wlr-virtual-pointer-v1, the protocol wayvnc uses to inject the pointer into the compositor. Regular mouse movement, clicking inside windows, and keyboard input all work normally; it's specifically opening new popup surfaces (layer-shell) via a synthetic pointer that isn't always fully supported by the compositor. There's nothing MyVNC can fix client-side for this — any fix would come from a newer wlroots/Hyprland/wayvnc version, or the panel's own settings on the remote machine.",
            "Klikk på visningsvinduer/popup-menyer i statuslinjer som Waybar eller Quickshell-paneler (f.eks. WiFi- eller batteriikonet) åpner noen ganger ingenting — dette skyldes en kjent begrensning i wlr-virtual-pointer-v1, protokollen wayvnc bruker for å sende pekeren til kompositøren. Vanlig musbevegelse, klikk i vinduer og tastatur fungerer som normalt; det er spesifikt å åpne nye popup-flater (layer-shell) via en syntetisk peker som ikke alltid støttes fullt ut av kompositøren. Dette er ikke noe MyVNC kan fikse på klientsiden — en eventuell løsning ligger i en nyere versjon av wlroots/Hyprland/wayvnc, eller i panelets egne innstillinger på fjernmaskinen.",
            "Klik på flyouts/popup-menuer i statusbjælker som Waybar eller Quickshell-paneler (f.eks. WiFi- eller batteriikonet) åbner nogle gange ingenting — det skyldes en kendt begrænsning i wlr-virtual-pointer-v1, protokollen wayvnc bruger til at sende markøren til kompositoren. Almindelig musebevægelse, klik i vinduer og tastatur fungerer som normalt; det er specifikt at åbne nye popup-flader (layer-shell) via en syntetisk markør, som ikke altid understøttes fuldt ud af kompositoren. Der er intet MyVNC kan rette på klientsiden her — en eventuel løsning ligger i en nyere version af wlroots/Hyprland/wayvnc, eller i panelets egne indstillinger på fjernmaskinen.",
            "Tilan avautuvien valikkojen/ponnahdusikkunoiden napsauttaminen tilarivillä (esim. Waybar tai Quickshell-paneelit, kuten WiFi- tai akkukuvake) ei joskus tee mitään — tämä johtuu tunnetusta rajoituksesta wlr-virtual-pointer-v1-protokollassa, jota wayvnc käyttää osoittimen välittämiseen kompositorille. Tavallinen hiiren liike, ikkunoiden napsautus ja näppäimistö toimivat normaalisti; nimenomaan uusien ponnahduspintojen (layer-shell) avaaminen synteettisellä osoittimella ei aina toimi täysin kompositorissa. MyVNC ei voi korjata tätä asiakaspuolella — mahdollinen korjaus tulisi uudemmasta wlroots/Hyprland/wayvnc-versiosta tai paneelin omista asetuksista etäkoneella.",
            "Að smella á sprettiglugga/valmyndir í stöðuslám eins og Waybar eða Quickshell-spjöldum (t.d. WiFi- eða rafhlöðutáknið) opnar stundum ekkert — þetta er þekkt takmörkun í wlr-virtual-pointer-v1, samskiptareglunni sem wayvnc notar til að senda bendilinn til gluggastjórans. Venjuleg músarhreyfing, smellir í gluggum og lyklaborð virka eðlilega; það er sérstaklega að opna nýja sprettifleti (layer-shell) með gervibendli sem gluggastjórinn styður ekki alltaf að fullu. Þetta er ekkert sem MyVNC getur lagað í biðlaranum — hugsanleg lausn myndi koma frá nýrri útgáfu af wlroots/Hyprland/wayvnc, eða stillingum spjaldsins sjálfs á fjartengdu vélinni.");

        Add("Settings.DebugLogging",
            "FELSÖKNINGSLOGG", "DEBUG LOGGING", "FEILSØKINGSLOGG", "FEJLFINDINGSLOG", "VIANMÄÄRITYSLOKI", "VILLULEIT-ANNÁLL");
        Add("Settings.DebugLoggingOn",
            "Loggar till myvnc.log", "Logging to myvnc.log", "Logger til myvnc.log",
            "Logger til myvnc.log", "Kirjaa lokiin myvnc.log", "Skráir í myvnc.log");
        Add("Settings.DebugLoggingOff",
            "Av", "Off", "Av", "Fra", "Pois", "Af");
        Add("Settings.OpenLog",
            "Öppna logg", "Open log", "Åpne logg", "Åbn log", "Avaa loki", "Opna annál");

        Add("Watchdog.Title",
            "MyVNC använder ovanligt mycket resurser", "MyVNC is using unusually high resources",
            "MyVNC bruker uvanlig mye ressurser", "MyVNC bruger usædvanligt mange ressourcer",
            "MyVNC käyttää poikkeuksellisen paljon resursseja", "MyVNC notar óvenju mikið af tilföngum");
        Add("Watchdog.Message",
            "MyVNC har använt ovanligt mycket minne ({0} MB) eller processor (~{1}% av en kärna) en längre stund — det kan tyda på ett fel i appen. Detaljer har loggats till myvnc.log. Överväg att starta om appen; hör gärna av dig med loggen om detta händer igen.",
            "MyVNC has been using unusually high memory ({0} MB) or CPU (~{1}% of one core) for a while — this may indicate a bug in the app. Details were logged to myvnc.log. Consider restarting the app; please share the log if this happens again.",
            "MyVNC har brukt uvanlig mye minne ({0} MB) eller prosessor (~{1}% av én kjerne) en stund — dette kan tyde på en feil i appen. Detaljer er logget til myvnc.log. Vurder å starte appen på nytt; del gjerne loggen hvis dette skjer igjen.",
            "MyVNC har brugt usædvanligt meget hukommelse ({0} MB) eller CPU (~{1}% af én kerne) et stykke tid — det kan tyde på en fejl i appen. Detaljer er logget til myvnc.log. Overvej at genstarte appen; del gerne loggen hvis dette sker igen.",
            "MyVNC on käyttänyt poikkeuksellisen paljon muistia ({0} Mt) tai suoritinta (~{1}% yhdestä ytimestä) jonkin aikaa — tämä voi viitata sovelluksen virheeseen. Tiedot kirjattiin tiedostoon myvnc.log. Harkitse sovelluksen uudelleenkäynnistystä; jaa loki, jos tämä toistuu.",
            "MyVNC hefur notað óvenju mikið minni ({0} MB) eða örgjörva (~{1}% af einum kjarna) um hríð — það gæti bent til villu í forritinu. Nánari upplýsingar voru skráðar í myvnc.log. Íhugaðu að endurræsa forritið; deildu gjarnan annálnum ef þetta gerist aftur.");
    }
}
