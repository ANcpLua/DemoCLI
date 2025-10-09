# DemoCLI - Azure DevOps Work Item Creator test

Ein .NET CLI-Tool zum automatischen Erstellen von User Stories in Azure DevOps.

## 🔧 Setup 

### 1. Konfiguration erstellen

Kopieren Sie `config.example.json` zu `config.json`:

```bash
cd DemoCLI
cp config.example.json config.json
```

### 2. Personal Access Token (PAT) konfigurieren

**Option A: Umgebungsvariable (Empfohlen für Sicherheit)**

```bash
export AZURE_DEVOPS_PAT="your-token-here"
```

**Option B: config.json (Nicht für Git-Commits!)**

Fügen Sie Ihren PAT in `config.json` ein (wird von .gitignore ignoriert).

### 3. Azure DevOps Konfiguration

Bearbeiten Sie `config.json`:

```json
{
  "Organization": "se25m002",
  "Project": "DemoCLI",
  "PersonalAccessToken": "",
  "WorkItemType": "User Story"
}
```

## 🚀 Verwendung

### User Stories erstellen

```bash
cd DemoCLI
dotnet run
```

Das Tool wird:
- ✅ Alle User Stories aus `templates.json` erstellen
- ✅ Work Item IDs ausgeben
- ✅ Einen Pull Request mit verlinkten Work Items erstellen

### Tests ausführen

```bash
dotnet test
```

## 📋 User Stories anpassen

Bearbeiten Sie `templates.json` um eigene User Stories hinzuzufügen:

```json
[
  {
    "Title": "Ihre User Story",
    "Description": "Als ... möchte ich ... damit ...",
    "AcceptanceCriteria": "- Kriterium 1\n- Kriterium 2",
    "StoryPoints": 5,
    "Priority": 1,
    "Tags": "Tag1; Tag2",
    "State": "New"
  }
]
```

## 🔒 Sicherheit

- ⚠️ **Niemals** PAT-Tokens in Git committen!
- ✅ Verwenden Sie Umgebungsvariablen für Tokens
- ✅ `config.json` ist in `.gitignore` enthalten
- ✅ Verwenden Sie `config.example.json` als Vorlage

## 📦 Build & Publish

```bash
dotnet build --configuration Release
dotnet publish --configuration Release --output ./publish
```

## 🧪 CI/CD Pipeline

Die Azure Pipeline (`azure-pipelines.yml`) führt automatisch aus:
- Kompilierung der Solution
- Unit Tests
- Veröffentlichung der Test-Resultate
- Artifact-Erstellung

Trigger: Commits zu `main`, `master` oder `develop`
