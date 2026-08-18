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

        Add("Common.Back",
            "Tillbaka", "Back", "Tilbake", "Tilbage", "Takaisin", "Til baka");
        Add("Common.Save",
            "Spara", "Save", "Lagre", "Gem", "Tallenna", "Vista");

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
        Add("Form.ForgetSshHostKey",
            "Glöm SSH-värdnyckel", "Forget SSH host key", "Glem SSH-vertsnøkkel",
            "Glem SSH-værtsnøgle", "Unohda SSH-isäntäavain", "Gleyma SSH-hýsilslykli");
        Add("Form.ForgetSshHostKeyTooltip",
            "Tar bort cachade SSH-värdnycklar för de adresser som fyllts i ovan, så nästa SSH-anslutning inte blockeras av \"host key changed\" (t.ex. efter att en maskin ominstallerats). Bypassar aldrig verifieringen av nästa nyckel som visas.",
            "Removes cached SSH host keys for the addresses filled in above, so the next SSH connection isn't blocked by \"host key changed\" (e.g. after a machine was reinstalled). Never bypasses verification of whatever key shows up next.",
            "Fjerner bufrede SSH-vertsnøkler for adressene fylt inn ovenfor, slik at neste SSH-tilkobling ikke blokkeres av \"host key changed\" (f.eks. etter at en maskin ble installert på nytt). Omgår aldri verifiseringen av neste nøkkel som dukker opp.",
            "Fjerner cachede SSH-værtsnøgler for adresserne udfyldt ovenfor, så den næste SSH-forbindelse ikke blokeres af \"host key changed\" (f.eks. efter en maskine er blevet geninstalleret). Omgår aldrig verifikationen af den næste nøgle, der dukker op.",
            "Poistaa välimuistiin tallennetut SSH-isäntäavaimet yllä täytetyille osoitteille, jotta seuraava SSH-yhteys ei esty \"host key changed\" -virheeseen (esim. koneen uudelleenasennuksen jälkeen). Ei koskaan ohita seuraavan näytettävän avaimen todennusta.",
            "Fjarlægir vistaða SSH-hýsilslykla fyrir vistföngin sem fyllt eru út hér að ofan, svo næsta SSH-tenging stöðvist ekki vegna \"host key changed\" (t.d. eftir að vél var endurupsett). Fer aldrei framhjá staðfestingu á næsta lykli sem birtist.");
        Add("Form.ForgetSshHostKeyOk",
            "Rensade {0} adress(er) från known_hosts", "Cleared {0} address(es) from known_hosts",
            "Fjernet {0} adresse(r) fra known_hosts", "Ryddede {0} adresse(r) fra known_hosts",
            "Poistettiin {0} osoite(tta) known_hosts-tiedostosta", "Hreinsaði {0} vistfang/vistföng úr known_hosts");
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
        Add("Session.Wake",
            "Väck", "Wake", "Vekk", "Væk", "Herätä", "Vekja");
        Add("Session.WakeTooltip",
            "Skickar ett ofarligt Shift-tryck för att väcka en DPMS-avstängd skärm. Fungerar när ett vanligt klick inte gör det, eftersom wlroots-baserade kompositorer (t.ex. Hyprland) slutar leverera muspekarhändelser helt när skärmen är avstängd via DPMS — bara tangentbordsinmatning kommer igenom.",
            "Sends a harmless Shift tap to wake a DPMS-blanked screen. Works when a plain click doesn't, because wlroots-based compositors (e.g. Hyprland) stop delivering pointer events at all while the screen is DPMS-off — only keyboard input gets through.",
            "Sender et harmløst Shift-trykk for å vekke en DPMS-avslått skjerm. Fungerer når et vanlig klikk ikke gjør det, fordi wlroots-baserte kompositører (f.eks. Hyprland) slutter å levere muspekerhendelser helt mens skjermen er DPMS-avslått — bare tastaturinndata slipper gjennom.",
            "Sender et harmløst Shift-tryk for at vække en DPMS-slukket skærm. Virker, når et almindeligt klik ikke gør det, fordi wlroots-baserede kompositorer (f.eks. Hyprland) helt holder op med at levere musehændelser, mens skærmen er DPMS-slukket — kun tastaturinput slipper igennem.",
            "Lähettää harmittoman Shift-painalluksen herättääkseen DPMS-sammutetun näytön. Toimii, kun tavallinen napsautus ei toimi, koska wlroots-pohjaiset kompositorit (esim. Hyprland) lakkaavat toimittamasta hiiritapahtumia kokonaan näytön ollessa DPMS-sammutettuna — vain näppäimistösyöte pääsee läpi.",
            "Sendir meinlaust Shift-slag til að vekja DPMS-slökktan skjá. Virkar þegar venjulegur smellur gerir það ekki, því wlroots-byggðir gluggastjórar (t.d. Hyprland) hætta alveg að skila músarbendilsviðburðum á meðan skjárinn er DPMS-slökktur — aðeins lyklaborðsinntak kemst í gegn.");
        Add("Session.SendFile",
            "Skicka fil…", "Send file…", "Send fil…", "Send fil…", "Lähetä tiedosto…", "Senda skrá…");
        Add("Session.SendFileTooltip",
            "Skickar en eller flera filer till fjärrdatorn via SFTP (SSH, port 22) — hamnar i ~/myvnc-shared. Kräver att samma användarnamn/lösenord som anslutningen fungerar för SSH-inloggning (stämmer normalt om wayvnc är konfigurerad med enable_pam=true). Dra och släpp filer på sessionen fungerar också.",
            "Sends one or more files to the remote host via SFTP (SSH, port 22) — lands in ~/myvnc-shared. Requires the same username/password as the connection to also work for SSH login (true by default if wayvnc is configured with enable_pam=true). Drag and drop files onto the session works too.",
            "Sender en eller flere filer til fjernmaskinen via SFTP (SSH, port 22) — havner i ~/myvnc-shared. Krever at samme brukernavn/passord som tilkoblingen også fungerer for SSH-innlogging (stemmer vanligvis hvis wayvnc er konfigurert med enable_pam=true). Dra og slipp filer på økten fungerer også.",
            "Sender en eller flere filer til fjernmaskinen via SFTP (SSH, port 22) — havner i ~/myvnc-shared. Kræver, at samme brugernavn/adgangskode som forbindelsen også virker til SSH-login (er normalt tilfældet, hvis wayvnc er konfigureret med enable_pam=true). Træk og slip filer på sessionen virker også.",
            "Lähettää yhden tai useamman tiedoston etäkoneelle SFTP:n kautta (SSH, portti 22) — päätyy kansioon ~/myvnc-shared. Vaatii, että sama käyttäjätunnus/salasana kuin yhteydessä toimii myös SSH-kirjautumiseen (näin on yleensä, jos wayvnc on määritetty enable_pam=true-asetuksella). Tiedostojen raahaaminen istunnon päälle toimii myös.",
            "Sendir eina eða fleiri skrár á fjartengdu vélina í gegnum SFTP (SSH, gátt 22) — lendir í ~/myvnc-shared. Krefst þess að sama notandanafn/lykilorð og tengingin virki líka fyrir SSH-innskráningu (er venjulega raunin ef wayvnc er stillt með enable_pam=true). Að draga og sleppa skrám á setuna virkar líka.");
        Add("Session.SendFileOk",
            "{0} fil(er) skickade till ~/myvnc-shared", "{0} file(s) sent to ~/myvnc-shared",
            "{0} fil(er) sendt til ~/myvnc-shared", "{0} fil(er) sendt til ~/myvnc-shared",
            "{0} tiedosto(a) lähetetty kansioon ~/myvnc-shared", "{0} skrá/skrár sendar í ~/myvnc-shared");
        Add("Session.SendFileFail",
            "Misslyckades: {0}", "Failed: {0}", "Mislyktes: {0}", "Mislykkedes: {0}", "Epäonnistui: {0}", "Mistókst: {0}");
        Add("Session.ReceiveFile",
            "Hämta fil…", "Receive file…", "Hent fil…", "Hent fil…", "Nouda tiedosto…", "Sækja skrá…");
        Add("Session.ReceiveFileTooltip",
            "Bläddra i ~/myvnc-shared på fjärrdatorn och hämta filer därifrån via SFTP (SSH, port 22).",
            "Browse ~/myvnc-shared on the remote host and download files from it via SFTP (SSH, port 22).",
            "Bla gjennom ~/myvnc-shared på fjernmaskinen og hent filer derfra via SFTP (SSH, port 22).",
            "Gennemse ~/myvnc-shared på fjernmaskinen og hent filer derfra via SFTP (SSH, port 22).",
            "Selaa etäkoneen kansiota ~/myvnc-shared ja nouda tiedostoja sieltä SFTP:n kautta (SSH, portti 22).",
            "Flettu í gegnum ~/myvnc-shared á fjartengdu vélinni og sæktu skrár þaðan í gegnum SFTP (SSH, gátt 22).");
        Add("Session.ReceiveFileLoading",
            "Hämtar fillista…", "Loading file list…", "Henter fillliste…", "Henter fil-liste…", "Ladataan tiedostolistaa…", "Sæki skráalista…");
        Add("Session.ReceiveFileEmpty",
            "Inga filer i ~/myvnc-shared på fjärrdatorn.", "No files in ~/myvnc-shared on the remote host.",
            "Ingen filer i ~/myvnc-shared på fjernmaskinen.", "Ingen filer i ~/myvnc-shared på fjernmaskinen.",
            "Ei tiedostoja etäkoneen kansiossa ~/myvnc-shared.", "Engar skrár í ~/myvnc-shared á fjartengdu vélinni.");
        Add("Session.ReceiveFileError",
            "Kunde inte lista filer: {0}", "Couldn't list files: {0}", "Kunne ikke liste filer: {0}",
            "Kunne ikke liste filer: {0}", "Tiedostoja ei voitu listata: {0}", "Ekki tókst að skrá skrár: {0}");
        Add("Session.ReceiveFileChooseFolder",
            "Välj mapp att spara i", "Choose a folder to save into", "Velg en mappe å lagre i",
            "Vælg en mappe at gemme i", "Valitse tallennuskansio", "Veldu möppu til að vista í");
        Add("Session.ReceiveFileDownload",
            "Hämta valda", "Download selected", "Hent valgte", "Hent valgte", "Nouda valitut", "Sækja valdar");
        Add("Session.ReceiveFileOk",
            "{0} fil(er) hämtade till {1}", "{0} file(s) downloaded to {1}",
            "{0} fil(er) hentet til {1}", "{0} fil(er) hentet til {1}",
            "{0} tiedosto(a) ladattu kansioon {1}", "{0} skrá/skrár sóttar í {1}");

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
        Add("Form.ErrorDuplicateAddress",
            "\"{1}\" används redan av anslutningen \"{0}\".", "\"{1}\" is already used by the connection \"{0}\".",
            "\"{1}\" brukes allerede av tilkoblingen \"{0}\".", "\"{1}\" bruges allerede af forbindelsen \"{0}\".",
            "\"{1}\" on jo käytössä yhteydessä \"{0}\".", "\"{1}\" er þegar notað af tengingunni \"{0}\".");

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
        Add("Card.AlreadyConnected",
            "Redan ansluten — klicka på kortet för att växla dit", "Already connected — click the card to switch to it",
            "Allerede tilkoblet — klikk på kortet for å bytte dit", "Allerede forbundet — klik på kortet for at skifte dertil",
            "Jo yhdistetty — siirry sinne napsauttamalla korttia", "Þegar tengt — smelltu á kortið til að skipta þangað");

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

        Add("About.Title",
            "Om MyVNC", "About MyVNC", "Om MyVNC", "Om MyVNC", "Tietoja MyVNC:stä", "Um MyVNC");
        Add("About.Version",
            "Version {0}", "Version {0}", "Versjon {0}", "Version {0}", "Versio {0}", "Útgáfa {0}");
        Add("About.Quote",
            "“Deliberated this VNC app notwithstanding a total absence of requirements”",
            "“Deliberated this VNC app notwithstanding a total absence of requirements”",
            "“Deliberated this VNC app notwithstanding a total absence of requirements”",
            "“Deliberated this VNC app notwithstanding a total absence of requirements”",
            "“Deliberated this VNC app notwithstanding a total absence of requirements”",
            "“Deliberated this VNC app notwithstanding a total absence of requirements”");
        Add("About.Description",
            "En egenbyggd VNC-klient för Windows, gjord för att ansluta rent till Hyprland/omarchy-maskiner som kör wayvnc. Byggd från grunden — egen RFB-protokollimplementation, inget medföljande VNC-bibliotek.",
            "A custom-built VNC client for Windows, made to connect cleanly to Hyprland/omarchy machines running wayvnc. Built from scratch — own RFB protocol implementation, no bundled VNC library.",
            "En skreddersydd VNC-klient for Windows, laget for å koble rent til Hyprland/omarchy-maskiner som kjører wayvnc. Bygget fra bunnen — egen RFB-protokollimplementasjon, ingen medfølgende VNC-bibliotek.",
            "En skræddersyet VNC-klient til Windows, lavet til at forbinde rent til Hyprland/omarchy-maskiner, der kører wayvnc. Bygget fra bunden — egen RFB-protokolimplementering, intet medfølgende VNC-bibliotek.",
            "Räätälöity VNC-asiakasohjelma Windowsille, tehty muodostamaan yhteys puhtaasti wayvnc:tä käyttäviin Hyprland/omarchy-koneisiin. Rakennettu alusta asti — oma RFB-protokollatoteutus, ei mukana tulevaa VNC-kirjastoa.",
            "Sérsmíðaður VNC-biðlari fyrir Windows, gerður til að tengjast Hyprland/omarchy-vélum sem keyra wayvnc á hreinan hátt. Byggður frá grunni — eigin RFB-samskiptareglur, ekkert meðfylgjandi VNC-bókasafn.");
        Add("Help.Title",
            "Hjälp", "Help", "Hjelp", "Hjælp", "Ohje", "Hjálp");
        Add("Help.ShortcutsHeader",
            "Tangentbordsgenvägar", "Keyboard shortcuts", "Tastatursnarveier", "Tastaturgenveje", "Pikanäppäimet", "Flýtileiðir á lyklaborði");
        Add("Help.ShortcutFullscreen",
            "Växla helskärm", "Toggle fullscreen", "Veksle fullskjerm", "Skift til fuld skærm", "Vaihda koko näyttöön", "Skipta um fylliskjá");
        Add("Help.KeyToolbar",
            "Höger Ctrl (håll in)", "Right Ctrl (hold)", "Høyre Ctrl (hold inne)", "Højre Ctrl (hold nede)",
            "Oikea Ctrl (pidä pohjassa)", "Hægri Ctrl (halda niðri)");
        Add("Help.ShortcutToolbar",
            "Visa verktygsfältet tillfälligt", "Show the toolbar temporarily", "Vis verktøylinjen midlertidig",
            "Vis værktøjslinjen midlertidigt", "Näytä työkalurivi tilapäisesti", "Sýna tækjastikuna tímabundið");
        Add("Help.KeyPinToolbar",
            "Vänster Ctrl × 3", "Left Ctrl × 3", "Venstre Ctrl × 3", "Venstre Ctrl × 3", "Vasen Ctrl × 3", "Vinstri Ctrl × 3");
        Add("Help.ShortcutPinToolbar",
            "Fäst/lossa verktygsfältet", "Pin/unpin the toolbar", "Fest/løsne verktøylinjen", "Fastgør/frigør værktøjslinjen",
            "Kiinnitä/irrota työkalurivi", "Festa/losa tækjastikuna");
        Add("Help.ShortcutPaste",
            "Klistra in i terminalen/Vim på fjärrdatorn", "Paste into the remote's terminal/Vim",
            "Lim inn i terminalen/Vim på fjernmaskinen", "Indsæt i terminalen/Vim på fjernmaskinen",
            "Liitä etäkoneen päätteeseen/Vimiin", "Líma inn í flugstöðina/Vim á fjartengdu vélinni");
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

        Add("Help.WayvncSetupsHeader",
            "Två vanliga wayvnc-upplägg", "Two common wayvnc setups", "To vanlige wayvnc-oppsett",
            "To almindelige wayvnc-opsætninger", "Kaksi yleistä wayvnc-asetusta", "Tvö algeng wayvnc-uppsetningar");

        Add("Help.WayvncExternalHeader",
            "Extern skärm (kan kopplas ur)", "External monitor (can be unplugged)", "Ekstern skjerm (kan kobles fra)",
            "Ekstern skærm (kan frakobles)", "Ulkoinen näyttö (voidaan irrottaa)", "Ytri skjár (hægt að aftengja)");
        Add("Help.WayvncExternalBody",
            "Om fjärrdatorn har en extern skärm som ibland kopplas ur, skapa en headless virtuell utgång som matchar dess upplösning och peka wayvnc mot den (-o <namn>) i en ExecStartPre-hook — annars tappar wayvnc sin källa så fort skärmen kopplas ur.",
            "If the remote machine has an external monitor that sometimes gets unplugged, create a headless virtual output matching its resolution and point wayvnc at it (-o <name>) via an ExecStartPre hook — otherwise wayvnc loses its source the moment the monitor is unplugged.",
            "Hvis fjernmaskinen har en ekstern skjerm som noen ganger kobles fra, opprett en hodeløs virtuell utgang som matcher oppløsningen og pek wayvnc mot den (-o <navn>) via en ExecStartPre-hook — ellers mister wayvnc kilden sin så snart skjermen kobles fra.",
            "Hvis fjernmaskinen har en ekstern skærm, der nogle gange frakobles, så opret en hovedløs virtuel udgang, der matcher dens opløsning, og peg wayvnc mod den (-o <navn>) via et ExecStartPre-hook — ellers mister wayvnc sin kilde, så snart skærmen frakobles.",
            "Jos etäkoneessa on ulkoinen näyttö, joka toisinaan irrotetaan, luo pääasematon virtuaalilähtö, joka vastaa sen resoluutiota, ja ohjaa wayvnc siihen (-o <nimi>) ExecStartPre-koukun kautta — muuten wayvnc menettää lähteensä heti kun näyttö irrotetaan.",
            "Ef fjartengda vélin er með ytri skjá sem er stundum aftengdur, búðu til höfuðlausa sýndarúttak sem passar við upplausn hans og beindu wayvnc að því (-o <nafn>) með ExecStartPre-krók — annars missir wayvnc uppsprettu sína um leið og skjárinn er aftengdur.");

        Add("Help.WayvncBuiltinHeader",
            "Inbyggd skärm", "Built-in screen", "Innebygd skjerm", "Indbygget skærm", "Sisäänrakennettu näyttö", "Innbyggður skjár");
        Add("Help.WayvncBuiltinBody",
            "Med en inbyggd skärm räcker vanlig wayvnc utan -o. Lägg till en liten watchdog-tjänst som startar om wayvnc automatiskt om fångst-pipelinen fastnar (t.ex. efter suspend/resume) — den kan annars sluta uppdatera bilden tyst.",
            "With a built-in screen, plain wayvnc without -o is enough. Add a small watchdog service that restarts wayvnc automatically if the capture pipeline gets stuck (e.g. after suspend/resume) — it can otherwise silently stop updating the picture.",
            "Med en innebygd skjerm holder det med vanlig wayvnc uten -o. Legg til en liten overvåkningstjeneste som starter wayvnc på nytt automatisk hvis fangst-pipelinen henger seg opp (f.eks. etter suspend/resume) — ellers kan den slutte å oppdatere bildet stille.",
            "Med en indbygget skærm er almindelig wayvnc uden -o nok. Tilføj en lille watchdog-tjeneste, der automatisk genstarter wayvnc, hvis capture-pipelinen sætter sig fast (f.eks. efter suspend/resume) — ellers kan den stille holde op med at opdatere billedet.",
            "Sisäänrakennetulla näytöllä pelkkä wayvnc ilman -o riittää. Lisää pieni vahtikoira-palvelu, joka käynnistää wayvnc:n automaattisesti uudelleen, jos kaappausputki jumittuu (esim. lepotilasta herätessä) — muuten se voi hiljaa lakata päivittämästä kuvaa.",
            "Með innbyggðum skjá dugar venjulegt wayvnc án -o. Bættu við litlum eftirlitsþjónusti sem endurræsir wayvnc sjálfkrafa ef upptökupípan festist (t.d. eftir svefn/vakningu) — annars getur hún hljóðlega hætt að uppfæra myndina.");

        Add("Help.LimitationsHeader",
            "Kända begränsningar", "Known limitations", "Kjente begrensninger",
            "Kendte begrænsninger", "Tunnetut rajoitukset", "Þekktar takmarkanir");
        Add("Help.LimitationSuspend",
            "En fjärrdator som är helt avstängd/suspenderad (inte bara låst eller skärmavstängd) kan inte väckas via tangentbord/mus över VNC — den har ingen nätverksstack kvar att skicka till. Kontrollera med ping: går den att nå men reagerar inte, är det ett Hyprland/kompositor-problem (t.ex. fastnat låsskärm); går den inte att nå alls, kolla maskinens vilo-/suspend-inställningar (hypridle, systemd-logind) istället.",
            "A remote machine that's fully suspended (not just locked or screen-blanked) can't be woken via keyboard/mouse over VNC — it has no running network stack left to receive input on. Check with ping first: reachable but unresponsive points at a Hyprland/compositor issue (e.g. a stuck lock screen); unreachable means check the machine's suspend settings (hypridle, systemd-logind) instead.",
            "En fjernmaskin som er helt suspendert (ikke bare låst eller skjermavslått) kan ikke vekkes via tastatur/mus over VNC — den har ingen nettverksstack igjen å motta input på. Sjekk med ping først: nåbar men uresponsiv peker på et Hyprland/kompositør-problem (f.eks. en fastlåst skjerm); ikke nåbar betyr at du bør sjekke maskinens hvilemodusinnstillinger (hypridle, systemd-logind) i stedet.",
            "En fjernmaskine, der er helt suspenderet (ikke bare låst eller med slukket skærm), kan ikke vækkes via tastatur/mus over VNC — den har ingen kørende netværksstack tilbage til at modtage input på. Tjek med ping først: kan nås, men reagerer ikke, peger på et Hyprland/kompositør-problem (f.eks. en fastlåst skærm); kan ikke nås betyder, at du bør tjekke maskinens suspend-indstillinger (hypridle, systemd-logind) i stedet.",
            "Etäkonetta, joka on täysin lepotilassa (ei vain lukittu tai näyttö sammunut), ei voi herättää näppäimistöllä/hiirellä VNC:n yli — sillä ei ole enää käynnissä olevaa verkkopinoa, joka voisi vastaanottaa syötteitä. Tarkista ensin pingillä: jos kone on tavoitettavissa mutta ei reagoi, kyseessä on Hyprland/kompositorin ongelma (esim. jumittunut lukitusruutu); jos konetta ei tavoiteta lainkaan, tarkista sen sijaan koneen lepotila-asetukset (hypridle, systemd-logind).",
            "Fjartengd vél sem er algjörlega í svefnstillingu (ekki bara læst eða með slökktan skjá) er ekki hægt að vekja með lyklaborði/mús yfir VNC — hún hefur ekkert keyrandi netkerfi eftir til að taka á móti inntaki. Athugaðu fyrst með ping: næst en svarar ekki bendir á Hyprland/samsetningarvanda (t.d. fastan læsingarskjá); næst ekki þýðir að athuga ætti svefnstillingar vélarinnar (hypridle, systemd-logind) í staðinn.");

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
