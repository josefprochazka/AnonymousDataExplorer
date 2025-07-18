using Microsoft.Data.Sqlite;

namespace AnonymousDataExplorer.Services
{
	public class DatabaseService
	{
		private readonly string _connectionString;

		public DatabaseService(IConfiguration configuration)
		{
			_connectionString = configuration.GetConnectionString("DefaultConnection");
		}

		public async Task<List<string>> GetTableNamesAsync()
		{
			var result = new List<string>();

			using var connection = new SqliteConnection(_connectionString);
			await connection.OpenAsync();

			var command = new SqliteCommand(
				   "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';",
				   connection);

			using var reader = await command.ExecuteReaderAsync();
			while (await reader.ReadAsync())
			{
				result.Add(reader.GetString(0));
			}

			return result;
		}
	}
}
