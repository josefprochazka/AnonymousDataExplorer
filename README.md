# AnonymousDataExplorer

Simple Blazor Server app for browsing and editing anonymous SQLite tables.

The app loads available tables from a local `.db` file and lets the user:
- Select a table from a dropdown
- See the data in a Telerik grid
- Add, edit, or delete records (even without knowing the structure ahead)

The data structure is fully dynamic – the app works without any predefined models or `DbSet<T>`. Columns are discovered at runtime.

## 🔧 Tech Stack

- **Blazor Server (.NET 8)**
- **Telerik UI for Blazor**
- **Entity Framework Core** (only used for getting `DbConnection`)
- **SQLite** (local file-based database)

## 📂 Structure

- `Components/Pages/TableSelector.razor` – main UI: table dropdown, data grid, modal dialogs
- `Services/DatabaseService.cs` – dynamic SQL access: select/insert/update/delete rows
- `Services/AppDbContext.cs` – EF Core context (used just to get configured connection)
- `Database/AppData.db` – local SQLite database with sample data

## ⚙️ How it works

EF Core is used to configure the `DbConnection`, but everything else is handled manually using SQL. No `DbSet<T>` or models are defined – the tables and their structure are unknown at compile-time.

That’s why most of the logic is built around dictionaries and dynamic metadata (`PRAGMA table_info(...)`, etc.).

## ▶️ Run locally

Make sure you have .NET 8 SDK installed.

```bash
git clone https://github.com/josefprochazka/AnonymousDataExplorer.git
