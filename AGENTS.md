# AGENTS.md — MyVNC

Instruktioner för AI-agenter (och mänskliga bidragsgivare) som arbetar i det här repot.
Versioneringsreglerna är en anpassning av
`C:\Data\Code\project-governance-starter-standalone\docs\versioning-releases.md` till MyVNC:s
faktiska setup (lokalt .NET-repo, ingen publicerad remote ännu). Vid konflikt gäller den här
filen för MyVNC specifikt; den generella governance-modellen i starterpaketet är källan för
principerna.

## Versionering

- **Kanonisk källa:** root `VERSION` (en rad, t.ex. `0.1.0`). Alla projekt i lösningen läser
  den automatiskt via `Directory.Build.props` (`Version`-property → Assembly-/File-/
  InformationalVersion). **Redigera aldrig version i enskilda `.csproj`-filer** — det finns
  bara en plats att ändra.
- **SemVer `MAJOR.MINOR.PATCH`:**
  - PATCH: bakåtkompatibla buggrättningar.
  - MINOR: ny bakåtkompatibel funktionalitet.
  - MAJOR: brytande ändring av användarkontraktet (t.ex. anslutningsprofilformatet, CLI-flaggor).
  - Förrelease vid behov: `0.2.0-rc.1`.
- **Bumpa versionen endast vid uttrycklig releaseförberedelse** — aldrig automatiskt efter varje
  commit eller varje uppgift. En vanlig arbetssession (buggfix, ny funktion under utveckling,
  refaktorering) bumpar INTE `VERSION` förrän användaren ber om en release, eller det är
  uppenbart att en sammanhållen leverans är klar och redo att markeras.
- **När `VERSION` ändras, uppdatera i samma ändring:**
  1. `VERSION`
  2. `CHANGELOG.md` (flytta `[Unreleased]`-innehåll till en ny `[X.Y.Z] - DATUM`-sektion)
  3. `docs/releases/X.Y.Z.md` (ny fil, samma mall som `docs/releases/0.1.0.md`)
  4. Bygg om (se "Bygg" nedan) och verifiera att appen startar innan releasen betraktas klar.
- **Release notes:** ange `Commit range` som `BASE_EXCLUSIVE..RELEASE_INCLUSIVE` (git-hashar)
  och lista varje commit i git-ordning under "Included commits", så snart repot har en
  tidigare release-tagg att avgränsa mot (0.1.0 är undantaget — se dess release note).
- **Taggar:** när en version releasas och committen är verifierad, skapa en annoterad tagg
  `vX.Y.Z` på den commiten. Taggar är immutabla — en felaktig release rättas med en ny
  patchversion, aldrig genom att flytta en publicerad tagg.

## Commitregler

- Dela upp ändringar i sammanhängande, självständigt granskningsbara slices — committa inte
  allt i en enda jätte-commit. Typiska slices i det här repot: RFB-protokoll/bibliotek,
  app-UI för en specifik funktion, lokalisering, dokumentation/versionering, build/tooling.
  Om en ändring spänner över flera orelaterade syften, dela den i flera commits även om de
  görs i samma session.
- Använd imperativa commit-meddelanden med prefix: `feat:`, `fix:`, `docs:`, `refactor:`,
  `test:`, `build:`, `chore:` eller `release:`.
- Granska `git status` (och diffen för det som stagas) före varje commit — blanda inte in
  filer som inte hör till slicen.
- En versionsbump (`VERSION` + `CHANGELOG.md` + release note) hör hemma i en egen
  `release:`-commit, separat från funktionsslicen den avslutar.
- Skriv inte om publicerade commits eller taggar. Rätta med en ny commit.
- Committa endast när användaren ber om det (gäller alla ändringar, inte bara release-relaterade).

## Bygg — tillsvidare: bygg alltid Release efter varje Debug-bygge

Skrivbordsikonen (`MyVNC.lnk`) pekar på Release-binären:
`src\MyVNC.App\bin\Release\net10.0-windows\MyVNC.App.exe`. Så länge den här instruktionen
gäller (tills användaren säger annat): kör Release-bygget direkt efter varje Debug-bygge, så
att skrivbordsikonen alltid startar senaste koden för manuell test.

```
taskkill //F //IM MyVNC.App.exe   # släpp ev. fillås
dotnet build MyVNC.slnx                    # Debug — snabb iteration/felsökning
dotnet build MyVNC.slnx -c Release          # Release — det ikonen på skrivbordet startar
```

Bygg alltid mot `MyVNC.slnx` (inte `.sln` — det finns inte).
