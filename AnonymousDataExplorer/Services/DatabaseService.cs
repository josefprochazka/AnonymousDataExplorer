using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AnonymousDataExplorer.Services
{
	public class AppDbContext : DbContext
	{
		private readonly string _connectionString;

		public AppDbContext(IConfiguration configuration)
		{
			_connectionString = configuration.GetConnectionString("DefaultConnection");
		}

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			optionsBuilder.UseSqlite(_connectionString);
		}
	}

	public class DatabaseService
	{
		private readonly AppDbContext _context;
		private readonly string _connectionString;

		public DatabaseService(AppDbContext context, IConfiguration configuration)
		{
			_context = context;
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

		public async Task<List<Dictionary<string, object>>> GetDataRowsAsync(string tableName)
		{
			var rows = new List<Dictionary<string, object>>();

			using var connection = new SqliteConnection(_connectionString);
			await connection.OpenAsync();

			var command = connection.CreateCommand();
			command.CommandText = $"SELECT * FROM [{tableName}]";

			using var reader = await command.ExecuteReaderAsync();
			while (await reader.ReadAsync())
			{
				var row = new Dictionary<string, object>();
				for (int i = 0; i < reader.FieldCount; i++)
				{
					row[reader.GetName(i)] = reader.GetValue(i);
				}
				rows.Add(row);
			}

			return rows;
		}

		public async Task<List<string>> GetColumnNamesOnlyAsync(string tableName)
		{
			var result = new List<string>();

			using var conn = new SqliteConnection(_connectionString);
			await conn.OpenAsync();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = $"PRAGMA table_info({tableName});";

			using var reader = await cmd.ExecuteReaderAsync();
			while (await reader.ReadAsync())
			{
				var name = reader["name"].ToString();
				if (!string.IsNullOrEmpty(name))
					result.Add(name);
			}

			return result;
		}
	}
}
