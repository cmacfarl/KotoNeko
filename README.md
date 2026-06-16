# KotoNeko 🐱

A WaniKani-style spaced-repetition app focused on **vocabulary** (not kanji), built
in C# with ASP.NET Core / Blazor Server and MySQL. Single-user, runs entirely
locally — no accounts, no authentication.

## Features

- **Typed answers** for every item — reading (kana) and meaning (English), exactly
  like WaniKani. No multiple choice, no "how did you feel" buttons.
- **Verb conjugation quizzing** — verbs get an extra question: a randomly chosen
  conjugation form (past, te-form, potential, passive, causative, causative-passive,
  imperative — affirmative & negative). One form per review.
- **Local conjugation engine** — all 14 forms are computed in C# from grammar rules
  (godan / ichidan / する / 来る plus 行く and ある irregulars).
- **jisho.org button** — on the vocab admin page, auto-detects a word's reading and
  verb class (and fills the meaning), then computes its conjugations. Editable
  afterwards.
- **Romaji → kana** auto-conversion on the reading field and on all kana quiz inputs
  (type `shinbun` → しんぶん). No OS IME juggling.
- **WaniKani SRS** — Apprentice → Guru → Master → Enlightened → Burned, with
  WaniKani's intervals and stage-drop penalty. A verb is one SRS item; reading,
  meaning, and the conjugation must all be correct to advance.
- **Native "DoubleCheck" undo** — undo the last answer mid-review to fix a typo. It
  rolls back the stage change and the log together, so a fumbled keystroke never
  penalizes you.
- **Sleep / wake** items to exclude them from lessons and reviews.
- **WaniKani-style dashboard** — lessons/reviews counts, SRS band tiles, a 24-hour
  review forecast, and a recently-missed list.
- **Admin** for vocabulary and for source materials (the "source" dropdown, e.g.
  *Tobira 2*, *Yotsuba-to*).

> Anki import is intentionally deferred, but the schema (notes/fields, nullable
> source) is designed so it can be added later without a redesign.

## Project layout

| Project              | Purpose                                                        |
|----------------------|----------------------------------------------------------------|
| `KotoNeko.Core`      | Domain models, SRS engine, conjugation engine, romaji + answer matching |
| `KotoNeko.Data`      | EF Core `DbContext`, MySQL provider, migrations                |
| `KotoNeko.Jisho`     | jisho.org client (reading + verb-class detection)              |
| `KotoNeko.Web`       | Blazor Server UI + application services                        |
| `KotoNeko.Tests`     | xUnit tests for the conjugation, SRS, romaji, and answer engines |

## Prerequisites

- .NET 8 SDK
- A MySQL 8 server

## Configure the database

The connection string ships with **placeholder values**. Set your real one in either:

- `src/KotoNeko.Web/appsettings.json` → `ConnectionStrings:KotoNeko`, or
- the `KOTONEKO_CONNECTION` environment variable (takes precedence), e.g.

```powershell
$env:KOTONEKO_CONNECTION = "Server=localhost;Port=3306;Database=kotoneko;User=root;Password=YOURPASSWORD;"
```

The database schema is created/updated automatically on startup (EF Core migrations
run on launch). You only need to ensure the database itself exists and the user can
connect — or grant rights to create it.

## Run

```powershell
dotnet run --project src/KotoNeko.Web
```

Then open **http://localhost:5131** (or the HTTPS URL shown in the console).

## Test

```powershell
dotnet test
```

## Typical flow

1. **Sources** → add your materials (e.g. *Tobira 2*).
2. **Vocabulary** → *Add vocabulary*. Type the Japanese word, click **Fetch from
   jisho** to fill the reading / verb class / conjugations, tweak as needed, save.
3. **Lessons** → learn new items (enters them at Apprentice I).
4. **Reviews** → when items come due, quiz reading + meaning (+ a conjugation for
   verbs). Use **Undo** if you mistype.
5. **Dashboard** → watch your SRS progress and what you've recently missed.
